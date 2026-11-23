using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>CX-02 sem banco: o esperado por forma, a tolerância por forma (sobra numa não compensa falta em outra) e o recolhimento.</summary>
public class CashClosingUnitTests
{
    private static readonly Guid Cash = Guid.Parse("019a0000-0000-7000-8000-0000000000c1");
    private static readonly Guid Debit = Guid.Parse("019a0000-0000-7000-8000-0000000000c2");
    private static readonly Guid Pix = Guid.Parse("019a0000-0000-7000-8000-0000000000c3");
    private static readonly Dictionary<Guid, string> Types = new() { [Cash] = "Cash", [Debit] = "Debit", [Pix] = "Pix" };

    [Fact]
    public void Esperado_do_dinheiro_soma_fundo_vendas_e_suprimentos_e_tira_sangrias_e_recolhimento()
    {
        // Fundo 100 + suprimento 50 − sangria 30 − recolhimento anterior 20 + recolhimento devolvido 20 = 120 além das vendas.
        var extra = CashClosing.ExpectedDrawer(100m, 0m,
            [(CashMovementKind.CashIn, 50m), (CashMovementKind.CashOut, 30m), (CashMovementKind.Deposit, 20m), (CashMovementKind.DepositReturn, 20m)]);
        extra.Should().Be(120m);

        // Vendas líquidas do troco por forma; o dinheiro contado sem forma cai na gaveta da forma Dinheiro; a forma vendida e não contada falta inteira.
        var lines = CashClosing.Lines(new Dictionary<Guid, decimal> { [Cash] = 80m, [Debit] = 45.5m, [Pix] = 10m }, Types,
            [new CashCountedLine(null, "Cash", 200m), new CashCountedLine(Debit, "Debit", 45.5m)], extra, new Dictionary<Guid, decimal> { [Pix] = 10m });
        lines.Should().HaveCount(3);
        lines.Single(l => l.MethodId == Cash).Should().BeEquivalentTo(new { Expected = 200m, Counted = 200m, Difference = 0m });
        lines.Single(l => l.MethodId == Debit).Difference.Should().Be(0m);
        lines.Single(l => l.MethodId == Pix).Should().BeEquivalentTo(new { Expected = 10m, Counted = 0m, PixNotFound = 10m, Difference = -20m });
    }

    [Fact]
    public void Tolerancia_vale_por_forma_e_sobra_numa_nao_compensa_falta_em_outra()
    {
        var sales = new Dictionary<Guid, decimal> { [Cash] = 100m, [Debit] = 100m };
        var balanced = CashClosing.Lines(sales, Types, [new CashCountedLine(Cash, "Cash", 104m), new CashCountedLine(Debit, "Debit", 96m)], 0m);
        CashClosing.WithinTolerance(balanced, 5m).Should().BeTrue();

        // Sobra de R$ 8 no dinheiro e falta de R$ 8 no débito: soma zero, mas cada forma passou da tolerância.
        var crossed = CashClosing.Lines(sales, Types, [new CashCountedLine(Cash, "Cash", 108m), new CashCountedLine(Debit, "Debit", 92m)], 0m);
        crossed.Sum(l => l.Difference).Should().Be(0m);
        CashClosing.WithinTolerance(crossed, 5m).Should().BeFalse();
    }

    [Fact]
    public void Venda_antes_do_Fechar_caixa_soma_mesmo_com_a_sessao_contada_e_depois_dele_nao_entra()
    {
        var opened = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var started = opened.AddHours(8);
        var now = started.AddHours(1);
        var counted = new CashSession { OpenedAt = opened, Status = CashSessionStatus.Counted, CountStartedAt = started };

        // Venda das 19h que saiu da quarentena com a sessão já contada: soma (o fechamento espera por ela).
        PosEventIngestion.AfterCountStarted(counted, started.AddHours(-1)).Should().BeNull();
        PosEventIngestion.OutsideSession(counted, started.AddHours(-1), now).Should().BeFalse();

        // Depois do "Fechar caixa", ou em sessão fechada: quarentena (CX-02). Antes da abertura: divergência, sem somar (M1 da OFF-02).
        PosEventIngestion.AfterCountStarted(counted, started.AddSeconds(1)).Should().NotBeNull();
        PosEventIngestion.AfterCountStarted(new CashSession { OpenedAt = opened, Status = CashSessionStatus.PendingReview }, started.AddHours(-1)).Should().NotBeNull();
        PosEventIngestion.OutsideSession(counted, opened.AddMinutes(-5), now).Should().BeTrue();
        PosEventIngestion.OutsideSession(new CashSession { OpenedAt = opened, Status = CashSessionStatus.Open }, now.AddMinutes(5), now).Should().BeTrue();
    }

    [Fact]
    public void Recolhimento_e_o_dinheiro_contado_menos_o_fundo_que_fica()
    {
        CashClosing.Deposit(530.40m, 100m).Should().Be(430.40m);
        CashClosing.Deposit(80m, 100m).Should().Be(0m);
    }
}
