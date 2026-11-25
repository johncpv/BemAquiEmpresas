using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Sales;

/// <summary>PDV-06 (unidade): quantidade devolvível, valor do item com o desconto rateado, prazo, situação da venda, saldo e código do vale.</summary>
public class ReturnRulesUnitTests
{
    [Theory]
    [InlineData(3, 0, 3)]
    [InlineData(3, 1, 2)]
    [InlineData(3, 3, 0)]
    [InlineData(3, 4, 0)]
    [InlineData(1.5, 0.25, 1.25)]
    public void Quantidade_devolvivel_e_o_vendido_menos_o_ja_devolvido(decimal sold, decimal returned, decimal expected) =>
        ReturnRules.Returnable(sold, returned).Should().Be(expected);

    [Fact]
    public void Valor_do_item_e_o_praticado_com_o_desconto_rateado_e_a_ultima_parte_fecha_os_centavos()
    {
        // 3 unidades a R$ 10,00 com R$ 1,00 de desconto (rateado do total): a linha vale R$ 29,00.
        var first = ReturnRules.Amount(29.00m, 3, 1, 0, 0);
        first.Should().Be(9.67m);
        var second = ReturnRules.Amount(29.00m, 3, 1, 1, first);
        second.Should().Be(9.67m);
        ReturnRules.Amount(29.00m, 3, 1, 2, first + second).Should().Be(9.66m, "a última parte leva o que falta: 29,00 − 19,34");
        ReturnRules.Amount(29.00m, 3, 3, 0, 0).Should().Be(29.00m);
    }

    [Fact]
    public void Prazo_da_loja_e_a_mensagem_fora_dele()
    {
        var deadline = ReturnRules.Deadline(new DateOnly(2026, 10, 1), 30);
        deadline.Should().Be(new DateOnly(2026, 10, 31));
        ReturnRules.IsLate(new DateOnly(2026, 10, 31), deadline).Should().BeFalse();
        ReturnRules.IsLate(new DateOnly(2026, 11, 1), deadline).Should().BeTrue();
        ReturnRules.LateMessage(deadline).Should().Be("Prazo de devolução encerrado em 31/10.");
    }

    [Fact]
    public void Venda_fica_devolvida_so_quando_nada_mais_pode_voltar()
    {
        ReturnRules.StatusAfter([(2m, 2m), (1m, 1m)]).Should().Be(SaleStatus.Returned);
        ReturnRules.StatusAfter([(2m, 1m), (1m, 1m)]).Should().Be(SaleStatus.PartiallyReturned);
    }

    [Fact]
    public void Saldo_do_vale_vencido_sem_saldo_ou_acima_do_saldo_e_recusado()
    {
        var today = new DateOnly(2026, 11, 24);
        ReturnRules.RedeemProblem(50m, today, today, 20m).Should().BeNull();
        ReturnRules.RedeemProblem(50m, today.AddDays(-1), today, 20m).Should().Be("Vale vencido em 23/11/2026.");
        ReturnRules.RedeemProblem(0m, today, today, 20m).Should().Be("Vale sem saldo.");
        ReturnRules.RedeemProblem(10m, today, today, 20m).Should().StartWith("O valor passa do saldo do vale");
        ReturnRules.RedeemProblem(10m, today, today, 1.001m).Should().NotBeNull();

        var credit = StoreCredit.Issue("h", "ABCD", null, 50m, today, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        credit.Debit(20m);
        credit.Balance.Should().Be(30m, "o restante continua no mesmo vale");
        credit.Credit(20m);
        credit.Balance.Should().Be(50m);
    }

    [Fact]
    public void Codigo_do_vale_aceita_o_que_o_operador_digita_e_o_hash_nao_e_o_codigo()
    {
        var code = ReturnRules.NewCode();
        code.Should().MatchRegex("^VT-[2-9A-HJKMNP-Z]{4}-[2-9A-HJKMNP-Z]{4}-[2-9A-HJKMNP-Z]{4}$");
        var normalized = ReturnRules.Normalize(code)!;
        ReturnRules.Normalize(" " + code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal) + " ").Should().Be(normalized);
        ReturnRules.Normalize(normalized).Should().Be(normalized);
        ReturnRules.Normalize("VT-0000-0000-0000").Should().BeNull("0 não está no alfabeto");
        ReturnRules.Hash(normalized).Should().HaveLength(64).And.NotContain(normalized);
    }
}
