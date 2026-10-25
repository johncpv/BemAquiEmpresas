using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-04 com PostgreSQL real (movimenta estoque e financeiro): recebimento parcial e total com entrada, lote, custo médio e títulos na
/// mesma transação (dataset fixo pedido → recebimento → fluxo de caixa), faltas completadas, idempotência, saldo e teto, estorno, encerrar
/// saldo, correção de preço e auditoria. Banco desta classe; cada teste cria os próprios fornecedores, itens e pedidos e filtra pelos ids.
/// </summary>
public partial class GoodsReceiptTests(TenancyFixture tenancy) : GoodsReceiptTestBase(tenancy), IClassFixture<TenancyFixture>
{
    /// <summary>
    /// CA 1, 5, 6, 8 e 9 (dataset fixo): A = 10 × 30,00, B = 20 × 35,00, frete 100,00, IPI 5% (50,00), condição 30/60 dias. O primeiro
    /// recebimento (A 10, B 10) entra no estoque com o custo rateado (34,50 e 40,25), nasce com 747,50 em dois títulos em aberto e o previsto
    /// do pedido cai para 402,50; o segundo completa o pedido (Received) e cancela o previsto.
    /// </summary>
    [Fact]
    public async Task Parcial_e_total_com_entrada_custo_medio_titulos_e_previsto_replanejado()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (receiverId, receiver) = await ActorAsync("warehouse");
        var supplier = await SupplierAsync(owner);
        var a = await LotItemAsync(tracksLot: true);
        var b = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, supplier, 100m, 5m, "30/60 dias", null, (a, 10m, 30m, null), (b, 20m, 35m, null));
        var id = order.GetProperty("id").GetGuid();
        Planned(await PayablesAsync(DocumentOrigin.Purchase, id)).Should().Be(1150m);

        var defaults = await receiver.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Base}/{id}/receipt-defaults");
        defaults.GetProperty("blocker").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        defaults.GetProperty("defaultLocationId").GetGuid().Should().Be(await DefaultLocationAsync(), "o local padrão (ALM)");
        defaults.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("open").GetDecimal()).Should().Equal(10m, 20m);

        var first = await ReceivedAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, a), 10m, "L-001"), (LineOf(order, b), 10m, null)));

        first.GetProperty("number").GetString().Should().MatchRegex(@"^REC-\d{4}-\d{4}$");
        first.GetProperty("status").GetString().Should().Be("Completed");
        first.GetProperty("freight").GetDecimal().Should().Be(65m, "100 × 650 ÷ 1.000");
        first.GetProperty("ipiAmount").GetDecimal().Should().Be(32.5m);
        first.GetProperty("amount").GetDecimal().Should().Be(747.5m);
        first.GetProperty("receivedBy").GetGuid().Should().Be(receiverId);
        var receiptId = first.GetProperty("id").GetGuid();
        var lines = first.GetProperty("lines").EnumerateArray().ToList();
        lines.Select(l => l.GetProperty("freightShare").GetDecimal()).Should().Equal(30m, 35m);
        lines.Select(l => l.GetProperty("ipiShare").GetDecimal()).Should().Equal(15m, 17.5m);
        lines.Select(l => l.GetProperty("unitCost").GetDecimal()).Should().Equal(34.5m, 40.25m);

        var movements = await MovementsAsync(receiptId);
        movements.Should().HaveCount(2);
        movements.Should().OnlyContain(m => m.Type == StockMovementType.Receipt && m.SourceNumber == first.GetProperty("number").GetString());
        movements.Single(m => m.ItemId == a).LotId.Should().NotBeNull("o lote nasce na entrada");
        (await WithDbAsync(db => db.Lots.AsNoTracking().SingleAsync(l => l.ItemId == a))).SupplierPartyId.Should().Be(supplier);
        (await AverageCostAsync(a)).Should().Be(34.5m);
        (await AverageCostAsync(b)).Should().Be(40.25m);

        var titles = await PayablesAsync(DocumentOrigin.GoodsReceipt, receiptId);
        titles.Should().HaveCount(2);
        titles.Should().OnlyContain(p => p.Status == PayableStatus.Open && !p.IsEstimate && p.PartyId == supplier);
        titles.Select(p => p.Amount).Should().Equal(373.75m, 373.75m);
        titles.Select(p => p.DueDate).Should().Equal(Today.AddDays(30), Today.AddDays(60));
        titles[0].Description.Should().Be($"Recebimento {first.GetProperty("number").GetString()} do pedido {order.GetProperty("number").GetString()} — parcela 1/2");
        Planned(await PayablesAsync(DocumentOrigin.Purchase, id)).Should().Be(402.5m, "1.150,00 − 747,50");
        var afterFirst = await GetAsync(owner, id);
        afterFirst.GetProperty("status").GetString().Should().Be("PartiallyReceived");

        var second = await ReceivedAsync(receiver, afterFirst, ReceiptBody(afterFirst, null, null, (LineOf(order, b), 10m, null)));

        second.GetProperty("freight").GetDecimal().Should().Be(35m, "o recebimento que completa leva o resto do frete");
        second.GetProperty("amount").GetDecimal().Should().Be(402.5m);
        (await GetAsync(owner, id)).GetProperty("status").GetString().Should().Be("Received");
        Planned(await PayablesAsync(DocumentOrigin.Purchase, id)).Should().Be(0m, "pedido recebido não deixa previsto");
        (await AverageCostAsync(b)).Should().Be(40.25m);
        (await WithDbAsync(db => db.PurchaseOrderLines.AsNoTracking().Where(l => l.OrderId == id).SumAsync(l => l.QuantityReceived))).Should().Be(30m);
    }

    /// <summary>CA 5 e 17: a mesma Idempotency-Key devolve o mesmo recebimento; o estoque e os títulos não duplicam.</summary>
    [Fact]
    public async Task Mesma_chave_devolve_o_mesmo_recebimento()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 4m, 25m, null));
        var key = Guid.NewGuid();
        var body = ReceiptBody(order, null, null, (LineOf(order, item), 4m, null));

        var one = await ReceiveAsync(receiver, order, body, key);
        var two = await ReceiveAsync(receiver, order, body, key);

        one.StatusCode.Should().Be(HttpStatusCode.Created);
        two.StatusCode.Should().Be(HttpStatusCode.Created);
        (await JsonAsync(two)).GetProperty("id").GetGuid().Should().Be((await JsonAsync(one)).GetProperty("id").GetGuid());
        var orderId = order.GetProperty("id").GetGuid();
        (await WithDbAsync(db => db.GoodsReceipts.CountAsync(r => r.PurchaseOrderId == orderId))).Should().Be(1);
        (await WithDbAsync(db => db.StockMovements.CountAsync(m => m.ItemId == item))).Should().Be(1);
    }

    /// <summary>CA 3 e 4: acima do saldo (tolerância 0%) é 422 na linha; frete ou IPI acima do proporcional, 422 no campo; nada grava.</summary>
    [Fact]
    public async Task Acima_do_saldo_e_frete_acima_do_proporcional_sao_recusados()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 50m, 10m, "À vista", null, (item, 5m, 100m, null));
        var line = LineOf(order, item);

        var over = await ReceiveAsync(receiver, order, ReceiptBody(order, null, null, (line, 5.0001m, null)));
        var freight = await ReceiveAsync(receiver, order, ReceiptBody(order, 50.01m, null, (line, 5m, null)));
        var ipi = await ReceiveAsync(receiver, order, ReceiptBody(order, null, 50.01m, (line, 5m, null)));

        over.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await over.Content.ReadAsStringAsync()).Should().Contain("Recebido acima do pedido: saldo 5.");
        freight.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await freight.Content.ReadAsStringAsync()).Should().Contain("Frete e IPI só podem baixar no recebimento.");
        ipi.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var orderId = order.GetProperty("id").GetGuid();
        (await WithDbAsync(db => db.GoodsReceipts.CountAsync(r => r.PurchaseOrderId == orderId))).Should().Be(0);
        var lower = await ReceivedAsync(receiver, order, ReceiptBody(order, 20m, 0m, (line, 5m, null)));
        lower.GetProperty("amount").GetDecimal().Should().Be(520m, "frete e IPI baixam, nunca sobem");
    }

    /// <summary>CA 3 e 4 com a tolerância: 10% deixa receber 11 de 10; o teto do aprovado (+10%) segura o valor.</summary>
    [Fact]
    public async Task Tolerancia_deixa_receber_acima_dentro_do_teto()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        await owner.PutAsJsonAsync("/api/v1/purchasing/parameters", new { defaultLeadDays = 15, receiptTolerancePct = 10m });
        try
        {
            var item = await ItemAsync();
            var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 10m, 10m, null));
            var received = await ReceivedAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, item), 11m, null)));

            received.GetProperty("amount").GetDecimal().Should().Be(110m);
            var orderId = order.GetProperty("id").GetGuid();
            var line = await WithDbAsync(db => db.PurchaseOrderLines.AsNoTracking().SingleAsync(l => l.OrderId == orderId));
            line.QuantityReceived.Should().Be(10m);
            line.QuantityOverReceived.Should().Be(1m);
            (await GetAsync(owner, orderId)).GetProperty("status").GetString().Should().Be("Received");
        }
        finally
        {
            await owner.PutAsJsonAsync("/api/v1/purchasing/parameters", new { defaultLeadDays = 15, receiptTolerancePct = 0m });
        }
    }

    /// <summary>CA 1 e 16: item que controla lote exige o lote (422); linha de outro pedido no corpo é 422; IDOR do recebimento e da linha.</summary>
    [Fact]
    public async Task Lote_obrigatorio_linha_de_outro_pedido_e_IDOR()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var supplier = await SupplierAsync(owner);
        var lotItem = await LotItemAsync(tracksLot: true);
        var order = await ApprovedOrderAsync(owner, buyer, supplier, 0m, 0m, "À vista", null, (lotItem, 2m, 10m, null));
        var other = await ApprovedOrderAsync(owner, buyer, supplier, 0m, 0m, "À vista", null, (await ItemAsync(), 2m, 10m, null));

        var noLot = await ReceiveAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, lotItem), 2m, null)));
        var foreign = await ReceiveAsync(receiver, order, ReceiptBody(order, null, null, (other.GetProperty("lines")[0].GetProperty("id").GetGuid(), 2m, null)));

        noLot.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await noLot.Content.ReadAsStringAsync()).Should().Contain("Este item controla lote: informe o lote.");
        foreign.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var receipt = await ReceivedAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, lotItem), 2m, "LT-9")));
        var receiptId = receipt.GetProperty("id").GetGuid();
        var lineId = receipt.GetProperty("lines")[0].GetProperty("id").GetGuid();
        (await owner.GetAsync($"{Base}/{other.GetProperty("id").GetGuid()}/receipts/{receiptId}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "recebimento de outro pedido");
        (await owner.GetAsync($"{Base}/{order.GetProperty("id").GetGuid()}/receipts/{receiptId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync($"{Receipts}/{Guid.CreateVersion7()}/lines/{lineId}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "linha de outro recebimento");
        (await owner.GetAsync($"{Receipts}/{receiptId}/lines/{lineId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>CA 7: a entrada completa a falta da reserva da OS na mesma transação e o recebimento mostra a OS atendida.</summary>
    [Fact]
    public async Task Entrada_completa_as_faltas_e_mostra_as_OS_atendidas()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var item = await ItemAsync();
        var os = await ShortageAsync(item, 3m);
        (await WithDbAsync(db => db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == os).SumAsync(r => r.Quantity))).Should().Be(0m, "sem saldo, só falta");
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 5m, 8m, null));

        var receipt = await ReceivedAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, item), 5m, null)));

        (await WithDbAsync(db => db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == os).SumAsync(r => r.Quantity))).Should().Be(3m);
        receipt.GetProperty("servedOrders").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).Should().Equal(os);
    }

    /// <summary>
    /// CA 12: estornar cancela os títulos do recebimento antes do estoque, nasce o movimento inverso, o recebido desce, o pedido volta à
    /// situação de antes e o saldo volta ao previsto; com título em OP, 409 e nada muda.
    /// </summary>
    [Fact]
    public async Task Estorno_cancela_os_titulos_devolve_o_previsto_e_recusa_com_titulo_em_OP()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "30 dias", null, (item, 10m, 12m, null));
        var orderId = order.GetProperty("id").GetGuid();
        var receipt = await ReceivedAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, item), 4m, null)));
        var receiptId = receipt.GetProperty("id").GetGuid();
        Planned(await PayablesAsync(DocumentOrigin.Purchase, orderId)).Should().Be(72m);

        await WithDbAsync(async db =>
        {
            var title = await db.Payables.SingleAsync(p => p.Origin == DocumentOrigin.GoodsReceipt && p.OriginId == receiptId);
            title.Status = PayableStatus.AwaitingApproval;
            return await db.SaveChangesAsync();
        });
        var refused = await owner.PostAsJsonAsync($"{Receipts}/{receiptId}/reverse", new { reason = "Lançado errado", version = Version(receipt) });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(refused)).Should().Be("payable_in_payment_order");
        (await MovementsAsync(receiptId)).Should().HaveCount(1, "nada mexeu no estoque");

        await WithDbAsync(async db =>
        {
            var title = await db.Payables.SingleAsync(p => p.Origin == DocumentOrigin.GoodsReceipt && p.OriginId == receiptId);
            title.Status = PayableStatus.Open;
            return await db.SaveChangesAsync();
        });
        var reversed = await owner.PostAsJsonAsync($"{Receipts}/{receiptId}/reverse", new { reason = "Lançado errado", version = Version(receipt) });

        reversed.StatusCode.Should().Be(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync());
        (await JsonAsync(reversed)).GetProperty("status").GetString().Should().Be("Reversed");
        (await PayablesAsync(DocumentOrigin.GoodsReceipt, receiptId)).Should().OnlyContain(p => p.Status == PayableStatus.Cancelled);
        var movements = await MovementsAsync(receiptId);
        movements.Should().HaveCount(2);
        movements.Sum(m => m.Quantity).Should().Be(0m);
        Planned(await PayablesAsync(DocumentOrigin.Purchase, orderId)).Should().Be(120m, "o saldo volta ao previsto");
        var back = await GetAsync(owner, orderId);
        back.GetProperty("status").GetString().Should().Be("Approved");
        back.GetProperty("quantityReceived").GetDecimal().Should().Be(0m);
    }

    /// <summary>CA 12: estorno de entrada já consumida sem saldo é 409 insufficient_stock com o item; os títulos continuam (rollback).</summary>
    [Fact]
    public async Task Estorno_sem_saldo_e_recusado_e_desfaz_tudo()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 3m, 10m, null));
        var receipt = await ReceivedAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, item), 3m, null)));
        var receiptId = receipt.GetProperty("id").GetGuid();
        var location = await DefaultLocationAsync();
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var issued = await scope.ServiceProvider.GetRequiredService<IStockMovements>().PostAsync(
                new StockPosting(StockMovementType.Issue, item, location, null, -2m, null, StockSources.ManualPosting, Guid.NewGuid()));
            issued.IsSuccess.Should().BeTrue(issued.Error?.Message);
            await transaction.CommitAsync();
        }

        var refused = await owner.PostAsJsonAsync($"{Receipts}/{receiptId}/reverse", new { reason = "Erro", version = Version(receipt) });

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(refused)).Should().Be("insufficient_stock");
        (await PayablesAsync(DocumentOrigin.GoodsReceipt, receiptId)).Should().OnlyContain(p => p.Status == PayableStatus.Open, "o cancelamento foi desfeito");
        (await WithDbAsync(db => db.GoodsReceipts.AsNoTracking().SingleAsync(r => r.Id == receiptId))).Status.Should().Be(GoodsReceiptStatus.Completed);
    }

    /// <summary>CA 13: encerrar o saldo cancela os previstos, marca o pedido Received com o saldo e devolve a linha de SC não recebida.</summary>
    [Fact]
    public async Task Encerrar_saldo_cancela_os_previstos_e_devolve_a_SC()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var item = await ItemAsync();
        var (_, scLine) = await RequestAsync(buyer, item, 10m);
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 10m, 5m, [scLine]));
        var orderId = order.GetProperty("id").GetGuid();
        await ReceivedAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, item), 6m, null)));
        var partial = await GetAsync(owner, orderId);

        var closed = await buyer.PostAsJsonAsync($"{Base}/{orderId}/close", new { reason = "Fornecedor não tem o resto", version = Version(partial) });

        closed.StatusCode.Should().Be(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync());
        var body = await JsonAsync(closed);
        body.GetProperty("status").GetString().Should().Be("Received");
        body.GetProperty("closedBalanceAmount").GetDecimal().Should().Be(20m);
        Planned(await PayablesAsync(DocumentOrigin.Purchase, orderId)).Should().Be(0m);
        (await RequestLineAsync(scLine)).Quantity.Should().Be(6m, "fica com o que foi recebido");
        var reopened = await WithDbAsync(db => db.PurchaseRequestLines.AsNoTracking().Where(l => l.ItemId == item && l.Status == PurchaseRequestStatus.Open).ToListAsync());
        reopened.Sum(l => l.Quantity).Should().Be(4m, "a quantidade não recebida volta a Open");
        (await buyer.PostAsJsonAsync($"{Base}/{orderId}/close", new { reason = "De novo", version = body.GetProperty("version").GetUInt32() }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>CA 16: o pedido parcialmente recebido não é cancelado nem editado (409 "Já houve recebimento").</summary>
    [Fact]
    public async Task Pedido_com_recebimento_nao_e_cancelado()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 10m, 5m, null));
        await ReceivedAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, item), 1m, null)));
        var partial = await GetAsync(owner, order.GetProperty("id").GetGuid());

        var cancel = await buyer.PostAsJsonAsync($"{Base}/{order.GetProperty("id").GetGuid()}/cancel", new { reason = "Desistimos", version = Version(partial) });

        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(cancel)).Should().Be("purchase_order_received");
    }
}
