using BemAqui.BuildingBlocks.Agenda;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Purchasing.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// Detector de Compras para a Minha pauta (DSH-04 CA 2): pedido de compra cujo nível atual a pessoa pode aprovar (o mesmo filtro do
/// "Aguardando você" da CMP-03a) e falta de material da OS sem solicitação de compra aberta (CMP-01: "a falta da BOM entra na Minha
/// pauta"). Faltas pelo contrato <see cref="IStockReservations"/> do Estoque; solicitações nas tabelas do próprio módulo.
/// </summary>
public sealed class PurchasingAgendaSource(
    IPurchasingDb db, PurchaseOrderService orders, IStockReservations reservations, IItems items, IProductionOrders productionOrders, IParties parties) : IAgendaSource
{
    public const string OrdersAwaiting = "purchasing.orders_awaiting";
    public const string ShortagesWithoutRequest = "purchasing.shortages_without_request";

    private static readonly AgendaCategory[] Declared =
    [
        new(OrdersAwaiting, "purchasing", "purchasing.approve", AgendaTier.Unblocks, "os pedidos de compra aguardando você"),
        new(ShortagesWithoutRequest, "purchasing", "purchasing.create", AgendaTier.Unblocks, "as faltas sem solicitação"),
    ];

    public string Module => "purchasing";

    public IReadOnlyList<AgendaCategory> Categories => Declared;

    public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var list = new List<AgendaItem?>();
        if (request.Wants(OrdersAwaiting))
        {
            list.Add(await AwaitingAsync(request, cancellationToken));
        }

        if (request.Wants(ShortagesWithoutRequest))
        {
            list.Add(await ShortagesAsync(request, cancellationToken));
        }

        return [.. list.OfType<AgendaItem>()];
    }

    private async Task<AgendaItem?> AwaitingAsync(AgendaRequest request, CancellationToken ct)
    {
        var ids = await orders.AwaitingAsync(request.UserId, ct);
        if (ids.Count == 0)
        {
            return null;
        }

        var rows = await db.PurchaseOrders.AsNoTracking().Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.Number, o.Total, o.ExpectedDate, o.PartyId }).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return null;
        }

        var top = rows.OrderBy(o => o.ExpectedDate).ThenByDescending(o => o.Total).Take(AgendaLimits.EntriesPerCategory).ToList();
        var names = (await parties.FindManyAsync([.. top.Select(o => o.PartyId).Distinct()], ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var first = rows.Min(o => o.ExpectedDate);
        var total = rows.Sum(o => o.Total);
        return new AgendaItem(
            OrdersAwaiting,
            AgendaText.Count(rows.Count, "pedido de compra aguarda você", "pedidos de compra aguardam você"),
            AgendaText.Sentence(AgendaText.Total(total), "o primeiro precisa chegar " + (first < request.Today ? "em " + AgendaText.DayMonth(first, request.Today) : AgendaText.Day(first, request.Today))),
            "/app/compras/pedidos?filtro=aguardando-voce", first, total, AgendaTier.Unblocks, rows.Count, "Aprovar",
            [.. top.Select(o => new AgendaEntry(o.Number, names.GetValueOrDefault(o.PartyId), PurchaseOrderService.Link(o.Id) + "?acao=aprovar", o.ExpectedDate, o.Total))]);
    }

    /// <summary>Falta aberta (OS, item) sem linha de solicitação que não esteja cancelada (Open, Quoting ou Ordered).</summary>
    private async Task<AgendaItem?> ShortagesAsync(AgendaRequest request, CancellationToken ct)
    {
        var shortages = await reservations.OpenShortagesAsync(ct);
        if (shortages.Count == 0)
        {
            return null;
        }

        var orderIds = shortages.Select(s => s.ProductionOrderId).Distinct().ToList();
        var covered = (await db.PurchaseRequestLines.AsNoTracking()
                .Where(l => l.ProductionOrderId != null && orderIds.Contains(l.ProductionOrderId.Value) && l.Status != PurchaseRequestStatus.Cancelled)
                .Select(l => new { OrderId = l.ProductionOrderId!.Value, l.ItemId }).ToListAsync(ct))
            .Select(l => (l.OrderId, l.ItemId)).ToHashSet();
        var open = shortages.Where(s => !covered.Contains((s.ProductionOrderId, s.ItemId))).ToList();
        if (open.Count == 0)
        {
            return null;
        }

        var top = open.Take(AgendaLimits.EntriesPerCategory).ToList();
        var itemNames = (await items.FindAsync([.. top.Select(s => s.ItemId).Distinct()], ct)).ToDictionary(i => i.Id, i => i.Code + " · " + i.Name);
        var orderNumbers = (await productionOrders.FindManyAsync([.. top.Select(s => s.ProductionOrderId).Distinct()], ct)).ToDictionary(o => o.Id, o => o.Number);
        var ordersAffected = open.Select(s => s.ProductionOrderId).Distinct().Count();
        var viewAll = request.Permissions.Has("inventory.view") ? "/app/estoque/faltas" : "/app/compras/solicitacoes";
        return new AgendaItem(
            ShortagesWithoutRequest,
            AgendaText.Count(open.Count, "falta de material está sem solicitação de compra", "faltas de material estão sem solicitação de compra"),
            AgendaText.Sentence(ordersAffected == 1 ? "1 OS espera o material" : $"{ordersAffected} OS esperam o material"),
            viewAll, null, null, AgendaTier.Unblocks, open.Count, "Gerar solicitação",
            [.. top.Select(s => new AgendaEntry(
                itemNames.GetValueOrDefault(s.ItemId) ?? "Item",
                (orderNumbers.TryGetValue(s.ProductionOrderId, out var number) ? number + " · " : string.Empty) + "faltam " + s.ShortQty.ToString("0.####", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")),
                $"/app/compras/solicitacoes/nova?item={s.ItemId}&os={s.ProductionOrderId}", null, null))]);
    }
}
