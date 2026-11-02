using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>RH-02c: regras sem banco do fechamento (data de pagamento, conta e centro) e da folha prevista (janela, 13º, férias e substituição).</summary>
public class PayrollClosingRulesTests
{
    private static readonly PayrollDueParameters Due = PayrollDueParameters.Default;

    /// <summary>Calendário de seg a sex, com o feriado de 15/11 e o de 25/12.</summary>
    private static bool Business(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && d != new DateOnly(2026, 11, 15) && d != new DateOnly(2026, 12, 25);

    [Fact]
    public void Data_de_pagamento_pelo_tipo_com_dia_nao_util_antecipando()
    {
        // Mensal de outubro: 5º dia útil de novembro de 2026 (2, 3, 4, 5, 6 → 06/11).
        PayrollClosingRules.Resolve(PayrollClosingRules.Rule(PayrollRunType.Monthly, new DateOnly(2026, 10, 1), Due), Business).Should().Be(new DateOnly(2026, 11, 6));
        // Adiantamento: dia 20 da competência; 20/09/2026 é domingo → 18/09.
        PayrollClosingRules.Resolve(PayrollClosingRules.Rule(PayrollRunType.Advance, new DateOnly(2026, 9, 1), Due), Business).Should().Be(new DateOnly(2026, 9, 18));
        // 13º: 30/11/2026 é segunda; 20/12/2026 é domingo → 18/12.
        PayrollClosingRules.Resolve(PayrollClosingRules.Rule(PayrollRunType.Thirteenth1, new DateOnly(2026, 11, 1), Due), Business).Should().Be(new DateOnly(2026, 11, 30));
        PayrollClosingRules.Resolve(PayrollClosingRules.Rule(PayrollRunType.Thirteenth2, new DateOnly(2026, 12, 1), Due), Business).Should().Be(new DateOnly(2026, 12, 18));
        // Férias e Rescisão: informada no fechamento.
        PayrollClosingRules.Rule(PayrollRunType.Vacation, new DateOnly(2026, 10, 1), Due).Should().BeOfType<PaymentDateRule.Informed>();
        PayrollClosingRules.Resolve(PayrollClosingRules.Rule(PayrollRunType.Termination, new DateOnly(2026, 10, 1), Due), Business).Should().BeNull();
        // Calendário sem dia útil: o n-ésimo não existe; a data fixa fica como está.
        PayrollClosingRules.Resolve(new PaymentDateRule.NthBusinessDay(2026, 11, 5), _ => false).Should().BeNull();
        PayrollClosingRules.Resolve(new PaymentDateRule.Fixed(new DateOnly(2026, 12, 20)), _ => false).Should().Be(new DateOnly(2026, 12, 20));
    }

    [Fact]
    public void Conta_pelo_centro_do_cargo_e_guia_pelo_grupo_da_maior_base()
    {
        PayrollClosingRules.GroupOf(positionHasWorkCenter: true).Should().Be(PayrollCostGroup.Direct);
        PayrollClosingRules.GroupOf(positionHasWorkCenter: false).Should().Be(PayrollCostGroup.Admin);
        PayrollClosingRules.ChargeGroup([(PayrollCostGroup.Direct, 5_000m), (PayrollCostGroup.Admin, 3_000m), (PayrollCostGroup.Admin, 1_500m)]).Should().Be(PayrollCostGroup.Direct);
        PayrollClosingRules.ChargeGroup([(PayrollCostGroup.Direct, 2_000m), (PayrollCostGroup.Admin, 3_000m)]).Should().Be(PayrollCostGroup.Admin);
        PayrollClosingRules.ChargeGroup([(PayrollCostGroup.Direct, 3_000m), (PayrollCostGroup.Admin, 3_000m)]).Should().Be(PayrollCostGroup.Direct, "empate fica na mão de obra direta");
        PayrollClosingRules.ChargeGroup([]).Should().Be(PayrollCostGroup.Admin);
        PayrollClosingRules.SalaryDescription(PayrollRunType.Monthly, new DateOnly(2026, 10, 1), "M001", "Ana").Should().Be("Salário Mensal outubro de 2026 — M001");
        PayrollClosingRules.ChargeDescription(PayrollChargeKind.Fgts, new DateOnly(2026, 10, 1)).Should().Be("Guia FGTS outubro de 2026");
        PayrollClosingRules.ClosedMessage(new DateOnly(2026, 10, 1), 1).Should().Be("Folha de outubro de 2026 fechada. 1 título a pagar gerado.");
    }

    [Fact]
    public void Previsao_de_tres_meses_com_13o_ferias_e_desligamento()
    {
        var ana = Guid.NewGuid();
        var bia = Guid.NewGuid();
        var run = Guid.NewGuid();
        var vacationRun = Guid.NewGuid();
        var input = new ForecastInput(new DateOnly(2026, 9, 1), run, 3,
            [new ForecastEmployee(ana, 2_500m, 3_000m, null), new ForecastEmployee(bia, 1_800m, 2_001m, new DateOnly(2026, 10, 31))],
            [new ForecastCharge(PayrollChargeKind.Fgts, 600m), new ForecastCharge(PayrollChargeKind.Union, 0m)],
            [new ForecastVacation(ana, new DateOnly(2026, 12, 1), 4_100m, vacationRun), new ForecastVacation(ana, new DateOnly(2027, 2, 1), 4_100m, vacationRun)],
            [], new ThirteenthForecast([new(ana, 3_000m, new DateOnly(2020, 1, 2), null), new(bia, 2_001m, new DateOnly(2020, 1, 2), new DateOnly(2026, 10, 31))], 11, 12, 15));
        var lines = PayrollForecastRules.Plan(input);

        lines.Where(l => l.Kind == PayrollForecastKind.Salary && l.EmployeeId == ana).Select(l => l.Period).Should().Equal(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1));
        lines.Where(l => l.Kind == PayrollForecastKind.Salary && l.EmployeeId == bia).Select(l => l.Period).Should().Equal([new DateOnly(2026, 10, 1)], "desligada em outubro");
        lines.Where(l => l.Kind == PayrollForecastKind.Charge).Should().HaveCount(3).And.OnlyContain(l => l.ChargeKind == PayrollChargeKind.Fgts && l.Amount == 600m);
        lines.Should().ContainSingle(l => l.Kind == PayrollForecastKind.Thirteenth1 && l.EmployeeId == ana && l.Period == new DateOnly(2026, 11, 1) && l.Amount == 1_500m);
        lines.Should().ContainSingle(l => l.Kind == PayrollForecastKind.Thirteenth2 && l.EmployeeId == ana && l.Period == new DateOnly(2026, 12, 1));
        lines.Should().NotContain(l => l.Kind == PayrollForecastKind.Thirteenth1 && l.EmployeeId == bia);
        lines.Where(l => l.Kind == PayrollForecastKind.Vacation).Select(l => l.Period).Should().Equal([new DateOnly(2026, 12, 1)], "só as férias dentro da janela");

        // 13º já fechado no ano: sai da previsão (CA 15). A janela anda com a última Mensal fechada.
        var closed = input with { ClosedThirteenth = [(PayrollRunType.Thirteenth1, 2026)] };
        PayrollForecastRules.Plan(closed).Should().NotContain(l => l.Kind == PayrollForecastKind.Thirteenth1);
        var next = PayrollForecastRules.Plan(input with { LastMonthly = new DateOnly(2026, 10, 1) });
        next.Where(l => l.Kind == PayrollForecastKind.Salary && l.EmployeeId == ana).Select(l => l.Period).Should().Equal(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 1));
        next.Select(l => l.Key).Should().OnlyHaveUniqueItems();
        PayrollForecastRules.Window(new DateOnly(2026, 9, 1), 99).Should().HaveCount(PayrollForecastRules.MaxMonths);
    }
}
