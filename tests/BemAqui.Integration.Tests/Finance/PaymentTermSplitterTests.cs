using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>CAD-05b CA 11 a 13 e 10: divisão de parcelas, validação e máscara, sem banco.</summary>
public class PaymentTermSplitterTests
{
    [Fact]
    public void Dividir_igualmente_gera_33_3333_e_a_ultima_absorve() =>
        PaymentTermSplitter.EqualPercents(3).Should().Equal(33.3333m, 33.3333m, 33.3334m);

    [Fact]
    public void Ultima_parcela_absorve_o_arredondamento_e_o_vencimento_e_em_dias_corridos()
    {
        var parts = PaymentTermSplitter.Split([new(0, 50m), new(31, 50m)], 0.03m, new DateOnly(2026, 1, 31));
        parts.Should().Equal(new InstallmentDue(1, new DateOnly(2026, 1, 31), 0.02m), new InstallmentDue(2, new DateOnly(2026, 3, 3), 0.01m));
    }

    [Theory]
    [InlineData(new[] { 30, 30 }, new[] { 50.0, 50.0 }, "crescentes")]
    [InlineData(new[] { -1 }, new[] { 100.0 }, "0 ou mais")]
    [InlineData(new[] { 0 }, new[] { 100.00001 }, "4 casas")]
    [InlineData(new[] { 0, 30 }, new[] { 60.0, 60.0 }, "Hoje está em 120%")]
    public void Validacao_das_parcelas(int[] days, double[] percents, string message) =>
        PaymentTermSplitter.Validate(days.Zip(percents, (d, p) => new Installment(d, (decimal)p)).ToList()).Should().Contain(message);

    [Fact]
    public void Mascara_de_agencia_e_conta()
    {
        FinanceValidation.MaskAgency("1234-5").Should().Be("****-5");
        FinanceValidation.MaskAgency("0001").Should().Be("****");
        FinanceValidation.MaskAccount("12345678-9").Should().Be("*****678-9");
        FinanceValidation.MaskAccount("12-3").Should().Be("*2-3");
    }
}
