using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Commercial.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Qualidade (PRD-05, <c>/api/v1/production/quality/*</c>, separado da supervisão para a lista permitida da sessão por PIN, ADR 0026 §4):
/// fila, etapa, checklist "Conforme / Não conforme / Não se aplica", início da inspeção, aprovar e reprovar. Tudo com
/// <c>production.approve_quality</c> (D6); a conclusão da OS é da <c>production.approve</c> (PRD-06a1). Quem apontou qualquer etapa da
/// tentativa não aprova, salvo sem outro aprovador elegível ("Autoinspeção", ponto 76). Toda decisão toca a versão (xmin) da OS: duas
/// decisões juntas na mesma etapa, só uma grava (CA 14).
/// </summary>
public sealed partial class QualityService(
    IProductionDb db, LaborWorkflow workflow, IUserDirectory users, IUserEmployeeLink links, IPermissionChecker permissions, ICurrentUser currentUser,
    INotificationService notifications, IEventPublisher events, INumberSequence numbers, IProjects projects, IParties parties, IItems items,
    ProductionBoardNotifier board, TimeProvider clock)
{
    public const string Permission = "production.approve_quality";
    public const string SelfInspectionType = "production.quality_self_inspection";
    public const string RejectedType = "production.quality_rejected";
    public const string NonConformityLink = "/app/producao/nao-conformidades/";

    public static Error StepNotFound() => Error.NotFound("step_not_found", "Etapa de qualidade não encontrada.");

    public static Error NotPending() => Error.Conflict("quality_not_pending", "Esta etapa de qualidade não está aguardando decisão.");

    /// <summary>Fila de qualidade (CA 2): "aguardando" (padrão: as que eu posso decidir) ou "todas"; a mais antiga primeiro.</summary>
    public async Task<QualityQueueView> QueueAsync(string? filter, CancellationToken cancellationToken = default)
    {
        var ids = await PendingStepIdsAsync(null, cancellationToken);
        var cards = await BuildAsync(ids, cancellationToken);
        var all = cards.Values.OrderBy(c => c.WaitingSince ?? DateTimeOffset.MaxValue).ThenBy(c => c.OrderNumber, StringComparer.Ordinal).ToList();
        var mine = all.Where(c => c.CanDecide).ToList();
        return new QualityQueueView(string.Equals(filter, "todas", StringComparison.Ordinal) ? all : mine, mine.Count, all.Count);
    }

    /// <summary>Etapa de qualidade aberta (CA 3 a 6), só a decidível; fora disso, 404 (IDOR).</summary>
    public async Task<Result<QualityStepDetail>> GetAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        if ((await PendingStepIdsAsync(stepId, cancellationToken)).Count == 0)
        {
            return StepNotFound();
        }

        return await DetailAsync(stepId, cancellationToken);
    }

    /// <summary>Marca um item do checklist da tentativa (CA 3 e 6): "Conforme", "Não conforme", "Não se aplica" ou desmarca.</summary>
    public async Task<Result<QualityStepDetail>> MarkAsync(Guid stepId, Guid itemId, QualityChecklistInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var scope = await LoadAsync(stepId, cancellationToken);
        if (scope.IsFailure)
        {
            return scope.Error!;
        }

        var step = scope.Value.Step;
        var item = await db.ProductionOrderChecklist.FirstOrDefaultAsync(c => c.Id == itemId && c.StepId == stepId && c.Attempt == step.Attempt, cancellationToken);
        if (item is null)
        {
            return Error.NotFound("checklist_item_not_found", "Item do checklist não encontrado nesta etapa.");
        }

        item.Result = input.Result;
        item.MarkedAt = input.Result is null ? null : clock.GetUtcNow();
        item.MarkedBy = input.Result is null ? null : currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(stepId, cancellationToken);
    }

    /// <summary>
    /// "Iniciar inspeção": abre o apontamento do inspetor na etapa de qualidade (as horas da inspeção entram no custo, PRD-04); a OS
    /// continua em Qualidade. Exige o usuário ligado a um funcionário ativo; a decisão fecha o apontamento (CA 3).
    /// </summary>
    public async Task<Result<QualityStepDetail>> StartAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(stepId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var scope = loaded.Value;
        if (currentUser.UserId is not { } me || await links.EmployeeOfUserAsync(me, cancellationToken) is not { } employeeId
            || await workflow.ActiveEmployeeAsync(employeeId, cancellationToken) is null)
        {
            return ShopfloorService.EmployeeNotActive();
        }

        if (await db.LaborEntries.AnyAsync(e => e.EmployeeId == employeeId && e.OrderStepId == stepId && e.EndedAt == null, cancellationToken))
        {
            return await DetailAsync(stepId, cancellationToken);
        }

        if (await workflow.AlreadyOpenAsync(employeeId, cancellationToken) is { } open)
        {
            return open;
        }

        workflow.Open(scope, employeeId, clock.GetUtcNow(), onBehalf: false);
        scope.Step.Status = ProductionStepStatus.InProgress;
        await SettleAsync(scope, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(stepId, cancellationToken);
    }

    /// <summary>
    /// "Aprovar qualidade" (CA 3 a 5): todos os itens "Conforme" ou "Não se aplica"; segregação por tentativa; a etapa vai a <c>Done</c>,
    /// o apontamento do inspetor fecha, a próxima etapa fica pronta ou a OS fica "Pronta para concluir" (nunca <c>Completed</c>), com
    /// <see cref="QualityApproved"/> no outbox e, na autoinspeção, o aviso ao Proprietário na mesma transação.
    /// </summary>
    public async Task<Result<QualityDecisionResult>> ApproveAsync(Guid stepId, QualityApproveInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(stepId, cancellationToken, input.Version);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var scope = loaded.Value;
        var (order, step) = (scope.Order, scope.Step);
        var checklist = await db.ProductionOrderChecklist.AsNoTracking().Where(c => c.StepId == stepId && c.Attempt == step.Attempt).ToListAsync(cancellationToken);
        if (checklist.Any(c => c.Result == ChecklistResult.NonConforming))
        {
            return Error.Validation("quality_non_conforming", "Há item não conforme: reprove a qualidade para abrir a não conformidade.");
        }

        if (checklist.Count(c => c.Result is null) is var missing and > 0)
        {
            return Error.Validation("checklist_incomplete", LaborRules.MissingChecklist(missing));
        }

        var me = currentUser.UserId!.Value;
        var segregation = QualityRules.Segregation(me, await InvolvedAsync(order, cancellationToken), await users.UsersWithPermissionAsync(Permission, cancellationToken));
        if (segregation == QualitySegregation.Forbidden)
        {
            return Error.Forbidden("quality_segregation", QualityRules.SegregationMessage);
        }

        var self = segregation == QualitySegregation.SelfInspection;
        var now = clock.GetUtcNow();
        await CloseInspectionAsync(scope, now, cancellationToken);
        workflow.Complete(scope, now, bySupervisor: false, reason: null, selfApproved: false);
        step.DecidedBy = me;
        step.DecidedAt = now;
        step.SelfInspection = self;
        order.SelfInspection = order.SelfInspection || self;
        db.QualityDecisions.Add(new QualityDecision { OrderId = order.Id, StepId = step.Id, Attempt = order.QualityAttempt, Result = QualityResult.Approved, DecidedBy = me, DecidedAt = now, SelfInspection = self });
        events.Publish(new QualityApproved(order.Id, step.Id, order.QualityAttempt, self));
        if (self)
        {
            var name = await users.DisplayNameAsync(me, cancellationToken) ?? "Um usuário";
            foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
            {
                notifications.Notify(owner, SelfInspectionType, $"Autoinspeção na {order.Number}: {name} aprovou a qualidade do próprio trabalho.", FabricationOrderService.Link + order.Id + "?aba=qualidade");
            }
        }

        await SettleAsync(scope, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var next = scope.Index + 1 < scope.Steps.Count ? scope.Steps[scope.Index + 1] : null;
        var ready = QualityRules.ReadyToComplete(scope.Steps.Select(s => s.Status));
        return new QualityDecisionResult(
            order.Id, order.Number, QualityResult.Approved, order.QualityAttempt, self, next?.Name, next?.Id, ready, await permissions.HasPermissionAsync("production", "approve", cancellationToken),
            null, null, null, self ? "Qualidade aprovada com a marca Autoinspeção" : "Qualidade aprovada");
    }

    /// <summary>Envolvidos na tentativa atual (CA 5): donos dos apontamentos das etapas produtivas desde o início da tentativa.</summary>
    private async Task<HashSet<Guid>> InvolvedAsync(ProductionOrder order, CancellationToken ct)
    {
        var since = order.QualityAttemptStartedAt;
        var employees = await (from e in db.LaborEntries.AsNoTracking()
                               join s in db.ProductionOrderSteps.AsNoTracking() on e.OrderStepId equals s.Id
                               where e.OrderId == order.Id && !s.IsQualityStep && (since == null || e.StartedAt >= since)
                               select e.EmployeeId).Distinct().ToListAsync(ct);
        return await workflow.UsersOfAsync(employees, ct);
    }

    /// <summary>Fecha os apontamentos abertos na etapa de qualidade (o inspetor que iniciou a inspeção), como na PRD-03a.</summary>
    private async Task CloseInspectionAsync(LaborScope scope, DateTimeOffset now, CancellationToken ct)
    {
        var mine = currentUser.UserId is { } me ? await links.EmployeeOfUserAsync(me, ct) : null;
        foreach (var open in await db.LaborEntries.Where(e => e.OrderStepId == scope.Step.Id && e.EndedAt == null).ToListAsync(ct))
        {
            await workflow.CloseAsync(scope, open, now, open.EmployeeId == mine ? LaborCloseKind.Operator : LaborCloseKind.StepCompleted, ct);
        }
    }

    /// <summary>Situação da OS pelas etapas (sem recalcular a etapa pelos apontamentos), versão tocada e aviso do Kanban depois do commit.</summary>
    private async Task SettleAsync(LaborScope scope, CancellationToken ct)
    {
        scope.Order.Status = LaborRules.OrderStatus(scope.Order.Status, scope.Steps.Select(s => new StepState(s.Status, s.IsQualityStep)).ToList());
        db.Entry(scope.Order).Property(o => o.Priority).IsModified = true;
        await board.ChangedAsync(scope.Order.Id, ct);
    }

    /// <summary>Etapa de qualidade decidível, com a OS e as etapas rastreadas; com a versão, conferida (409 se mudou).</summary>
    private async Task<Result<LaborScope>> LoadAsync(Guid stepId, CancellationToken ct, uint? version = null)
    {
        var scope = await workflow.LoadAsync(stepId, null, ct);
        if (scope is null || !scope.Step.IsQualityStep)
        {
            return StepNotFound();
        }

        if (version is { } v)
        {
            if (scope.Order.Version != v)
            {
                return InstallationOrderService.Stale();
            }

            db.Entry(scope.Order).Property(o => o.Version).OriginalValue = v;
        }

        if (!QualityRules.IsDecidable(scope.Order.Status, scope.Step.Status, scope.Step.IsQualityStep))
        {
            return NotPending();
        }

        return scope;
    }

    /// <summary>Etapas de qualidade decidíveis (ou só a pedida), das OS de fabricação abertas.</summary>
    private async Task<List<Guid>> PendingStepIdsAsync(Guid? only, CancellationToken ct) =>
        await (from s in db.ProductionOrderSteps.AsNoTracking()
               join o in db.ProductionOrders.AsNoTracking() on s.OrderId equals o.Id
               where o.Type == ProductionOrderType.Fabrication && o.Status != ProductionOrderStatus.Cancelled && o.Status != ProductionOrderStatus.Completed
                  && s.IsQualityStep && (s.Status == ProductionStepStatus.Ready || s.Status == ProductionStepStatus.InProgress || s.Status == ProductionStepStatus.Paused)
                  && (only == null || s.Id == only)
               select s.Id).ToListAsync(ct);
}