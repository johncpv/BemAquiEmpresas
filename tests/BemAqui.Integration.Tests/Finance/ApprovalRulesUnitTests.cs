using BemAqui.Finance.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-03b CA 6 a 8: uma regra por nível; conta vence faixa dentro do teto; valor de referência.</summary>
public class ApprovalRulesUnitTests
{
    private static readonly Guid Ocupacao = Guid.NewGuid();
    private static readonly Guid Outra = Guid.NewGuid();
    private static readonly ApprovalRule A = new() { MinAmount = 0, MaxAmount = 5_000m, Level = 1 };
    private static readonly ApprovalRule B = new() { MinAmount = 5_000.01m, Level = 1 };
    private static readonly ApprovalRule C = new() { MinAmount = 0, MaxAmount = 10_000m, AccountId = Ocupacao, Level = 1 };
    private static readonly ApprovalRule D = new() { MinAmount = 20_000.01m, Level = 2 };
    private static readonly ApprovalRule[] Rules = [A, B, C, D];

    [Theory]
    [InlineData(3_000, false, "A")]
    [InlineData(8_000, false, "B")]
    [InlineData(8_000, true, "C")]
    [InlineData(12_000, true, "B")]
    [InlineData(25_000, false, "B,D")]
    public void Exemplo_da_story(double amount, bool ocupacao, string expected)
    {
        var names = new Dictionary<ApprovalRule, string> { [A] = "A", [B] = "B", [C] = "C", [D] = "D" };
        var resolved = ApprovalRules.Resolve(Rules, (decimal)amount, ocupacao ? Ocupacao : null);
        string.Join(",", resolved.Select(r => names[r])).Should().Be(expected);
    }

    [Fact]
    public void Contas_diferentes_valem_a_faixa_e_a_referencia_pega_o_maior()
    {
        ApprovalRules.Resolve(Rules, 8_000m, null).Should().Equal(B);
        _ = Outra;
        ApprovalRules.Reference(5_000m, 6_000m, 20_000m).Should().Be(20_000m);
        ApprovalRules.Reference(5_000m, 6_000m, 5_000m).Should().Be(6_000m);
    }
}
