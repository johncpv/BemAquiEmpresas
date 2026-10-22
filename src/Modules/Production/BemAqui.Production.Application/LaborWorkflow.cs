using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>OS de fabricação e as etapas, rastreadas, para um comando de apontamento.</summary>
public sealed record LaborScope(ProductionOrder Order, List<ProductionOrderStep> Steps, ProductionOrderStep Step)
{
    public int Index => Steps.IndexOf(Step);
}

/// <summary>
/// Efeitos comuns do apontamento (PRD-03a), usados pelo app do operador, pelo supervisor e pela "Concluir etapa" da PRD-02: abrir
/// e fechar apontamento, concluir a etapa e recalcular a situação da etapa e da OS na mesma transação, com o evento
/// <see cref="ProductionStepStarted"/> quando a OS sai de Aberta, <see cref="LaborEntryClosed"/> em cada fechamento e o aviso do
/// Kanban depois do commit. Toda mudança toca a versão (xmin) da OS: dois comandos juntos na mesma OS, só um grava (CA 30).
/// </summary>
public sealed class LaborWorkflow(
    IProductionDb db, IEventPublisher events, INotificationService notifications, IUserDirectory users, IUserEmployeeLink links, IEmployees employees,
    ICurrentUser currentUser, TimeProvider clock, ProductionBoardNotifier board)
{
    public const string OrderLink = FabricationOrderService.Link;
    public const string SelfApprovedType = "production.labor_self_approved";

    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>Etapa da OS de fabricação, com a OS e todas as etapas rastreadas; null se não existe neste tenant (404).</summary>
    public async Task<LaborScope?> LoadAsync(Guid stepId, Guid? orderId, CancellationToken ct)
    {
        var step = await db.ProductionOrderSteps.FirstOrDefaultAsync(s => s.Id == stepId && (orderId == null || s.OrderId == orderId), ct);
        if (step is null)
        {
            return null;
        }

        var order = await db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == step.OrderId && o.Type == ProductionOrderType.Fabrication, ct);
        if (order is null)
        {
            return null;
        }

        var steps = await db.ProductionOrderSteps.Where(s => s.OrderId == order.Id).OrderBy(s => s.Sequence).ToListAsync(ct);
        return new LaborScope(order, steps, steps.Single(s => s.Id == stepId));
    }

    /// <summary>
    /// Pode apontar nesta etapa (CA 8, 9, 16): OS não cancelada nem concluída, etapa que não é de qualidade (PRD-05), pronta, em
    /// andamento ou pausada (vários operadores, CA 14) e sem aguardar aprovação; da segunda etapa em diante, BOM liberada (D12).
    /// O servidor não restringe por centro de trabalho (D11).
    /// </summary>
    public static async Task<Error?> CanStartAsync(LaborScope scope, IBomGate bom, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(bom);
        if (scope.Order.Status == ProductionOrderStatus.Cancelled)
        {
            return Error.Conflict("order_cancelled", "A OS foi cancelada: não recebe apontamento.");
        }

        if (scope.Step.IsQualityStep)
        {
            return Error.Forbidden("quality_step", "A etapa de qualidade é decidida pela inspeção.");
        }

        if (scope.Order.Status == ProductionOrderStatus.Completed || scope.Step.AwaitingApprovalSince is not null
            || scope.Step.Status is not (ProductionStepStatus.Ready or ProductionStepStatus.InProgress or ProductionStepStatus.Paused))
        {
            return Error.Conflict("step_not_ready", "Esta etapa não está pronta para iniciar.");
        }

        if (LaborRules.NeedsBom(scope.Index) && !await bom.IsReleasedAsync(scope.Order.Id, ct))
        {
            return Error.Conflict("bom_not_released", "A BOM desta OS ainda não foi liberada. Fale com a Engenharia.");
        }

        return null;
    }

    /// <summary>Apontamento aberto do funcionário, se houver, com a etapa e a OS para a mensagem do CA 10.</summary>
    public async Task<Error?> AlreadyOpenAsync(Guid employeeId, CancellationToken ct)
    {
        var open = await (from e in db.LaborEntries.AsNoTracking()
                          join s in db.ProductionOrderSteps.AsNoTracking() on e.OrderStepId equals s.Id
                          join o in db.ProductionOrders.AsNoTracking() on e.OrderId equals o.Id
                          where e.EmployeeId == employeeId && e.EndedAt == null
                          select new { s.Name, o.Number }).FirstOrDefaultAsync(ct);
        return open is null ? null : AlreadyOpen(open.Name, open.Number);
    }

    public static Error AlreadyOpen(string step, string order) => Error.Conflict("labor_already_open", $"Você já está na etapa {step} da {order}. Finalize ou pause antes.");

    /// <summary>Abre o apontamento e põe a etapa e a OS em andamento (CA 7). Quem apontou fica gravado (D11).</summary>
    public LaborEntry Open(LaborScope scope, Guid employeeId, DateTimeOffset startedAt, bool onBehalf)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var entry = new LaborEntry
        {
            OrderId = scope.Order.Id, OrderStepId = scope.Step.Id, EmployeeId = employeeId, StartedAt = startedAt, RecordedBy = currentUser.UserId, OnBehalf = onBehalf,
            CreatedAt = Now,
        };
        db.LaborEntries.Add(entry);
        if (scope.Step.ActualStart is null || scope.Step.ActualStart > startedAt)
        {
            scope.Step.ActualStart = startedAt;
        }

        return entry;
    }

    /// <summary>
    /// Fecha o apontamento (CA 13, 14, 20): a pausa aberta fecha junto, <c>paused_minutes</c> = Σ pausas e o
    /// <see cref="LaborEntryClosed"/> vai para o outbox com os minutos líquidos. Devolve os minutos líquidos.
    /// </summary>
    public async Task<int> CloseAsync(LaborScope scope, LaborEntry entry, DateTimeOffset endedAt, LaborCloseKind kind, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(entry);
        var pauses = await PausesOfAsync(entry.Id, ct);
        foreach (var open in pauses.Where(p => p.EndedAt is null))
        {
            open.EndedAt = endedAt < open.StartedAt ? open.StartedAt : endedAt;
        }

        return Close(scope, entry, endedAt, kind, pauses);
    }

    /// <summary>Fecha com as pausas já carregadas (as do apontamento novo do supervisor ainda não estão no banco).</summary>
    public int Close(LaborScope scope, LaborEntry entry, DateTimeOffset endedAt, LaborCloseKind kind, IReadOnlyCollection<StepPause> pauses)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(pauses);
        var spans = pauses.Select(p => new PauseSpan(p.StartedAt, p.EndedAt)).ToList();
        entry.EndedAt = endedAt;
        entry.PausedMinutes = LaborRules.PausedMinutes(spans, endedAt);
        entry.CloseKind = kind;
        entry.ClosedBy = currentUser.UserId;
        var minutes = LaborRules.NetMinutes(entry.StartedAt, endedAt, spans, endedAt);
        events.Publish(new LaborEntryClosed(entry.Id, scope.Order.Id, scope.Order.ProjectId, scope.Step.Id, entry.EmployeeId, minutes));
        return minutes;
    }

    /// <summary>Etapa concluída (CA 13, 20): <c>Done</c> com o fim real e quem concluiu; a próxima fica pronta.</summary>
    public void Complete(LaborScope scope, DateTimeOffset now, bool bySupervisor, string? reason, bool selfApproved)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var step = scope.Step;
        step.ActualStart ??= now;
        step.ActualEnd = now;
        step.Status = ProductionStepStatus.Done;
        step.CompletedAt = now;
        step.CompletedBy = currentUser.UserId;
        step.AwaitingApprovalSince = null;
        step.SelfApproved = step.SelfApproved || selfApproved;
        if (bySupervisor)
        {
            step.CompletedBySupervisor = true;
            step.CompletionReason = reason;
        }

        var index = scope.Index;
        if (index + 1 < scope.Steps.Count && scope.Steps[index + 1].Status == ProductionStepStatus.Waiting)
        {
            scope.Steps[index + 1].Status = ProductionStepStatus.Ready;
        }
    }

    /// <summary>
    /// Situação da etapa iniciada pelos apontamentos abertos (CA 11, 12, 14), a da OS pelas etapas (CA 16, 17) e o evento quando a
    /// OS sai de Aberta (CA 7). Toca a versão da OS e avisa o Kanban depois do commit (CA 23, 30).
    /// </summary>
    public async Task SettleAsync(LaborScope scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var step = scope.Step;
        if (step.Status != ProductionStepStatus.Done)
        {
            // Consulta rastreada (o mesmo objeto do comando, com as mudanças ainda não gravadas) mais os novos do comando.
            var open = (await db.LaborEntries.Where(e => e.OrderStepId == step.Id && e.EndedAt == null).ToListAsync(ct))
                .Concat(db.LaborEntries.Local.Where(e => e.OrderStepId == step.Id)).Distinct().Where(e => e.EndedAt is null).ToList();
            var ids = open.Select(e => e.Id).ToList();
            var paused = (await db.StepPauses.Where(p => ids.Contains(p.LaborEntryId) && p.EndedAt == null).ToListAsync(ct))
                .Concat(db.StepPauses.Local.Where(p => ids.Contains(p.LaborEntryId))).Distinct().Where(p => p.EndedAt is null).Select(p => p.LaborEntryId).ToHashSet();
            var working = ids.Any(id => !paused.Contains(id));
            if (ids.Count > 0 || step.Status is ProductionStepStatus.InProgress or ProductionStepStatus.Paused || step.ActualStart is not null)
            {
                step.Status = LaborRules.StartedStepStatus(working, step.AwaitingApprovalSince is not null);
            }
        }

        var order = scope.Order;
        var before = order.Status;
        order.Status = LaborRules.OrderStatus(before, scope.Steps.Select(s => new StepState(s.Status, s.IsQualityStep)).ToList());
        if (before == ProductionOrderStatus.Open && order.Status != ProductionOrderStatus.Open)
        {
            order.ActualStart ??= Now;
            events.Publish(new ProductionStepStarted(order.Id, order.ProjectId, step.Id, step.ActualStart ?? Now));
        }

        db.Entry(order).Property(o => o.Priority).IsModified = true;
        await board.ChangedAsync(order.Id, ct);
    }

    public async Task<List<StepPause>> PausesOfAsync(Guid entryId, CancellationToken ct) =>
        await db.StepPauses.Where(p => p.LaborEntryId == entryId).OrderBy(p => p.StartedAt).ToListAsync(ct);

    /// <summary>
    /// Usuários envolvidos na etapa (CA 21): quem apontou (<c>recorded_by</c>) e o usuário de cada funcionário com apontamento nela.
    /// </summary>
    public async Task<HashSet<Guid>> InvolvedUsersAsync(Guid stepId, CancellationToken ct)
    {
        var entries = await db.LaborEntries.AsNoTracking().Where(e => e.OrderStepId == stepId).Select(e => new { e.EmployeeId, e.RecordedBy }).ToListAsync(ct);
        return await UsersOfAsync(entries.Select(e => e.EmployeeId).Distinct().ToList(), entries.Where(e => e.RecordedBy is not null).Select(e => e.RecordedBy!.Value), ct);
    }

    public async Task<HashSet<Guid>> UsersOfAsync(IReadOnlyCollection<Guid> employeeIds, IEnumerable<Guid> recorders, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        var involved = recorders.ToHashSet();
        foreach (var employee in employeeIds)
        {
            if (await links.UserOfEmployeeAsync(employee, ct) is { } user)
            {
                involved.Add(user.UserId);
            }
        }

        return involved;
    }

    /// <summary>
    /// Quem apontou não corrige nem aprova a própria etapa (CA 21): sem envolvimento, passa; envolvido, só passa quando não há outro
    /// aprovador elegível (<see cref="SoleOwnerRule"/>), e aí devolve true ("Autoaprovado") e avisa o Proprietário na mesma transação.
    /// </summary>
    public async Task<Result<bool>> SegregationAsync(HashSet<Guid> involved, LaborScope scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(involved);
        ArgumentNullException.ThrowIfNull(scope);
        if (currentUser.UserId is not { } me || !involved.Contains(me))
        {
            return false;
        }

        if (!await SoleOwnerRule.NoOtherEligibleAsync(users, "production.approve", me, involved, ct))
        {
            return Error.Forbidden("self_approval", "Quem fez o apontamento não corrige nem aprova a própria etapa.");
        }

        var name = await users.DisplayNameAsync(me, ct) ?? "Um usuário";
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, SelfApprovedType, $"{name} corrigiu ou aprovou o próprio apontamento na {scope.Order.Number}, etapa {scope.Step.Name}.", OrderLink + scope.Order.Id + "?aba=apontamentos");
        }

        return true;
    }

    /// <summary>Funcionário ativo hoje (CAD-04c1): afastado ou desligado não aponta.</summary>
    public async Task<EmployeeSummary?> ActiveEmployeeAsync(Guid employeeId, CancellationToken ct) =>
        (await employees.FindAsync([employeeId], ct)) is [{ IsActive: true, Status: EmployeeSituation.Active } found] ? found : null;

    /// <summary>Data e hora em São Paulo para as mensagens ("dd/MM HH:mm").</summary>
    public static string Local(DateTimeOffset at) => BusinessTime.ToBusiness(at).ToString("dd/MM HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}