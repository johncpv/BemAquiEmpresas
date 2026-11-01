using BemAqui.Fiscal.Domain;
using BemAqui.Purchasing.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// FIS-04b: regras puras do recebimento pela nota, sem banco: custo de entrada com seguro, outras despesas e crédito (CA 1 e 5), crédito
/// por tributo recuperável (CA 5), parcelas pelas duplicatas, sem duplicatas e proporcionais (CA 6 a 8), parte sem item de estoque (CA 9),
/// diferença de preço (CA 3) e o texto da regra de crédito.
/// </summary>
public class InvoiceReceiptRulesTests
{
    private static readonly DateOnly Day = new(2026, 11, 10);

    [Fact]
    public void Custo_de_entrada_soma_os_rateios_e_tira_o_credito()
    {
        // 10 un × R$ 50 = R$ 500 + frete 20 + IPI 25 + seguro 3 + outras 2 = 550 → R$ 55/un; com crédito de 90 (ICMS), 46.
        InvoiceReceiptRules.EntryUnitCost(500m, 20m, 25m, 3m, 2m, 0m, 10m).Should().Be(55m);
        InvoiceReceiptRules.EntryUnitCost(500m, 20m, 25m, 3m, 2m, 90m, 10m).Should().Be(46m);
        InvoiceReceiptRules.EntryUnitCost(100m, 0m, 0m, 0m, 0m, 0m, 3m).Should().Be(33.333333m, "6 casas");
    }

    [Fact]
    public void Sem_credito_nenhum_tributo_sai_do_custo_e_com_credito_so_os_marcados()
    {
        InvoiceReceiptRules.TaxCredit([], 90m, 8.25m, 38m, 25m).Should().Be(0m, "o padrão (P-01) deixa ICMS, IPI, PIS e COFINS no custo");
        InvoiceReceiptRules.TaxCredit(["ICMS"], 90m, 8.25m, 38m, 25m).Should().Be(90m);
        InvoiceReceiptRules.TaxCredit(["ICMS", "IPI", "PIS", "COFINS"], 90m, 8.25m, 38m, 25m).Should().Be(161.25m);
    }

    [Fact]
    public void Uma_parcela_por_duplicata_com_o_valor_e_o_vencimento_dela()
    {
        var plan = InvoiceReceiptRules.Plan(1000m, [], [(Day, 400m), (Day.AddDays(30), 600m)], fromDuplicates: true);

        plan.Mismatch.Should().BeFalse();
        plan.Receipt.Should().Equal(new InvoiceInstallment(1, Day, 400m), new InvoiceInstallment(2, Day.AddDays(30), 600m));
        plan.NoStock.Should().BeEmpty();
    }

    [Fact]
    public void Duplicatas_que_nao_somam_o_total_viram_titulos_pelo_valor_do_recebimento_proporcionais_e_com_o_aviso()
    {
        var plan = InvoiceReceiptRules.Plan(1000m, [], [(Day, 333m), (Day.AddDays(30), 333m), (Day.AddDays(60), 334.5m)], fromDuplicates: true);

        plan.Mismatch.Should().BeTrue();
        plan.Receipt.Sum(p => p.Amount).Should().Be(1000m, "os títulos ficam pelo valor do recebimento");
        plan.Receipt[^1].Amount.Should().Be(1000m - plan.Receipt[0].Amount - plan.Receipt[1].Amount, "a última absorve o centavo");
        InvoiceReceiptRules.InstallmentDescription(123, "REC-2026-0001", 1, 3, true).Should().EndWith(InvoiceReceiptRules.MismatchNotice);
    }

    [Fact]
    public void Sem_duplicatas_a_condicao_do_pedido_nao_leva_aviso()
    {
        var plan = InvoiceReceiptRules.Plan(900m, [], [(Day, 300m), (Day.AddDays(30), 300m), (Day.AddDays(60), 300m)], fromDuplicates: false);

        plan.Mismatch.Should().BeFalse();
        plan.Receipt.Select(p => p.Amount).Should().Equal(300m, 300m, 300m);
    }

    [Fact]
    public void A_parte_sem_item_de_estoque_divide_cada_duplicata_com_o_recebimento_e_cada_conta_tem_suas_parcelas()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var plan = InvoiceReceiptRules.Plan(800m, [(a, 150m), (b, 50m)], [(Day, 500m), (Day.AddDays(30), 500m)], fromDuplicates: true);

        plan.Mismatch.Should().BeFalse();
        plan.Receipt.Select(p => p.Amount).Should().Equal(400m, 400m);
        plan.NoStock.Should().HaveCount(2);
        plan.NoStock[0].Installments.Select(p => (p.Number, p.Amount)).Should().Equal((1, 75m), (2, 75m));
        plan.NoStock[1].Installments.Select(p => (p.Number, p.Amount)).Should().Equal((3, 25m), (4, 25m)); // a segunda conta numera depois da primeira (idempotência por parcela)
        for (var i = 0; i < 2; i++)
        {
            (plan.Receipt[i].Amount + plan.NoStock[0].Installments[i].Amount + plan.NoStock[1].Installments[i].Amount).Should().Be(500m, "cada duplicata fecha exatamente");
        }
    }

    [Fact]
    public void Parte_sem_item_de_estoque_e_valor_menos_desconto_mais_IPI() =>
        InvoiceReceiptRules.NoStockAmount(200m, 10m, 5m).Should().Be(195m);

    [Theory]
    [InlineData(50, 50.01, false)]
    [InlineData(50, 49.99, false)]
    [InlineData(50, 50.011, true)]
    [InlineData(50, 48, true)]
    public void Preco_da_nota_diferente_em_mais_de_um_centavo_bloqueia(double order, double invoice, bool differs) =>
        InvoiceReceiptRules.PriceDiffers((decimal)order, (decimal)invoice).Should().Be(differs);

    [Fact]
    public void Texto_da_regra_de_credito_mostra_a_confirmacao()
    {
        FiscalSettingsRules.InputTaxCreditLabel([], confirmed: false).Should().Be("Sem crédito de impostos (a confirmar com a contadora)");
        FiscalSettingsRules.InputTaxCreditLabel([], confirmed: true).Should().Be("Sem crédito de impostos");
        FiscalSettingsRules.InputTaxCreditLabel(["PIS", "ICMS"], confirmed: true).Should().Be("Crédito de ICMS e PIS: sai do custo e fica guardado na nota");
        FiscalSettingsRules.InputTaxCreditDisplay([]).Should().Be("Nenhum: ICMS, IPI, PIS e COFINS da nota ficam no custo");
    }

    [Fact]
    public void Parametro_de_credito_aceita_so_os_tributos_da_entrada()
    {
        var settings = new FiscalSettings { InputTaxCredit = ["ICMS", "ISS"] };

        FiscalSettingsRules.Validate(settings).Should().ContainKey("inputTaxCredit");
        new FiscalSettings().InputTaxCredit.Should().BeEmpty("o seed é nenhum crédito (P-01)");
    }
}
