using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-03a, regras sem banco: o destinatário pelo cadastro e a operação pelo tipo do item (CA 1), o rateio do frete e o cálculo refeito sem
/// imposto digitado (CA 3), o saneamento e os limites do esquema (CA 4), as pré-condições da nota (CA 6) e o pedido ao Emissor (sem duplicatas,
/// D4; informações do item e transporte nas complementares enquanto a lacuna L-d não fecha).
/// </summary>
public class OutgoingInvoiceRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 31);

    private static TaxRuleCandidates Candidates(bool requireConfirmed = false)
    {
        var rules = TaxRuleSeeds.Build(FiscalSettingsService.NewDefaults(Now), Today.AddDays(-1), Now);
        foreach (var rule in rules)
        {
            rule.Activate(Guid.CreateVersion7(), Now, false);
        }

        return new TaxRuleCandidates(rules, new TaxIssuer("SP", 3), requireConfirmed, Today);
    }

    private static (OutgoingInvoice Invoice, List<OutgoingInvoiceItem> Items) Draft(string state = "SP", RecipientType type = RecipientType.Contributor, params decimal[] prices)
    {
        var invoice = OutgoingInvoice.NewDraft(Guid.CreateVersion7(), "ROM-2026-0001", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null, Now);
        invoice.RecipientName = "CLIENTE LTDA";
        invoice.RecipientDocument = "11222333000181";
        invoice.RecipientType = type;
        invoice.RecipientState = state;
        invoice.RecipientStreet = "Rua A";
        invoice.RecipientCityCode = "3550308";
        var items = (prices.Length == 0 ? [100_000m] : prices).Select((p, i) => new OutgoingInvoiceItem
        {
            InvoiceId = invoice.Id, Sequence = i + 1, ItemId = Guid.CreateVersion7(), Code = "ELEV-" + i, Description = "Elevador", Ncm = "84281000", Unit = "UN",
            ShippedQuantity = 1, Quantity = 1, UnitPrice = p, IpiRate = 5m, QuoteItemKind = "Equipment", Operation = TaxOperation.OwnProductionSale,
        }).ToList();
        return (invoice, items);
    }

    [Theory]
    [InlineData(true, "110042490114", RecipientType.Contributor)]
    [InlineData(true, "ISENTO", RecipientType.Exempt)]
    [InlineData(true, null, RecipientType.NonContributor)]
    [InlineData(false, "110042490114", RecipientType.NonContributor)]
    public void Tipo_do_destinatario_vem_do_cadastro(bool company, string? ie, RecipientType expected) =>
        OutgoingInvoiceRules.RecipientTypeOf(company, ie).Should().Be(expected);

    [Theory]
    [InlineData("Equipment", false, TaxOperation.OwnProductionSale)]
    [InlineData("Product", true, TaxOperation.OwnProductionSale)]
    [InlineData("Product", false, TaxOperation.Resale)]
    public void Operacao_pelo_tipo_do_item_nunca_remessa(string kind, bool fabricated, TaxOperation expected) =>
        OutgoingInvoiceRules.OperationFor(kind, fabricated).Should().Be(expected);

    [Fact]
    public void Rateio_do_frete_fecha_no_centavo()
    {
        var parts = OutgoingInvoiceRules.Apportion([100m, 200m, 0.01m], 100m);
        parts.Sum().Should().Be(100m);
        parts[0].Should().Be(33.33m);
        OutgoingInvoiceRules.Apportion([0m, 0m], 10m).Sum().Should().Be(10m);
    }

    [Fact]
    public void Calculo_refeito_pela_regra_ativa_soma_os_itens_e_o_custo_do_projeto()
    {
        var (invoice, items) = Draft(prices: [100_000m, 50_000m]);
        invoice.Freight = 1_500m;

        OutgoingInvoiceCalculator.Recalculate(invoice, items, Candidates());

        invoice.CalculationError.Should().BeNull();
        items.Select(i => i.Freight).Should().Equal(1_000m, 500m);
        items.Should().OnlyContain(i => i.Cfop == "5101" && i.TaxRuleVersionId != null);
        invoice.Totals.Products.Should().Be(150_000m);
        invoice.TotalAmount.Should().Be(items.Sum(i => i.Total)).And.Be(151_500m * 1.05m, "IPI de 5% sobre a base com frete");
        invoice.CostTaxAmount.Should().Be(items.Sum(i => i.CostTaxAmount));
        invoice.TaxAmount.Should().Be(invoice.Totals.Taxes.Sum(t => t.Amount));
        invoice.NatureOfOperation.Should().Be("Venda de produção do estabelecimento");
    }

    [Fact]
    public void Sem_regra_ou_sem_aliquota_do_IPI_a_nota_guarda_o_motivo_e_nao_emite()
    {
        var (invoice, items) = Draft();
        items[0].IpiRate = null;
        OutgoingInvoiceCalculator.Recalculate(invoice, items, Candidates());
        invoice.CalculationError.Should().Contain("alíquota do IPI na TIPI");
        OutgoingInvoiceRules.InvoicePreconditions(invoice, items, "/app/terceiros/x").Single(p => p.Key == "calculation").Ok.Should().BeFalse();

        items[0].IpiRate = 5m;
        OutgoingInvoiceCalculator.Recalculate(invoice, items, Candidates(requireConfirmed: true));
        invoice.CalculationError.Should().StartWith("Não há regra fiscal", "em produção só regra conferida (FIS-02b CA 4)");
        OutgoingInvoiceRules.InvoicePreconditions(invoice, items, "/x").Single(p => p.Key == "rules").Ok.Should().BeFalse();
    }

    [Fact]
    public void DIFAL_bloqueia_a_emissao()
    {
        var (invoice, items) = Draft("MG", RecipientType.NonContributor);
        OutgoingInvoiceCalculator.Recalculate(invoice, items, Candidates());
        invoice.FinalConsumer.Should().BeTrue();
        OutgoingInvoiceRules.InvoicePreconditions(invoice, items, "/x").Single(p => p.Key == "difal").Ok.Should().BeFalse();
    }

    [Fact]
    public void Saneamento_tira_controle_normaliza_espacos_e_recusa_pelo_limite_do_esquema()
    {
        NfeTextRules.Clean("  Elevador\t\tde\u0007 carga\r\n2 paradas​ ").Should().Be("Elevador de carga 2 paradas");
        NfeTextRules.Clean(" \t ").Should().BeNull();
        var (invoice, items) = Draft();
        items[0].Description = new string('x', 121);
        items[0].AdditionalInfo = new string('y', 501);
        invoice.RecipientName = new string('z', 61);

        var errors = OutgoingInvoiceRules.TextErrors(invoice, items, null);

        errors["items[0].description"].Should().Equal("A descrição do item 1 passa de 120 caracteres.");
        errors.Should().ContainKey("items[0].additionalInfo").And.ContainKey("recipient.name");
        OutgoingInvoiceRules.TextErrors(invoice, items, new string('c', 5001)).Should().ContainKey("additionalInformation");
    }

    [Fact]
    public void Pre_condicoes_da_nota_cobram_documento_e_IBGE_do_cliente()
    {
        var (invoice, items) = Draft();
        invoice.RecipientDocument = null;
        invoice.RecipientCityCode = "35503";
        var list = OutgoingInvoiceRules.InvoicePreconditions(invoice, items, "/app/terceiros/1");
        list.Single(p => p.Key == "recipientDocument").Should().Match<IssuePrecondition>(p => !p.Ok && p.Link == "/app/terceiros/1");
        list.Single(p => p.Key == "recipientAddress").Ok.Should().BeFalse();
    }

    [Fact]
    public void Pedido_ao_Emissor_sem_duplicatas_com_os_tributos_do_calculo_e_as_informacoes_nas_complementares()
    {
        var (invoice, items) = Draft();
        invoice.AdditionalInformation = OutgoingInvoiceRules.SiteInfo("Rua da Obra, 100");
        invoice.Volumes = 2;
        invoice.GrossWeight = 850.5m;
        items[0].AdditionalInfo = OutgoingInvoiceRules.SerialsInfo(["SN-1", "SN-2"]);
        OutgoingInvoiceCalculator.Recalculate(invoice, items, Candidates());

        var request = OutgoingNfePayloads.Build(invoice, items, "47225005000186", 2, "Transportes X");

        request.Series.Should().Be(2);
        request.Recipient.Cnpj.Should().Be("11222333000181");
        request.Recipient.StateRegistrationIndicator.Should().Be(NfeStateRegistration.Taxpayer);
        request.Payments.Should().ContainSingle().Which.Method.Should().Be(NfePaymentMethod.NoPayment);
        var item = request.Items.Should().ContainSingle().Subject;
        item.Cfop.Should().Be("5101");
        item.Taxes.Icms.Amount.Should().Be(18_000m);
        item.Taxes.Ipi!.Amount.Should().Be(5_000m);
        item.Taxes.IbsCbs!.Cbs!.Amount.Should().Be(900m);
        request.AdditionalInformation.Should().Be("Endereço da obra: Rua da Obra, 100 | Item 1: Séries: SN-1, SN-2 | Transporte: transportadora Transportes X, 2 volumes, peso bruto 850,5 kg.");
        NfePayload.Hash(request).Should().Be(NfePayload.Hash(OutgoingNfePayloads.Build(invoice, items, "47225005000186", 2, "Transportes X")), "o mesmo pedido dá o mesmo hash (reenvio)");
    }

    [Fact]
    public void Rejeitada_volta_ao_rascunho_com_a_proxima_tentativa_e_denegada_nao()
    {
        var (invoice, _) = Draft();
        invoice.BeginSubmission(new string('a', 64), Now);
        invoice.Apply(new EmitterNfeState(Guid.CreateVersion7(), FiscalDocumentStatus.Rejected, "1".PadLeft(44, '3'), 2, 7, null, null, 225, "Rejeição", "Corrija"), Now);

        invoice.Reopen(null, Now).Should().BeNull();
        (invoice.Status, invoice.Attempt, invoice.Number, invoice.IdempotencyKey).Should().Be((OutgoingInvoiceStatus.Draft, 2, (long?)null, $"nfe-{invoice.Id:D}-2"));
        invoice.Discard(null, Now).Should().BeNull();
        invoice.IsActive.Should().BeFalse("descartado sai do índice de nota ativa");

        var (denied, _) = Draft();
        denied.BeginSubmission(new string('a', 64), Now);
        denied.Apply(new EmitterNfeState(Guid.CreateVersion7(), FiscalDocumentStatus.Denied, "2".PadLeft(44, '3'), 2, 8, null, null, 301, "Denegado", null), Now);
        denied.Reopen(null, Now)!.Code.Should().Be(FiscalErrorCodes.InvoiceDenied);
    }
}
