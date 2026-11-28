using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>FIN-10a CA 4 a 6 e 15 (ADR 0054): quem solicitou a OP decide a própria, por modo, e o PUT legado só alterna Nunca e Só o Proprietário.</summary>
public class SelfApprovalRulesTests
{
    [Theory]
    // modo, solicitante é do Proprietário, membros do Proprietário, pode
    [InlineData(SelfApprovalMode.Never, true, 1, true)] // D6 a: Proprietário único
    [InlineData(SelfApprovalMode.Never, true, 2, false)]
    [InlineData(SelfApprovalMode.Never, false, 1, false)]
    [InlineData(SelfApprovalMode.OwnerOnly, true, 1, true)]
    [InlineData(SelfApprovalMode.OwnerOnly, true, 3, true)]
    [InlineData(SelfApprovalMode.OwnerOnly, false, 1, false)]
    [InlineData(SelfApprovalMode.AnyApprover, false, 2, true)] // a alçada do nível 1 e finance.approve conferem o resto
    [InlineData(SelfApprovalMode.AnyApprover, true, 2, true)]
    public void Solicitante_decide_a_propria_conforme_o_modo(SelfApprovalMode mode, bool owner, int members, bool allowed) =>
        PaymentOrderRules.CanDecide(true, owner, members, mode).Should().Be((allowed, allowed));

    [Theory]
    [InlineData(SelfApprovalMode.Never)]
    [InlineData(SelfApprovalMode.AnyApprover)]
    public void Quem_nao_solicitou_decide_sem_selo(SelfApprovalMode mode) =>
        PaymentOrderRules.CanDecide(false, false, 2, mode).Should().Be((true, false));

    [Theory]
    [InlineData(0, SelfApprovalMode.Never)]
    [InlineData(1, SelfApprovalMode.OwnerOnly)] // o booleano antigo ligado já era "Proprietário aprova a própria"
    [InlineData(2, SelfApprovalMode.AnyApprover)]
    [InlineData(3, SelfApprovalMode.Never)] // fora da faixa: falha fechada
    [InlineData(-1, SelfApprovalMode.Never)]
    public void Valor_gravado_vira_modo(int stored, SelfApprovalMode expected) =>
        PaymentOrderRules.ModeOf(stored).Should().Be(expected);

    [Theory]
    // atual, pedido, booleano legado, resultado
    [InlineData(SelfApprovalMode.Never, null, null, SelfApprovalMode.Never)]
    [InlineData(SelfApprovalMode.AnyApprover, null, null, SelfApprovalMode.AnyApprover)]
    [InlineData(SelfApprovalMode.Never, null, true, SelfApprovalMode.OwnerOnly)]
    [InlineData(SelfApprovalMode.OwnerOnly, null, false, SelfApprovalMode.Never)]
    [InlineData(SelfApprovalMode.OwnerOnly, null, true, SelfApprovalMode.OwnerOnly)]
    [InlineData(SelfApprovalMode.AnyApprover, null, false, SelfApprovalMode.AnyApprover)] // nunca rebaixa
    [InlineData(SelfApprovalMode.AnyApprover, null, true, SelfApprovalMode.AnyApprover)]
    [InlineData(SelfApprovalMode.Never, SelfApprovalMode.AnyApprover, false, SelfApprovalMode.AnyApprover)] // o campo novo vence
    [InlineData(SelfApprovalMode.AnyApprover, SelfApprovalMode.Never, true, SelfApprovalMode.Never)]
    public void PUT_resolve_o_modo(SelfApprovalMode current, SelfApprovalMode? requested, bool? legacy, SelfApprovalMode expected) =>
        PaymentOrderRules.ResolveMode(current, requested, legacy).Should().Be(expected);
}
