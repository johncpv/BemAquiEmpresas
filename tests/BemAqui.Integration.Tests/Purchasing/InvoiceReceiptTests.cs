using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// FIS-04b com PostgreSQL real: "Receber pela nota" a partir da NF-e de entrada conferida. A nota entra direto no banco já conferida (a
/// importação do XML é da FIS-04a); o recebimento, o estoque, os títulos e a transição da nota são os de produção. Cada teste cria o próprio
/// pedido e a própria nota; o parâmetro de crédito é devolvido ao padrão no fim do teste que o muda.
/// </summary>
public class InvoiceReceiptTests(TenancyFixture tenancy) : GoodsReceiptTestBase(tenancy), IClassFixture<TenancyFixture>
{
    private const string Invoices = "/api/v1/purchasing/incoming-invoices";

    private sealed record NoteItem(Guid? PoLine, Guid? Item, decimal Quantity, decimal Price, string? Lot = null, decimal Icms = 0m, decimal Ipi = 0m, Guid? Account = null);

    private sealed record Scenario(HttpClient Owner, HttpClient Receiver, JsonElement Order, Guid OrderId, Guid Supplier, Guid ItemA, Guid ItemB, Guid LineA, Guid LineB);

    /// <summary>Pedido aprovado: A 10 × R$ 50 (controla lote) e B 5 × R$ 100, frete R$ 40, IPI 5% (R$ 50): total R$ 1.090,00.</summary>
    private async Task<Scenario> ScenarioAsync(string term = "À vista")
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var supplier = await SupplierAsync(owner);
        var a = await LotItemAsync(tracksLot: true);
        var b = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, supplier, 40m, 5m, term, null, (a, 10m, 50m, null), (b, 5m, 100m, null));
        return new Scenario(owner, receiver, order, order.GetProperty("id").GetGuid(), supplier, a, b, LineOf(order, a), LineOf(order, b));
    }

    /// <summary>A nota já conferida (FIS-04a CA 12), ligada ao pedido, com os itens no de-para (fator 1) ou "Sem item de estoque".</summary>
    private Task<Guid> CheckedInvoiceAsync(Scenario s, decimal freight, decimal insurance, IncomingDuplicate[] duplicates, params NoteItem[] items)
    {
        var products = items.Sum(i => Math.Round(i.Quantity * i.Price, 2));
        var ipi = items.Sum(i => i.Ipi);
        var total = products + freight + insurance + ipi;
        var now = DateTimeOffset.UtcNow;
        return WithDbAsync(async db =>
        {
            var key = string.Concat(Enumerable.Range(0, 44).Select(_ => (char)('0' + Random.Shared.Next(10))));
            var invoice = new IncomingInvoice
            {
                AccessKey = key, Number = Random.Shared.Next(1000, 999999), Series = 1, IssuedAt = now.AddDays(-3), IssuerDocument = "12345678000199", IssuerName = "Fornecedor da nota",
                Totals = new IncomingInvoiceTotals(products, freight, insurance, 0m, 0m, ipi, 0m, items.Sum(i => i.Icms), 0m, 0m, total), Total = total, Duplicates = duplicates,
                ImportedAt = now,
            };
            invoice.SetXmlProof("12345678000199", "135260000000001", null);
            invoice.SetParty(s.Supplier, now);
            invoice.LinkOrder(s.OrderId, false, now);
            db.IncomingInvoices.Add(invoice);
            var sequence = 0;
            foreach (var i in items)
            {
                var line = new IncomingInvoiceItem
                {
                    InvoiceId = invoice.Id, Sequence = ++sequence, SupplierCode = "F" + sequence, Description = "Item da nota " + sequence, Unit = "UN", Quantity = i.Quantity,
                    UnitPrice = i.Price, Amount = Math.Round(i.Quantity * i.Price, 2), Taxes = new IncomingItemTaxes("00", 0m, 18m, i.Icms, 0m, 0m, 5m, i.Ipi, 0m, 0m), LotNumber = i.Lot,
                };
                if (i.Account is { } account)
                {
                    line.MarkNoStockItem(account);
                }
                else
                {
                    line.MapTo(i.Item!.Value, 1m, ItemMappingSource.Manual);
                    line.LinkLine(i.PoLine);
                }

                db.IncomingInvoiceItems.Add(line);
            }

            invoice.Check(null, now, [], null);
            await db.SaveChangesAsync();
            return invoice.Id;
        });
    }

    private static NoteItem[] Standard(Scenario s, decimal priceA = 50m, decimal icmsA = 0m) =>
        [new(s.LineA, s.ItemA, 10m, priceA, "LT-NF", icmsA, 25m), new(s.LineB, s.ItemB, 5m, 100m, null, 0m, 25m)];

    private static IncomingDuplicate[] TwoDuplicates(decimal each) => [new("001", Today.AddDays(10), each), new("002", Today.AddDays(40), each)];

    private static async Task<JsonElement> DefaultsAsync(HttpClient client, Guid invoice)
    {
        var response = await client.GetAsync($"{Invoices}/{invoice}/receipt-defaults");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static object Body(JsonElement defaults, string? lot = null) => new
    {
        receivedOn = (string?)null, notes = (string?)null,
        lines = defaults.GetProperty("lines").EnumerateArray().Select(l => new
        {
            poLineId = l.GetProperty("poLineId").GetGuid(), lotNumber = lot, lotExpiry = (string?)null, locationId = (Guid?)null,
        }),
        orderVersion = defaults.GetProperty("orderVersion").GetUInt32(), invoiceVersion = defaults.GetProperty("invoiceVersion").GetUInt32(),
    };

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, Guid invoice, object body, Guid? key = null) =>
        client.SendAsync(Post($"{Invoices}/{invoice}/receipt", body, key));

    private Task<IncomingInvoice> InvoiceAsync(Guid id) => WithDbAsync(db => db.IncomingInvoices.AsNoTracking().SingleAsync(i => i.Id == id));

    private Task<Guid> ExpenseAccountAsync() =>
        WithDbAsync(db => db.Accounts.Where(a => a.Type == AccountType.Expense && a.AcceptsEntries && a.IsActive && !a.IsDeleted).OrderBy(a => a.Code).Select(a => a.Id).FirstAsync());

    /// <summary>
    /// CA 1, 5, 6 e 14: o formulário vem com a quantidade, o lote e o frete da nota; na mesma transação nasce o recebimento <c>Invoice</c>
    /// ligado à nota, as entradas pelo custo da nota, um título <c>Open</c> por duplicata (valor e vencimento), o previsto do pedido zera e a
    /// nota fica <c>Received</c> com o recebimento e a regra de crédito; a mesma nota não é recebida de novo (409 com o link).
    /// </summary>
    [Fact]
    public async Task Receber_pela_nota_da_entrada_pelo_custo_da_nota_e_os_titulos_pelas_duplicatas()
    {
        var s = await ScenarioAsync();
        var invoiceId = await CheckedInvoiceAsync(s, 40m, 0m, TwoDuplicates(545m), Standard(s));

        var defaults = await DefaultsAsync(s.Receiver, invoiceId);
        defaults.GetProperty("blocker").ValueKind.Should().Be(JsonValueKind.Null, defaults.ToString());
        defaults.GetProperty("priceBlocked").GetBoolean().Should().BeFalse();
        defaults.GetProperty("freight").GetDecimal().Should().Be(40m);
        defaults.GetProperty("ipiAmount").GetDecimal().Should().Be(50m);
        defaults.GetProperty("creditRule").GetString().Should().Be("Sem crédito de impostos (a confirmar com a contadora)");
        var lineA = defaults.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("poLineId").GetGuid() == s.LineA);
        lineA.GetProperty("quantity").GetDecimal().Should().Be(10m);
        lineA.GetProperty("suggestedLot").GetString().Should().Be("LT-NF", "o lote vem do rastro do XML");

        var preview = await s.Receiver.PostAsJsonAsync($"{Invoices}/{invoiceId}/receipt/preview", Body(defaults));
        preview.StatusCode.Should().Be(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var impact = await JsonAsync(preview);
        impact.GetProperty("fromDuplicates").GetBoolean().Should().BeTrue();
        impact.GetProperty("receipt").GetProperty("installments").EnumerateArray().Select(i => i.GetProperty("amount").GetDecimal()).Should().Equal(545m, 545m);

        var created = await RegisterAsync(s.Receiver, invoiceId, Body(defaults));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var receipt = await JsonAsync(created);
        var receiptId = receipt.GetProperty("id").GetGuid();
        receipt.GetProperty("origin").GetString().Should().Be("Invoice");
        receipt.GetProperty("incomingInvoiceId").GetGuid().Should().Be(invoiceId);
        receipt.GetProperty("amount").GetDecimal().Should().Be(1090m);

        var movements = await MovementsAsync(receiptId);
        movements.Should().HaveCount(2);
        movements.Single(m => m.ItemId == s.ItemA).UnitCost.Should().Be(54.5m, "(500 + frete 20 + IPI 25) ÷ 10");
        movements.Single(m => m.ItemId == s.ItemB).UnitCost.Should().Be(109m, "(500 + frete 20 + IPI 25) ÷ 5");
        (await WithDbAsync(db => db.Lots.AsNoTracking().AnyAsync(l => l.ItemId == s.ItemA && l.LotNumber == "LT-NF"))).Should().BeTrue();

        var titles = await PayablesAsync(DocumentOrigin.GoodsReceipt, receiptId);
        titles.Select(t => (t.Installment, t.Amount, t.DueDate, t.Status)).Should().Equal(
            ((short)1, 545m, (DateOnly?)Today.AddDays(10), PayableStatus.Open), ((short)2, 545m, (DateOnly?)Today.AddDays(40), PayableStatus.Open));
        titles.Should().OnlyContain(t => !t.IsEstimate && t.Description.StartsWith("NF-e ", StringComparison.Ordinal));
        Planned(await PayablesAsync(DocumentOrigin.Purchase, s.OrderId)).Should().Be(0m, "o pedido foi todo recebido");

        var invoice = await InvoiceAsync(invoiceId);
        invoice.Status.Should().Be(IncomingInvoiceStatus.Received);
        invoice.GoodsReceiptId.Should().Be(receiptId);
        invoice.InputTaxCreditApplied.Should().Be("Sem crédito de impostos (a confirmar com a contadora)");

        var again = await RegisterAsync(s.Receiver, invoiceId, Body(defaults));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(again);
        Code(problem).Should().Be("incoming_invoice_received");
        problem.GetRawText().Should().Contain("/app/compras/recebimentos/" + receiptId);
        (await DefaultsAsync(s.Receiver, invoiceId)).GetProperty("goodsReceiptId").GetGuid().Should().Be(receiptId, "recebida: o modo leitura leva ao recebimento");
    }

    /// <summary>CA 3: preço da nota diferente do pedido bloqueia o recebimento (422) e "Pedir correção de preço" avisa quem aprova compras.</summary>
    [Fact]
    public async Task Preco_da_nota_diferente_do_pedido_bloqueia_e_pede_a_correcao_a_quem_aprova()
    {
        var s = await ScenarioAsync();
        var invoiceId = await CheckedInvoiceAsync(s, 40m, 0m, TwoDuplicates(555m), Standard(s, priceA: 52m));

        var defaults = await DefaultsAsync(s.Receiver, invoiceId);
        defaults.GetProperty("priceBlocked").GetBoolean().Should().BeTrue();
        defaults.GetProperty("blocker").GetString().Should().Be("O preço da nota é diferente do pedido. Peça a correção do preço a quem aprova compras.");
        var refused = await RegisterAsync(s.Receiver, invoiceId, Body(defaults, "LT-1"));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(refused)).Should().Be("incoming_invoice_price_difference");
        (await WithDbAsync(db => db.GoodsReceipts.CountAsync(r => r.PurchaseOrderId == s.OrderId))).Should().Be(0);

        var asked = await s.Receiver.PostAsync($"{Invoices}/{invoiceId}/price-correction-request", null);
        asked.StatusCode.Should().Be(HttpStatusCode.OK, await asked.Content.ReadAsStringAsync());
        var result = await JsonAsync(asked);
        result.GetProperty("notified").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("lines").EnumerateArray().Select(l => l.GetGuid()).Should().Equal(s.LineA);
    }

    /// <summary>CA 7: sem duplicatas, a condição do pedido conta a partir da emissão da nota (não da data do recebimento).</summary>
    [Fact]
    public async Task Sem_duplicatas_os_vencimentos_vem_da_condicao_do_pedido_a_partir_da_emissao()
    {
        var s = await ScenarioAsync();
        var invoiceId = await CheckedInvoiceAsync(s, 40m, 0m, [], Standard(s));
        var issuedOn = BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness((await InvoiceAsync(invoiceId)).IssuedAt);

        var defaults = await DefaultsAsync(s.Receiver, invoiceId);
        var created = await RegisterAsync(s.Receiver, invoiceId, Body(defaults));

        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var titles = await PayablesAsync(DocumentOrigin.GoodsReceipt, (await JsonAsync(created)).GetProperty("id").GetGuid());
        titles.Should().ContainSingle().Which.DueDate.Should().Be(DateOnly.FromDateTime(issuedOn.DateTime), "À vista a partir da emissão");
        titles[0].Amount.Should().Be(1090m);
    }

    /// <summary>FIS-05 CA 8: a nota lida do PDF (sem XML) é recebida pelo mesmo caminho e a marca "Lida do PDF, sem XML" vai para os títulos a pagar.</summary>
    [Fact]
    public async Task Nota_lida_do_pdf_e_recebida_igual_e_leva_a_marca_nos_titulos()
    {
        var s = await ScenarioAsync();
        var invoiceId = await CheckedInvoiceAsync(s, 40m, 0m, [], Standard(s));
        await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fiscal.incoming_invoices SET source = 'Pdf', signature_valid = false, protocol = '' WHERE id = {invoiceId}"));

        var created = await RegisterAsync(s.Receiver, invoiceId, Body(await DefaultsAsync(s.Receiver, invoiceId)));

        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var titles = await PayablesAsync(DocumentOrigin.GoodsReceipt, (await JsonAsync(created)).GetProperty("id").GetGuid());
        titles.Should().ContainSingle().Which.Description.Should().Contain(BemAqui.Fiscal.Domain.IncomingReadingRules.PdfMark);
    }

    /// <summary>
    /// CA 8 e 9: o item "Sem item de estoque" não entra no estoque e vira títulos próprios (origem <c>IncomingInvoice</c>) na conta escolhida,
    /// um por duplicata; as duplicatas que não somam o total da nota não bloqueiam e deixam o aviso nos títulos.
    /// </summary>
    [Fact]
    public async Task Item_sem_estoque_vira_titulos_proprios_e_duplicatas_que_nao_fecham_deixam_o_aviso()
    {
        var s = await ScenarioAsync();
        var account = await ExpenseAccountAsync();
        var invoiceId = await CheckedInvoiceAsync(s, 40m, 0m, TwoDuplicates(600m), [.. Standard(s), new NoteItem(null, null, 1m, 100m, Account: account)]);

        var defaults = await DefaultsAsync(s.Receiver, invoiceId);
        defaults.GetProperty("noStock").EnumerateArray().Should().ContainSingle().Which.GetProperty("amount").GetDecimal().Should().Be(100m);
        var created = await RegisterAsync(s.Receiver, invoiceId, Body(defaults));

        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var receiptId = (await JsonAsync(created)).GetProperty("id").GetGuid();
        (await MovementsAsync(receiptId)).Should().HaveCount(2, "o item sem estoque não entra no estoque");
        var receiptTitles = await PayablesAsync(DocumentOrigin.GoodsReceipt, receiptId);
        receiptTitles.Sum(t => t.Amount).Should().Be(1090m, "os títulos do recebimento ficam pelo valor do recebimento");
        var own = await PayablesAsync(DocumentOrigin.IncomingInvoice, invoiceId);
        own.Should().HaveCount(2);
        own.Sum(t => t.Amount).Should().Be(100m);
        own.Should().OnlyContain(t => t.AccountId == account && t.Status == PayableStatus.Open);
        receiptTitles.Concat(own).Should().OnlyContain(t => t.Description.Contains("As duplicatas não somam o total da nota"));
    }

    /// <summary>CA 5: com o ICMS marcado como recuperável, o ICMS dos itens sai do custo de entrada e fica guardado na nota como crédito.</summary>
    [Fact]
    public async Task Credito_de_ICMS_configurado_sai_do_custo_e_fica_na_nota()
    {
        var s = await ScenarioAsync();
        var invoiceId = await CheckedInvoiceAsync(s, 40m, 0m, TwoDuplicates(545m), Standard(s, icmsA: 90m));
        await SetCreditAsync(["ICMS"]);
        try
        {
            var defaults = await DefaultsAsync(s.Receiver, invoiceId);
            defaults.GetProperty("creditRule").GetString().Should().StartWith("Crédito de ICMS");
            var created = await RegisterAsync(s.Receiver, invoiceId, Body(defaults));

            created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            var receipt = await JsonAsync(created);
            receipt.GetProperty("taxCredit").GetDecimal().Should().Be(90m);
            receipt.GetProperty("amount").GetDecimal().Should().Be(1090m, "o crédito não muda o que se paga ao fornecedor");
            (await MovementsAsync(receipt.GetProperty("id").GetGuid())).Single(m => m.ItemId == s.ItemA).UnitCost.Should().Be(45.5m, "(500 + 20 + 25 − 90) ÷ 10");
            var invoice = await InvoiceAsync(invoiceId);
            invoice.InputTaxCreditAmount.Should().Be(90m);
            invoice.InputTaxCreditApplied.Should().StartWith("Crédito de ICMS");
        }
        finally
        {
            await SetCreditAsync([]);
        }
    }

    /// <summary>
    /// CA 6: falha no meio desfaz tudo. Com a conta da parte sem item de estoque desativada depois da conferência, o Finance recusa o título
    /// dela depois da entrada no estoque e dos títulos do recebimento: nada fica (recebimento, movimento, lote, título, nota recebida).
    /// </summary>
    [Fact]
    public async Task Recusa_de_um_titulo_desfaz_o_recebimento_a_entrada_e_a_nota()
    {
        var s = await ScenarioAsync();
        var account = await ExpenseAccountAsync();
        var invoiceId = await CheckedInvoiceAsync(s, 40m, 0m, TwoDuplicates(595m), [.. Standard(s), new NoteItem(null, null, 1m, 100m, Account: account)]);
        var defaults = await DefaultsAsync(s.Receiver, invoiceId);
        await SetAccountActiveAsync(account, false);
        try
        {
            var refused = await RegisterAsync(s.Receiver, invoiceId, Body(defaults));

            refused.StatusCode.Should().Be(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
            Code(await JsonAsync(refused)).Should().Be("payable_account_missing");
        }
        finally
        {
            await SetAccountActiveAsync(account, true);
        }

        (await WithDbAsync(db => db.GoodsReceipts.CountAsync(r => r.PurchaseOrderId == s.OrderId))).Should().Be(0);
        (await WithDbAsync(db => db.StockMovements.CountAsync(m => m.ItemId == s.ItemA || m.ItemId == s.ItemB))).Should().Be(0);
        (await WithDbAsync(db => db.Lots.CountAsync(l => l.ItemId == s.ItemA))).Should().Be(0);
        (await PayablesAsync(DocumentOrigin.IncomingInvoice, invoiceId)).Should().BeEmpty();
        (await InvoiceAsync(invoiceId)).Status.Should().Be(IncomingInvoiceStatus.Checked);
        (await GetAsync(s.Owner, s.OrderId)).GetProperty("status").GetString().Should().Be("Approved");
    }

    /// <summary>CA 11: o estorno cancela os títulos das duas origens, tira o estoque e devolve a nota a <c>Checked</c>; ela pode ser recebida de novo.</summary>
    [Fact]
    public async Task Estorno_cancela_os_titulos_das_duas_origens_e_a_nota_volta_a_conferida()
    {
        var s = await ScenarioAsync();
        var account = await ExpenseAccountAsync();
        var invoiceId = await CheckedInvoiceAsync(s, 40m, 0m, TwoDuplicates(595m), [.. Standard(s), new NoteItem(null, null, 1m, 100m, Account: account)]);
        var created = await RegisterAsync(s.Receiver, invoiceId, Body(await DefaultsAsync(s.Receiver, invoiceId)));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var receipt = await JsonAsync(created);
        var receiptId = receipt.GetProperty("id").GetGuid();

        var reversed = await s.Owner.PostAsJsonAsync($"{Receipts}/{receiptId}/reverse", new { reason = "Nota lançada no pedido errado", version = Version(receipt) });

        reversed.StatusCode.Should().Be(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync());
        (await PayablesAsync(DocumentOrigin.GoodsReceipt, receiptId)).Should().OnlyContain(t => t.Status == PayableStatus.Cancelled);
        (await PayablesAsync(DocumentOrigin.IncomingInvoice, invoiceId)).Should().OnlyContain(t => t.Status == PayableStatus.Cancelled);
        var invoice = await InvoiceAsync(invoiceId);
        invoice.Status.Should().Be(IncomingInvoiceStatus.Checked);
        invoice.GoodsReceiptId.Should().BeNull();
        Planned(await PayablesAsync(DocumentOrigin.Purchase, s.OrderId)).Should().Be(1090m, "o saldo volta ao previsto do pedido");

        var again = await RegisterAsync(s.Receiver, invoiceId, Body(await DefaultsAsync(s.Receiver, invoiceId)));
        again.StatusCode.Should().Be(HttpStatusCode.Created, await again.Content.ReadAsStringAsync());
        (await PayablesAsync(DocumentOrigin.IncomingInvoice, invoiceId)).Count(t => t.Status == PayableStatus.Open).Should().Be(2);
    }

    /// <summary>CA 2 e 12: <c>purchasing.create</c> sem <c>purchasing.receive</c> é 403; quem pediu o pedido não recebe pela nota (segregação); nota de outro id é 404.</summary>
    [Fact]
    public async Task Sem_receive_ou_quem_pediu_nao_recebe_e_nota_inexistente_e_404()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, requester) = await ActorAsync("purchasing:view,create,edit,receive");
        var (_, buyerOnly) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, requester, supplier, 0m, 0m, "À vista", null, (item, 2m, 10m, null));
        var s = new Scenario(owner, receiver, order, order.GetProperty("id").GetGuid(), supplier, item, item, LineOf(order, item), LineOf(order, item));
        var invoiceId = await CheckedInvoiceAsync(s, 0m, 0m, [], new NoteItem(s.LineA, item, 2m, 10m));
        var body = Body(await DefaultsAsync(receiver, invoiceId));

        (await RegisterAsync(buyerOnly, invoiceId, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var segregated = await RegisterAsync(requester, invoiceId, body);
        segregated.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(segregated)).Should().Be("segregation_of_duties");
        (await receiver.GetAsync($"{Invoices}/{Guid.CreateVersion7()}/receipt-defaults")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RegisterAsync(receiver, Guid.CreateVersion7(), body)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>CA 13: dois "Receber pela nota" ao mesmo tempo (chaves diferentes): só um passa; o estoque entra uma vez e os títulos não duplicam.</summary>
    [Fact]
    public async Task Dois_recebimentos_da_mesma_nota_ao_mesmo_tempo_so_um_passa()
    {
        var s = await ScenarioAsync();
        var (_, second) = await ActorAsync("warehouse");
        var invoiceId = await CheckedInvoiceAsync(s, 40m, 0m, TwoDuplicates(545m), Standard(s));
        var body = Body(await DefaultsAsync(s.Receiver, invoiceId));

        var results = await Task.WhenAll(RegisterAsync(s.Receiver, invoiceId, body), RegisterAsync(second, invoiceId, body));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, string.Join(" | ", await Task.WhenAll(results.Select(r => r.Content.ReadAsStringAsync()))));
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        (await WithDbAsync(db => db.GoodsReceipts.CountAsync(r => r.IncomingInvoiceId == invoiceId))).Should().Be(1);
        (await WithDbAsync(db => db.StockMovements.Where(m => m.ItemId == s.ItemA).SumAsync(m => m.Quantity))).Should().Be(10m);
        var receiptId = await WithDbAsync(db => db.GoodsReceipts.Where(r => r.IncomingInvoiceId == invoiceId).Select(r => r.Id).SingleAsync());
        (await PayablesAsync(DocumentOrigin.GoodsReceipt, receiptId)).Should().HaveCount(2);
    }

    private Task<int> SetCreditAsync(string[] taxes) => WithDbAsync(async db =>
    {
        var settings = await db.FiscalSettings.SingleAsync();
        settings.InputTaxCredit = taxes;
        return await db.SaveChangesAsync();
    });

    private Task<int> SetAccountActiveAsync(Guid id, bool active) => WithDbAsync(async db =>
    {
        var account = await db.Accounts.SingleAsync(a => a.Id == id);
        account.IsActive = active;
        return await db.SaveChangesAsync();
    });
}
