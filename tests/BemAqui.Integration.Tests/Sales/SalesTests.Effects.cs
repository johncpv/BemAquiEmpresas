using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Sales.Application;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Sales;

/// <summary>VND-01 CA 9 a 13: baixa de estoque com custo, recebível por dia e canal, DRE e o contrato de escrita do recebível.</summary>
public partial class SalesTests
{
    [Fact]
    public async Task Baixa_sai_do_local_da_venda_com_custo_idempotente_e_volta_no_cancelamento()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 10, 4.123456m);
        var service = await ProductAsync(type: BemAqui.Inventory.Contracts.ItemType.Service);
        var request = Online(Today.AddDays(-21), location, [Line(product, 2, 15m), Line(product, 1, 15m), Line(service, 1, 5m)], [Paid(methods.Pix, 50m)]);
        (await IngestAsync(request)).IsSuccess.Should().BeTrue();
        await DrainAsync();

        // CA 9: uma perna por item, com origem de venda e rastreio até ela; o serviço não baixa.
        (await OnHandAsync(product, location)).Should().Be(7m);
        var movements = await WithDbAsync(db => db.StockMovements.AsNoTracking().Where(m => m.SourceType == StockSources.Sale && m.SourceId == request.SaleId).ToListAsync());
        movements.Should().ContainSingle().Which.Quantity.Should().Be(-3m);
        movements[0].SourceNumber.Should().Be(request.Number);
        var posting = await WithDbAsync(db => db.SaleStockPostings.AsNoTracking().SingleAsync(p => p.SaleId == request.SaleId));
        posting.Status.Should().Be(SaleStockPostingStatus.Posted);
        posting.StockMovementId.Should().Be(movements[0].Id);

        // CA 10: custo unitário da baixa (decimal(18,6)) na linha.
        var lines = await WithDbAsync(db => db.SaleLines.AsNoTracking().Where(l => l.SaleId == request.SaleId).OrderBy(l => l.LineNumber).ToListAsync());
        lines.Take(2).Should().OnlyContain(l => l.UnitCost == 4.123456m);
        lines[2].UnitCost.Should().BeNull("serviço não tem custo de estoque");
        (await WithDbAsync(db => db.Sales.Where(s => s.Id == request.SaleId).Select(s => s.StockStatus).SingleAsync())).Should().Be(SaleStockStatus.Posted);

        // Idempotente: o evento reentregue (outro handler, outra mensagem) não baixa de novo.
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<SaleStockConsumer>()
                .HandleAsync(new SaleCompleted(request.SaleId, request.Number, SaleChannel.Online, null, null, Today.AddDays(-21), 50m, DateTimeOffset.UtcNow), default);
            await transaction.CommitAsync();
        }

        (await OnHandAsync(product, location)).Should().Be(7m);

        // Cancelamento: estorno pelo movimento original, uma vez só.
        (await CancelAsync(request.SaleId)).IsSuccess.Should().BeTrue();
        await DrainAsync();
        (await OnHandAsync(product, location)).Should().Be(10m);
        (await WithDbAsync(db => db.SaleStockPostings.AsNoTracking().SingleAsync(p => p.SaleId == request.SaleId))).Status.Should().Be(SaleStockPostingStatus.Reversed);
        (await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == "Sale" && a.EntityId == request.SaleId.ToString()).Select(a => a.Changes).ToListAsync()))
            .Count(c => c != null && c.Contains("cancelReason", StringComparison.Ordinal))
            .Should().BeGreaterThan(0, "o cancelamento da venda é auditado");
    }

    [Fact]
    public async Task Sem_saldo_nada_sai_do_estoque_e_a_baixa_fica_com_falta()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 1, 2m);
        var request = Online(Today.AddDays(-22), location, [Line(product, 3, 10m)], [Paid(methods.Pix, 30m)]);
        (await IngestAsync(request)).IsSuccess.Should().BeTrue("a venda concluída entra; o saldo é problema da baixa");
        await DrainAsync();

        (await OnHandAsync(product, location)).Should().Be(1m, "o saldo nunca fica negativo (ADR 0023)");
        (await WithDbAsync(db => db.SaleStockPostings.AsNoTracking().SingleAsync(p => p.SaleId == request.SaleId))).Status.Should().Be(SaleStockPostingStatus.Shortfall);
        (await WithDbAsync(db => db.Sales.Where(s => s.Id == request.SaleId).Select(s => s.StockStatus).SingleAsync())).Should().Be(SaleStockStatus.Shortfall);
    }

    [Fact]
    public async Task Recebivel_por_dia_canal_forma_provedor_e_vencimento_sem_caixa_idempotente_e_refeito_no_cancelamento()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 20, 2m);
        var day = Today.AddDays(-23);

        // Um título manual de antes não muda (regressão da Konebras: colunas novas nulas).
        var manualAccount = await WithDbAsync(db => db.Accounts.Where(a => a.SeedKey == "1.1").Select(a => a.Id).SingleAsync());
        var customer = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Cliente " + Unique("C"), roles = new[] { "Customer" } }));
        var manualRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/finance/receivables")
        {
            Content = JsonContent.Create(new { partyId = customer, description = "Título manual", accountId = manualAccount, costCenterId = (Guid?)null, projectId = (Guid?)null, amount = 123.45m, issueDate = Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), dueDate = Today.AddDays(10).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) }),
        };
        manualRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var pixSale = Online(day, location, [Line(product, 1, 30m)], [Paid(methods.Pix, 30m)]);
        var card1 = Online(day, location, [Line(product, 1, 100m)], [Paid(methods.Credit, 100m, brand: "Visa")]);
        var card2 = Online(day, location, [Line(product, 1, 50m)], [Paid(methods.Credit, 50m, brand: "Elo")]);
        foreach (var sale in new[] { pixSale, card1, card2 })
        {
            (await IngestAsync(sale)).IsSuccess.Should().BeTrue();
        }

        await DrainAsync();
        var receivables = await WithDbAsync(db => db.Receivables.AsNoTracking().Where(r => r.SaleChannel == "Online" && r.IssueDate == day && r.Status != ReceivableStatus.Cancelled).ToListAsync());
        receivables.Should().HaveCount(2, "um por forma, provedor e vencimento no dia e canal");
        var card = receivables.Single(r => r.Origin == DocumentOrigin.CardSettlement);
        card.PartyId.Should().Be(methods.Acquirer);
        card.GrossAmount.Should().Be(150m);
        card.ExpectedFee.Should().Be(4.50m);
        card.NetAmount.Should().Be(145.50m);
        card.Amount.Should().Be(145.50m, "o título vale o líquido");
        card.DueDate.Should().Be(day.AddDays(30));
        card.SettlementDate.Should().Be(day.AddDays(30));
        card.CashSessionId.Should().BeNull("canal sem caixa");
        var pix = receivables.Single(r => r.Origin == DocumentOrigin.Sale);
        (pix.GrossAmount, pix.ExpectedFee, pix.NetAmount, pix.DueDate).Should().Be((30m, 0m, 30m, day));

        // Reprocessar o dia não duplica.
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<SaleReceivablesConsumer>().RecomposeAsync(day, SaleChannel.Online, default);
            await transaction.CommitAsync();
        }

        (await WithDbAsync(db => db.Receivables.CountAsync(r => r.SaleChannel == "Online" && r.IssueDate == day))).Should().Be(2);

        // Venda cancelada sai do grupo; o recebível de venda não se cancela à mão.
        (await CancelAsync(card2.SaleId)).IsSuccess.Should().BeTrue();
        await DrainAsync();
        var after = await WithDbAsync(db => db.Receivables.AsNoTracking().SingleAsync(r => r.Id == card.Id));
        (after.GrossAmount, after.ExpectedFee, after.NetAmount, after.Amount).Should().Be((100m, 3m, 97m, 97m));
        var refused = await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{card.Id}/cancel", new { reason = "teste", version = after.Version });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Contas a receber: filtro "Origem: Vendas" e o canal na lista.
        var page = await JsonAsyncOk(await owner.GetAsync("/api/v1/finance/receivables?origin=vendas&pageSize=100"));
        var items = page.GetProperty("page").GetProperty("items").EnumerateArray().ToList();
        items.Should().Contain(i => i.GetProperty("id").GetGuid() == card.Id && i.GetProperty("saleChannel").GetString() == "Online");
        items.Should().OnlyContain(i => i.GetProperty("origin").GetString() == "Sale" || i.GetProperty("origin").GetString() == "CardSettlement");
        items.Single(i => i.GetProperty("id").GetGuid() == pix.Id).GetProperty("partyName").GetString().Should().Be(ReceivableService.NoPartySales);

        // O título manual segue igual, com as colunas novas nulas.
        var created = await owner.SendAsync(manualRequest);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var manualId = (await JsonAsyncOk2(created)).GetProperty("items")[0].GetProperty("id").GetGuid();
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<SaleReceivablesConsumer>().RecomposeAsync(day, SaleChannel.Online, default);
            await transaction.CommitAsync();
        }

        var untouched = await WithDbAsync(db => db.Receivables.AsNoTracking().SingleAsync(r => r.Id == manualId));
        (untouched.Amount, untouched.Status, untouched.Origin).Should().Be((123.45m, ReceivableStatus.Open, DocumentOrigin.Manual));
        new object?[] { untouched.SaleChannel, untouched.CashSessionId, untouched.SettlementDate, untouched.GrossAmount, untouched.ExpectedFee, untouched.NetAmount, untouched.SaleGroupKey }
            .Should().OnlyContain(v => v == null);
    }

    [Fact]
    public async Task Contrato_de_escrita_aceita_a_sessao_de_caixa_sem_mudar_de_forma_e_nao_duplica()
    {
        var day = Today.AddDays(-24);
        var session = Guid.NewGuid();
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var summary = new SaleReceivableSummary(new SaleReceivableKey(day, "Pos", session, methods.Credit, methods.Acquirer, day.AddDays(30)), DocumentOrigin.CardSettlement,
            methods.Acquirer, "Fechamento do caixa 1", 200m, 6m, 194m);
        Guid id;
        await using (var scope = await ScopeAsync())
        {
            var writer = scope.ServiceProvider.GetRequiredService<ISaleReceivables>();
            var first = await writer.PostAsync(summary);
            first.Value.Created.Should().BeTrue();
            var again = await writer.PostAsync(summary);
            (again.Value.Created, again.Value.Changed, again.Value.ReceivableId).Should().Be((false, false, first.Value.ReceivableId));
            id = first.Value.ReceivableId;
            (await writer.PostAsync(summary with { Net = 1m })).Error!.Code.Should().Be("sale_receivable_amounts");
        }

        var saved = await WithDbAsync(db => db.Receivables.AsNoTracking().SingleAsync(r => r.Id == id));
        (saved.CashSessionId, saved.SaleChannel, saved.Amount).Should().Be((session, "Pos", 194m));
    }

    [Fact]
    public async Task DRE_recebe_a_receita_das_vendas_por_dia_loja_e_canal_sem_contar_o_recebivel()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 20, 2m);
        var day = Today.AddDays(-25);
        (await IngestAsync(Online(day, location, [Line(product, 2, 50m, 10m)], [Paid(methods.Credit, 90m)]))).IsSuccess.Should().BeTrue();
        (await IngestAsync(Online(day, location, [Line(product, 1, 10m)], [Paid(methods.Pix, 10m)]) with { Channel = SaleChannel.ServiceOrder })).IsSuccess.Should().BeTrue();
        await DrainAsync();

        await using var scope = await ScopeAsync();
        var revenue = await scope.ServiceProvider.GetRequiredService<ISalesRevenue>().RevenueAsync(day, day);
        revenue.Should().HaveCount(2);
        var online = revenue.Single(r => r.Channel == SaleChannel.Online);
        (online.Sales, online.Gross, online.Discounts, online.Net).Should().Be((1, 100m, 10m, 90m));

        var account = await scope.ServiceProvider.GetRequiredService<SaleReceivableWriter>().RevenueAccountAsync();
        var routed = await scope.ServiceProvider.GetRequiredService<RevenueRecognitionRouter>().RecognizeAsync(day, day, null);
        routed.Amounts.Where(a => a.Date == day && a.AccountId == account).Sum(a => a.Amount).Should().Be(100m, "90 online + 10 das OS, pelo valor da venda e não pelo líquido da adquirente");
        var documents = await scope.ServiceProvider.GetRequiredService<RevenueRecognitionRouter>().DocumentsAsync([account!.Value], day, day);
        documents.Where(d => d.Kind == SalesRevenueSource.DocumentKind).Sum(d => d.Amount).Should().Be(100m);
        documents.Should().NotContain(d => d.Kind == "Receivable" && d.Date == day && d.Amount == 87.30m, "o recebível do cartão (líquido) não entra como receita");
    }

    [Fact]
    public async Task Forma_usada_em_venda_so_desativa()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 5, 2m);
        (await IngestAsync(Online(Today.AddDays(-26), location, [Line(product, 1, 10m)], [Paid(methods.Cash, 10m)]))).IsSuccess.Should().BeTrue();
        var refused = await owner.DeleteAsync("/api/v1/finance/payment-methods/" + methods.Cash);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(refused)).GetProperty("code").GetString().Should().Be("payment_method_in_use");
        await DrainAsync();
    }

    [Fact]
    public async Task Produto_vendido_nao_e_excluido_no_catalogo_so_desativado()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var sold = await ProductAsync(location, 5, 2m);
        var unsold = await ProductAsync();
        (await IngestAsync(Online(Today.AddDays(-28), location, [Line(sold, 1, 10m)], [Paid(methods.Pix, 10m)]))).IsSuccess.Should().BeTrue();

        // IProductUsageProvider da venda: o Catálogo recusa a exclusão (409 product_in_use) e o produto sem venda sai.
        var refused = await owner.DeleteAsync("/api/v1/catalog/products/" + sold);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(refused)).GetProperty("code").GetString().Should().Be("product_in_use");
        (await owner.DeleteAsync("/api/v1/catalog/products/" + unsold)).IsSuccessStatusCode.Should().BeTrue();
        await DrainAsync();
    }

    [Fact]
    public async Task Porta_do_catalogo_com_fake_vende_a_variacao_e_baixa_pelo_item_ligado()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var item = ItemOf(await ProductAsync(location, 5, 7m));
        var parent = Guid.NewGuid();
        var variation = Guid.NewGuid();
        var factory = tenancy.Derive(b => Microsoft.AspNetCore.TestHost.WebHostBuilderExtensions.ConfigureTestServices(b,
            s => s.AddScoped<BemAqui.Catalog.Contracts.ISellableProducts>(_ => new FakeCatalog(variation, parent, item))));
        var info = (await factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        var request = Online(Today.AddDays(-27), location, [Line(variation, 2, 40m)], [Paid(methods.Pix, 80m)]);
        await using (var scope = await factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var result = await scope.ServiceProvider.GetRequiredService<ISaleIngestion>().IngestAsync(request);
            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            (await scope.ServiceProvider.GetRequiredService<ISaleIngestion>().IngestAsync(request with { SaleId = Guid.CreateVersion7(), Number = Unique("PED"), Lines = [Line(Guid.NewGuid(), 1, 80m)] }))
                .Error!.Message.Should().Contain("Produto não encontrado");
        }

        var line = await WithDbAsync(db => db.SaleLines.AsNoTracking().SingleAsync(l => l.SaleId == request.SaleId));
        (line.ProductId, line.ParentProductId, line.ItemId, line.Description, line.UnitCode).Should().Be((variation, (Guid?)parent, (Guid?)item, "Camiseta básica Azul M", "UN"));
        await DrainAsync();
        (await WithDbAsync(db => db.StockPositions.AsNoTracking().Where(p => p.ItemId == item && p.LocationId == location).Select(p => p.QuantityOnHand).FirstOrDefaultAsync())).Should().Be(3m, "a variação baixa pelo item de estoque ligado");
    }

    /// <summary>Catálogo falso com a forma do <c>ISellableProducts</c> da CAT-01: uma variação de grade ligada a um item de estoque.</summary>
    private sealed class FakeCatalog(Guid variation, Guid parent, Guid item) : BemAqui.Catalog.Contracts.ISellableProducts
    {
        public async Task<BemAqui.Catalog.Contracts.SellableProduct?> FindAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            var found = await FindManyAsync([productId], cancellationToken);
            return found.Count > 0 ? found[0] : null;
        }

        public Task<IReadOnlyList<BemAqui.Catalog.Contracts.SellableProduct>> FindManyAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BemAqui.Catalog.Contracts.SellableProduct>>(productIds.Where(p => p == variation)
                .Select(_ => new BemAqui.Catalog.Contracts.SellableProduct(variation, parent, "Camiseta básica Azul M", Guid.NewGuid(), "UN", item, 80m, Guid.NewGuid(), 3, true, false, null)).ToList());

        public Task<IReadOnlyList<BemAqui.Catalog.Contracts.ProductItemLink>> ItemLinksAsync(IReadOnlyCollection<Guid>? productIds, IReadOnlyCollection<Guid>? itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BemAqui.Catalog.Contracts.ProductItemLink>>([new(variation, item)]);
    }

    private static async Task<System.Text.Json.JsonElement> JsonAsyncOk2(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
}
