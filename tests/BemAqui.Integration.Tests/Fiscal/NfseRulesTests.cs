using BemAqui.Commercial.Domain;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Application.Invoices.Nfse;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-06, regras sem banco: o rascunho pela regra de prestação de serviço com o ISS, as retenções e o IBS/CBS (CA 2), quando o Emissor emite
/// a NFS-e (lacuna L-m, CA 3 e 5), a conferência do registro do portal e a comparação do ISS digitado (CA 6), o projetado com o ISS do orçado
/// até a nota (CA 7), as transições da NFS-e e o pedido ao Emissor com o hash estável.
/// </summary>
public class NfseRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 1);

    private static (OutgoingInvoice Invoice, List<OutgoingInvoiceItem> Items) Draft(params decimal[] prices)
    {
        var invoice = OutgoingInvoice.NewNfseDraft(Guid.CreateVersion7(), "OSI-1", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null, Now);
        invoice.RecipientState = "SP";
        invoice.RecipientType = RecipientType.Contributor;
        var sequence = 0;
        var items = prices.Select(p => new OutgoingInvoiceItem
        {
            InvoiceId = invoice.Id, Sequence = ++sequence, Code = "SERVICO", Description = "Instalação", QuoteItemKind = "Service", Operation = TaxOperation.ServiceProvision, Quantity = 1,
            ShippedQuantity = 1, UnitPrice = p,
        }).ToList();
        var rules = TaxRuleSeeds.Build(FiscalSettingsService.NewDefaults(Now), Today.AddDays(-1), Now);
        foreach (var rule in rules)
        {
            rule.Activate(Guid.CreateVersion7(), Now, false);
        }

        OutgoingInvoiceCalculator.Recalculate(invoice, items, new TaxRuleCandidates(rules, new TaxIssuer("SP", 3), false, Today));
        return (invoice, items);
    }

    [Fact]
    public void Rascunho_pela_regra_de_servico_tem_ISS_PIS_COFINS_e_IBS_CBS_e_nenhum_ICMS_ou_IPI()
    {
        var (invoice, items) = Draft(10_000m);

        invoice.CalculationError.Should().BeNull();
        invoice.Operation.Should().Be(TaxOperation.ServiceProvision);
        var taxes = items[0].Taxes.ToDictionary(t => t.Code, t => t.Amount);
        taxes.Should().ContainKey(TaxCodes.Iss).WhoseValue.Should().Be(500m);
        taxes.Should().NotContainKey(TaxCodes.Icms).And.NotContainKey(TaxCodes.Ipi);
        items[0].Cfop.Should().BeNull("serviço não tem CFOP");
        invoice.TotalAmount.Should().Be(10_000m, "o ISS não soma ao valor do serviço");
        invoice.IdempotencyKey.Should().Be($"nfse-{invoice.Id:D}-1");
    }

    [Fact]
    public void Discriminacao_sugerida_e_saneada_e_cabe_no_limite()
    {
        NfseRules.Discrimination("Elevador\tde  carga\u0007", "PRJ-2026-0001", "OSI-0001").Should().Be("Instalação de Elevador de carga, projeto PRJ-2026-0001, OS OSI-0001");
        NfseRules.Discrimination(null, "P", "O").Should().Be("Instalação de equipamento, projeto P, OS O");
        NfseRules.Discrimination(new string('x', 3000), "P", "O").Should().HaveLength(NfseRules.DescriptionMaxLength);
    }

    [Theory]
    [InlineData(new[] { "consultar", "nfse:emitir" }, new[] { "3508504" }, "3508504", true)]
    [InlineData(new[] { "consultar", "nfse:emitir" }, null, "3508504", false)]
    [InlineData(new[] { "consultar" }, new[] { "3508504" }, "3508504", false)]
    [InlineData(new[] { "nfse:emitir" }, new[] { "3550308" }, "3508504", false)]
    public void Emissor_so_emite_com_o_escopo_e_o_municipio_habilitado(string[] scopes, string[]? cities, string city, bool expected) =>
        NfseRules.EmitterEnabled(scopes, cities, city).Should().Be(expected);

    [Fact]
    public void ISS_digitado_diferente_do_calculado_avisa_sem_bloquear_e_um_centavo_passa()
    {
        NfseRules.IssDifference(480m, 500m).Should().Be("O ISS da nota é diferente do calculado (R$ 500,00). Confira com a contadora.");
        NfseRules.IssDifference(500.01m, 500m).Should().BeNull();
        NfseRules.IssDifference(500m, 500m).Should().BeNull();
    }

    [Fact]
    public void Registro_do_portal_confere_numero_codigo_data_valor_e_ISS()
    {
        NfseRules.ParseNumber(" 000.123-4 ").Should().Be(1234);
        NfseRules.ParseNumber("abc").Should().BeNull();
        NfseRules.ParseNumber("0").Should().BeNull();
        NfseRules.RegistrationErrors(1234, "AB12", Today, 10_000m, 500m, Today, Today.AddDays(-1)).Should().BeEmpty();
        var errors = NfseRules.RegistrationErrors(null, " ", Today.AddDays(1), 0m, -1m, Today, Today.AddDays(-1));
        errors.Keys.Should().BeEquivalentTo(["number", "verificationCode", "issuedOn", "servicesAmount", "issAmount"]);
        NfseRules.RegistrationErrors(1, "X", Today.AddDays(-5), 100m, 5m, Today, Today.AddDays(-1)).Keys.Should().Equal(["issuedOn"], "a nota não é de antes do aceite técnico");
        NfseRules.CancellationErrors("curto", null).Keys.Should().BeEquivalentTo(["reason", "protocol"]);
        NfseRules.CancellationErrors("Valor digitado errado na prefeitura", "P-1").Should().BeEmpty();
    }

    [Fact]
    public void Registro_do_portal_autoriza_com_o_ISS_da_nota_e_o_cancelamento_registrado_fecha_a_nota()
    {
        var (invoice, items) = Draft(6_000m, 4_000m);
        NfseInvoiceService.ApplyTypedIss(items, 480m);
        items.SelectMany(i => i.Taxes).Where(t => t.Code == TaxCodes.Iss).Select(t => t.Amount).Should().Equal(288m, 192m);
        invoice.ApplyTotals(OutgoingInvoiceCalculator.Totals(items), null);

        invoice.RegisterFromPortal(9_001, "AB12", 10_000m, 480m, null, Now).Should().BeNull();

        (invoice.Status, invoice.Origin, invoice.Number, invoice.IssTyped, invoice.TotalAmount).Should().Be((OutgoingInvoiceStatus.Authorized, InvoiceOrigin.PortalRegistered, 9_001L, 480m, 10_000m));
        invoice.RegisterFromPortal(9_002, "X", 1m, 0m, null, Now).Should().NotBeNull("só o rascunho é registrado");
        invoice.MarkAuthorizationPublished(Today, Now).Should().BeTrue();
        invoice.RegisterPortalCancellation("Valor digitado errado na prefeitura", "P-1", null, Now, Today).Should().BeNull();
        invoice.Status.Should().Be(OutgoingInvoiceStatus.Cancelled);
        invoice.PortalCancellation!.Protocol.Should().Be("P-1");
        invoice.RegisterPortalCancellation("Valor digitado errado na prefeitura", "P-2", null, Now, Today)!.Code.Should().Be(FiscalErrorCodes.NotAuthorized);
    }

    [Fact]
    public void Resposta_do_Emissor_traz_o_numero_e_o_codigo_sem_serie_e_sem_regredir()
    {
        var (invoice, _) = Draft(10_000m);
        invoice.BeginSubmission(new string('a', 64), Now).Should().BeNull();
        var doc = Guid.CreateVersion7();
        invoice.ApplyNfse(new BemAqui.Fiscal.Application.Emitter.NfseDocument(doc, FiscalDocumentStatus.Authorized, "00123", "COD-9", null, null).ToState(), "COD-9", Now).Should().BeTrue();
        (invoice.Status, invoice.Number, invoice.VerificationCode, invoice.Series).Should().Be((OutgoingInvoiceStatus.Authorized, 123L, "COD-9", (int?)null));
        invoice.ApplyNfse(new BemAqui.Fiscal.Application.Emitter.NfseDocument(doc, FiscalDocumentStatus.Processing, null, null, null, null).ToState(), null, Now).Should().BeFalse();
        invoice.Status.Should().Be(OutgoingInvoiceStatus.Authorized);
    }

    [Fact]
    public void Pedido_ao_Emissor_leva_o_servico_o_ISS_e_o_hash_e_estavel()
    {
        var (invoice, items) = Draft(10_000m);
        invoice.RecipientDocument = "47225005000186";
        invoice.RecipientName = "CLIENTE";
        invoice.Service = new NfseServiceInfo("7.02", "7.02.01", "070201000", "120011000", "3508504", "Caçapava", IssLocation.WorkSite, 5m, 500m, true, false, "Instalação", "000", "000001",
            NfseRules.NetAmount(10_000m, 500m, true));

        var request = NfsePayloads.Build(invoice, items, "47225005000186", "35261047225005000186550010000000421234567890");

        (request.Series, request.Service.IssAmount, request.Service.NetAmount, request.Service.IssWithheld, request.Service.Base).Should().Be(("1", 500m, 9_500m, true, 10_000m));
        request.Service.IbsCbs!.OperationCode.Should().HaveLength(6);
        request.LinkedNfeAccessKey.Should().HaveLength(44);
        request.Taker.Cnpj.Should().Be("47225005000186");
        NfsePayloads.Hash(request).Should().Be(NfsePayloads.Hash(request with { Service = request.Service with { IssAmount = 500.00m } }));
        NfsePayloads.Hash(request).Should().NotBe(NfsePayloads.Hash(request with { Service = request.Service with { IssAmount = 480m } }));
    }

    [Fact]
    public void Projetado_usa_o_ISS_do_orcado_ate_a_nota_e_depois_o_da_nota()
    {
        var budget = new Dictionary<CostType, decimal> { [CostType.Tax] = 1_000m };

        var before = ProjectMarginCalculator.Compute(110_000m, budget, [], []);
        before.Lines.Single(l => l.CostType == CostType.Tax).Projected.Should().Be(1_000m, "sem nota, o imposto orçado (D5)");
        before.TaxBasis.Should().Be(TaxProjectionBasis.Budgeted);

        var after = ProjectMarginCalculator.Compute(110_000m, budget, [], [new LedgerCostTotals(CostType.Tax, ProjectCostSource.InvoiceTax, false, 965m, 10_000m)]);
        var tax = after.Lines.Single(l => l.CostType == CostType.Tax);
        tax.Realized.Should().Be(965m, "o ISS realizado pela NFS-e");
        tax.Projected.Should().Be(965m + Math.Round(1_000m * (1 - (10_000m / 110_000m)), 2, MidpointRounding.AwayFromZero));
        after.TaxBasis.Should().Be(TaxProjectionBasis.Invoices);
    }
}
