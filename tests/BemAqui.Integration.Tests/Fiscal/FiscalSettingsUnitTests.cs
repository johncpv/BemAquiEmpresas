using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Fiscal.Application.Certificates;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-02a, regras sem banco: CRT pelo regime, validações do CA 4, hash do valor (CA 4 e 5), campos críticos por documento (CA 6),
/// conferência do A1 e do CNPJ do titular (CA 11), limiares do vencimento (CA 14) e o "Ver conteúdo" do Emissor (CA 2).
/// </summary>
public class FiscalSettingsUnitTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static FiscalSettings Defaults() => FiscalSettingsService.NewDefaults(Now);

    [Theory]
    [InlineData(TaxRegime.LucroPresumido, 3)]
    [InlineData(TaxRegime.LucroReal, 3)]
    [InlineData(TaxRegime.SimplesNacional, 1)]
    [InlineData(TaxRegime.SimplesNacionalExcess, 2)]
    [InlineData(TaxRegime.Mei, 4)]
    public void CRT_sai_do_regime(TaxRegime regime, int crt)
    {
        FiscalSettings.CrtOf(regime).Should().Be(crt);
        var settings = Defaults();
        settings.Regime = regime;
        settings.SyncCrt();
        settings.Crt.Should().Be(crt);
    }

    [Fact]
    public void Valores_pre_configurados_da_tabela_da_story_passam_na_validacao()
    {
        var s = Defaults();
        FiscalSettingsRules.Validate(s).Should().BeEmpty();
        s.Regime.Should().Be(TaxRegime.LucroPresumido);
        s.Crt.Should().Be(3);
        s.CnaeMain.Should().Be("4329103");
        s.NfeSeries.Should().Be(2);
        s.Environment.Should().Be(FiscalEnvironment.Homologation);
        (s.CfopSaleInStateContributor, s.CfopSaleInStateFinal, s.CfopSaleInterstateContributor, s.CfopSaleInterstateFinal, s.CfopResaleInState, s.CfopResaleInterstate)
            .Should().Be(("5101", "5101", "6101", "6107", "5102", "6102"));
        (s.CstIcms, s.CstPisCofins, s.CstIpi).Should().Be(("00", "01", "50"));
        (s.IcmsInternalRate, s.IcmsInterstateSouthSoutheastRate, s.IcmsInterstateOtherRate, s.IcmsImportedRate, s.PisRate, s.CofinsRate).Should().Be((18m, 12m, 7m, 4m, 0.65m, 3m));
        s.CostTaxes.Should().BeEquivalentTo(["ICMS", "PIS", "COFINS", "ISS", "IBS_CBS"], "IPI fora do custo (ponto 41)");
        (s.NfseServiceItem, s.NfseMunicipalCode, s.NfseIssRate, s.NfseIssLocation, s.NfseIssWithheld, s.NfseFederalWithholding).Should().Be(("7.02", (string?)null, 5m, IssLocation.WorkSite, false, false));
        (s.IbsCbsCst, s.IbsCbsClassification, s.CbsRate, s.IbsRate, s.IbsCityRate).Should().Be(("000", "000001", 0.9m, 0.1m, 0m));
        (s.CancellationWindowHours, s.FreightMode, s.VolumeSpecies, s.IncludeDuplicates).Should().Be((24, 0, "VOLUME", false));
    }

    public static TheoryData<Action<FiscalSettings>, string> Invalid => new()
    {
        { s => s.CnaeMain = "432910", "cnaeMain" },
        { s => s.CnaeSecondary = ["12345678"], "cnaeSecondary" },
        { s => s.NfeSeries = 0, "nfeSeries" },
        { s => s.NfeSeries = 1000, "nfeSeries" },
        { s => s.CfopSaleInStateContributor = "6101", "cfopSaleInStateContributor" },
        { s => s.CfopSaleInterstateFinal = "5107", "cfopSaleInterstateFinal" },
        { s => s.CfopResaleInState = "510", "cfopResaleInState" },
        { s => s.CstIcms = "101", "cstIcms" },
        { s => s.CstPisCofins = "10", "cstPisCofins" },
        { s => s.CstIpi = "00", "cstIpi" },
        { s => s.IcmsInternalRate = 100.5m, "icmsInternalRate" },
        { s => s.PisRate = -1m, "pisRate" },
        { s => s.CbsRate = 0.12345m, "cbsRate" },
        { s => s.CancellationWindowHours = 0, "cancellationWindowHours" },
        { s => s.CancellationWindowHours = 169, "cancellationWindowHours" },
        { s => s.NfseMunicipalCode = "0702", "nfseMunicipalCode" },
        { s => s.NfseServiceItem = "7,02", "nfseServiceItem" },
        { s => s.IbsCbsCst = "00", "ibsCbsCst" },
        { s => s.IbsCbsClassification = "1", "ibsCbsClassification" },
        { s => s.FreightMode = 5, "freightMode" },
        { s => s.CostTaxes = ["ICMS", "XPTO"], "costTaxes" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Validacoes_do_CA_4(Action<FiscalSettings> change, string field)
    {
        var s = Defaults();
        change(s);
        FiscalSettingsRules.Validate(s).Should().ContainKey(field);
    }

    [Fact]
    public void CST_do_ICMS_segue_o_CRT_do_regime()
    {
        var s = Defaults();
        s.Regime = TaxRegime.SimplesNacional;
        FiscalSettingsRules.Validate(s).Should().ContainKey("cstIcms", "no Simples o CST 00 não vale; é CSOSN");
        s.CstIcms = "102";
        FiscalSettingsRules.Validate(s).Should().BeEmpty();
        FiscalSettingsRules.Validate(s).Should().NotContainKey("cstIcms");
    }

    [Fact]
    public void Hash_muda_com_o_valor_e_so_com_ele()
    {
        var key = FiscalSettingKeys.Find("pisRate")!;
        var s = Defaults();
        var hash = key.HashOf(s);
        hash.Should().HaveLength(64);
        key.HashOf(Defaults()).Should().Be(hash, "o mesmo valor tem o mesmo hash");
        s.PisRate = 0.65000m;
        key.HashOf(s).Should().Be(hash, "0,65 e 0,65000 são o mesmo valor");
        s.PisRate = 1.65m;
        key.HashOf(s).Should().NotBe(hash, "mudou o valor, a conferência não vale mais");
        FiscalSettingKeys.Find("cofinsRate")!.HashOf(Defaults()).Should().NotBe(FiscalSettingKeys.Hash("pisRate", "3"), "a chave entra no hash");
    }

    [Fact]
    public void Chaves_unicas_em_secoes_conhecidas_e_criticas_por_documento()
    {
        FiscalSettingKeys.All.Select(k => k.Key).Should().OnlyHaveUniqueItems();
        FiscalSettingKeys.All.Select(k => k.Section).Distinct().Should().BeSubsetOf(FiscalSettingKeys.Sections.Select(s => s.Code));
        FiscalSettingKeys.Sections.Should().OnlyContain(s => FiscalSettingKeys.All.Any(k => k.Section == s.Code), "toda seção tem campo");

        var nfe = FiscalSettingKeys.CriticalFor(FiscalDocumentType.Nfe).Select(k => k.Key).ToList();
        var nfse = FiscalSettingKeys.CriticalFor(FiscalDocumentType.Nfse).Select(k => k.Key).ToList();
        nfe.Should().Contain(["regime", "crt", "nfeSeries", "cfopSaleInStateContributor", "cstIcms", "icmsInternalRate", "pisRate", "ibsCbsCst", "ibsCbsClassification", "cancellationWindowHours"]);
        nfe.Should().NotContain(["nfseServiceItem", "nfseMunicipalCode", "nfseIssRate", "costTaxes", "environment"], "os da NFS-e só para a NFS-e; custo e ambiente não bloqueiam");
        nfse.Should().Contain(["regime", "nfseServiceItem", "nfseMunicipalCode", "nfseCtn", "nfseNbs", "nfseIssRate", "ibsCbsCst"]);
        nfse.Should().NotContain(["nfeSeries", "cfopSaleInStateContributor", "cstIcms"]);
    }

    [Fact]
    public void Todo_campo_tem_texto_para_a_contadora()
    {
        var s = Defaults();
        foreach (var key in FiscalSettingKeys.All)
        {
            FiscalSettingsMapping.Display(key, s).Should().NotBeNullOrWhiteSpace(key.Key);
        }

        FiscalSettingsMapping.Display(FiscalSettingKeys.Find("icmsInternalRate")!, s).Should().Be("18%");
        FiscalSettingsMapping.Display(FiscalSettingKeys.Find("pisRate")!, s).Should().Be("0,65%");
        FiscalSettingsMapping.Display(FiscalSettingKeys.Find("cnaeMain")!, s).Should().Be("4329-1/03");
        FiscalSettingsMapping.Display(FiscalSettingKeys.Find("nfseMunicipalCode")!, s).Should().Be("A informar");
    }

    [Fact]
    public void Mapeamento_normaliza_CNAE_e_textos_e_mantem_o_ambiente()
    {
        var s = Defaults();
        var values = FiscalSettingsMapping.ToValues(s) with { CnaeMain = "4329-1/03", CnaeSecondary = ["2822-4/01", "2822-4/01", " "], VolumeSpecies = " caixa ", NfseMunicipalCode = "  ", Environment = FiscalEnvironment.Production };
        FiscalSettingsMapping.Apply(s, values);
        s.CnaeMain.Should().Be("4329103");
        s.CnaeSecondary.Should().Equal("2822401");
        s.VolumeSpecies.Should().Be("CAIXA");
        s.NfseMunicipalCode.Should().BeNull();
        s.Environment.Should().Be(FiscalEnvironment.Homologation, "a troca do ambiente tem regra própria (CA 8)");
    }

    [Fact]
    public void A1_valido_le_titular_CNPJ_thumbprint_e_validade()
    {
        var pfx = TestCertificates.Pfx();
        var result = FiscalCertificateInspector.Inspect(pfx, new Secret(TestCertificates.Password), Now);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Document.Should().Be("47225005000186");
        result.Value.Subject.Should().Be("EMPRESA A LTDA:47225005000186");
        result.Value.Thumbprint.Should().Be(TestCertificates.Thumbprint(pfx));
        result.Value.NotAfter.Should().BeCloseTo(Now.AddYears(1), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void CNPJ_vem_do_CN_quando_falta_o_otherName()
    {
        var result = FiscalCertificateInspector.Inspect(TestCertificates.Pfx(cnpj: "11222333000181", altName: false), new Secret(TestCertificates.Password), Now);
        result.Value.Document.Should().Be("11222333000181");
    }

    [Fact]
    public void Recusa_senha_errada_sem_chave_privada_A3_sem_politica_vencido_e_sem_CNPJ()
    {
        Error Of(byte[] pfx, string password = TestCertificates.Password) => FiscalCertificateInspector.Inspect(pfx, new Secret(password), Now).Error!;

        Of(TestCertificates.Pfx(), "errada").Message.Should().Be(FiscalCertificateInspector.OpenFailed);
        Of(TestCertificates.Pfx(privateKey: false)).Message.Should().Be(FiscalCertificateInspector.NoPrivateKey);
        Of(TestCertificates.Pfx(policy: TestCertificates.A3Policy)).Message.Should().Be(FiscalCertificateInspector.NotA1);
        Of(TestCertificates.Pfx(policy: null)).Message.Should().Be(FiscalCertificateInspector.NotA1);
        Of(TestCertificates.Pfx(notBefore: Now.AddYears(-2), notAfter: Now.AddDays(-3))).Message.Should().StartWith("O certificado venceu em ");
        Of(TestCertificates.Pfx(cnpj: string.Empty, altName: false)).Message.Should().Be(FiscalCertificateInspector.NoDocument);
        Of([1, 2, 3]).Fields!.Should().ContainKey("password");
    }

    [Theory]
    [InlineData(45, null)]
    [InlineData(30, 30)]
    [InlineData(20, 30)]
    [InlineData(15, 15)]
    [InlineData(10, 15)]
    [InlineData(7, 7)]
    [InlineData(1, 7)]
    public void Limiar_do_aviso_de_vencimento(int days, int? threshold) => FiscalCertificateRules.ThresholdFor(days).Should().Be(threshold);

    [Fact]
    public void CNPJ_de_outro_titular_aparece_mascarado() => FiscalCertificateRules.Mask("11222333000181").Should().Be("11.222.***/****-81");

    [Fact]
    public void Ver_conteudo_dos_eventos_do_Emissor_passa_a_exigir_fiscal_approve() =>
        EmissorFiscalWebhook.Provider.ContentPermission.Should().Be("fiscal.approve", "CA 2: a FND-16 usa a aprovação do módulo dono quando ela existe no catálogo");
}
