using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Contracts;
using BemAqui.Purchasing.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-03a: pedido de compra com a regra padrão "Proprietário aprova tudo" do tipo <c>PurchaseOrder</c> (seed). Quem pede é um
/// comprador (sem <c>purchasing.approve</c>); quem aprova é um membro do Proprietário. Banco desta classe.
/// </summary>
public partial class PurchaseOrderTests(TenancyFixture tenancy) : PurchaseOrderTestBase(tenancy), IClassFixture<TenancyFixture>
{
    private const string Buyer = "purchasing:view,create,edit";

    /// <summary>CA 1: manual, com a condição padrão do fornecedor, a previsão hoje + prazo padrão, o número PC e a idempotência.</summary>
    [Fact]
    public async Task Pedido_manual_nasce_rascunho_com_os_padroes_e_a_mesma_chave_devolve_o_mesmo_pedido()
    {
        var (_, owner) = await ActorAsync("owner");
        var (buyerId, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner, "30/60/90 dias");
        var item = await ItemAsync();
        var key = Guid.NewGuid();
        var body = new
        {
            partyId = supplier, projectId = (Guid?)null, paymentTermId = (Guid?)null, expectedDate = (string?)null, freight = 10m, ipiPct = 5m, notes = (string?)null,
            lines = new[] { new { itemId = item, quantity = 3m, unitId = (Guid?)null, unitPrice = 1.005m, requestLineIds = Array.Empty<Guid>() } },
        };

        var order = await CreateAsync(buyer, body, key);
        var again = await CreateAsync(buyer, body, key);

        again.GetProperty("id").GetGuid().Should().Be(order.GetProperty("id").GetGuid(), "mesma Idempotency-Key, mesmo pedido");
        order.GetProperty("number").GetString().Should().MatchRegex(@"^PC-\d{4}-\d{4}$");
        order.GetProperty("status").GetString().Should().Be("Draft");
        order.GetProperty("paymentTermId").GetGuid().Should().Be(await TermAsync("30/60/90 dias"), "padrão do fornecedor");
        order.GetProperty("expectedDate").GetString().Should().Be(Iso(Today.AddDays(15)), "hoje + prazo padrão de compra (CMP-01)");
        order.GetProperty("projectId").ValueKind.Should().Be(JsonValueKind.Null, "estoque geral");
        order.GetProperty("subtotal").GetDecimal().Should().Be(3.02m, "3 × 1,005 = 3,015 → 3,02 (AwayFromZero)");
        order.GetProperty("ipiAmount").GetDecimal().Should().Be(0.15m);
        order.GetProperty("total").GetDecimal().Should().Be(13.17m);
        order.GetProperty("requestedBy").GetGuid().Should().Be(buyerId);
        (await WithDbAsync(db => db.PurchaseOrders.CountAsync(o => o.PartyId == supplier))).Should().Be(1);
    }

    /// <summary>CA 1 e 4: a partir das linhas de SC em aberto, as linhas passam a Ordered e o destino vem da SC (projeto), com natureza Material.</summary>
    [Fact]
    public async Task Pedido_a_partir_da_SC_liga_as_linhas_e_leva_o_projeto_da_SC()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();
        var project = Project();
        var (requestId, lineId) = await RequestAsync(buyer, item, 8m, project);

        var order = await CreateAsync(buyer, OrderBody(supplier, null, null, (item, 8m, 12.5m, [lineId])));

        order.GetProperty("projectId").GetGuid().Should().Be(project, "todas as linhas são do mesmo projeto");
        var line = order.GetProperty("lines")[0];
        line.GetProperty("costType").GetString().Should().Be("Material");
        line.GetProperty("requests")[0].GetProperty("requestId").GetGuid().Should().Be(requestId);
        (await RequestLineAsync(lineId)).Status.Should().Be(PurchaseRequestStatus.Ordered);
        (await WithDbAsync(db => db.PurchaseRequests.AsNoTracking().SingleAsync(r => r.Id == requestId))).Status.Should().Be(PurchaseRequestStatus.Ordered);
    }

    /// <summary>CA 22: duas criações com a mesma linha de SC: a segunda é recusada (a linha já virou pedido).</summary>
    [Fact]
    public async Task Mesma_linha_de_SC_em_dois_pedidos_a_segunda_e_recusada()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();
        var (_, lineId) = await RequestAsync(buyer, item, 2m);

        var results = await Task.WhenAll(
            buyer.SendAsync(Post(Base, OrderBody(supplier, null, null, (item, 2m, 10m, [lineId])))),
            buyer.SendAsync(Post(Base, OrderBody(supplier, null, null, (item, 2m, 10m, [lineId])))));

        results.Select(r => r.StatusCode).Should().Contain(HttpStatusCode.Created).And.ContainSingle(s => s == HttpStatusCode.Created);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        (await WithDbAsync(db => db.PurchaseOrderLines.CountAsync(l => l.ItemId == item))).Should().Be(1);
    }

    /// <summary>CA 1 (R-PRP-1): o "Novo pedido" pela SC sugere o fornecedor do último pedido aprovado do item e o último preço pago.</summary>
    [Fact]
    public async Task Padroes_sugerem_o_ultimo_fornecedor_e_o_ultimo_preco_pago()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner, "30 dias");
        var item = await ItemAsync();
        var first = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (item, 1m, 12.345678m, null))));
        (await DecideAsync(owner, first, true)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (_, lineId) = await RequestAsync(buyer, item, 4m);

        var defaults = await JsonAsync(await buyer.GetAsync($"{Base}/defaults?requestLineIds={lineId}"));

        defaults.GetProperty("partyId").GetGuid().Should().Be(supplier);
        defaults.GetProperty("partySuggested").GetBoolean().Should().BeTrue();
        defaults.GetProperty("paymentTermId").GetGuid().Should().Be(await TermAsync("30 dias"));
        var line = defaults.GetProperty("lines")[0];
        line.GetProperty("unitPrice").GetDecimal().Should().Be(12.345678m);
        line.GetProperty("priceSuggested").GetBoolean().Should().BeTrue();
        line.GetProperty("quantity").GetDecimal().Should().Be(4m);
        line.GetProperty("requestLineIds")[0].GetGuid().Should().Be(lineId);
    }

    /// <summary>CA 2 e 11: a API da CMP-02 cria com rfqId, quotes_count e a data pelo prazo da resposta; uma resposta só mostra "1 cotação".</summary>
    [Fact]
    public async Task Criacao_pela_cotacao_grava_a_cotacao_e_avisa_uma_cotacao()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();
        var rfq = Guid.CreateVersion7();

        var order = await CreateAsync(buyer, new
        {
            partyId = supplier, projectId = (Guid?)null, paymentTermId = (Guid?)null, expectedDate = (string?)null, freight = 0m, ipiPct = 0m, notes = (string?)null,
            lines = new[] { new { itemId = item, quantity = 1m, unitId = (Guid?)null, unitPrice = 100m, requestLineIds = Array.Empty<Guid>() } }, rfqId = rfq, quotesCount = 1, leadDays = 7,
        });

        order.GetProperty("rfqId").GetGuid().Should().Be(rfq);
        order.GetProperty("quotesCount").GetInt32().Should().Be(1);
        order.GetProperty("expectedDate").GetString().Should().Be(Iso(Today.AddDays(7)), "prazo da resposta");
        order.GetProperty("singleQuoteWarning").GetString().Should().Be("1 cotação: este pedido veio de uma cotação com uma resposta só.");
        var submitted = await SubmitAsync(buyer, order);
        (await DecideAsync(owner, submitted, true)).StatusCode.Should().Be(HttpStatusCode.OK, "o aviso não bloqueia a aprovação (D8)");
    }

    /// <summary>CA 3: fornecedor sem papel Fornecedor, item inativo, serviço e projeto fora de andamento: 422 no campo.</summary>
    [Fact]
    public async Task Fornecedor_item_e_projeto_invalidos_dao_422_no_campo()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var customer = await SupplierAsync(owner, supplierRole: false);
        var supplier = await SupplierAsync(owner);
        var active = await ItemAsync();
        var inactive = await ItemAsync(active: false);
        var service = await ItemAsync(ItemType.Service);
        var closed = Project("Closed");

        async Task<JsonElement> Refused(object body)
        {
            var response = await buyer.SendAsync(Post(Base, body));
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
            return await JsonAsync(response);
        }

        (await Refused(OrderBody(customer, null, null, (active, 1m, 1m, null)))).GetProperty("errors").TryGetProperty("partyId", out _).Should().BeTrue();
        (await Refused(OrderBody(supplier, null, null, (inactive, 1m, 1m, null)))).GetProperty("errors").TryGetProperty("lines[0].itemId", out _).Should().BeTrue();
        Code(await Refused(OrderBody(supplier, null, null, (service, 1m, 1m, null)))).Should().Be("service_not_allowed");
        (await Refused(OrderBody(supplier, closed, null, (active, 1m, 1m, null)))).GetProperty("errors").TryGetProperty("projectId", out _).Should().BeTrue();
        (await Refused(OrderBody(supplier, null, null, (active, 1m, 0m, null)))).GetProperty("errors").TryGetProperty("lines[0].unitPrice", out _).Should().BeTrue();
    }

    /// <summary>CA 3: o servidor recalcula ao salvar; o valor do cliente nunca é confiado (não há total na entrada).</summary>
    [Fact]
    public async Task Editar_rascunho_recalcula_os_totais_no_servidor()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();
        var order = await CreateAsync(buyer, OrderBody(supplier, null, null, (item, 2m, 10m, null)));
        var id = order.GetProperty("id").GetGuid();

        var updated = await buyer.PutAsJsonAsync($"{Base}/{id}", new
        {
            partyId = supplier, projectId = (Guid?)null, paymentTermId = order.GetProperty("paymentTermId").GetGuid(), expectedDate = order.GetProperty("expectedDate").GetString(),
            freight = 50m, ipiPct = 10m, notes = "Entregar na portaria 2", lines = new[] { new { itemId = item, quantity = 4m, unitId = (Guid?)null, unitPrice = 10.555m, requestLineIds = Array.Empty<Guid>() } },
            version = Version(order),
        });

        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var saved = await JsonAsync(updated);
        saved.GetProperty("subtotal").GetDecimal().Should().Be(42.22m);
        saved.GetProperty("ipiAmount").GetDecimal().Should().Be(4.22m);
        saved.GetProperty("total").GetDecimal().Should().Be(96.44m);
        saved.GetProperty("status").GetString().Should().Be("Draft", "rascunho editado continua rascunho");
    }
}
