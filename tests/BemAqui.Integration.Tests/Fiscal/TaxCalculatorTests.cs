using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Application.TaxRules;
using BemAqui.Fiscal.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-02b, regras sem banco: o motor <see cref="TaxCalculator"/> com a tabela de casos de referência (CA 7), o DIFAL calculado e
/// bloqueado (CA 8), a resolução por especificidade e a recusa sem regra (CA 5 e 9), a sobreposição (CA 5), a validação (CA 2), o aviso de
/// revisão (CA 6) e o seed a partir dos valores da FIS-02a (CA 1).
/// <para><b>Casos sintéticos</b>, tirados da legislação e dos valores pré-configurados (a confirmar com a contadora); os casos dela entram
/// nesta mesma tabela quando chegarem.</para>
/// </summary>
public class TaxCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 31);
    private static readonly TaxIssuer Sp = new("SP", 3);

    private static IReadOnlyList<TaxRule> Seeds() => TaxRuleSeeds.Build(FiscalSettingsService.NewDefaults(Now), Today, Now);

    private static TaxRule Seed(string key) => Seeds().Single(r => r.SeedKey == key);

    private static TaxItem Item(decimal qty = 2, decimal price = 1000, int origin = 0, decimal? ipi = 5m, decimal discount = 0, decimal freight = 0, decimal other = 0) =>
        new("Elevador", "84281000", origin, null, qty, price, discount, freight, other, ipi);

    private static TaxCalculation Calc(TaxRule rule, TaxRecipient recipient, params TaxItem[] items)
    {
        var result = TaxCalculator.Calculate(Sp, recipient, items, [.. items.Select(_ => rule)]);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private static decimal Amount(TaxCalculation c, string code) => c.Totals.Single(t => t.Code == code).Amount;

    private static decimal Base(TaxCalculation c, string code) => c.Totals.Single(t => t.Code == code).Base;

    /// <summary>Tabela de referência do ICMS (CA 7): UF de destino, origem, tipo de destinatário → base, alíquota e valor.</summary>
    public static TheoryData<string, string, int, RecipientType, decimal, decimal, decimal, string> IcmsCases => new()
    {
        // caso, UF destino, origem, destinatário, base, alíquota, ICMS, CFOP
        { "interno SP a contribuinte (18%)", "SP", 0, RecipientType.Contributor, 2000m, 18m, 360m, "5101" },
        { "interestadual Sul/Sudeste (12%)", "MG", 0, RecipientType.Contributor, 2000m, 12m, 240m, "6101" },
        { "interestadual Nordeste (7%)", "BA", 0, RecipientType.Contributor, 2000m, 7m, 140m, "6101" },
        { "interestadual ES (7%)", "ES", 0, RecipientType.Contributor, 2000m, 7m, 140m, "6101" },
        { "importado origem 1 (4%)", "RJ", 1, RecipientType.Contributor, 2000m, 4m, 80m, "6101" },
        { "importado origem 8 (4%)", "PR", 8, RecipientType.Contributor, 2000m, 4m, 80m, "6101" },
        { "interno a não contribuinte: IPI na base (18%)", "SP", 0, RecipientType.NonContributor, 2100m, 18m, 378m, "5101" },
    };

    [Theory]
    [MemberData(nameof(IcmsCases))]
    public void ICMS_da_tabela_de_referencia(string caso, string state, int origin, RecipientType recipient, decimal icmsBase, decimal rate, decimal icms, string cfop)
    {
        var rule = Seed(recipient == RecipientType.Contributor ? TaxRuleSeeds.OwnSaleContributor : TaxRuleSeeds.OwnSaleNonContributor);
        var c = Calc(rule, new TaxRecipient(state, recipient, recipient != RecipientType.Contributor), Item(origin: origin));

        var line = c.Items[0].Taxes.Single(t => t.Code == TaxCodes.Icms);
        line.Base.Should().Be(icmsBase, caso);
        line.Rate.Should().Be(rate, caso);
        line.Amount.Should().Be(icms, caso);
        c.Items[0].Cfop.Should().Be(cfop, caso);
    }

    [Fact]
    public void Venda_interna_a_contribuinte_calcula_todos_os_tributos_e_o_custo_do_projeto()
    {
        var c = Calc(Seed(TaxRuleSeeds.OwnSaleContributor), new TaxRecipient("SP", RecipientType.Contributor, false), Item());

        c.Products.Should().Be(2000m);
        Amount(c, TaxCodes.Ipi).Should().Be(100m, "IPI de 5% pela TIPI do NCM");
        Amount(c, TaxCodes.Icms).Should().Be(360m);
        Amount(c, TaxCodes.Pis).Should().Be(13m, "PIS 0,65%");
        Amount(c, TaxCodes.Cofins).Should().Be(60m, "COFINS 3%");
        Amount(c, TaxCodes.Cbs).Should().Be(18m, "CBS 0,9%");
        Amount(c, TaxCodes.IbsState).Should().Be(2m, "IBS estadual 0,1%");
        Amount(c, TaxCodes.IbsCity).Should().Be(0m);
        c.InvoiceTotal.Should().Be(2100m, "produtos + IPI; IBS/CBS não somam ao total em 2026");
        c.ProjectCost.Should().Be(453m, "ICMS, PIS, COFINS e IBS/CBS entram no custo; IPI fora (CA 11)");
        c.Items[0].Taxes.Single(t => t.Code == TaxCodes.Ipi).InProjectCost.Should().BeFalse();
        c.Blocks.Should().BeEmpty();
    }

    [Fact]
    public void Marca_de_custo_da_regra_controla_o_que_entra_no_custo_do_projeto()
    {
        var rule = Seed(TaxRuleSeeds.OwnSaleContributor);
        rule.CostTaxes = ["ICMS", "IPI"];
        var c = Calc(rule, new TaxRecipient("SP", RecipientType.Contributor, false), Item());
        c.ProjectCost.Should().Be(460m, "ICMS 360 + IPI 100");
    }

    [Fact]
    public void DIFAL_e_calculado_e_mostrado_mas_bloqueia_a_emissao()
    {
        var c = Calc(Seed(TaxRuleSeeds.OwnSaleNonContributor), new TaxRecipient("BA", RecipientType.NonContributor, true), Item());

        Amount(c, TaxCodes.Icms).Should().Be(147m, "7% sobre 2.100 (com o IPI)");
        Amount(c, TaxCodes.Difal).Should().Be(231m, "(18% − 7%) × 2.100, base única");
        c.Items[0].Cfop.Should().Be("6107");
        c.Blocks.Should().Equal(TaxCalculator.DifalBlockedMessage);
        c.Items[0].Taxes.Single(t => t.Code == TaxCodes.Difal).InProjectCost.Should().BeFalse();

        var inState = Calc(Seed(TaxRuleSeeds.OwnSaleNonContributor), new TaxRecipient("SP", RecipientType.NonContributor, true), Item());
        inState.Blocks.Should().BeEmpty("dentro do estado não há DIFAL");
        inState.Totals.Should().NotContain(t => t.Code == TaxCodes.Difal);
    }

    [Fact]
    public void ICMS_ST_com_MVA_desconta_o_ICMS_proprio_e_soma_ao_total()
    {
        var rule = Seed(TaxRuleSeeds.OwnSaleContributor);
        rule.Icms = rule.Icms! with { Cst = "10" };
        rule.IcmsSt = new IcmsStParameters(4, 40m, 18m, 0m);
        var c = Calc(rule, new TaxRecipient("SP", RecipientType.Contributor, false), Item(qty: 1));

        Amount(c, TaxCodes.Icms).Should().Be(180m);
        Base(c, TaxCodes.IcmsSt).Should().Be(1470m, "(1.000 + IPI 50) × 1,40");
        Amount(c, TaxCodes.IcmsSt).Should().Be(84.60m, "264,60 − 180,00");
        c.InvoiceTotal.Should().Be(1134.60m, "produtos + IPI + ST");
    }

    [Fact]
    public void Desconto_frete_e_outras_despesas_entram_na_base_da_operacao()
    {
        var c = Calc(Seed(TaxRuleSeeds.OwnSaleContributor), new TaxRecipient("SP", RecipientType.Contributor, false), Item(qty: 1, ipi: 0m, discount: 100m, freight: 50m, other: 10m));

        Base(c, TaxCodes.Icms).Should().Be(960m);
        Amount(c, TaxCodes.Icms).Should().Be(172.80m);
        Amount(c, TaxCodes.Pis).Should().Be(6.24m);
        Amount(c, TaxCodes.Cofins).Should().Be(28.80m);
        c.InvoiceTotal.Should().Be(960m);
    }

    [Fact]
    public void Reducao_de_base_no_CST_20()
    {
        var rule = Seed(TaxRuleSeeds.OwnSaleContributor);
        rule.Icms = rule.Icms! with { Cst = "20", BaseReduction = 33.3333m };
        var c = Calc(rule, new TaxRecipient("SP", RecipientType.Contributor, false), Item(qty: 1, ipi: 0m));
        Base(c, TaxCodes.Icms).Should().Be(666.67m);
        Amount(c, TaxCodes.Icms).Should().Be(120m);
    }

    [Fact]
    public void Arredonda_so_no_item_com_duas_casas_e_o_total_e_a_soma_dos_itens()
    {
        TaxCalculator.Round(0.125m).Should().Be(0.13m, "AwayFromZero");
        TaxCalculator.Round(0.135m).Should().Be(0.14m);
        var rule = Seed(TaxRuleSeeds.OwnSaleContributor);
        var c = Calc(rule, new TaxRecipient("SP", RecipientType.Contributor, false), Item(qty: 1, price: 10.01m, ipi: 0m), Item(qty: 1, price: 10.01m, ipi: 0m));

        c.Items[0].Taxes.Single(t => t.Code == TaxCodes.Pis).Amount.Should().Be(0.07m, "0,065065 → 0,07");
        Amount(c, TaxCodes.Pis).Should().Be(0.14m, "soma dos itens (sobre o total daria 0,13)");
        Amount(c, TaxCodes.Cofins).Should().Be(0.60m);
        Amount(c, TaxCodes.Icms).Should().Be(3.60m);
        Calc(rule, new TaxRecipient("SP", RecipientType.Contributor, false), Item(qty: 3, price: 33.333m, ipi: 0m)).Products.Should().Be(100m, "99,999 → 100,00");
    }

    [Fact]
    public void Prestacao_de_servico_calcula_ISS_PIS_COFINS_e_IBS_CBS_sem_CFOP()
    {
        var c = Calc(Seed(TaxRuleSeeds.Service), new TaxRecipient("SP", RecipientType.Contributor, false), new TaxItem("Instalação", null, 0, null, 1, 1000m));

        Amount(c, TaxCodes.Iss).Should().Be(50m, "ISS 5%");
        Amount(c, TaxCodes.Pis).Should().Be(6.50m);
        Amount(c, TaxCodes.Cofins).Should().Be(30m);
        Amount(c, TaxCodes.Cbs).Should().Be(9m);
        Amount(c, TaxCodes.IbsState).Should().Be(1m);
        c.Totals.Should().NotContain(t => t.Code == TaxCodes.Icms || t.Code == TaxCodes.Ipi);
        c.Items[0].Cfop.Should().BeNull();
        c.InvoiceTotal.Should().Be(1000m);
        c.ProjectCost.Should().Be(96.50m);
    }

    [Fact]
    public void Simples_Nacional_nao_destaca_ICMS_proprio()
    {
        var rule = Seed(TaxRuleSeeds.OwnSaleContributor);
        rule.Icms = rule.Icms! with { Cst = "102" };
        var result = TaxCalculator.Calculate(new TaxIssuer("SP", 1), new TaxRecipient("SP", RecipientType.Contributor, false), [Item()], [rule]);
        result.Value.Totals.Single(t => t.Code == TaxCodes.Icms).Amount.Should().Be(0m);
    }

    [Fact]
    public void IPI_pela_TIPI_sem_aliquota_e_itens_invalidos_sao_recusados()
    {
        var rule = Seed(TaxRuleSeeds.OwnSaleContributor);
        var recipient = new TaxRecipient("SP", RecipientType.Contributor, false);
        var missing = TaxCalculator.Calculate(Sp, recipient, [Item(ipi: null)], [rule]);
        missing.Error!.Code.Should().Be(TaxCalculator.IpiRateMissingCode);
        missing.Error.Message.Should().Be("Informe a alíquota do IPI da TIPI para o NCM 84281000.");

        TaxCalculator.Calculate(Sp, recipient, [Item(qty: 0)], [rule]).Error!.Code.Should().Be(TaxCalculator.InvalidItemCode);
        TaxCalculator.Calculate(Sp, recipient, [Item(qty: 1, price: 10, discount: 20)], [rule]).Error!.Message.Should().Be("O desconto não passa do valor do item.");
        TaxCalculator.Calculate(Sp, recipient with { State = "XX" }, [Item()], [rule]).IsFailure.Should().BeTrue();
    }

    private static TaxRule Active(TaxRule rule)
    {
        rule.Activate(Guid.NewGuid(), Now, false);
        return rule;
    }

    [Fact]
    public void Resolucao_escolhe_a_mais_especifica_por_NCM_e_sem_regra_recusa()
    {
        var general = Active(Seed(TaxRuleSeeds.OwnSaleContributor));
        var specific = Active(Seed(TaxRuleSeeds.OwnSaleContributor));
        specific.Conditions = specific.Conditions with { NcmPrefixes = ["8428"] };
        var draft = Seed(TaxRuleSeeds.OwnSaleNonContributor);

        TaxRuleResolver.Pick([general, specific, draft], "SP", RecipientType.Contributor, "84281000", Today, false).Should().BeSameAs(specific);
        TaxRuleResolver.Pick([general, specific], "SP", RecipientType.Contributor, "73089010", Today, false).Should().BeSameAs(general);
        TaxRuleResolver.Pick([general, specific, draft], "SP", RecipientType.NonContributor, "84281000", Today, false).Should().BeNull("o rascunho não vale para a emissão");
        TaxRuleResolver.Pick([general], "SP", RecipientType.Contributor, "84281000", Today, requireConfirmed: true).Should().BeNull("em produção só regra conferida");
        TaxRuleResolver.Pick([general], "SP", RecipientType.Contributor, "84281000", Today.AddDays(-1), false).Should().BeNull("fora da vigência");

        TaxRuleResolver.NoRule(TaxOperation.OwnProductionSale, new TaxRecipient("BA", RecipientType.NonContributor, true), "84281000").Message
            .Should().Be("Não há regra fiscal para Venda de produção própria para BA/não contribuinte e NCM 84281000.");
    }

    [Fact]
    public void Sobreposicao_aceita_a_mais_especifica_por_NCM_so_contra_a_sem_NCM()
    {
        var a = Seed(TaxRuleSeeds.OwnSaleContributor);
        var b = Seed(TaxRuleSeeds.OwnSaleContributor);
        TaxRuleRules.Overlaps(a, b).Should().BeTrue("mesma operação, destinatário e sem NCM");
        TaxRuleRules.Overlaps(a, Seed(TaxRuleSeeds.OwnSaleNonContributor)).Should().BeFalse("tipos de destinatário diferentes");
        TaxRuleRules.Overlaps(a, Seed(TaxRuleSeeds.Resale)).Should().BeFalse("outra operação");

        b.Conditions = b.Conditions with { NcmPrefixes = ["8428"] };
        TaxRuleRules.Overlaps(a, b).Should().BeFalse("a com NCM é mais específica que a sem NCM");
        a.Conditions = a.Conditions with { NcmPrefixes = ["84281000"] };
        TaxRuleRules.Overlaps(a, b).Should().BeTrue("prefixos que se cobrem");
        a.Conditions = a.Conditions with { NcmPrefixes = ["7308"] };
        TaxRuleRules.Overlaps(a, b).Should().BeFalse();

        var c = Seed(TaxRuleSeeds.OwnSaleContributor);
        var d = Seed(TaxRuleSeeds.OwnSaleContributor);
        c.ValidTo = Today.AddDays(10);
        d.ValidFrom = Today.AddDays(11);
        TaxRuleRules.Overlaps(c, d).Should().BeFalse("vigências que não se cruzam");
        d.Conditions = d.Conditions with { States = ["BA"] };
        c.Conditions = c.Conditions with { States = ["MG"] };
        d.ValidFrom = Today;
        TaxRuleRules.Overlaps(c, d).Should().BeFalse("UFs diferentes");
    }

    [Fact]
    public void Validacao_da_regra_em_portugues_por_campo()
    {
        Seeds().Should().AllSatisfy(r => TaxRuleRules.Validate(r).Should().BeEmpty(r.Name));
        var rule = Seed(TaxRuleSeeds.OwnSaleContributor);
        rule.CfopInternal = "6101";
        rule.CfopInterstate = "5101";
        rule.Icms = rule.Icms! with { Cst = "101", InternalRate = 120m };
        rule.Pis = null;
        rule.Conditions = new TaxRuleConditions(["XX"], [], ["1"]);
        rule.ValidTo = rule.ValidFrom.AddDays(-1);
        var errors = TaxRuleRules.Validate(rule);
        errors.Keys.Should().Contain(["cfopInternal", "cfopInterstate", "icms.cst", "icms.internalRate", "pis", "conditions.states", "conditions.ncmPrefixes", "validTo"]);
        errors["icms.cst"].Should().Equal("Use um CST do ICMS do Regime Normal (ex.: 00).");

        var service = Seed(TaxRuleSeeds.Service);
        service.Icms = Seed(TaxRuleSeeds.OwnSaleContributor).Icms;
        TaxRuleRules.Validate(service).Should().ContainKey("operation");
        TaxRuleRules.SuggestInterstateCfop("5102").Should().Be("6102");
        TaxRuleRules.SuggestInterstateCfop("6102").Should().BeNull();
    }

    [Fact]
    public void Seed_monta_as_regras_dos_valores_da_FIS_02a_em_rascunho_e_sem_autor()
    {
        var settings = FiscalSettingsService.NewDefaults(Now);
        settings.PisRate = 1.65m;
        settings.CfopResaleInState = "5405";
        var rules = TaxRuleSeeds.Build(settings, Today, Now);

        rules.Select(r => r.SeedKey).Should().Equal(TaxRuleSeeds.OwnSaleContributor, TaxRuleSeeds.OwnSaleNonContributor, TaxRuleSeeds.Resale, TaxRuleSeeds.Service);
        rules.Should().OnlyContain(r => r.Status == TaxRuleStatus.Draft && r.IsSeed && r.Editors.Length == 0 && !r.IsConfirmed && r.CrtScope == 3);
        rules.Should().OnlyContain(r => r.Pis!.Rate == 1.65m, "vem dos valores salvos, nunca de valor inventado");
        rules.Single(r => r.SeedKey == TaxRuleSeeds.Resale).CfopInternal.Should().Be("5405");
        rules.Single(r => r.SeedKey == TaxRuleSeeds.OwnSaleNonContributor).CfopInterstate.Should().Be("6107");
        rules.Single(r => r.SeedKey == TaxRuleSeeds.OwnSaleNonContributor).Difal.Should().NotBeNull();
        rules.Single(r => r.SeedKey == TaxRuleSeeds.Service).Iss.Should().Be(new IssParameters("7.02", 5m, IssLocation.WorkSite, false));
        rules.Should().NotContain(r => r.Operation == TaxOperation.ShipmentToSite || r.Operation == TaxOperation.ReturnFromSite, "D4");
    }

    [Fact]
    public void Aviso_de_revisao_pelos_valores_que_cada_operacao_usa()
    {
        var sale = Seed(TaxRuleSeeds.OwnSaleContributor);
        var service = Seed(TaxRuleSeeds.Service);
        TaxRuleRules.AffectedBy(sale, ["icmsInternalRate"], false).Should().BeTrue();
        TaxRuleRules.AffectedBy(service, ["icmsInternalRate"], false).Should().BeFalse();
        TaxRuleRules.AffectedBy(service, ["nfseIssRate"], false).Should().BeTrue();
        TaxRuleRules.AffectedBy(sale, ["volumeSpecies"], false).Should().BeFalse();
        TaxRuleRules.AffectedBy(sale, ["volumeSpecies"], true).Should().BeTrue("troca de regime afeta todas");
    }

    [Fact]
    public void Nova_versao_copia_os_valores_e_editar_limpa_a_conferencia()
    {
        var by = Guid.NewGuid();
        var rule = Seed(TaxRuleSeeds.OwnSaleContributor);
        rule.Confirm(by, Now);
        rule.Touch(by, Now);
        rule.IsConfirmed.Should().BeFalse("editar volta para 'A confirmar com a contadora'");
        rule.Editors.Should().Equal(by);
        rule.Activate(Guid.NewGuid(), Now, false);
        var next = rule.NextVersion(by, Now);
        next.RuleId.Should().Be(rule.RuleId);
        next.RuleVersion.Should().Be(2);
        next.Status.Should().Be(TaxRuleStatus.Draft);
        next.Icms.Should().Be(rule.Icms);
        FluentActions.Invoking(() => rule.Touch(by, Now)).Should().Throw<InvalidOperationException>();
    }
}
