using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-02: cotação de ponta a ponta com a regra padrão "Proprietário aprova tudo": criar a partir das SC (CA 1), fornecedores (CA 2),
/// respostas, conversão e quem lançou (CA 3, 5), anexo acima do limite (CA 6), comparativo com dataset fixo (CA 7), conclusão (CA 8),
/// pedidos (CA 9, 11), cancelamento (CA 12), concluída imutável (CA 14), concorrência e auditoria (CA 15). Banco desta classe.
/// </summary>
public class RfqTests(TenancyFixture tenancy) : RfqTestBase(tenancy), IClassFixture<TenancyFixture>
{
    /// <summary>CA 1: SC de duas OS (projetos diferentes) com o mesmo item somam numa linha; a mesma chave devolve a mesma cotação.</summary>
    [Fact]
    public async Task Cotacao_a_partir_de_SC_de_varias_OS_soma_por_item_e_poe_as_SC_em_cotacao()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (alfa, beta) = (await SupplierAsync(owner), await SupplierAsync(owner));
        var (plate, bolt) = (await ItemAsync(), await ItemAsync());
        var (scA, lineA) = await RequestAsync(buyer, plate, 8m, Project());
        var (scB, lineB) = await RequestAsync(buyer, plate, 4.5m, Project());
        var (_, lineC) = await RequestAsync(buyer, bolt, 100m);
        var key = Guid.NewGuid();

        var rfq = await CreateRfqAsync(buyer, [lineA, lineB, lineC], [alfa, beta], key);
        var again = await CreateRfqAsync(buyer, [lineA, lineB, lineC], [alfa, beta], key);

        Id(again).Should().Be(Id(rfq), "mesma Idempotency-Key, mesma cotação");
        rfq.GetProperty("number").GetString().Should().MatchRegex(@"^COT-\d{4}-\d{4}$");
        rfq.GetProperty("status").GetString().Should().Be("Open");
        var plateLine = Line(rfq, LineOf(rfq, plate));
        plateLine.GetProperty("quantity").GetDecimal().Should().Be(12.5m, "8 + 4,5 das duas SC");
        plateLine.GetProperty("requests").GetArrayLength().Should().Be(2);
        rfq.GetProperty("suppliers").GetArrayLength().Should().Be(2);
        foreach (var line in new[] { lineA, lineB, lineC })
        {
            (await RequestLineAsync(line)).Status.Should().Be(PurchaseRequestStatus.Quoting);
        }

        (await WithDbAsync(db => db.PurchaseRequests.AsNoTracking().Where(r => r.Id == scA || r.Id == scB).Select(r => r.Status).ToListAsync()))
            .Should().AllBeEquivalentTo(PurchaseRequestStatus.Quoting, "a situação da SC acompanha as linhas na mesma gravação");
        var second = await CreateRfqAsync(buyer, [lineA], [alfa], expected: HttpStatusCode.Conflict);
        Code(second).Should().Be("purchase_request_line_not_open", "a linha já está em cotação");
    }

    /// <summary>CA 1: sem SC, com itens digitados (compra pontual).</summary>
    [Fact]
    public async Task Cotacao_sem_SC_com_itens_digitados()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();

        var rfq = await CreateTypedRfqAsync(buyer, [(item, 3m)], [supplier]);

        Line(rfq, LineOf(rfq, item)).GetProperty("requests").GetArrayLength().Should().Be(0);
        var mixed = await buyer.SendAsync(Post(Rfqs, new { requestLineIds = new[] { Guid.NewGuid() }, lines = new[] { new { itemId = item, quantity = 1m, unitId = (Guid?)null } }, supplierIds = new[] { supplier } }));
        mixed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>CA 2: incluir e remover fornecedor com a cotação aberta; o mesmo fornecedor não se repete; com resposta não sai.</summary>
    [Fact]
    public async Task Fornecedores_entram_e_saem_sem_repetir()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (alfa, beta) = (await SupplierAsync(owner), await SupplierAsync(owner));
        var item = await ItemAsync();
        var rfq = await CreateTypedRfqAsync(buyer, [(item, 1m)], [alfa]);

        var duplicate = await buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/suppliers", new { partyId = alfa, version = Version(rfq) });
        duplicate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var added = await buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/suppliers", new { partyId = beta, version = Version(rfq) });
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        rfq = await JsonAsync(added);
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, alfa), [new Quote(LineOf(rfq, item), 10m)]);

        var responded = await buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/suppliers/{SupplierRow(rfq, alfa)}/remove", new { version = Version(rfq) });
        responded.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(responded)).Should().Be("rfq_supplier_responded");
        var removed = await buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/suppliers/{SupplierRow(rfq, beta)}/remove", new { version = Version(rfq) });
        removed.StatusCode.Should().Be(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());
        (await JsonAsync(removed)).GetProperty("suppliers").GetArrayLength().Should().Be(1);
    }

    /// <summary>CA 3 e 5: preço na unidade do fornecedor (fator do código do fornecedor), "Não cotou", quem lançou e o histórico de cada alteração.</summary>
    [Fact]
    public async Task Resposta_converte_a_unidade_do_fornecedor_e_registra_quem_lancou()
    {
        var (_, owner) = await ActorAsync("owner");
        var (buyerId, buyer) = await ActorAsync(Buyer);
        var (alfa, beta) = (await SupplierAsync(owner), await SupplierAsync(owner));
        var item = await ItemAsync();
        await WithDbAsync(db =>
        {
            db.ItemSupplierCodes.Add(new BemAqui.Inventory.Domain.ItemSupplierCode
            {
                ItemId = item, PartyId = alfa, SupplierCode = "CX-" + Guid.NewGuid().ToString("N")[..4], SupplierUnit = "CX", ConversionFactor = 50m, CreatedAt = DateTimeOffset.UtcNow,
            });
            return db.SaveChangesAsync();
        });
        var rfq = await CreateTypedRfqAsync(buyer, [(item, 100m)], [alfa, beta]);
        var line = LineOf(rfq, item);

        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, alfa), [new Quote(line, 100m, 7, 80m, InSupplierUnit: true)], freight: 20m, ipiPct: 10m);
        var declined = await buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/suppliers/{SupplierRow(rfq, beta)}/decline", new { version = Version(rfq) });
        declined.StatusCode.Should().Be(HttpStatusCode.OK, await declined.Content.ReadAsStringAsync());
        rfq = await JsonAsync(declined);

        var a = SupplierOf(rfq, alfa);
        var quote = a.GetProperty("quotes")[0];
        quote.GetProperty("unitPrice").GetDecimal().Should().Be(2m, "R$ 100 a caixa de 50");
        quote.GetProperty("supplierUnitPrice").GetDecimal().Should().Be(100m);
        quote.GetProperty("supplierUnit").GetString().Should().Be("CX");
        quote.GetProperty("availableQuantity").GetDecimal().Should().Be(80m);
        a.GetProperty("total").GetDecimal().Should().Be(240m, "200 + 20 de frete + 10% de IPI sobre 200");
        a.GetProperty("respondedBy").GetGuid().Should().Be(buyerId);
        a.GetProperty("respondedByName").ValueKind.Should().NotBe(JsonValueKind.Undefined);
        a.GetProperty("paymentTermName").GetString().Should().Be("30/60/90 dias", "condição padrão do fornecedor");
        SupplierOf(rfq, beta).GetProperty("declined").GetBoolean().Should().BeTrue();

        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, alfa), [new Quote(line, 1.9m, 7)]);
        var history = await AuditOfAsync(nameof(RfqQuote), (await WithDbAsync(db => db.RfqQuotes.AsNoTracking().SingleAsync(q => q.RfqLineId == line))).Id);
        history.Should().Contain(h => h.Action == "Updated" && h.UserId == buyerId && h.Changes.Contains("unitPrice", StringComparison.Ordinal), "cada alteração de preço fica com o autor");
        var response = await buyer.GetAsync($"{Rfqs}/{Id(rfq)}/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("entity").GetString()).Should().Contain(["Rfq", "RfqSupplier", "RfqQuote"]);
    }

    /// <summary>CA 3: preço na unidade do fornecedor sem código do fornecedor é recusado; linha de outra cotação dá 404 (IDOR).</summary>
    [Fact]
    public async Task Resposta_sem_unidade_do_fornecedor_ou_com_linha_de_outra_cotacao_e_recusada()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var (item, other) = (await ItemAsync(), await ItemAsync());
        var rfq = await CreateTypedRfqAsync(buyer, [(item, 1m)], [supplier]);
        var foreign = await CreateTypedRfqAsync(buyer, [(other, 1m)], [supplier]);

        (await RespondRawAsync(buyer, rfq, SupplierRow(rfq, supplier), [new Quote(LineOf(rfq, item), 10m, InSupplierUnit: true)])).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RespondRawAsync(buyer, rfq, SupplierRow(rfq, supplier), [new Quote(LineOf(foreign, other), 10m)])).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RespondRawAsync(buyer, rfq, SupplierRow(foreign, supplier), [new Quote(LineOf(rfq, item), 10m)])).StatusCode.Should().Be(HttpStatusCode.NotFound, "fornecedor de outra cotação");
    }

    /// <summary>
    /// CA 6: acima de R$ 5.000,00 sem o anexo da proposta, 422; com o anexo, grava; o anexo recusado depois (antivírus) bloqueia a
    /// conclusão com a mesma mensagem.
    /// </summary>
    [Fact]
    public async Task Resposta_acima_do_limite_exige_o_anexo_e_a_conclusao_confere_de_novo()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();
        var rfq = await CreateTypedRfqAsync(buyer, [(item, 1m)], [supplier]);
        var row = SupplierRow(rfq, supplier);
        var line = LineOf(rfq, item);

        var exact = await RespondRawAsync(buyer, rfq, row, [new Quote(line, 5_000m)]);
        exact.StatusCode.Should().Be(HttpStatusCode.OK, "R$ 5.000,00 não passa do limite");
        rfq = await JsonAsync(exact);
        var above = await RespondRawAsync(buyer, rfq, row, [new Quote(line, 4_990m)], freight: 10.01m);
        above.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(above);
        Code(problem).Should().Be("quote_attachment_required");
        problem.GetProperty("detail").GetString().Should().Be("Respostas acima de R$ 5.000,00 exigem o anexo da proposta do fornecedor.");

        var attachment = await AttachAsync(row);
        rfq = await RespondAsync(buyer, rfq, row, [new Quote(line, 4_990m)], freight: 10.01m);
        SupplierOf(rfq, supplier).GetProperty("attachmentRequired").GetBoolean().Should().BeTrue();
        SupplierOf(rfq, supplier).GetProperty("attachmentsCount").GetInt32().Should().Be(1);

        await InfectAsync(attachment);
        var close = await CloseRawAsync(buyer, rfq, new Pick(line, row));
        close.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(close)).Should().Be("quote_attachment_required");
        (await WithDbAsync(db => db.Rfqs.AsNoTracking().SingleAsync(r => r.Id == Id(rfq)))).Status.Should().Be(RfqStatus.Open);
    }

    /// <summary>
    /// CA 7 e 8 (dataset fixo da unidade): Beta tem o menor custo da linha A pelo frete rateado; escolher Alfa exige o motivo; Gama é o menor
    /// da linha B. Concluída, cada linha guarda o vencedor, o sugerido e o motivo.
    /// </summary>
    [Fact]
    public async Task Comparativo_destaca_o_menor_custo_e_o_vencedor_diferente_exige_o_motivo()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (alfa, beta, gama) = (await SupplierAsync(owner), await SupplierAsync(owner), await SupplierAsync(owner));
        var (itemA, itemB) = (await ItemAsync(), await ItemAsync());
        var rfq = await CreateTypedRfqAsync(buyer, [(itemA, 100m), (itemB, 10m)], [alfa, beta, gama]);
        var (a, b) = (LineOf(rfq, itemA), LineOf(rfq, itemB));
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, alfa), [new Quote(a, 10m, 5), new Quote(b, 50m, 5)], freight: 300m);
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, beta), [new Quote(a, 11m, 10), new Quote(b, 52m, 10)], ipiPct: 5m);
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, gama), [new Quote(b, 49m, 20)]);

        Line(rfq, a).GetProperty("cheapestSupplierId").GetGuid().Should().Be(SupplierRow(rfq, beta));
        Line(rfq, b).GetProperty("cheapestSupplierId").GetGuid().Should().Be(SupplierRow(rfq, gama));
        rfq.GetProperty("cheapestOverallSupplierId").GetGuid().Should().Be(SupplierRow(rfq, beta));
        SupplierOf(rfq, alfa).GetProperty("total").GetDecimal().Should().Be(1_800m);
        SupplierOf(rfq, beta).GetProperty("total").GetDecimal().Should().Be(1_701m);

        var noReason = await CloseRawAsync(buyer, rfq, new Pick(a, SupplierRow(rfq, alfa)), new Pick(b, SupplierRow(rfq, gama)));
        noReason.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var otherNoText = await CloseRawAsync(buyer, rfq, new Pick(a, SupplierRow(rfq, alfa), "Other"), new Pick(b, SupplierRow(rfq, gama)));
        otherNoText.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "Outro exige o texto");
        var missing = await CloseRawAsync(buyer, rfq, new Pick(a, SupplierRow(rfq, beta)));
        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a linha B foi cotada e ficou sem vencedor");

        var closed = await CloseAsync(buyer, rfq, new Pick(a, SupplierRow(rfq, alfa), "LeadTime"), new Pick(b, SupplierRow(rfq, gama), "Quality", "ignorado"));

        closed.GetProperty("status").GetString().Should().Be("Closed");
        var lineA = await WithDbAsync(db => db.RfqLines.AsNoTracking().SingleAsync(l => l.Id == a));
        lineA.SelectedSupplierId.Should().Be(SupplierRow(rfq, alfa));
        lineA.SuggestedSupplierId.Should().Be(SupplierRow(rfq, beta));
        lineA.SelectionReason.Should().Be(RfqSelectionReason.LeadTime);
        var lineB = await WithDbAsync(db => db.RfqLines.AsNoTracking().SingleAsync(l => l.Id == b));
        lineB.SelectionReason.Should().BeNull("Gama era o menor: sem motivo");
        (await AuditOfAsync(nameof(RfqLine), a)).Should().Contain(h => h.Changes.Contains("selectionReason", StringComparison.Ordinal), "o motivo fica no histórico (CA 15)");
    }

    /// <summary>
    /// CA 8, 9 e 11: uma só resposta basta; os pedidos nascem em rascunho com rfq_id e quotes_count = 1, a previsão hoje + o prazo, o frete
    /// e o IPI, o destino da SC e as SC em Ordered; gerar de novo não duplica.
    /// </summary>
    [Fact]
    public async Task Uma_resposta_conclui_e_gera_o_pedido_sem_duplicar()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (alfa, beta) = (await SupplierAsync(owner), await SupplierAsync(owner));
        var item = await ItemAsync();
        var project = Project();
        var (requestId, requestLine) = await RequestAsync(buyer, item, 10m, project);
        var rfq = await CreateRfqAsync(buyer, [requestLine], [alfa, beta]);
        var line = LineOf(rfq, item);
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, alfa), [new Quote(line, 12.5m, 12)], freight: 15m, ipiPct: 2m);

        rfq = await CloseAsync(buyer, rfq, new Pick(line, SupplierRow(rfq, alfa)));
        rfq.GetProperty("quotesCount").GetInt32().Should().Be(1);
        rfq.GetProperty("singleQuoteWarning").GetString().Should().Be("1 cotação: só um fornecedor respondeu a esta cotação.");
        rfq.GetProperty("canGenerateOrders").GetBoolean().Should().BeTrue();
        var preview = rfq.GetProperty("orders")[0];
        preview.GetProperty("total").GetDecimal().Should().Be(142.5m, "125 + 15 + 2,50");

        var key = Guid.NewGuid();
        var generated = await GenerateAsync(buyer, rfq, key);
        generated.GetProperty("created").GetInt32().Should().Be(1);
        var orderId = generated.GetProperty("orders")[0].GetProperty("purchaseOrderId").GetGuid();
        var order = await GetAsync(buyer, orderId);
        order.GetProperty("status").GetString().Should().Be("Draft");
        order.GetProperty("rfqId").GetGuid().Should().Be(Id(rfq));
        order.GetProperty("quotesCount").GetInt32().Should().Be(1);
        order.GetProperty("singleQuoteWarning").GetString().Should().NotBeNullOrEmpty("o diálogo de aprovação mostra o aviso 1 cotação (CMP-03a CA 11)");
        order.GetProperty("partyId").GetGuid().Should().Be(alfa);
        order.GetProperty("projectId").GetGuid().Should().Be(project, "destino da SC");
        order.GetProperty("expectedDate").GetString().Should().Be(Iso(Today.AddDays(12)), "hoje + o prazo da resposta");
        order.GetProperty("freight").GetDecimal().Should().Be(15m);
        order.GetProperty("ipiPct").GetDecimal().Should().Be(2m);
        order.GetProperty("total").GetDecimal().Should().Be(142.5m);
        order.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal().Should().Be(12.5m);
        (await RequestLineAsync(requestLine)).Status.Should().Be(PurchaseRequestStatus.Ordered);
        (await WithDbAsync(db => db.PurchaseRequests.AsNoTracking().SingleAsync(r => r.Id == requestId))).Status.Should().Be(PurchaseRequestStatus.Ordered);

        var replay = await GenerateAsync(buyer, rfq, key);
        replay.GetProperty("orders")[0].GetProperty("purchaseOrderId").GetGuid().Should().Be(orderId, "mesma chave");
        var again = await GenerateAsync(buyer, await GetRfqAsync(buyer, rfq));
        again.GetProperty("created").GetInt32().Should().Be(0, "gerar de novo mostra os pedidos já gerados");
        again.GetProperty("orders")[0].GetProperty("purchaseOrderId").GetGuid().Should().Be(orderId);
        (await WithDbAsync(db => db.PurchaseOrders.CountAsync(o => o.RfqId == Id(rfq)))).Should().Be(1);
        (await WithDbAsync(db => db.RfqSuppliers.AsNoTracking().SingleAsync(s => s.RfqId == Id(rfq) && s.PartyId == alfa))).PurchaseOrderId.Should().Be(orderId);
    }

    /// <summary>CA 9: com dois vencedores, um pedido por fornecedor com as linhas vencedoras de cada um.</summary>
    [Fact]
    public async Task Dois_vencedores_geram_dois_pedidos()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (alfa, beta) = (await SupplierAsync(owner), await SupplierAsync(owner));
        var (itemA, itemB) = (await ItemAsync(), await ItemAsync());
        var (_, scA) = await RequestAsync(buyer, itemA, 2m);
        var (_, scB) = await RequestAsync(buyer, itemB, 3m);
        var rfq = await CreateRfqAsync(buyer, [scA, scB], [alfa, beta]);
        var (a, b) = (LineOf(rfq, itemA), LineOf(rfq, itemB));
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, alfa), [new Quote(a, 10m), new Quote(b, 30m)]);
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, beta), [new Quote(a, 12m), new Quote(b, 20m)]);
        rfq = await CloseAsync(buyer, rfq, new Pick(a, SupplierRow(rfq, alfa)), new Pick(b, SupplierRow(rfq, beta)));

        var generated = await GenerateAsync(buyer, rfq);

        generated.GetProperty("created").GetInt32().Should().Be(2);
        var orders = await WithDbAsync(db => db.PurchaseOrders.AsNoTracking().Where(o => o.RfqId == Id(rfq)).ToListAsync());
        orders.Select(o => o.PartyId).Should().BeEquivalentTo([alfa, beta]);
        orders.Should().AllSatisfy(o => o.QuotesCount.Should().Be(2));
        var lines = await WithDbAsync(db => db.PurchaseOrderLines.AsNoTracking().Where(l => orders.Select(o => o.Id).Contains(l.OrderId)).ToListAsync());
        lines.Single(l => l.ItemId == itemA).OrderId.Should().Be(orders.Single(o => o.PartyId == alfa).Id);
        lines.Single(l => l.ItemId == itemB).RequestLineIds.Should().Equal(scB);
    }

    /// <summary>CA 12: cancelar com motivo devolve as SC a Open; concluída ou cancelada não cancela de novo.</summary>
    [Fact]
    public async Task Cancelar_devolve_as_SC()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();
        var (requestId, requestLine) = await RequestAsync(buyer, item, 5m);
        var rfq = await CreateRfqAsync(buyer, [requestLine], [supplier]);

        (await buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/cancel", new { reason = "", version = Version(rfq) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var cancelled = await buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/cancel", new { reason = "Comprado no balcão", version = Version(rfq) });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());

        (await JsonAsync(cancelled)).GetProperty("status").GetString().Should().Be("Cancelled");
        (await RequestLineAsync(requestLine)).Status.Should().Be(PurchaseRequestStatus.Open);
        (await WithDbAsync(db => db.PurchaseRequests.AsNoTracking().SingleAsync(r => r.Id == requestId))).Status.Should().Be(PurchaseRequestStatus.Open);
        var again = await buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/cancel", new { reason = "De novo", version = Version(await JsonAsync(cancelled)) });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>CA 8: linha sem nenhuma resposta fica sem vencedor e a SC dela volta a Open na conclusão.</summary>
    [Fact]
    public async Task Linha_sem_resposta_volta_para_a_SC_na_conclusao()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var (itemA, itemB) = (await ItemAsync(), await ItemAsync());
        var (_, scA) = await RequestAsync(buyer, itemA, 2m);
        var (_, scB) = await RequestAsync(buyer, itemB, 2m);
        var rfq = await CreateRfqAsync(buyer, [scA, scB], [supplier]);
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, supplier), [new Quote(LineOf(rfq, itemA), 10m)]);

        await CloseAsync(buyer, rfq, new Pick(LineOf(rfq, itemA), SupplierRow(rfq, supplier)));

        (await RequestLineAsync(scA)).Status.Should().Be(PurchaseRequestStatus.Quoting, "vai para o pedido do vencedor");
        (await RequestLineAsync(scB)).Status.Should().Be(PurchaseRequestStatus.Open, "ninguém cotou: volta para o comprador");
    }

    /// <summary>CA 14: concluída, preço, resposta, fornecedor e vencedor dão 409 "A cotação está concluída."; o anexo também é recusado.</summary>
    [Fact]
    public async Task Cotacao_concluida_e_imutavel()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (alfa, beta) = (await SupplierAsync(owner), await SupplierAsync(owner));
        var item = await ItemAsync();
        var rfq = await CreateTypedRfqAsync(buyer, [(item, 1m)], [alfa]);
        var line = LineOf(rfq, item);
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, alfa), [new Quote(line, 10m)]);
        rfq = await CloseAsync(buyer, rfq, new Pick(line, SupplierRow(rfq, alfa)));

        foreach (var response in new[]
                 {
                     await RespondRawAsync(buyer, rfq, SupplierRow(rfq, alfa), [new Quote(line, 1m)]),
                     await CloseRawAsync(buyer, rfq, new Pick(line, SupplierRow(rfq, alfa))),
                     await buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/suppliers", new { partyId = beta, version = Version(rfq) }),
                     await buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/suppliers/{SupplierRow(rfq, alfa)}/decline", new { version = Version(rfq) }),
                 })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var problem = await JsonAsync(response);
            Code(problem).Should().Be("rfq_closed");
            problem.GetProperty("detail").GetString().Should().Be("A cotação está concluída.");
        }

        using var file = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.4\n%%EOF"u8.ToArray()), "file", "proposta.pdf" } };
        (await buyer.PostAsync($"/api/v1/attachments/Rfq/{SupplierRow(rfq, alfa)}", file)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Concorrência: duas respostas sobre a mesma versão; uma grava e a outra recebe 409 (sem supor a ordem de chegada).</summary>
    [Fact]
    public async Task Duas_gravacoes_na_mesma_versao_uma_recebe_409()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (alfa, beta) = (await SupplierAsync(owner), await SupplierAsync(owner));
        var item = await ItemAsync();
        var rfq = await CreateTypedRfqAsync(buyer, [(item, 1m)], [alfa, beta]);
        var line = LineOf(rfq, item);

        var results = await Task.WhenAll(
            RespondRawAsync(buyer, rfq, SupplierRow(rfq, alfa), [new Quote(line, 10m)]),
            RespondRawAsync(buyer, rfq, SupplierRow(rfq, beta), [new Quote(line, 11m)]));

        results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await WithDbAsync(db => db.RfqQuotes.CountAsync(q => q.RfqId == Id(rfq)))).Should().Be(1);
    }

    /// <summary>Concorrência: concluir e responder ao mesmo tempo; um dos dois recebe 409 e a cotação não fica meio concluída.</summary>
    [Fact]
    public async Task Concluir_e_responder_ao_mesmo_tempo_um_recebe_409()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (alfa, beta) = (await SupplierAsync(owner), await SupplierAsync(owner));
        var item = await ItemAsync();
        var rfq = await CreateTypedRfqAsync(buyer, [(item, 1m)], [alfa, beta]);
        var line = LineOf(rfq, item);
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, alfa), [new Quote(line, 10m)]);

        var results = await Task.WhenAll(CloseRawAsync(buyer, rfq, new Pick(line, SupplierRow(rfq, alfa))), RespondRawAsync(buyer, rfq, SupplierRow(rfq, beta), [new Quote(line, 9m)]));

        results.Select(r => r.StatusCode).Should().Contain(HttpStatusCode.OK).And.Contain(HttpStatusCode.Conflict);
        var saved = await WithDbAsync(db => db.Rfqs.AsNoTracking().SingleAsync(r => r.Id == Id(rfq)));
        var betaQuotes = await WithDbAsync(db => db.RfqQuotes.CountAsync(q => q.RfqId == Id(rfq) && q.RfqSupplierId == SupplierRow(rfq, beta)));
        (saved.Status == RfqStatus.Closed ? betaQuotes == 0 : betaQuotes == 1).Should().BeTrue("ou concluiu sem a resposta de Beta, ou gravou a resposta e continua aberta");
    }

    /// <summary>CA 13: fornecedor sem o papel Fornecedor é recusado na criação e na inclusão.</summary>
    [Fact]
    public async Task Terceiro_sem_papel_de_fornecedor_e_recusado()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var customer = await SupplierAsync(owner, supplierRole: false);
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();

        var refused = await buyer.SendAsync(Post(Rfqs, new { requestLineIds = Array.Empty<Guid>(), lines = new[] { new { itemId = item, quantity = 1m, unitId = (Guid?)null } }, supplierIds = new[] { customer } }));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var rfq = await CreateTypedRfqAsync(buyer, [(item, 1m)], [supplier]);
        (await buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/suppliers", new { partyId = customer, version = Version(rfq) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
