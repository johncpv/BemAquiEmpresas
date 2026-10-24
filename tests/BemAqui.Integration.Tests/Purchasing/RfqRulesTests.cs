using BemAqui.Purchasing.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-02: regras puras da cotação, sem banco: custo total com frete e IPI e o vencedor sugerido (CA 7, 8), conversão da unidade do
/// fornecedor (CA 3), limite do anexo (CA 6) e o aviso "1 cotação" (CA 11).
/// </summary>
public class RfqRulesTests
{
    private static readonly Guid LineA = Guid.CreateVersion7();
    private static readonly Guid LineB = Guid.CreateVersion7();
    private static readonly Guid Alfa = Guid.CreateVersion7();
    private static readonly Guid Beta = Guid.CreateVersion7();
    private static readonly Guid Gama = Guid.CreateVersion7();

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Dataset fixo (CA 7): duas linhas (100 KG e 10 UN) e três fornecedores. Alfa tem o menor preço da linha A, mas o frete alto pesa;
    /// Beta tem o menor custo total da linha A com o frete rateado; Gama só cotou a linha B.
    /// </summary>
    private static RfqComparison Dataset() => RfqRules.Compare(
        [new RfqComparisonLine(LineA, 100m), new RfqComparisonLine(LineB, 10m)],
        [new RfqComparisonSupplier(Alfa, 1, 300m, 0m), new RfqComparisonSupplier(Beta, 2, 0m, 5m), new RfqComparisonSupplier(Gama, 3, 0m, 0m)],
        [
            new RfqComparisonQuote(LineA, Alfa, 10m, 5), new RfqComparisonQuote(LineB, Alfa, 50m, 5),
            new RfqComparisonQuote(LineA, Beta, 11m, 10), new RfqComparisonQuote(LineB, Beta, 52m, 10),
            new RfqComparisonQuote(LineB, Gama, 49m, 20),
        ]);

    [Fact]
    public void Custo_total_do_fornecedor_e_subtotal_mais_frete_mais_IPI()
    {
        var comparison = Dataset();
        var alfa = comparison.Suppliers.Single(s => s.SupplierId == Alfa);
        alfa.Subtotal.Should().Be(1500m, "100 × 10 + 10 × 50");
        alfa.Total.Should().Be(1800m, "1.500 + 300 de frete");
        alfa.MaxLeadDays.Should().Be(5);
        alfa.CoversAll.Should().BeTrue();
        var beta = comparison.Suppliers.Single(s => s.SupplierId == Beta);
        beta.Subtotal.Should().Be(1620m);
        beta.IpiAmount.Should().Be(81m, "5% de 1.620");
        beta.Total.Should().Be(1701m);
        comparison.Suppliers.Single(s => s.SupplierId == Gama).CoversAll.Should().BeFalse("só cotou a linha B");
    }

    [Fact]
    public void Menor_custo_da_linha_usa_o_frete_rateado_pelo_valor_e_o_IPI()
    {
        var comparison = Dataset();
        // Linha A: Alfa 1.000 + 300 × 1.000/1.500 = 1.200; Beta 1.100 + 5% = 1.155 → Beta, apesar do preço maior.
        comparison.Costs.Single(c => c.LineId == LineA && c.SupplierId == Alfa).LandedCost.Should().Be(1200m);
        comparison.Costs.Single(c => c.LineId == LineA && c.SupplierId == Beta).LandedCost.Should().Be(1155m);
        comparison.CheapestByLine[LineA].Should().Be(Beta);
        // Linha B: Alfa 500 + 100 = 600; Beta 520 + 26 = 546; Gama 490 → Gama.
        comparison.CheapestByLine[LineB].Should().Be(Gama);
        comparison.Costs.Single(c => c.LineId == LineB && c.SupplierId == Gama).LineTotal.Should().Be(490m);
    }

    [Fact]
    public void Menor_total_geral_so_entre_quem_cotou_todas_as_linhas()
    {
        Dataset().CheapestOverall.Should().Be(Beta, "Beta 1.701 < Alfa 1.800; Gama não cotou a linha A");
    }

    [Fact]
    public void Empate_no_custo_desempata_pelo_menor_prazo_e_depois_pela_ordem()
    {
        var comparison = RfqRules.Compare(
            [new RfqComparisonLine(LineA, 1m)],
            [new RfqComparisonSupplier(Alfa, 1, 0m, 0m), new RfqComparisonSupplier(Beta, 2, 0m, 0m), new RfqComparisonSupplier(Gama, 3, 0m, 0m)],
            [new RfqComparisonQuote(LineA, Alfa, 10m, 9), new RfqComparisonQuote(LineA, Beta, 10m, 3), new RfqComparisonQuote(LineA, Gama, 10m, 3)]);
        comparison.CheapestByLine[LineA].Should().Be(Beta);
        comparison.CheapestOverall.Should().Be(Beta);
    }

    [Fact]
    public void Uma_resposta_so_basta_para_sugerir()
    {
        var comparison = RfqRules.Compare([new RfqComparisonLine(LineA, 3m)], [new RfqComparisonSupplier(Alfa, 1, 0m, 0m)], [new RfqComparisonQuote(LineA, Alfa, D("1.005"), 0)]);
        comparison.CheapestByLine[LineA].Should().Be(Alfa);
        comparison.Suppliers.Single().Total.Should().Be(D("3.02"), "3 × 1,005 = 3,015 → 3,02 (AwayFromZero), a mesma conta do pedido");
    }

    [Theory]
    [InlineData("100", "50", "2")] // 1 caixa de 50 un a R$ 100 → R$ 2 por un
    [InlineData("10", "3", "3.333333")]
    [InlineData("0.000001", "4", "0")] // fica zero: o serviço recusa
    [InlineData("1", "0.5", "2")]
    public void Preco_na_unidade_do_fornecedor_vira_preco_na_unidade_de_estoque(string price, string factor, string expected) =>
        RfqRules.ToStockUnitPrice(D(price), D(factor)).Should().Be(D(expected));

    [Fact]
    public void Fator_zero_nao_converte()
    {
        var act = () => RfqRules.ToStockUnitPrice(10m, 0m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("5000.00", false)]
    [InlineData("5000.01", true)]
    [InlineData("4999.99", false)]
    public void Anexo_so_acima_do_limite(string total, bool required) => RfqRules.NeedsAttachment(D(total), 5000m).Should().Be(required);

    [Fact]
    public void Mensagem_do_anexo_leva_o_limite_em_reais() =>
        RfqRules.AttachmentRequiredMessage(5000m).Should().Be("Respostas acima de R$ 5.000,00 exigem o anexo da proposta do fornecedor.");

    [Fact]
    public void Motivo_so_quando_o_vencedor_nao_e_o_de_menor_custo()
    {
        RfqRules.NeedsReason(Alfa, Alfa).Should().BeFalse();
        RfqRules.NeedsReason(Beta, Alfa).Should().BeTrue();
        RfqRules.NeedsReason(Beta, null).Should().BeFalse();
    }

    [Fact]
    public void Aviso_1_cotacao_so_na_concluida_com_uma_resposta()
    {
        RfqRules.SingleQuoteWarning(RfqStatus.Closed, 1).Should().Be("1 cotação: só um fornecedor respondeu a esta cotação.");
        RfqRules.SingleQuoteWarning(RfqStatus.Closed, 2).Should().BeNull();
        RfqRules.SingleQuoteWarning(RfqStatus.Open, 1).Should().BeNull();
        PurchaseOrderRules.SingleQuoteWarning(Guid.NewGuid(), 1).Should().NotBeNull("o pedido gerado leva o mesmo aviso na aprovação (CMP-03a CA 11)");
    }
}
