using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// Base dos testes do recebimento (CMP-04) com PostgreSQL real: estoque, títulos, alçadas e o motor são os de produção; a OS e a BOM
/// liberada vêm de fakes da fonte (para a falta da reserva, CA 7), como na EST-02. O pedido é aprovado pela regra padrão "Proprietário
/// aprova tudo": pede o comprador, aprova o Proprietário e recebe o Almoxarifado do seed (<c>purchasing.view</c> + <c>purchasing.receive</c>).
/// </summary>
public abstract class GoodsReceiptTestBase(TenancyFixture tenancy) : PurchaseOrderTestBase(tenancy)
{
    protected const string Buyer = "purchasing:view,create,edit";
    protected const string Receipts = "/api/v1/purchasing/goods-receipts";

    protected static readonly ConcurrentDictionary<Guid, (ProductionOrderState Status, Guid ProjectId, string Number)> Orders = new();

    protected static readonly ConcurrentDictionary<Guid, ReleasedBom> Boms = new();

    protected override void Configure(IServiceCollection services)
    {
        base.Configure(services);
        services.AddScoped<IProductionOrders, FakeOrders>();
        services.AddScoped<IReleasedBomSource, FakeBoms>();
    }

    protected async Task<Guid> LotItemAsync(bool tracksLot)
    {
        var id = await ItemAsync();
        await WithDbAsync(async db =>
        {
            var item = await db.Items.SingleAsync(i => i.Id == id);
            item.TracksLot = tracksLot;
            return await db.SaveChangesAsync();
        });
        return id;
    }

    protected Task<Guid> DefaultLocationAsync() => WithDbAsync(db => db.Locations.Where(l => l.IsDefault).Select(l => l.Id).SingleAsync());

    /// <summary>Pedido aprovado (comprador pede, Proprietário aprova), com frete, IPI (%) e a condição dada.</summary>
    protected async Task<JsonElement> ApprovedOrderAsync(
        HttpClient owner, HttpClient buyer, Guid supplier, decimal freight, decimal ipiPct, string term, Guid? project, params (Guid Item, decimal Quantity, decimal Price, Guid[]? Requests)[] lines)
    {
        var body = new
        {
            partyId = supplier, projectId = project, paymentTermId = await TermAsync(term), expectedDate = Iso(Today.AddDays(20)), freight, ipiPct, notes = (string?)null,
            lines = lines.Select(l => new { itemId = l.Item, quantity = l.Quantity, unitId = (Guid?)null, unitPrice = l.Price, requestLineIds = l.Requests ?? [] }),
        };
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, body));
        var approved = await DecideAsync(owner, order, true);
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        return await JsonAsync(approved);
    }

    protected static Guid LineOf(JsonElement order, Guid item) =>
        order.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("itemId").GetGuid() == item).GetProperty("id").GetGuid();

    protected static object ReceiptBody(JsonElement order, decimal? freight, decimal? ipi, params (Guid PoLine, decimal Quantity, string? Lot)[] lines) => new
    {
        receivedOn = (string?)null, freight, ipiAmount = ipi, notes = (string?)null,
        lines = lines.Select(l => new { poLineId = l.PoLine, quantity = l.Quantity, inSupplierUnit = false, lotNumber = l.Lot, lotExpiry = (string?)null, locationId = (Guid?)null }),
        version = Version(order),
    };

    protected static Task<HttpResponseMessage> ReceiveAsync(HttpClient client, JsonElement order, object body, Guid? key = null) =>
        client.SendAsync(Post($"{Base}/{order.GetProperty("id").GetGuid()}/receipts", body, key));

    protected static async Task<JsonElement> ReceivedAsync(HttpClient client, JsonElement order, object body)
    {
        var response = await ReceiveAsync(client, order, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    /// <summary>Títulos de uma origem direto no banco (o teste não depende da tela do Financeiro).</summary>
    protected Task<List<BemAqui.Finance.Domain.Payable>> PayablesAsync(DocumentOrigin origin, Guid originId) =>
        WithDbAsync(db => db.Payables.AsNoTracking().Where(p => p.Origin == origin && p.OriginId == originId).OrderBy(p => p.Installment).ThenBy(p => p.CreatedAt).ToListAsync());

    protected Task<List<StockMovement>> MovementsAsync(Guid receiptId) =>
        WithDbAsync(db => db.StockMovements.AsNoTracking().Where(m => m.SourceType == StockSources.GoodsReceipt && m.SourceId == receiptId).OrderBy(m => m.PostedSeq).ToListAsync());

    protected Task<decimal> AverageCostAsync(Guid item) => WithDbAsync(db => db.Items.AsNoTracking().Where(i => i.Id == item).Select(i => i.AverageCost).SingleAsync());

    protected static decimal Planned(IEnumerable<BemAqui.Finance.Domain.Payable> payables) => payables.Where(p => p.Status != PayableStatus.Cancelled).Sum(p => p.Amount);

    /// <summary>OS conhecida pelo fake com a BOM liberada pedindo o item (a falta nasce na reconciliação, EST-02).</summary>
    protected async Task<Guid> ShortageAsync(Guid item, decimal quantity)
    {
        var order = Guid.CreateVersion7();
        Orders[order] = (ProductionOrderState.Open, Guid.CreateVersion7(), "OS-2026-" + Random.Shared.Next(10000, 99999).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Boms[order] = new ReleasedBom(Guid.CreateVersion7(), 1, DateTimeOffset.UtcNow.AddMinutes(-30), [new ReleasedBomLine(Guid.CreateVersion7(), item, quantity)]);
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<StockReservationService>().ReconcileOrderAsync(order, "Evento BomReleased · teste");
        await transaction.CommitAsync();
        return order;
    }

    private sealed class FakeOrders : IProductionOrders
    {
        public Task<ProductionOrderInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.TryGetValue(id, out var o) ? new ProductionOrderInfo(id, o.Number, ProductionOrderKind.Fabrication, o.ProjectId, Guid.Empty, null, 1m, o.Status, null, null) : null);

        public async Task<IReadOnlyList<ProductionOrderInfo>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        {
            var found = new List<ProductionOrderInfo>();
            foreach (var id in ids)
            {
                if (await FindAsync(id, cancellationToken) is { } info)
                {
                    found.Add(info);
                }
            }

            return found;
        }

        public Task<IReadOnlyList<ProductionOrderInfo>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductionOrderInfo>>([]);

        public Task<ProductionOrderState?> StatusAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.TryGetValue(id, out var o) ? o.Status : (ProductionOrderState?)null);

        public async Task<IReadOnlyList<ProductionOrderInfo>> OpenAsync(CancellationToken cancellationToken = default) =>
            await FindManyAsync(Orders.Where(o => o.Value.Status is not (ProductionOrderState.Completed or ProductionOrderState.Cancelled)).Select(o => o.Key).ToList(), cancellationToken);
    }

    private sealed class FakeBoms : IReleasedBomSource
    {
        public Task<ReleasedBom?> ReleasedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Boms.TryGetValue(productionOrderId, out var bom) ? bom : null);

        public Task<IReadOnlyList<Guid>> OpenOrdersWithReleasedBomAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(Boms.Keys.Where(k => Orders.TryGetValue(k, out var o) && o.Status is not (ProductionOrderState.Cancelled or ProductionOrderState.Completed)).ToList());
    }
}
