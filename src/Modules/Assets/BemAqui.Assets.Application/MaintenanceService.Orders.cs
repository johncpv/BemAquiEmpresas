using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Time;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// OS de manutenção (PAT-03a CA 4 a 8): abrir (<c>assets.create</c>, com <c>Idempotency-Key</c> na rota), iniciar, marcar o checklist, concluir e
/// cancelar (<c>assets.edit</c>). Cada comando grava a OS e a situação do bem num <c>SaveChanges</c>; a gravação concorrente cai no <c>xmin</c>
/// (409 <c>concurrency_conflict</c>, CA 8).
/// </summary>
public sealed partial class MaintenanceService
{
    public async Task<Result<MaintenanceOrderDetail>> GetOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await db.MaintenanceOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        return order is null ? OrderNotFound() : await DetailAsync(order, cancellationToken);
    }

    /// <summary>
    /// "Abrir OS de manutenção" (CA 4): nasce <c>Open</c> com o número <c>MAN</c> da sequência na mesma transação; com "máquina parada", o bem
    /// passa a "Em manutenção" na hora. A preventiva do plano (<c>PlanId</c>) copia o checklist do plano e é recusada se o plano já tem OS aberta.
    /// </summary>
    public async Task<Result<MaintenanceOrderDetail>> OpenAsync(MaintenanceOrderInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.AssetId is not { } assetId)
        {
            return AssetService.Invalid("assetId", "Escolha o bem.");
        }

        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);
        if (asset is null)
        {
            return AssetService.Invalid("assetId", "Escolha um bem da lista.");
        }

        if (asset.Status == AssetStatus.Disposed)
        {
            return Error.Conflict(AssetService.DisposedCode, "Bem baixado não recebe OS de manutenção. Reative o bem primeiro.");
        }

        if (input.Type is not { } type || !Enum.IsDefined(type))
        {
            return AssetService.Invalid("type", "Escolha o tipo: corretiva ou preventiva.");
        }

        var problem = AssetRules.Clean(input.Problem);
        if (problem is null || problem.Length > MaintenanceRules.ProblemMax)
        {
            return AssetService.Invalid("problem", type == MaintenanceOrderType.Corrective
                ? "Descreva o problema, com até 1.000 caracteres." : "Descreva o que será feito, com até 1.000 caracteres.");
        }

        var scheduledOn = input.ScheduledOn ?? Today;
        if (scheduledOn.Year < 2000 || scheduledOn > Today.AddYears(5))
        {
            return AssetService.Invalid("scheduledOn", "Informe a data agendada.");
        }

        if (input.SupplierPartyId is { } supplier && !await IsActiveSupplierAsync(supplier, cancellationToken))
        {
            return AssetService.Invalid("supplierPartyId", "Escolha um fornecedor ativo da lista.");
        }

        MaintenancePlan? plan = null;
        if (input.PlanId is { } planId)
        {
            plan = await db.MaintenancePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId && p.AssetId == assetId, cancellationToken);
            if (plan is not { IsActive: true } || type != MaintenanceOrderType.Preventive)
            {
                return AssetService.Invalid("planId", "Escolha um plano ativo do bem para a preventiva.");
            }

            if (await db.MaintenanceOrders.AsNoTracking().Where(o => o.PlanId == planId && (o.Status == MaintenanceOrderStatus.Open || o.Status == MaintenanceOrderStatus.InProgress))
                    .Select(o => o.Number).FirstOrDefaultAsync(cancellationToken) is { } openNumber)
            {
                return Error.Conflict(PlanHasOpenOrderCode, $"O plano já tem a OS {openNumber} aberta. Conclua ou cancele antes de abrir outra.");
            }
        }

        var checklist = plan?.Checklist ?? MaintenanceRules.CleanChecklist(input.Checklist);
        if (ChecklistError(checklist) is { } badChecklist)
        {
            return badChecklist;
        }

        var order = await AddOrderAsync(asset, plan, null, type, problem, input.MachineStopped, scheduledOn, input.SupplierPartyId, checklist, currentUser.UserId, cancellationToken);
        return await DetailAsync(order, cancellationToken);
    }

    /// <summary>
    /// Grava a OS nova com o número <c>MAN</c> e o checklist numa transação; o bem passa a "Em manutenção" com a máquina parada. Usado pela rota e
    /// pelo job do plano (<paramref name="dueKey"/>: uma OS por vencimento, índice único).
    /// </summary>
    internal async Task<MaintenanceOrder> AddOrderAsync(
        Asset asset, MaintenancePlan? plan, string? dueKey, MaintenanceOrderType type, string problem, bool machineStopped, DateOnly scheduledOn, Guid? supplier,
        IReadOnlyList<string> checklist, Guid? by, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = new MaintenanceOrder
        {
            Number = await numbers.NextAsync(DocType, ct), AssetId = asset.Id, PlanId = plan?.Id, DueKey = dueKey, Type = type, Problem = problem, MachineStopped = machineStopped,
            ScheduledOn = scheduledOn, SupplierPartyId = supplier, CreatedBy = by, CreatedAt = clock.GetUtcNow(),
        };
        db.MaintenanceOrders.Add(order);
        for (var i = 0; i < checklist.Count; i++)
        {
            db.MaintenanceOrderChecklist.Add(new MaintenanceOrderChecklistItem { OrderId = order.Id, Sequence = i + 1, Text = checklist[i] });
        }

        if (machineStopped)
        {
            asset.Status = MaintenanceRules.AssetStatusAfter(asset.Status, true);
        }

        events.Publish(new MaintenanceOrderOpened(order.Id, asset.Id, asset.WorkCenterId, machineStopped, scheduledOn));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return order;
    }

    /// <summary>"Iniciar" (CA 5): <c>Open → InProgress</c> com a data e hora de início; o bem passa a "Em manutenção".</summary>
    public async Task<Result<MaintenanceOrderDetail>> StartAsync(Guid id, MaintenanceStartInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (order, asset) = await LoadAsync(id, input.Version, cancellationToken);
        if (order is null)
        {
            return OrderNotFound();
        }

        if (order.Status != MaintenanceOrderStatus.Open)
        {
            return Error.Conflict(StatusCode, "Só a OS aberta é iniciada. Recarregue a página.");
        }

        order.Start(currentUser.UserId, clock.GetUtcNow());
        asset!.Status = MaintenanceRules.AssetStatusAfter(asset.Status, true);
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(order, cancellationToken);
    }

    /// <summary>Marcar o item do checklist (CA 6): só na OS em andamento; o item precisa ser da OS da rota (404).</summary>
    public async Task<Result<MaintenanceOrderDetail>> MarkAsync(Guid id, Guid itemId, MaintenanceChecklistMarkInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = await db.MaintenanceOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        var item = order is null ? null : await db.MaintenanceOrderChecklist.FirstOrDefaultAsync(i => i.Id == itemId && i.OrderId == id, cancellationToken);
        if (order is null || item is null)
        {
            return Error.NotFound(OrderNotFoundCode, "Item do checklist não encontrado.");
        }

        if (order.Status != MaintenanceOrderStatus.InProgress)
        {
            return Error.Conflict(StatusCode, "Inicie a OS para marcar o checklist.");
        }

        if (input.Result is { } result && !Enum.IsDefined(result))
        {
            return AssetService.Invalid("result", "Marque feito ou não se aplica.");
        }

        item.Result = input.Result;
        item.MarkedAt = input.Result is null ? null : clock.GetUtcNow();
        item.MarkedBy = input.Result is null ? null : currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(order, cancellationToken);
    }

    /// <summary>
    /// "Concluir manutenção" (CA 6): com o checklist completo e o que foi feito, a OS fica <c>Done</c>; o bem volta a ativo se nenhuma outra OS o
    /// segura; a preventiva do plano grava a última execução (a data da conclusão e, por horas, o horímetro) e recalcula o próximo vencimento.
    /// </summary>
    public async Task<Result<MaintenanceOrderDetail>> CompleteAsync(Guid id, MaintenanceCompleteInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (order, asset) = await LoadAsync(id, input.Version, cancellationToken);
        if (order is null)
        {
            return OrderNotFound();
        }

        if (order.Status != MaintenanceOrderStatus.InProgress)
        {
            return Error.Conflict(StatusCode, order.Status == MaintenanceOrderStatus.Open ? "Inicie a OS antes de concluir." : "Só a OS em andamento é concluída. Recarregue a página.");
        }

        var workDone = AssetRules.Clean(input.WorkDone);
        if (workDone is null || workDone.Length > MaintenanceRules.WorkDoneMax)
        {
            return AssetService.Invalid("workDone", "Descreva o que foi feito, com até 2.000 caracteres.");
        }

        var missing = MaintenanceRules.Missing(await db.MaintenanceOrderChecklist.AsNoTracking().Where(i => i.OrderId == id).Select(i => i.Result).ToListAsync(cancellationToken));
        if (missing > 0)
        {
            return Error.Validation(ChecklistIncompleteCode, MaintenanceRules.MissingMessage(missing) + ".");
        }

        AssetMeterReading? reading = null;
        if (input.MeterValue is not null)
        {
            (reading, var badReading) = await NewReadingAsync(order.AssetId, input.MeterValue, null, cancellationToken);
            if (badReading is not null)
            {
                return badReading with { Fields = new Dictionary<string, string[]> { ["meterValue"] = [badReading.Message] } };
            }

            db.AssetMeterReadings.Add(reading!);
        }

        var now = clock.GetUtcNow();
        order.Complete(workDone, currentUser.UserId, now);
        var plan = order.PlanId is { } planId ? await db.MaintenancePlans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken) : null;
        if (plan is not null)
        {
            plan.LastDoneAt = DateOnly.FromDateTime(BusinessTime.ToBusiness(now).DateTime);
            if (plan.FrequencyType == MaintenanceFrequency.Hours)
            {
                var current = reading?.Value;
                if (current is null && (await CurrentMetersAsync([order.AssetId], cancellationToken)).TryGetValue(order.AssetId, out var latest))
                {
                    current = latest;
                }

                // Sem leitura nenhuma, o horímetro previsto vale como o da execução (o próximo conta dele).
                plan.LastDoneMeter = current ?? plan.NextDueMeter ?? plan.LastDoneMeter;
            }

            plan.Recalculate(await PointsAsync(order.AssetId, reading, cancellationToken));
        }

        await ReleaseAssetAsync(asset!, order, cancellationToken);
        events.Publish(new MaintenanceOrderCompleted(order.Id, order.AssetId, asset!.WorkCenterId, order.MachineStopped, order.ScheduledOn));
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(order, cancellationToken);
    }

    /// <summary>"Cancelar OS" (CA 7): com o motivo; o bem volta a ativo se nenhuma outra OS o segura; o plano não muda.</summary>
    public async Task<Result<MaintenanceOrderDetail>> CancelAsync(Guid id, MaintenanceCancelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (order, asset) = await LoadAsync(id, input.Version, cancellationToken);
        if (order is null)
        {
            return OrderNotFound();
        }

        if (!MaintenanceRules.IsOpen(order.Status))
        {
            return Error.Conflict(StatusCode, "Só a OS aberta ou em andamento é cancelada. Recarregue a página.");
        }

        var reason = AssetRules.Clean(input.Reason);
        if (reason is null || reason.Length > MaintenanceRules.ReasonMax)
        {
            return AssetService.Invalid("reason", "Informe o motivo, com até 200 caracteres.");
        }

        order.Cancel(reason, currentUser.UserId, clock.GetUtcNow());
        await ReleaseAssetAsync(asset!, order, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(order, cancellationToken);
    }

    /// <summary>A OS concluída ou cancelada solta o bem, a menos que outra OS em andamento (ou aberta com a máquina parada) o segure.</summary>
    private async Task ReleaseAssetAsync(Asset asset, MaintenanceOrder order, CancellationToken ct)
    {
        var others = await db.MaintenanceOrders.AsNoTracking().AnyAsync(o => o.AssetId == asset.Id && o.Id != order.Id
            && (o.Status == MaintenanceOrderStatus.InProgress || (o.Status == MaintenanceOrderStatus.Open && o.MachineStopped)), ct);
        asset.Status = MaintenanceRules.AssetStatusAfter(asset.Status, others || MaintenanceRules.HoldsAsset(order.Status, order.MachineStopped));
    }

    private async Task<(MaintenanceOrder? Order, Asset? Asset)> LoadAsync(Guid id, uint? version, CancellationToken ct)
    {
        var order = await db.MaintenanceOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return (null, null);
        }

        if (version is { } v)
        {
            db.Entry(order).Property(o => o.Version).OriginalValue = v;
        }

        // O bem é lido mesmo se excluído logicamente (a OS antiga continua legível); o bem com OS aberta nunca é excluído (uso do histórico).
        var asset = await db.Assets.IgnoreQueryFilters().FirstAsync(a => a.Id == order.AssetId, ct);
        return (order, asset);
    }

    private async Task<MaintenanceOrderDetail> DetailAsync(MaintenanceOrder o, CancellationToken ct)
    {
        var asset = await db.Assets.IgnoreQueryFilters().AsNoTracking().Where(a => a.Id == o.AssetId).Select(a => new { a.Tag, a.Name, a.Status, a.WorkCenterId }).FirstAsync(ct);
        var items = await db.MaintenanceOrderChecklist.AsNoTracking().Where(i => i.OrderId == o.Id).OrderBy(i => i.Sequence).ToListAsync(ct);
        var plan = o.PlanId is { } p ? await db.MaintenancePlans.AsNoTracking().Where(x => x.Id == p).Select(x => new { x.Description, x.FrequencyType }).FirstOrDefaultAsync(ct) : null;
        var centers = await CenterNamesAsync([asset.WorkCenterId], ct);
        var suppliers = await PartyNamesAsync([o.SupplierPartyId], ct);
        var names = await UserNamesAsync(items.Select(i => i.MarkedBy).Append(o.CreatedBy).Append(o.StartedBy).Append(o.DoneBy), ct);
        string? Name(Guid? user) => user is { } u ? names.GetValueOrDefault(u) : null;
        return new MaintenanceOrderDetail(
            o.Id, o.Number, o.AssetId, asset.Tag, asset.Name, asset.Status, asset.WorkCenterId, asset.WorkCenterId is { } c ? centers.GetValueOrDefault(c) : null,
            o.Type, o.Status, o.Problem, o.MachineStopped, o.ScheduledOn, o.SupplierPartyId, o.SupplierPartyId is { } s ? suppliers.GetValueOrDefault(s) : null,
            o.PlanId, plan?.Description, o.StartedAt, Name(o.StartedBy), o.DoneAt, Name(o.DoneBy), o.WorkDone, o.CancelledAt, o.CancelReason, Name(o.CreatedBy), o.CreatedAt,
            items.Select(i => new MaintenanceChecklistItemView(i.Id, i.Sequence, i.Text, i.Result, i.MarkedAt, Name(i.MarkedBy))).ToList(),
            MaintenanceRules.Missing(items.Select(i => i.Result)), plan?.FrequencyType == MaintenanceFrequency.Hours, o.Version);
    }

    private static Error OrderNotFound() => Error.NotFound(OrderNotFoundCode, "OS de manutenção não encontrada.");
}
