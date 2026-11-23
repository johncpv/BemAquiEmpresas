using System.Net;
using System.Net.Http.Json;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Sales.Application;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Sales;

/// <summary>
/// PDV-05 (opção B da PP-02): a venda do caixa nunca é recusada por saldo; a baixa roda fora do caminho do caixa, sai o que há e a falta vira
/// divergência no Estoque, aplicada depois ou fechada pelo cancelamento. O motor e o <c>CHECK</c> do saldo não mudam.
/// </summary>
public partial class SalesTests
{
    /// <summary>Venda do caixa já concluída, com caixa e sessão (a OFF-02 entrega assim).</summary>
    private static SaleIngestionRequest Register(DateOnly day, Guid location, IReadOnlyList<SaleLineInput> lines, IReadOnlyList<SalePaymentInput> payments) =>
        new(Guid.CreateVersion7(), SaleChannel.Pos, Unique("01-1"), "PosDevice", null, NoonOf(day), lines, payments, RegisterId: Guid.NewGuid(), CashSessionId: Guid.NewGuid(),
            StockLocationId: location, FromDevice: true);

    private async Task<HttpResponseMessage> ApplyShortfallAsync(HttpClient client, Guid product, Guid location)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/shortfalls/apply") { Content = JsonContent.Create(new { itemId = ItemOf(product), locationId = location }) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    private Task<List<StockShortfall>> ShortfallsAsync(Guid saleId) =>
        WithDbAsync(db => db.StockShortfalls.AsNoTracking().Where(s => s.SourceType == StockSources.Sale && s.SourceId == saleId).ToListAsync());

    [Fact]
    public async Task Venda_do_caixa_sem_saldo_baixa_o_que_tem_e_a_falta_vira_divergencia_sem_recusar()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 1, 2.5m);
        var request = Register(Today.AddDays(-31), location, [Line(product, 3, 10m)], [Paid(methods.Cash, 30m)]);
        (await IngestAsync(request)).IsSuccess.Should().BeTrue("a venda do caixa entra sem olhar o saldo");
        (await WithDbAsync(db => db.StockMovements.CountAsync(m => m.SourceId == request.SaleId))).Should().Be(0, "a baixa não roda na transação da venda (CA 2)");
        await DrainAsync();

        // CA 1, 5 e 6B: sai o que há, o saldo nunca fica negativo e o restante fica aberto, com a venda válida e em falta.
        (await OnHandAsync(product, location)).Should().Be(0m);
        var movement = await WithDbAsync(db => db.StockMovements.AsNoTracking().SingleAsync(m => m.SourceType == StockSources.Sale && m.SourceId == request.SaleId));
        (movement.Quantity, movement.SourceNumber).Should().Be((-1m, request.Number));
        var shortfall = (await ShortfallsAsync(request.SaleId)).Should().ContainSingle().Subject;
        (shortfall.Quantity, shortfall.Status, shortfall.LocationId, shortfall.SourceNumber).Should().Be((2m, StockShortfallStatus.Open, location, request.Number));
        var sale = await WithDbAsync(db => db.Sales.AsNoTracking().SingleAsync(s => s.Id == request.SaleId));
        (sale.Status, sale.StockStatus).Should().Be((SaleStatus.Completed, SaleStockStatus.Shortfall));
        var posting = await WithDbAsync(db => db.SaleStockPostings.AsNoTracking().SingleAsync(p => p.SaleId == request.SaleId));
        (posting.Status, posting.ShortfallQuantity, posting.StockMovementId).Should().Be((SaleStockPostingStatus.Shortfall, 2m, movement.Id));

        // CA 8: o custo da baixa (decimal(18,6)) na linha, para a margem.
        (await WithDbAsync(db => db.SaleLines.AsNoTracking().SingleAsync(l => l.SaleId == request.SaleId))).UnitCost.Should().Be(2.5m);

        // CA 3: o evento entregue de novo não baixa nem abre falta de novo.
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<SaleStockConsumer>()
                .HandleAsync(new SaleCompleted(request.SaleId, request.Number, SaleChannel.Pos, null, request.CashSessionId, Today.AddDays(-31), 30m, DateTimeOffset.UtcNow), default);
            await transaction.CommitAsync();
        }

        (await WithDbAsync(db => db.StockMovements.CountAsync(m => m.SourceId == request.SaleId))).Should().Be(1);
        (await ShortfallsAsync(request.SaleId)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Aplicar_baixa_lanca_a_falta_depois_de_regularizar_e_a_venda_fica_baixada()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location);
        var request = Register(Today.AddDays(-32), location, [Line(product, 2, 10m)], [Paid(methods.Cash, 20m)]);
        (await IngestAsync(request)).IsSuccess.Should().BeTrue();
        await DrainAsync();

        // CA 7: a divergência aparece por local e item, com a venda ligada.
        var list = await (await owner.GetAsync($"/api/v1/inventory/shortfalls?locationId={location}")).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var row = list.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("itemId").GetGuid() == ItemOf(product));
        row.GetProperty("quantity").GetDecimal().Should().Be(2m);
        row.GetProperty("sales")[0].GetProperty("sourceId").GetGuid().Should().Be(request.SaleId);

        // Sem saldo, aplicar não lança nada.
        var refused = await ApplyShortfallAsync(owner, product, location);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await OnHandAsync(product, location)).Should().Be(0m);

        // Regularizado o estoque, "Aplicar baixa" lança o restante pela origem da falta e a venda fica baixada, com o custo da baixa.
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await scope.ServiceProvider.GetRequiredService<IStockMovements>()
                .PostAsync(new StockPosting(StockMovementType.Receipt, ItemOf(product), location, null, 5m, 3m, StockSources.GoodsReceipt, Guid.NewGuid()))).IsSuccess.Should().BeTrue();
            await transaction.CommitAsync();
        }

        var applied = await ApplyShortfallAsync(owner, product, location);
        applied.StatusCode.Should().Be(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        await DrainAsync();
        (await OnHandAsync(product, location)).Should().Be(3m);
        var shortfall = (await ShortfallsAsync(request.SaleId)).Single();
        shortfall.Status.Should().Be(StockShortfallStatus.Applied);
        (await WithDbAsync(db => db.StockMovements.AsNoTracking().SingleAsync(m => m.Id == shortfall.MovementId))).SourceType.Should().Be(StockSources.StockShortfall);
        (await WithDbAsync(db => db.Sales.Where(s => s.Id == request.SaleId).Select(s => s.StockStatus).SingleAsync())).Should().Be(SaleStockStatus.Posted);
        (await WithDbAsync(db => db.SaleLines.AsNoTracking().SingleAsync(l => l.SaleId == request.SaleId))).UnitCost.Should().Be(3m);
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == nameof(StockShortfall) && a.EntityId == shortfall.Id.ToString()))).Should().BeGreaterThan(0, "a resolução é auditada");
    }

    [Fact]
    public async Task Cancelamento_da_venda_do_caixa_fecha_a_falta_antes_de_dar_entrada()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 1, 2m);
        var request = Register(Today.AddDays(-33), location, [Line(product, 3, 10m)], [Paid(methods.Cash, 30m)]);
        (await IngestAsync(request)).IsSuccess.Should().BeTrue();
        await DrainAsync();

        // CA 9: só volta ao estoque o que saiu (1); a falta de 2 fecha sem movimento.
        (await CancelAsync(request.SaleId)).IsSuccess.Should().BeTrue();
        await DrainAsync();
        (await OnHandAsync(product, location)).Should().Be(1m);
        (await ShortfallsAsync(request.SaleId)).Single().Status.Should().Be(StockShortfallStatus.Cancelled);
        (await WithDbAsync(db => db.SaleStockPostings.AsNoTracking().SingleAsync(p => p.SaleId == request.SaleId))).Status.Should().Be(SaleStockPostingStatus.Reversed);
        (await WithDbAsync(db => db.Sales.Where(s => s.Id == request.SaleId).Select(s => s.StockStatus).SingleAsync())).Should().Be(SaleStockStatus.Reversed);
    }
}
