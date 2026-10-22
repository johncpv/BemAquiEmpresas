using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.HR.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BemAqui.Production.Application;

/// <summary>
/// Supervisor (PRD-03a CA 15, 18 a 22, 31): aba Apontamentos da OS, "Apontar por funcionário", "Corrigir apontamento",
/// "Aprovar etapa" e os apontamentos abertos há muito tempo. Quem apontou não corrige nem aprova a própria etapa, salvo sem
/// outro aprovador elegível (<see cref="SoleOwnerRule"/>, CA 21). OS, etapa e apontamento conferidos pelo id da OS (IDOR, CA 29).
/// </summary>
public sealed class SupervisorLaborService(
    IProductionDb db, LaborWorkflow workflow, IBomGate bom, IEmployees employees, IUserEmployeeLink links, IUserDirectory users, IPermissionChecker permissions,
    ICurrentUser currentUser, IEventPublisher events, IConfiguration configuration)
{
    public const string AlertHoursSetting = "Production:OpenLaborAlertHours";

    /// <summary>Aba Apontamentos (CA 31): funcionário, início, fim, pausas com motivo, minutos líquidos, quem registrou e as correções.</summary>
    public async Task<Result<OrderLaborView>> ListAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId && o.Type == ProductionOrderType.Fabrication, cancellationToken);
        if (order is null)
        {
            return InstallationOrderService.NotFound();
        }

        var steps = (await db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == orderId).OrderBy(s => s.Sequence).ToListAsync(cancellationToken));
        var byStep = steps.ToDictionary(s => s.Id);
        var entries = await db.LaborEntries.AsNoTracking().Where(e => e.OrderId == orderId).OrderByDescending(e => e.StartedAt).ToListAsync(cancellationToken);
        var ids = entries.Select(e => e.Id).ToList();
        var pauses = (await db.StepPauses.AsNoTracking().Where(p => ids.Contains(p.LaborEntryId)).OrderBy(p => p.StartedAt).ToListAsync(cancellationToken)).ToLookup(p => p.LaborEntryId);
        var names = (await employees.FindAsync(entries.Select(e => e.EmployeeId).Distinct().ToList(), cancellationToken)).ToDictionary(e => e.Id, e => e.Name);
        var userNames = new Dictionary<Guid, string?>();
        foreach (var user in entries.SelectMany(e => new[] { e.RecordedBy, e.ClosedBy, e.CorrectedBy }).Where(u => u is not null).Select(u => u!.Value).Distinct())
        {
            userNames[user] = await users.DisplayNameAsync(user, cancellationToken);
        }

        var myEmployee = currentUser.UserId is { } me ? await links.EmployeeOfUserAsync(me, cancellationToken) : null;
        var now = workflow.Now;
        var views = entries.Select(e =>
        {
            var step = byStep.GetValueOrDefault(e.OrderStepId);
            var spans = pauses[e.Id].Select(p => new PauseSpan(p.StartedAt, p.EndedAt)).ToList();
            var net = LaborRules.NetMinutes(e.StartedAt, e.EndedAt, spans, now);
            return new LaborEntryView(
                e.Id, e.OrderStepId, step?.Code ?? string.Empty, step?.Name ?? string.Empty, e.EmployeeId, names.GetValueOrDefault(e.EmployeeId, "Funcionário"), e.StartedAt, e.EndedAt,
                e.EndedAt is null ? LaborRules.PausedMinutes(spans, now) : e.PausedMinutes, net, LaborRules.Hours(net),
                pauses[e.Id].Select(p => new LaborPauseView(p.Id, p.Reason, p.Notes, p.StartedAt, p.EndedAt)).ToList(), Name(userNames, e.RecordedBy), e.OnBehalf, e.CloseKind,
                Name(userNames, e.ClosedBy), e.CorrectedAt, Name(userNames, e.CorrectedBy), e.CorrectionReason, e.SelfApproved, e.Notes,
                myEmployee is { } mine && e.EmployeeId == mine, e.Version);
        }).ToList();
        var awaiting = steps.FirstOrDefault(s => s.AwaitingApprovalSince is not null);
        return new OrderLaborView(order.Id, order.Number, views, views.Sum(v => v.Hours), awaiting?.Id, awaiting?.Name, order.Version);
    }

    /// <summary>Funcionários ativos hoje para o "Apontar por funcionário" (CA 18): afastado e desligado ficam fora.</summary>
    public async Task<IReadOnlyList<LaborEmployeeOption>> EmployeesAsync(CancellationToken cancellationToken = default) =>
        (await employees.ActiveAsync(cancellationToken)).Where(e => e.Status == EmployeeSituation.Active)
            .Select(e => new LaborEmployeeOption(e.Id, e.Name, e.Registration, e.PositionName)).ToList();

    /// <summary>
    /// "Apontar por funcionário" (CA 18, 19): funcionário ativo, etapa da OS, início e fim (nulo = agora) e as pausas; marca
    /// "Apontado por {supervisor}" e os efeitos do início (CA 7) e do fechamento (CA 13). Início no futuro, fim antes do início ou
    /// mais de 7 dias: 422; sobreposição com outro apontamento do mesmo funcionário: 409.
    /// </summary>
    public async Task<Result<OrderLaborView>> RecordAsync(Guid orderId, LaborEntryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("shopfloor", "create", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Apontar por funcionário exige também a ação de apontar do app do operador.");
        }

        var scope = await workflow.LoadAsync(input.StepId, orderId, cancellationToken);
        if (scope is null)
        {
            return Error.NotFound("step_not_found", "Etapa não encontrada nesta OS.");
        }

        if (await workflow.ActiveEmployeeAsync(input.EmployeeId, cancellationToken) is not { } employee)
        {
            return LaborRules.Invalid("employeeId", "Escolha um funcionário ativo (afastado ou desligado não aponta).");
        }

        if (await LaborWorkflow.CanStartAsync(scope, bom, cancellationToken) is { } refused)
        {
            return refused;
        }

        var now = workflow.Now;
        var endedAt = input.EndedAt ?? now;
        var pauses = (input.Pauses ?? []).Select(p => (p.Reason, Notes: Clean(p.Notes), p.StartedAt, p.EndedAt)).ToList();
        if (LaborRules.ValidateInterval(input.StartedAt, endedAt, pauses, now) is { } invalid)
        {
            return invalid;
        }

        if (await OverlapAsync(employee, input.StartedAt, endedAt, null, cancellationToken) is { } overlap)
        {
            return overlap;
        }

        var entry = workflow.Open(scope, employee.Id, input.StartedAt, onBehalf: true);
        entry.Notes = Clean(input.Notes);
        var created = pauses.Select(p => new StepPause { LaborEntryId = entry.Id, OrderId = orderId, Reason = p.Reason, Notes = p.Notes, StartedAt = p.StartedAt, EndedAt = p.EndedAt }).ToList();
        db.StepPauses.AddRange(created);
        workflow.Close(scope, entry, endedAt, LaborCloseKind.Supervisor, created);
        await workflow.SettleAsync(scope, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ListAsync(orderId, cancellationToken);
    }

    /// <summary>
    /// "Corrigir apontamento" (CA 20, 21): início, fim e pausas com motivo, nas regras do CA 19; a correção fica auditada (antes e
    /// depois) e publica <see cref="LaborEntryCorrected"/>; o aberto fecha como "Encerrado pelo supervisor".
    /// </summary>
    public async Task<Result<OrderLaborView>> CorrectAsync(Guid orderId, Guid entryId, LaborCorrectionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entry = await db.LaborEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.OrderId == orderId, cancellationToken);
        if (entry is null)
        {
            return Error.NotFound("labor_entry_not_found", "Apontamento não encontrado nesta OS.");
        }

        if (entry.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O apontamento mudou desde que você abriu. Recarregue.");
        }

        db.Entry(entry).Property(e => e.Version).OriginalValue = input.Version;
        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > LaborRules.MaxReason)
        {
            return LaborRules.Invalid("reason", $"Informe o motivo da correção, com até {LaborRules.MaxReason} caracteres.");
        }

        var scope = (await workflow.LoadAsync(entry.OrderStepId, orderId, cancellationToken))!;
        var involved = await workflow.UsersOfAsync([entry.EmployeeId], cancellationToken);
        var segregation = await workflow.SegregationAsync(involved, scope, cancellationToken);
        if (segregation.IsFailure)
        {
            return segregation.Error!;
        }

        var now = workflow.Now;
        var pauses = (input.Pauses ?? []).Select(p => (p.Reason, Notes: Clean(p.Notes), p.StartedAt, p.EndedAt)).ToList();
        if (LaborRules.ValidateInterval(input.StartedAt, input.EndedAt, pauses, now) is { } invalid)
        {
            return invalid;
        }

        var employee = await employees.FindAsync([entry.EmployeeId], cancellationToken) is [var found, ..] ? found : null;
        if (await OverlapAsync(employee, input.StartedAt, input.EndedAt, entry.Id, cancellationToken, entry.EmployeeId) is { } overlap)
        {
            return overlap;
        }

        var wasOpen = entry.EndedAt is null;
        db.StepPauses.RemoveRange(await workflow.PausesOfAsync(entry.Id, cancellationToken));
        var created = pauses.Select(p => new StepPause { LaborEntryId = entry.Id, OrderId = orderId, Reason = p.Reason, Notes = p.Notes, StartedAt = p.StartedAt, EndedAt = p.EndedAt }).ToList();
        db.StepPauses.AddRange(created);
        entry.StartedAt = input.StartedAt;
        entry.CorrectedAt = now;
        entry.CorrectedBy = currentUser.UserId;
        entry.CorrectionReason = reason;
        entry.SelfApproved = entry.SelfApproved || segregation.Value;
        if (wasOpen)
        {
            workflow.Close(scope, entry, input.EndedAt, LaborCloseKind.Supervisor, created);
        }
        else
        {
            entry.EndedAt = input.EndedAt;
            entry.PausedMinutes = LaborRules.PausedMinutes(created.Select(p => new PauseSpan(p.StartedAt, p.EndedAt)), input.EndedAt);
            events.Publish(new LaborEntryCorrected(entry.Id));
        }

        await workflow.SettleAsync(scope, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ListAsync(orderId, cancellationToken);
    }

    /// <summary>
    /// "Aprovar etapa" (CA 15, 21): a etapa "Aguardando aprovação" vai a <c>Done</c> e a próxima fica pronta. Quem apontou nela não
    /// aprova, salvo sem outro aprovador elegível ("Autoaprovado" e aviso ao Proprietário).
    /// </summary>
    public async Task<Result<OrderLaborView>> ApproveStepAsync(Guid orderId, Guid stepId, ApproveStepInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var scope = await workflow.LoadAsync(stepId, orderId, cancellationToken);
        if (scope is null)
        {
            return Error.NotFound("step_not_found", "Etapa não encontrada nesta OS.");
        }

        if (scope.Order.Version != input.Version)
        {
            return InstallationOrderService.Stale();
        }

        db.Entry(scope.Order).Property(o => o.Version).OriginalValue = input.Version;
        if (scope.Step.AwaitingApprovalSince is null || scope.Step.Status == ProductionStepStatus.Done || scope.Order.Status == ProductionOrderStatus.Cancelled)
        {
            return Error.Conflict("step_not_awaiting", "Esta etapa não está aguardando aprovação.");
        }

        var segregation = await workflow.SegregationAsync(await workflow.InvolvedUsersAsync(stepId, cancellationToken), scope, cancellationToken);
        if (segregation.IsFailure)
        {
            return segregation.Error!;
        }

        workflow.Complete(scope, workflow.Now, bySupervisor: false, reason: null, selfApproved: segregation.Value);
        await workflow.SettleAsync(scope, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ListAsync(orderId, cancellationToken);
    }

    /// <summary>
    /// "Apontamentos abertos há mais de {x} h" (CA 22): {x} = 12 h (parâmetro <c>Production:OpenLaborAlertHours</c>), com funcionário,
    /// OS, etapa, início e a última pausa, do mais antigo para o mais novo.
    /// </summary>
    public async Task<OpenLaborList> OpenAsync(CancellationToken cancellationToken = default)
    {
        var hours = configuration.GetValue<int?>(AlertHoursSetting) is int h && h > 0 ? h : LaborRules.DefaultOpenAlertHours;
        var now = workflow.Now;
        var limit = now.AddHours(-hours);
        var rows = await (from e in db.LaborEntries.AsNoTracking()
                          join s in db.ProductionOrderSteps.AsNoTracking() on e.OrderStepId equals s.Id
                          join o in db.ProductionOrders.AsNoTracking() on e.OrderId equals o.Id
                          where e.EndedAt == null && e.StartedAt < limit
                          orderby e.StartedAt
                          select new { Entry = e, s.Code, s.Name, o.Number, OrderVersion = o.Version }).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Entry.Id).ToList();
        var pauses = (await db.StepPauses.AsNoTracking().Where(p => ids.Contains(p.LaborEntryId)).ToListAsync(cancellationToken)).ToLookup(p => p.LaborEntryId);
        var names = (await employees.FindAsync(rows.Select(r => r.Entry.EmployeeId).Distinct().ToList(), cancellationToken)).ToDictionary(e => e.Id, e => e.Name);
        return new OpenLaborList(hours, rows.Select(r =>
        {
            var last = pauses[r.Entry.Id].OrderByDescending(p => p.StartedAt).FirstOrDefault();
            var net = LaborRules.NetMinutes(r.Entry.StartedAt, null, pauses[r.Entry.Id].Select(p => new PauseSpan(p.StartedAt, p.EndedAt)), now);
            return new OpenLaborEntry(r.Entry.Id, r.Entry.OrderId, r.Number, r.Entry.OrderStepId, r.Code ?? string.Empty, r.Name, names.GetValueOrDefault(r.Entry.EmployeeId, "Funcionário"),
                r.Entry.StartedAt, LaborRules.Hours(net), last?.Reason, last?.StartedAt, r.OrderVersion, r.Entry.Version);
        }).ToList());
    }

    /// <summary>Sobreposição com outro apontamento do funcionário (CA 19): 409 com o intervalo que já existe.</summary>
    private async Task<Error?> OverlapAsync(EmployeeSummary? employee, DateTimeOffset startedAt, DateTimeOffset endedAt, Guid? except, CancellationToken ct, Guid? employeeId = null)
    {
        var id = employee?.Id ?? employeeId!.Value;
        var spans = await db.LaborEntries.AsNoTracking()
            .Where(e => e.EmployeeId == id && e.StartedAt < endedAt && (e.EndedAt == null || e.EndedAt > startedAt))
            .Select(e => new EntrySpan(e.Id, e.StartedAt, e.EndedAt)).ToListAsync(ct);
        var found = LaborRules.Overlapping(startedAt, endedAt, spans, except);
        if (found is null)
        {
            return null;
        }

        var end = found.EndedAt is { } ended ? LaborWorkflow.Local(ended) : "agora";
        return Error.Conflict("labor_overlap", $"{employee?.Name ?? "O funcionário"} já tem apontamento entre {LaborWorkflow.Local(found.StartedAt)} e {end}.");
    }

    private static string? Name(Dictionary<Guid, string?> names, Guid? user) => user is { } id ? names.GetValueOrDefault(id) : null;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}