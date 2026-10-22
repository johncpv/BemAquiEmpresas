using BemAqui.HR.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class ShopfloorService
{
    /// <summary>
    /// "Iniciar etapa" (CA 7 a 10, 14, 16, 30): na mesma transação nasce o apontamento do funcionário do usuário, a etapa vai a
    /// <c>InProgress</c>, a OS sai de Aberta (com <c>ProductionStepStarted</c>) e o Kanban é avisado depois do commit. Etapa não
    /// pronta 409, OS cancelada 409, outro tenant 404, qualidade 403; um apontamento aberto por funcionário (índice único).
    /// </summary>
    public async Task<Result<ShopfloorActionResult>> StartAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        var me = await OperatorAsync(cancellationToken);
        if (me.IsFailure)
        {
            return me.Error!;
        }

        var scope = await workflow.LoadAsync(stepId, null, cancellationToken);
        if (scope is null)
        {
            return StepNotFound();
        }

        if (await LaborWorkflow.CanStartAsync(scope, bom, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await workflow.AlreadyOpenAsync(me.Value.Id, cancellationToken) is { } open)
        {
            return open;
        }

        workflow.Open(scope, me.Value.Id, workflow.Now, onBehalf: false);
        await workflow.SettleAsync(scope, cancellationToken);
        if (await SaveAsync(scope, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await ResultAsync(stepId, me.Value, 0m, [], cancellationToken);
    }

    /// <summary>
    /// "Pausar" (CA 11): pausa no apontamento do operador; a etapa fica <c>Paused</c> quando ninguém mais trabalha nela e a OS
    /// quando nenhuma etapa está em andamento. "Falta de material" avisa quem tem <c>inventory.create</c>, com link para a OS.
    /// </summary>
    public async Task<Result<ShopfloorActionResult>> PauseAsync(Guid stepId, ShopfloorPauseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        if (!Enum.IsDefined(input.Reason))
        {
            return LaborRules.Invalid("reason", "Escolha o motivo da pausa.");
        }

        if (notes is { Length: > LaborRules.MaxPauseNotes })
        {
            return LaborRules.Invalid("notes", $"Escreva até {LaborRules.MaxPauseNotes} caracteres.");
        }

        var loaded = await MineAsync(stepId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (employee, scope, entry) = loaded.Value;
        if (await db.StepPauses.AnyAsync(p => p.LaborEntryId == entry.Id && p.EndedAt == null, cancellationToken))
        {
            return Error.Conflict("already_paused", "A etapa já está pausada para você. Toque em Retomar.");
        }

        db.StepPauses.Add(new StepPause { LaborEntryId = entry.Id, OrderId = scope.Order.Id, Reason = input.Reason, Notes = notes, StartedAt = workflow.Now });
        await workflow.SettleAsync(scope, cancellationToken);
        if (input.Reason == PauseReason.Material)
        {
            foreach (var user in await users.UsersWithPermissionAsync("inventory.create", cancellationToken))
            {
                notifications.Notify(user, MaterialPauseType, $"{employee.Name} parou a {scope.Order.Number}, etapa {scope.Step.Name}, por falta de material.", LaborWorkflow.OrderLink + scope.Order.Id);
            }
        }

        return await SaveAsync(scope, cancellationToken) is { } conflict ? conflict : await ResultAsync(stepId, employee, 0m, [], cancellationToken);
    }

    /// <summary>"Retomar" (CA 12): a pausa fecha e a etapa e a OS voltam a <c>InProgress</c>.</summary>
    public async Task<Result<ShopfloorActionResult>> ResumeAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        var loaded = await MineAsync(stepId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (employee, scope, entry) = loaded.Value;
        var pause = await db.StepPauses.FirstOrDefaultAsync(p => p.LaborEntryId == entry.Id && p.EndedAt == null, cancellationToken);
        if (pause is null)
        {
            return Error.Conflict("not_paused", "A etapa não está pausada para você.");
        }

        pause.EndedAt = workflow.Now;
        await workflow.SettleAsync(scope, cancellationToken);
        return await SaveAsync(scope, cancellationToken) is { } conflict ? conflict : await ResultAsync(stepId, employee, 0m, [], cancellationToken);
    }

    /// <summary>"Sair da etapa (só eu)" (CA 14): fecha só o apontamento do operador, sem checklist; a etapa continua com os outros.</summary>
    public async Task<Result<ShopfloorActionResult>> LeaveAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        var loaded = await MineAsync(stepId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (employee, scope, entry) = loaded.Value;
        if (!await db.LaborEntries.AnyAsync(e => e.OrderStepId == stepId && e.EndedAt == null && e.Id != entry.Id, cancellationToken))
        {
            return Error.Conflict("only_one_on_step", "Você é o único na etapa: finalize ou pause.");
        }

        var minutes = await workflow.CloseAsync(scope, entry, workflow.Now, LaborCloseKind.Left, cancellationToken);
        await workflow.SettleAsync(scope, cancellationToken);
        return await SaveAsync(scope, cancellationToken) is { } conflict ? conflict : await ResultAsync(stepId, employee, LaborRules.Hours(minutes), [], cancellationToken);
    }

    /// <summary>
    /// "Finalizar etapa" (CA 13) e "Concluir etapa para todos" (CA 14): exige o checklist completo (422 com a lista); fecha o
    /// apontamento do operador e, para todos, os dos outros ("Encerrado por {nome}"); publica <c>LaborEntryClosed</c> por apontamento.
    /// A etapa vai a <c>Done</c> e a próxima fica pronta; a que exige aprovação fica "Aguardando aprovação" e avisa quem aprova (CA 15).
    /// </summary>
    public async Task<Result<ShopfloorActionResult>> FinishAsync(Guid stepId, ShopfloorFinishInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await MineAsync(stepId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (employee, scope, entry) = loaded.Value;
        var others = await db.LaborEntries.Where(e => e.OrderStepId == stepId && e.EndedAt == null && e.Id != entry.Id).ToListAsync(cancellationToken);
        if (others.Count > 0 && !input.ForAll)
        {
            return Error.Conflict("others_on_step", "Há mais gente nesta etapa: use \"Sair da etapa (só eu)\" ou \"Concluir etapa para todos\".");
        }

        var missing = await db.ProductionOrderChecklist.AsNoTracking().Where(c => c.StepId == stepId && c.Result == null).OrderBy(c => c.Sequence).Select(c => c.Text).ToListAsync(cancellationToken);
        if (missing.Count > 0)
        {
            return Error.Validation("checklist_incomplete", LaborRules.MissingChecklist(missing.Count), new Dictionary<string, string[]> { ["checklist"] = [.. missing] });
        }

        var now = workflow.Now;
        var minutes = await workflow.CloseAsync(scope, entry, now, LaborCloseKind.Operator, cancellationToken);
        var names = (await employees.FindAsync(others.Select(o => o.EmployeeId).Distinct().ToList(), cancellationToken)).ToDictionary(e => e.Id, e => e.Name);
        foreach (var other in others)
        {
            await workflow.CloseAsync(scope, other, now, LaborCloseKind.StepCompleted, cancellationToken);
        }

        if (scope.Step.RequiresApproval)
        {
            scope.Step.AwaitingApprovalSince = now;
            foreach (var user in await users.UsersWithPermissionAsync("production.approve", cancellationToken))
            {
                notifications.Notify(user, "production.step_awaiting_approval", $"A etapa {scope.Step.Name} da {scope.Order.Number} aguarda sua aprovação.", LaborWorkflow.OrderLink + scope.Order.Id + "?aba=apontamentos");
            }
        }
        else
        {
            workflow.Complete(scope, now, bySupervisor: false, reason: null, selfApproved: false);
        }

        await workflow.SettleAsync(scope, cancellationToken);
        return await SaveAsync(scope, cancellationToken) is { } conflict
            ? conflict
            : await ResultAsync(stepId, employee, LaborRules.Hours(minutes), others.Select(o => names.GetValueOrDefault(o.EmployeeId, "Funcionário")).ToList(), cancellationToken);
    }

    /// <summary>Marcar ou desmarcar um item do checklist (CA 13): só quem está na etapa, gravado com usuário e data (auditado).</summary>
    public async Task<Result<ShopfloorStepDetail>> MarkChecklistAsync(Guid stepId, Guid itemId, ShopfloorChecklistInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await MineAsync(stepId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var item = await db.ProductionOrderChecklist.FirstOrDefaultAsync(c => c.Id == itemId && c.StepId == stepId, cancellationToken);
        if (item is null)
        {
            return Error.NotFound("checklist_item_not_found", "Item do checklist não encontrado nesta etapa.");
        }

        item.Result = input.Done ? ChecklistResult.Done : null;
        item.MarkedAt = input.Done ? workflow.Now : null;
        item.MarkedBy = input.Done ? currentUser.UserId : null;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(stepId, loaded.Value.Employee, cancellationToken);
    }

    /// <summary>Apontamento aberto do operador nesta etapa (CA 29: o funcionário é o do usuário; o de outro nunca é tocado).</summary>
    private async Task<Result<(EmployeeSummary Employee, LaborScope Scope, LaborEntry Entry)>> MineAsync(Guid stepId, CancellationToken ct)
    {
        var me = await OperatorAsync(ct);
        if (me.IsFailure)
        {
            return me.Error!;
        }

        var scope = await workflow.LoadAsync(stepId, null, ct);
        if (scope is null)
        {
            return StepNotFound();
        }

        var entry = await db.LaborEntries.FirstOrDefaultAsync(e => e.OrderStepId == stepId && e.EmployeeId == me.Value.Id && e.EndedAt == null, ct);
        return entry is null ? Error.Conflict("labor_not_open", "Você não está nesta etapa. Toque em Iniciar etapa.") : (me.Value, scope, entry);
    }

    /// <summary>Grava; o índice único do apontamento aberto vira o 409 do CA 10 (dois tablets ao mesmo tempo, CA 30).</summary>
    private async Task<Error?> SaveAsync(LaborScope scope, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException && e.InnerException?.Message.Contains("ux_labor_entries_open_employee", StringComparison.Ordinal) == true)
        {
            return LaborWorkflow.AlreadyOpen(scope.Step.Name, scope.Order.Number);
        }
    }

    /// <summary>A etapa como ficou (ou nada, se saiu da fila), as horas do operador e a próxima da fila (propositivo).</summary>
    private async Task<Result<ShopfloorActionResult>> ResultAsync(Guid stepId, EmployeeSummary employee, decimal myHours, IReadOnlyList<string> closedFor, CancellationToken ct)
    {
        var detail = await DetailAsync(stepId, employee, ct);
        ShopfloorQueueStep? next = null;
        if (detail.IsFailure || detail.Value.Mine is null)
        {
            var ids = (await QueueStepIdsAsync(ct)).Where(id => id != stepId).ToList();
            var views = await BuildAsync(ids, employee, ct);
            next = LaborRules.QueueOrder(views.Values.Select(v => v.Key)).Select(id => views[id].View).FirstOrDefault();
        }

        return new ShopfloorActionResult(detail.IsSuccess ? detail.Value : null, myHours, next, closedFor);
    }
}