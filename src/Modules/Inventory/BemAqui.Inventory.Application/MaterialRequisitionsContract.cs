using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Pedido de material do operador e consulta por OS (EST-03 CA 5 a 7, ponto 57), atrás do <see cref="IMaterialRequisitions"/>. O
/// pedido <b>nunca</b> baixa: grava a requisição <c>Requested</c> com as linhas (a reserva e o local que mais têm o item) e avisa quem
/// tem <c>inventory.create</c>, uma vez por pedido. Nenhum tipo daqui traz custo, valor ou lote (CA 6).
/// </summary>
public sealed class MaterialRequisitionsContract(
    IInventoryDb db, IReleasedBomSource boms, IProductionOrders orders, INumberSequence numbers, IUserDirectory users, INotificationService notifications,
    ICurrentUser currentUser, TenantContextHolder tenant, TimeProvider clock) : IMaterialRequisitions
{
    public const int MaxRequestLines = 30;

    public async Task<Result<OrderMaterialOptions>> OptionsAsync(Guid productionOrderId, Guid? stepId, CancellationToken cancellationToken = default)
    {
        if (NotReleased() is { } blocked)
        {
            return blocked;
        }

        if (await orders.StatusAsync(productionOrderId, cancellationToken) is null)
        {
            return Error.NotFound("production_order_not_found", "OS de fabricação não encontrada.");
        }

        var bom = await boms.ReleasedForOrderAsync(productionOrderId, cancellationToken);
        var rows = await db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == productionOrderId).ToListAsync(cancellationToken);
        var itemIds = (bom?.Lines.Select(l => l.ItemId) ?? []).Distinct().ToList();
        var heads = await HeadsAsync(itemIds, cancellationToken);
        var items = itemIds.Where(heads.ContainsKey).Select(id =>
        {
            var head = heads[id];
            var mine = rows.Where(r => r.ItemId == id).ToList();
            return new MaterialOptionItem(id, head.Code, head.Name, head.Unit, bom!.Lines.Where(l => l.ItemId == id).Sum(l => l.RequiredQty), mine.Sum(r => r.Quantity), mine.Sum(r => r.QuantityIssued));
        }).OrderBy(i => i.ItemCode, StringComparer.Ordinal).ToList();
        var requests = db.MaterialRequisitions.AsNoTracking().Where(r => r.ProductionOrderId == productionOrderId && r.Kind == MaterialRequisitionKind.Issue && r.StepId != null);
        if (stepId is { } step)
        {
            requests = requests.Where(r => r.StepId == step);
        }

        var found = await requests.OrderByDescending(r => r.RequestedAt).Take(20).ToListAsync(cancellationToken);
        return new OrderMaterialOptions(bom is not null, items, await InfosAsync(found, cancellationToken));
    }

    public async Task<Result<MaterialRequestInfo>> RequestAsync(MaterialRequestCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (NotReleased() is { } blocked)
        {
            return blocked;
        }

        var order = await orders.FindAsync(command.ProductionOrderId, cancellationToken);
        if (order is null)
        {
            return Error.NotFound("production_order_not_found", "OS de fabricação não encontrada.");
        }

        if (order.Status is ProductionOrderState.Cancelled or ProductionOrderState.Completed)
        {
            return Error.Conflict("order_closed", "A OS está concluída ou cancelada: não recebe material.");
        }

        if (command.Lines is not { Count: > 0 } || command.Lines.Count > MaxRequestLines)
        {
            return Invalid("lines", $"Escolha de 1 a {MaxRequestLines} itens da BOM.");
        }

        var bom = await boms.ReleasedForOrderAsync(command.ProductionOrderId, cancellationToken);
        if (bom is null)
        {
            return Error.Conflict("bom_not_released", "Esta OS ainda não tem BOM liberada. Avise o supervisor.");
        }

        var bomItems = bom.Lines.Select(l => l.ItemId).ToHashSet();
        var rows = await db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == command.ProductionOrderId).ToListAsync(cancellationToken);
        var defaultLocation = await db.Locations.AsNoTracking().Where(l => l.IsDefault && l.IsActive).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(cancellationToken);
        var lines = new List<(Guid ItemId, decimal Quantity, Guid? ReservationId, Guid LocationId)>();
        for (var i = 0; i < command.Lines.Count; i++)
        {
            var line = command.Lines[i];
            var quantity = RequisitionRules.Round(line.Quantity);
            if (!bomItems.Contains(line.ItemId))
            {
                return Invalid($"lines[{i}].itemId", "Peça só itens da BOM desta OS.");
            }

            if (quantity <= 0)
            {
                return Invalid($"lines[{i}].quantity", "Informe uma quantidade maior que zero.");
            }

            var reservation = rows.Where(r => r.ItemId == line.ItemId).OrderByDescending(r => r.Quantity).ThenByDescending(r => r.RequestedQuantity).FirstOrDefault();
            if ((reservation?.LocationId ?? defaultLocation) is not { } location)
            {
                return Error.Conflict("no_default_location", "O estoque ainda não tem local padrão. Avise o almoxarifado.");
            }

            lines.Add((line.ItemId, quantity, reservation?.Id, location));
        }

        var now = clock.GetUtcNow();
        var requisition = new MaterialRequisition
        {
            Number = await numbers.NextAsync(RequisitionRules.DocType, cancellationToken), Kind = MaterialRequisitionKind.Issue, Status = MaterialRequisitionStatus.Requested,
            ProductionOrderId = order.Id, ProjectId = order.ProjectId, StepId = command.StepId, StepName = Cut(command.StepName, 120), RequestedBy = currentUser.UserId, RequestedAt = now,
        };
        db.MaterialRequisitions.Add(requisition);
        db.MaterialRequisitionLines.AddRange(lines.Select((l, i) => new MaterialRequisitionLine
        {
            RequisitionId = requisition.Id, Sequence = i + 1, ReservationId = l.ReservationId, ItemId = l.ItemId, LocationId = l.LocationId, Quantity = l.Quantity,
            IsExtra = false,
        }));
        await db.SaveChangesAsync(cancellationToken);
        var title = $"Material pedido para a {order.Number}, etapa {command.StepName}, por {command.RequesterName}.";
        foreach (var user in (await users.UsersWithPermissionAsync("inventory.create", cancellationToken)).Where(u => u != currentUser.UserId))
        {
            await notifications.NotifyOnceAsync(user, MaterialRequisitionService.RequestType, title, MaterialRequisitionService.Link + requisition.Id, $"inventory-requisition:{requisition.Id}", cancellationToken);
        }

        return (await InfosAsync([requisition], cancellationToken))[0];
    }

    /// <summary>A situação que o operador vê (CA 7): aguardando, separado ou recusado com o motivo.</summary>
    public static MaterialRequestState StateOf(MaterialRequisitionStatus status) => status switch
    {
        MaterialRequisitionStatus.Completed => MaterialRequestState.Separated,
        MaterialRequisitionStatus.Rejected or MaterialRequisitionStatus.Reversed => MaterialRequestState.Declined,
        _ => MaterialRequestState.Waiting,
    };

    private Error? NotReleased() =>
        tenant.Resolved?.IsReleased(StockReservationService.Module) == true
            ? null
            : Error.Forbidden(Error.Codes.ModuleNotReleased, "Funcionalidade não liberada nessa versão do sistema.");

    private async Task<List<MaterialRequestInfo>> InfosAsync(IReadOnlyList<MaterialRequisition> requisitions, CancellationToken ct)
    {
        var ids = requisitions.Select(r => r.Id).ToList();
        var lines = await db.MaterialRequisitionLines.AsNoTracking().Where(l => ids.Contains(l.RequisitionId)).OrderBy(l => l.Sequence).ToListAsync(ct);
        var heads = await HeadsAsync(lines.Select(l => l.ItemId).Distinct().ToList(), ct);
        return requisitions.Select(r => new MaterialRequestInfo(
            r.Id, r.Number, r.StepId, StateOf(r.Status), r.Status == MaterialRequisitionStatus.Rejected ? r.RejectReason ?? r.DecisionComment : null, r.RequestedAt,
            r.CompletedAt ?? r.DecidedAt,
            lines.Where(l => l.RequisitionId == r.Id).GroupBy(l => l.ItemId).Select(g =>
            {
                var head = heads.GetValueOrDefault(g.Key);
                return new MaterialRequestInfoLine(g.Key, head?.Code ?? string.Empty, head?.Name ?? "Item removido", head?.Unit ?? string.Empty, g.Sum(l => l.Quantity));
            }).ToList())).ToList();
    }

    private sealed record Head(string Code, string Name, string Unit);

    private async Task<Dictionary<Guid, Head>> HeadsAsync(List<Guid> ids, CancellationToken ct) =>
        ids.Count == 0
            ? []
            : await (from i in db.Items.IgnoreQueryFilters().AsNoTracking()
                     join u in db.Units.IgnoreQueryFilters().AsNoTracking() on i.UnitId equals u.Id
                     where ids.Contains(i.Id) && i.Type != ItemType.Service
                     select new { i.Id, i.Code, i.Name, Unit = u.Code }).ToDictionaryAsync(x => x.Id, x => new Head(x.Code, x.Name, x.Unit), ct);

    private static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}
