using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application.Exports;

/// <summary>
/// Exportação das listas de compras (EXP-04 CA 15): a mesma consulta da tela (<c>ListAsync</c> de cada serviço, com o "Mostrar", inclusive
/// "Aguardando você", a situação e a busca), sem paginação e até o teto do <see cref="ListExportService"/>; o total é o <c>total</c> da lista.
/// </summary>
public sealed class PurchasingExportService(
    IPurchasingDb db, PurchaseRequestService requests, RfqService rfqs, PurchaseOrderService orders, GoodsReceiptService receipts, IParties parties, IProjects projects,
    IItems items, IProductionOrders productionOrders, IUserDirectory users)
{
    private static readonly PageRequest All = new();

    public async Task<ExportData<PurchaseRequestExportRow>> RequestsAsync(PurchaseRequestListQuery query, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var list = await requests.ListAsync(query, All, max, ct);
        var rows = list.Items.Select(r => new PurchaseRequestExportRow(
            r.Number, PurchasingExports.RequestOrigins.GetValueOrDefault(r.Origin, r.Origin.ToString()), PurchasingExports.Destination(r.ProjectNumber, r.ProjectName), r.OrderNumber,
            r.LineCount, r.NearestNeededBy, PurchasingExports.RequestStatuses.GetValueOrDefault(r.Status, r.Status.ToString()), Day(r.CreatedAt))).ToList();
        var found = query.ItemId is { } itemId ? await items.FindAsync([itemId], ct) : null;
        var item = found is { Count: > 0 } ? found[0] : null;
        var order = query.ProductionOrderId is { } orderId ? await productionOrders.FindAsync(orderId, ct) : null;
        ExportFilter[] filters =
        [
            new("Mostrar", Show(query.Show)),
            new("Origem", query.Origin is { } o ? PurchasingExports.RequestOrigins.GetValueOrDefault(o) : null),
            new("Situação", query.Status is { } s ? PurchasingExports.RequestStatuses.GetValueOrDefault(s) : null),
            new("Projeto", await ProjectAsync(query.ProjectId, ct)),
            new("Item", item is null ? null : $"{item.Code} {item.Name}"),
            new("OS", order?.Number),
            new("Busca", Term(query.Term)),
        ];
        return new ExportData<PurchaseRequestExportRow>(PurchasingExports.Requests, filters, new ExportRows<PurchaseRequestExportRow>(list.Total, rows));
    }

    public async Task<ExportData<RfqExportRow>> RfqsAsync(RfqListQuery query, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var list = await rfqs.ListAsync(query, All, max, ct);
        var rows = list.Items.Select(r => new RfqExportRow(r.Number, r.LineCount, r.SupplierCount, r.RespondedCount, Day(r.CreatedAt),
            PurchasingExports.RfqStatuses.GetValueOrDefault(r.Status, r.Status.ToString()))).ToList();
        ExportFilter[] filters =
        [
            new("Mostrar", Show(query.Show)),
            new("Situação", query.Status is { } s ? PurchasingExports.RfqStatuses.GetValueOrDefault(s) : null),
            new("Busca", Term(query.Term)),
        ];
        return new ExportData<RfqExportRow>(PurchasingExports.Rfqs, filters, new ExportRows<RfqExportRow>(list.Total, rows));
    }

    /// <summary>Pedidos (CA 15): as colunas da tela, as datas de aprovação e de envio, e a linha "Total" com a soma dos pedidos (não conta no total da lista).</summary>
    public async Task<ExportData<PurchaseOrderExportRow>> OrdersAsync(PurchaseOrderListQuery query, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var list = await orders.ListAsync(query, All, max, ct);
        var ids = list.Items.Select(o => o.Id).ToList();
        var dates = await db.PurchaseOrders.AsNoTracking().Where(o => ids.Contains(o.Id)).Select(o => new { o.Id, o.ApprovedAt, o.SentAt }).ToDictionaryAsync(o => o.Id, ct);
        var rows = list.Items.Select(o =>
        {
            var d = dates.GetValueOrDefault(o.Id);
            return new PurchaseOrderExportRow(o.Number, o.PartyName, PurchasingExports.Destination(o.ProjectNumber, o.ProjectName), o.Total, o.ExpectedDate,
                PurchasingExports.OrderStatuses.GetValueOrDefault(o.Status, o.Status.ToString()), d?.ApprovedAt is { } a ? Day(a) : null, d?.SentAt is { } s ? Day(s) : null);
        }).ToList();
        if (rows.Count > 0)
        {
            rows.Add(new PurchaseOrderExportRow(PurchasingExports.TotalLabel, null, null, rows.Sum(r => r.Total), null, null, null, null));
        }

        ExportFilter[] filters =
        [
            new("Mostrar", Show(query.Show)),
            new("Situação", query.Status is { } st ? PurchasingExports.OrderStatuses.GetValueOrDefault(st) : null),
            new("Fornecedor", await PartyAsync(query.PartyId, ct)),
            new("Projeto", await ProjectAsync(query.ProjectId, ct)),
            new("Busca", Term(query.Term)),
        ];
        return new ExportData<PurchaseOrderExportRow>(PurchasingExports.Orders, filters, new ExportRows<PurchaseOrderExportRow>(list.Total, rows));
    }

    /// <summary>Aba "A receber" (CA 15): os pedidos com saldo, pela previsão de entrega, com o saldo a receber pelo preço do pedido.</summary>
    public async Task<ExportData<ToReceiveExportRow>> ToReceiveAsync(string? term, int max, CancellationToken ct)
    {
        var list = await orders.ListAsync(new PurchaseOrderListQuery(PurchaseOrderService.ToReceive, null, null, null, term), All, max, ct);
        var ids = list.Items.Select(o => o.Id).ToList();
        var lines = await db.PurchaseOrderLines.AsNoTracking().Where(l => ids.Contains(l.OrderId)).Select(l => new { l.OrderId, l.Quantity, l.QuantityReceived, l.UnitPrice }).ToListAsync(ct);
        var rows = list.Items.Select(o => new ToReceiveExportRow(o.Number, o.PartyName, o.ExpectedDate,
            PurchasingExports.OpenAmount(lines.Where(l => l.OrderId == o.Id).Select(l => (l.Quantity, l.QuantityReceived, l.UnitPrice))),
            PurchasingExports.OrderStatuses.GetValueOrDefault(o.Status, o.Status.ToString()))).ToList();
        return new ExportData<ToReceiveExportRow>(PurchasingExports.ToReceive, [new("Busca", Term(term))], new ExportRows<ToReceiveExportRow>(list.Total, rows));
    }

    /// <summary>Aba "Recebidos" (CA 15): número, pedido, fornecedor, data, itens, total, quem recebeu e se foi estornado.</summary>
    public async Task<ExportData<ReceivedExportRow>> ReceivedAsync(GoodsReceiptListQuery query, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var list = await receipts.ListAsync(query, All, max, ct);
        var ids = list.Items.Select(r => r.Id).ToList();
        var counts = await db.GoodsReceiptLines.AsNoTracking().Where(l => ids.Contains(l.ReceiptId)).GroupBy(l => l.ReceiptId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var by = await db.GoodsReceipts.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.ReceivedBy }).ToDictionaryAsync(r => r.Id, r => r.ReceivedBy, ct);
        var names = new Dictionary<Guid, string?>();
        foreach (var user in by.Values.OfType<Guid>().Distinct())
        {
            names[user] = await users.DisplayNameAsync(user, ct);
        }

        var rows = list.Items.Select(r => new ReceivedExportRow(r.Number, r.PurchaseOrderNumber, r.PartyName, r.ReceivedOn, counts.GetValueOrDefault(r.Id), r.Amount,
            by.GetValueOrDefault(r.Id) is { } u ? names.GetValueOrDefault(u) : null, r.Status == GoodsReceiptStatus.Reversed)).ToList();
        var order = query.PurchaseOrderId is { } orderId ? await db.PurchaseOrders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.Number).FirstOrDefaultAsync(ct) : null;
        ExportFilter[] filters =
        [
            new("Situação", query.Status is { } s ? PurchasingExports.ReceiptStatuses.GetValueOrDefault(s) : null),
            new("Fornecedor", await PartyAsync(query.PartyId, ct)),
            new("Pedido", order),
            new("Busca", Term(query.Term)),
        ];
        return new ExportData<ReceivedExportRow>(PurchasingExports.Received, filters, new ExportRows<ReceivedExportRow>(list.Total, rows));
    }

    private static DateOnly Day(DateTimeOffset utc) => DateOnly.FromDateTime(BusinessTime.ToBusiness(utc).DateTime);

    private static string? Show(string? show) => show is null ? null : PurchasingExports.Shows.GetValueOrDefault(show, show);

    private static string? Term(string? term) => string.IsNullOrWhiteSpace(term) ? null : term.Trim();

    private async Task<string?> PartyAsync(Guid? id, CancellationToken ct) =>
        id is { } partyId && await parties.FindAsync(partyId, ct) is { } p ? p.TradeName ?? p.LegalName : null;

    private async Task<string?> ProjectAsync(Guid? id, CancellationToken ct) => id is { } projectId ? (await projects.FindAsync(projectId, ct))?.Number : null;
}
