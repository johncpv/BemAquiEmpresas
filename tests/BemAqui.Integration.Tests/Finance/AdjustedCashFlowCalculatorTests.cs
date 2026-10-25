using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-07 CA 2 a 6: cenário ajustado sem banco, com tabela de casos.</summary>
public class AdjustedCashFlowCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 21); // quarta-feira
    private static readonly DateOnly To = Today.AddDays(90);
    private static readonly Guid A = Guid.NewGuid(); // histórico, 10,4 dias
    private static readonly Guid B = Guid.NewGuid(); // sem histórico
    private static readonly Guid C = Guid.NewGuid(); // histórico, 3,0 dias e 40% de inadimplência

    private static readonly Dictionary<Guid, PartyPunctuality> Parties = new()
    {
        [A] = new PartyPunctuality(10.4m, true, 0m),
        [B] = new PartyPunctuality(0m, false, 0m),
        [C] = new PartyPunctuality(3.0m, true, 40m),
    };

    private static AdjustedReceivable One(Guid party, int dueOffset, decimal balance, decimal threshold = 20m, bool tenantHistory = true, decimal tenantAvg = 6.0m) =>
        AdjustedCashFlowCalculator.Calculate(Today, To, [new ProjectedReceivable(Guid.NewGuid(), party, Today.AddDays(dueOffset), balance)], Parties, tenantAvg, tenantHistory, threshold)
            .Receivables.Single();

    public static TheoryData<string, int, decimal, int, decimal, string> Cases => new()
    {
        // cliente, vencimento (dias), saldo, data ajustada esperada (dias), valor esperado, caso
        { "A", 5, 2_000m, 15, 2_000m, "atraso médio de 10,4 arredonda para 10, dias corridos" },
        { "B", 20, 3_000m, 26, 3_000m, "sem histórico usa a média da empresa (6,0)" },
        { "C", 12, 4_000m, 15, 2_400m, "acima do limite: valor × (1 − 40%)" },
        { "A", -10, 1_000m, 0, 1_000m, "vencido: max(hoje, vencimento + atraso) = hoje" },
        { "A", -3, 1_000m, 7, 1_000m, "vencido há 3 dias com atraso de 10 cai daqui a 7" },
        { "C", -45, 5_000m, 0, 3_000m, "vencido há mais de 30 dias, ponderado" },
        { "C", 0, 333.33m, 3, 200.00m, "arredonda a 2 casas por título (199,998 → 200,00)" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Desloca_e_pondera_cada_recebivel(string party, int due, decimal balance, int adjusted, decimal amount, string why)
    {
        var id = party switch { "A" => A, "B" => B, _ => C };
        var r = One(id, due, balance);
        r.AdjustedDate.Should().Be(Today.AddDays(adjusted), why);
        r.AdjustedAmount.Should().Be(amount, why);
        r.BaseDate.Should().Be(due < 0 ? Today : Today.AddDays(due), "a base lança o vencido hoje");
    }

    [Theory]
    [InlineData(10.4, 10)]
    [InlineData(10.5, 11)]
    [InlineData(2.5, 3)]
    [InlineData(0.4, 0)]
    public void Atraso_arredonda_para_dias_inteiros_away_from_zero(double avg, int days)
    {
        AdjustedCashFlowCalculator.Delay(new PartyPunctuality((decimal)avg, true, 0), 0, true).Should().Be((days, DelaySource.Customer));
    }

    [Fact]
    public void Sem_historico_na_empresa_nao_desloca()
    {
        var r = One(B, 20, 3_000m, tenantHistory: false, tenantAvg: 0);
        r.AdjustedDate.Should().Be(Today.AddDays(20));
        r.Source.Should().Be(DelaySource.None);
        AdjustedCashFlowCalculator.Delay(null, 6m, true).Should().Be((6, DelaySource.Tenant), "terceiro que nunca teve título usa a média");
    }

    [Theory]
    [InlineData(8_000, 20_000, 40.00)]
    [InlineData(0, 0, 0)]
    [InlineData(500, 0, 100)]
    [InlineData(30_000, 20_000, 100)]
    [InlineData(1_000, 3_000, 33.33)]
    [InlineData(0, 10_000, 0)]
    public void Inadimplencia_vencido_30_dias_sobre_faturado_12_meses(int overdue, int invoiced, double pct)
    {
        AdjustedCashFlowCalculator.DefaultPct(overdue, invoiced).Should().Be((decimal)pct);
    }

    [Theory]
    [InlineData(20.00, 1_000, 1_000)]
    [InlineData(20.01, 1_000, 799.90)]
    [InlineData(19.99, 1_000, 1_000)]
    [InlineData(100, 1_000, 0)]
    public void Pondera_so_acima_do_limite(double pct, int balance, double expected)
    {
        AdjustedCashFlowCalculator.Weigh(balance, (decimal)pct, 20m).Should().Be((decimal)expected);
    }

    [Fact]
    public void Deslocado_para_depois_do_horizonte_sai_do_periodo_e_fecha_a_conta()
    {
        var receivables = new[]
        {
            new ProjectedReceivable(Guid.NewGuid(), A, Today.AddDays(85), 1_500m), // vai para +95
            new ProjectedReceivable(Guid.NewGuid(), C, Today.AddDays(12), 4_000m), // 2.400 em +15
            new ProjectedReceivable(Guid.NewGuid(), B, Today.AddDays(30), 1_200m), // previsto com data, +36
        };
        var flow = AdjustedCashFlowCalculator.Calculate(Today, To, receivables, Parties, 6m, true, 20m);
        flow.BeyondHorizonCount.Should().Be(1);
        flow.BeyondHorizonTotal.Should().Be(1_500m);
        flow.AtRiskTotal.Should().Be(1_600m);
        flow.InflowDelta[Today.AddDays(85)].Should().Be(-1_500m);
        flow.InflowDelta.ContainsKey(Today.AddDays(95)).Should().BeFalse();
        flow.InflowDelta[Today.AddDays(12)].Should().Be(-4_000m);
        flow.InflowDelta[Today.AddDays(15)].Should().Be(2_400m);
        flow.InflowDelta[Today.AddDays(30)].Should().Be(-1_200m);
        flow.InflowDelta[Today.AddDays(36)].Should().Be(1_200m);
        var baseTotal = receivables.Sum(r => r.Balance);
        (baseTotal + flow.InflowDelta.Values.Sum()).Should().Be(baseTotal - flow.AtRiskTotal - flow.BeyondHorizonTotal, "base − ajustado = risco + deslocado");
    }

    [Fact]
    public void Pagamentos_recorrentes_e_realizado_nao_mudam()
    {
        // O ajustado só soma a variação de entradas aos totais da base (CA 6): saídas e o realizado ficam iguais.
        var totals = new Dictionary<DateOnly, CashFlowDayTotals>
        {
            [Today.AddDays(-2)] = new(100m, 30m),
            [Today] = new(1_000m, 400m, OverdueInflow: 1_000m, OverdueOutflow: 400m),
            [Today.AddDays(5)] = new(0m, 900m),
        };
        var flow = AdjustedCashFlowCalculator.Calculate(Today, To, [new ProjectedReceivable(Guid.NewGuid(), A, Today.AddDays(-1), 1_000m)], Parties, 6m, true, 20m);
        var adjusted = new Dictionary<DateOnly, CashFlowDayTotals>(totals);
        foreach (var (day, delta) in flow.InflowDelta)
        {
            adjusted[day] = adjusted.GetValueOrDefault(day) with { Inflow = adjusted.GetValueOrDefault(day).Inflow + delta };
        }

        adjusted[Today].Inflow.Should().Be(0m);
        adjusted[Today.AddDays(9)].Inflow.Should().Be(1_000m, "vencido ontem com atraso de 10 dias");
        adjusted.Sum(d => d.Value.Outflow).Should().Be(totals.Sum(d => d.Value.Outflow));
        adjusted[Today.AddDays(-2)].Should().Be(totals[Today.AddDays(-2)]);
    }

    [Fact]
    public void Motivo_do_deslocamento_em_portugues()
    {
        CashFlowService.Reason(One(A, 5, 100m), "Alfa").Should().Be("Atraso médio de 10 dias de Alfa.");
        CashFlowService.Reason(One(B, 5, 100m), "Beta").Should().Be("Média da empresa: 6 dias.");
        CashFlowService.Reason(One(C, 5, 100m), "Gama").Should().Be("Atraso médio de 3 dias de Gama. Valor ponderado pela inadimplência de 40,00%.");
    }
}
