using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Commercial.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Requisição de material da OS (EST-03, ADR 0023 §1): baixa, envio para aprovação, aprovação, confirmação e recusa do pedido do
/// operador, devolução, perda e estorno. O efeito de estoque é a chamada síncrona ao <see cref="IStockMovements"/> na transação da
/// requisição; a reserva da OS é baixada na mesma transação pelo <see cref="ReservationAllocator"/> (só a origem
/// <c>MaterialRequisition</c> tira do reservado); o custo do projeto vai pelo <see cref="StockIssued"/>/<see cref="StockReturned"/>
/// que o motor publica no outbox (ADR 0025 §2). Ordem das travas: a da OS (a mesma da reconciliação), depois os itens em ordem
/// crescente de <c>item_id</c>, as posições e os lotes (ADR 0023 §3).
/// </summary>
public sealed partial class MaterialRequisitionService(
    IInventoryDb db, IStockMovements ledger, ReservationAllocator allocator, StockReservationService reservations, INumberSequence numbers, IProductionOrders orders,
    IProjects projects, IUserDirectory users, IPermissionChecker permissions, INotificationService notifications, ICurrentUser currentUser, TimeProvider clock)
{
    public const string Link = "/app/estoque/requisicoes/";
    public const string ApprovalType = "inventory.requisition_pending";
    public const string DecisionType = "inventory.requisition_decided";
    public const string RequestType = "inventory.requisition_requested";
    public const string SelfApprovedType = "inventory.self_approved";

    /// <summary>
    /// "Baixar material" (CA 2 e 3) ou "Enviar para aprovação" (CA 4): na mesma transação nasce a requisição <c>Completed</c> com um
    /// <c>Issue</c> por linha, ou <c>PendingApproval</c> sem baixar nada quando o consumo acima da BOM passa do limite.
    /// </summary>
    public async Task<Result<RequisitionDetail>> IssueAsync(Guid productionOrderId, RequisitionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = await OpenOrderAsync(productionOrderId, cancellationToken);
        if (order.IsFailure)
        {
            return order.Error!;
        }

        var lines = await ValidateLinesAsync(productionOrderId, input.Lines, cancellationToken);
        if (lines.IsFailure)
        {
            return lines.Error!;
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await reservations.LockOrderAsync(productionOrderId, cancellationToken);
        var requisition = new MaterialRequisition
        {
            Number = await numbers.NextAsync(RequisitionRules.DocType, cancellationToken), Kind = MaterialRequisitionKind.Issue, Status = MaterialRequisitionStatus.Completed,
            ProductionOrderId = productionOrderId, ProjectId = order.Value.ProjectId, RequestedBy = currentUser.UserId, RequestedAt = now, CompletedBy = currentUser.UserId,
        };
        db.MaterialRequisitions.Add(requisition);
        var entities = AddLines(requisition, lines.Value, 1);
        var executed = await ExecuteIssueAsync(requisition, entities, evaluateApproval: true, order.Value.Number, cancellationToken);
        if (executed.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return executed.Error!;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(requisition.Id, cancellationToken);
    }

    /// <summary>
    /// A baixa propriamente dita, na transação aberta e já com a trava da OS: trava os itens, relê as reservas, separa o que sai da
    /// reserva do excedente (CA 3), aplica a regra do valor (CA 4, só no envio), baixa a reserva e o físico e reconcilia as reservas da
    /// OS pela fonte (ADR 0023 §6). Falta de disponível para o excedente: 409 <c>insufficient_stock</c> e nada é gravado.
    /// </summary>
    private async Task<Result> ExecuteIssueAsync(MaterialRequisition requisition, List<MaterialRequisitionLine> lines, bool evaluateApproval, string? orderNumber, CancellationToken ct)
    {
        var itemIds = lines.Select(l => l.ItemId).Distinct().Order().ToList();
        foreach (var itemId in itemIds)
        {
            await allocator.LockItemAsync(itemId, ct);
        }

        // Lida depois da trava dos itens: a reserva só muda sob ela (CA 16).
        var reservationIds = lines.Where(l => l.ReservationId is not null).Select(l => l.ReservationId!.Value).Distinct().ToList();
        var reserved = await db.Reservations.AsNoTracking().Where(r => reservationIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Quantity, ct);
        var demands = lines.Select(l => new RequisitionDemand(l.ReservationId, l.ItemId, l.Quantity)).ToList();
        var excess = RequisitionRules.Excess(demands, reserved);
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].OverQuantity = excess[i].Over;
            if (excess[i].Over > 0 && lines[i].OverReason is null)
            {
                return Error.Conflict("reservation_changed", "A reserva mudou desde que você abriu: recarregue e confira as quantidades.");
            }
        }

        if (evaluateApproval)
        {
            var costs = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.AverageCost, ct);
            requisition.OverBomValue = RequisitionRules.OverBomValue(demands, excess, costs);
            var limit = (await db.InventoryParameters.AsNoTracking().FirstOrDefaultAsync(ct))?.ManualApprovalAmount ?? InventoryParameters.DefaultApprovalAmount;
            if (RequisitionRules.NeedsApproval(requisition.OverBomValue, limit))
            {
                requisition.Status = MaterialRequisitionStatus.PendingApproval;
                requisition.CompletedAt = null;
                await db.SaveChangesAsync(ct);
                await NotifyApproversAsync(requisition, orderNumber, ct);
                return Result.Success();
            }
        }

        var order = new ReservationOrder(requisition.ProductionOrderId, requisition.ProjectId);
        var origin = ReservationOrigins.Requisition(requisition.Number);
        foreach (var itemId in itemIds)
        {
            var mine = Enumerable.Range(0, lines.Count).Where(i => lines[i].ItemId == itemId).ToList();
            var parts = mine.GroupBy(i => lines[i].LocationId)
                .Select(g => new ReservationIssue(g.Key, g.Sum(i => excess[i].FromReservation), g.Sum(i => lines[i].Quantity))).ToList();
            var positions = await allocator.IssueAsync(order, itemId, parts, origin, ct);
            foreach (var group in mine.GroupBy(i => lines[i].LocationId))
            {
                var over = group.Sum(i => excess[i].Over);
                var position = positions.FirstOrDefault(p => p.LocationId == group.Key);
                if (over > 0 && (position?.Available ?? 0m) < over)
                {
                    return await InsufficientAsync(itemId, group.Key, Math.Max(0m, position?.Available ?? 0m), ct);
                }
            }
        }

        var postings = lines.Select(l => new StockPosting(
            StockMovementType.Issue, l.ItemId, l.LocationId, l.LotId, -l.Quantity, null, StockSources.Requisition, l.Id, 1, null, requisition.ProjectId, requisition.ProductionOrderId,
            Reason: l.OverReason is { } reason ? RequisitionRules.OverReasonText(reason) : null, SourceNumber: requisition.Number)).ToList();
        var posted = await ledger.PostManyAsync(postings, ct);
        if (posted.IsFailure)
        {
            return posted.Error!;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].MovementId = posted.Value[i].MovementId;
        }

        requisition.Status = MaterialRequisitionStatus.Completed;
        requisition.CompletedBy ??= currentUser.UserId;
        requisition.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await reservations.ReconcileOrderAsync(requisition.ProductionOrderId, origin, cancellationToken: ct);
        return Result.Success();
    }

    /// <summary>Linhas validadas (CA 2, 3 e 15), na ordem enviada; as entidades nascem só depois das validações.</summary>
    private sealed record ValidLine(Guid? ReservationId, Guid ItemId, Guid LocationId, Guid? LotId, decimal Quantity, string? OverReason, bool IsExtra);

    private List<MaterialRequisitionLine> AddLines(MaterialRequisition requisition, IReadOnlyList<ValidLine> lines, int firstSequence)
    {
        var entities = lines.Select((l, i) => new MaterialRequisitionLine
        {
            RequisitionId = requisition.Id, Sequence = firstSequence + i, ReservationId = l.ReservationId, ItemId = l.ItemId, LocationId = l.LocationId, LotId = l.LotId,
            Quantity = l.Quantity, OverReason = l.OverReason, IsExtra = l.IsExtra,
        }).ToList();
        db.MaterialRequisitionLines.AddRange(entities);
        return entities;
    }

    /// <summary>
    /// Validação das linhas (CA 2, 3 e 15): item com estoque e ativo, local ativo, lote do item com saldo no local (outro item ou local:
    /// 422), reserva da própria OS (de outra OS: 404, IDOR) e do mesmo item; a reserva de outro local não conta (a linha vira excedente);
    /// o excedente sobre o reservado de agora exige o motivo.
    /// </summary>
    private async Task<Result<List<ValidLine>>> ValidateLinesAsync(Guid orderId, IReadOnlyList<RequisitionLineInput>? input, CancellationToken ct)
    {
        if (input is not { Count: > 0 })
        {
            return Invalid("lines", "Inclua ao menos uma linha com quantidade.");
        }

        if (input.Count > RequisitionRules.MaxLines)
        {
            return Invalid("lines", $"Use até {RequisitionRules.MaxLines} linhas por requisição.");
        }

        var itemIds = input.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var locationIds = input.Select(l => l.LocationId).Distinct().ToList();
        var locations = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var orderRows = await db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == orderId).ToListAsync(ct);
        var valid = new List<ValidLine>();
        for (var i = 0; i < input.Count; i++)
        {
            var line = input[i];
            var field = $"lines[{i}]";
            var quantity = RequisitionRules.Round(line.Quantity);
            if (quantity <= 0)
            {
                return Invalid(field + ".quantity", "Informe uma quantidade maior que zero.");
            }

            if (!items.TryGetValue(line.ItemId, out var item))
            {
                return Invalid(field + ".itemId", "Item não encontrado.");
            }

            if (!item.IsActive || item.Type == ItemType.Service)
            {
                return Invalid(field + ".itemId", item.Type == ItemType.Service ? "Serviço não tem estoque." : "Item inativo não movimenta estoque.");
            }

            if (!locations.TryGetValue(line.LocationId, out var location) || !location.IsActive)
            {
                return Invalid(field + ".locationId", location is null ? "Local não encontrado." : "Local inativo não movimenta estoque.");
            }

            var lot = await LotAsync(item, line.LocationId, line.LotId, field, ct);
            if (lot.IsFailure)
            {
                return lot.Error!;
            }

            Guid? reservationId = null;
            if (line.ReservationId is { } requested)
            {
                var row = orderRows.FirstOrDefault(r => r.Id == requested);
                if (row is null)
                {
                    return Error.NotFound("reservation_not_found", "Reserva não encontrada nesta OS.");
                }

                if (row.ItemId != line.ItemId)
                {
                    return Invalid(field + ".reservationId", "A reserva é de outro item.");
                }

                reservationId = row.LocationId == line.LocationId ? row.Id : null;
            }

            var reason = Clean(line.OverReason);
            if (reason is { Length: > RequisitionRules.MaxReason })
            {
                return Invalid(field + ".overReason", $"Escreva o motivo com até {RequisitionRules.MaxReason} caracteres.");
            }

            valid.Add(new ValidLine(reservationId, line.ItemId, line.LocationId, lot.Value, quantity, reason, orderRows.All(r => r.ItemId != line.ItemId)));
        }

        // CA 3: o excedente sobre o reservado de agora pede o motivo (a regra é conferida de novo sob a trava, na baixa).
        var reserved = orderRows.ToDictionary(r => r.Id, r => r.Quantity);
        var excess = RequisitionRules.Excess(valid.Select(v => new RequisitionDemand(v.ReservationId, v.ItemId, v.Quantity)).ToList(), reserved);
        for (var i = 0; i < valid.Count; i++)
        {
            if (excess[i].Over > 0 && valid[i].OverReason is null)
            {
                return Invalid($"lines[{i}].overReason", "Informe o motivo do consumo acima da BOM.");
            }
        }

        return valid;
    }

    /// <summary>Lote da linha (CA 15): obrigatório no item com lote, do mesmo item e com saldo registrado no local; senão 422.</summary>
    private async Task<Result<Guid?>> LotAsync(Item item, Guid locationId, Guid? lotId, string field, CancellationToken ct)
    {
        if (!item.TracksLot)
        {
            return lotId is null ? (Guid?)null : Invalid(field + ".lotId", "Este item não controla lote.");
        }

        if (lotId is not { } id)
        {
            return Invalid(field + ".lotId", "Este item controla lote: escolha o lote.");
        }

        if (!await db.Lots.AnyAsync(l => l.Id == id && l.ItemId == item.Id, ct))
        {
            return Invalid(field + ".lotId", "Lote não encontrado neste item.");
        }

        return await db.StockBalances.AnyAsync(b => b.ItemId == item.Id && b.LocationId == locationId && b.LotId == id, ct)
            ? id
            : Invalid(field + ".lotId", "Este lote não está neste local.");
    }

    /// <summary>OS de fabricação que ainda recebe material (CA 2): concluída ou cancelada, 409; inexistente (ou de outro tenant), 404.</summary>
    private async Task<Result<ProductionOrderInfo>> OpenOrderAsync(Guid orderId, CancellationToken ct)
    {
        var order = await orders.FindAsync(orderId, ct);
        if (order is null)
        {
            return OrderNotFound();
        }

        return order.Status is ProductionOrderState.Cancelled or ProductionOrderState.Completed
            ? Error.Conflict("order_closed", "A OS está concluída ou cancelada: não recebe material. Devolva o que sobrou ao estoque.")
            : order;
    }

    /// <summary>Aviso a quem aprova no Estoque, menos o autor (CA 4, R-PRP-5), uma vez por requisição.</summary>
    private async Task NotifyApproversAsync(MaterialRequisition requisition, string? orderNumber, CancellationToken ct)
    {
        var title = $"Requisição {requisition.Number} da {orderNumber ?? "OS"} aguardando aprovação: {ManualPostingRules.Money(requisition.OverBomValue)} acima da BOM.";
        var author = requisition.CompletedBy ?? requisition.RequestedBy;
        foreach (var user in (await users.UsersWithPermissionAsync("inventory.approve", ct)).Where(u => u != author))
        {
            await notifications.NotifyOnceAsync(user, ApprovalType, title, Link + requisition.Id, $"inventory-requisition-approval:{requisition.Id}", ct);
        }
    }

    private async Task NotifySelfApprovedAsync(string what, string link, CancellationToken ct)
    {
        var name = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, ct) ?? "Um usuário" : "Um usuário";
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, SelfApprovedType, $"{name} {what} ({SoleOwnerRule.SelfApprovedLabel}).", link);
        }
    }

    private async Task<Error> InsufficientAsync(Guid itemId, Guid locationId, decimal available, CancellationToken ct)
    {
        var item = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => i.Id == itemId)
            .Select(i => new { i.Name, Unit = db.Units.IgnoreQueryFilters().Where(u => u.Id == i.UnitId).Select(u => u.Code).FirstOrDefault() }).FirstAsync(ct);
        var location = await db.Locations.IgnoreQueryFilters().AsNoTracking().Where(l => l.Id == locationId).Select(l => l.Name).FirstOrDefaultAsync(ct) ?? "o local";
        return StockLedger.Insufficient(available, item.Unit ?? string.Empty, item.Name, location);
    }

    internal static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    internal static Error NotFound() => Error.NotFound("requisition_not_found", "Requisição não encontrada.");

    private static Error OrderNotFound() => Error.NotFound("production_order_not_found", "OS de fabricação não encontrada.");

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
