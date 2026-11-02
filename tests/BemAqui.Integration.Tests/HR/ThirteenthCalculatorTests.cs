using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-04: o motor do 13º, sem banco. <b>Dataset sintético</b> (ADR 0039 §4, CA 4): funcionários fictícios, sem nome nem CPF, com o
/// resultado calculado à mão pelas tabelas do pacote de 2026 (INSS da Portaria MPS/MF 13/2026, IRRF da Lei 15.191/2025 com a redução da
/// Lei 15.270/2025, FGTS 8%, encargos de Lucro Presumido 28,8%) e os parâmetros pré-configurados (avos com 15 dias, 1ª parcela até o mês
/// da parcela, doença e acidente pela previdência saem dos dias, médias dos meses com avos). Nenhum holerite real.
/// </summary>
public class ThirteenthCalculatorTests
{
    private static readonly IReadOnlyList<PayrollCalcItem> Items =
        PayrollItemService.Standards.Select((s, i) => new PayrollCalcItem(new Guid(i + 1, 0, 0, new byte[8]), s.Code, s.Name, s.Kind, s.Calc, s.Inss, s.Fgts, s.Irrf, s.DeductsIrrf)).ToList();

    private static IReadOnlyList<PayrollCalcTable> Tables2026 { get; } = PayrollTaxPackages.All.Single(p => p.Package == 2026).Tables
        .Select(t => new PayrollCalcTable(Guid.NewGuid(), t.Kind, t.ValidFrom!.Value, PayrollTaxTableStatus.Unconfirmed, t.Rounding ?? PayrollRounding.AwayFromZero, t.Content!)).ToList();

    private static ThirteenthSnapshot Employee(ThirteenthInstallment installment, decimal salary, DateOnly? admission = null, DateOnly? termination = null,
        IReadOnlyList<PayrollCalcLeave>? leaves = null, IReadOnlyList<ThirteenthAbsence>? absences = null, IReadOnlyList<ThirteenthVariable>? variables = null,
        decimal advancePaid = 0m, decimal vacationAdvance = 0m, IReadOnlyList<PayrollCalcDependent>? dependents = null, ThirteenthParameters? parameters = null)
    {
        var prm = parameters ?? ThirteenthParameters.Default;
        return new ThirteenthSnapshot(2026, installment, ThirteenthCalculator.LastMonth(installment, installment == ThirteenthInstallment.First ? 11 : 12), salary,
            admission ?? new DateOnly(2020, 1, 2), termination, null, false, dependents ?? [], leaves ?? [], absences ?? [], variables ?? [], advancePaid, vacationAdvance,
            PayrollCalcParameters.Default, prm);
    }

    private static PayrollCalcResult Calc(ThirteenthSnapshot e) => ThirteenthCalculator.Calculate(new ThirteenthInput(e, Items, Tables2026));

    private static Dictionary<string, decimal> Lines(PayrollCalcResult r) => r.Lines.GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));

    private static Dictionary<string, decimal> L(params (string Code, decimal Amount)[] lines) => lines.ToDictionary(l => l.Code, l => l.Amount);

    private static readonly ThirteenthInstallment First = ThirteenthInstallment.First;
    private static readonly ThirteenthInstallment Second = ThirteenthInstallment.Second;

    public sealed record Case(string Name, ThirteenthSnapshot Input, IReadOnlyDictionary<string, decimal> Lines, decimal InssBase, decimal Fgts, decimal Employer, decimal Net);

    public static TheoryData<string> Names => new(Dataset.Select(c => c.Name));

    /// <summary>O dataset sintético (CA 4), cada valor à mão; a regra está no nome do caso.</summary>
    private static readonly IReadOnlyList<Case> Dataset =
    [
        // Admitido em 10/06: junho com 21 dias conta; jun a nov pelo ponto e dezembro projetado = 7 avos. 3.000 × 7/12 = 1.750; metade 875;
        // FGTS 8% = 70; sem INSS e IRRF.
        new("01 1ª parcela, admitido no meio do ano", Employee(First, 3000m, new DateOnly(2026, 6, 10)), L(("13PARC1", 875m), ("FGTS", 70m)), 0m, 70m, 0m, 875m),
        // Ano inteiro, 2.400: metade 1.200 menos 400 já pagos com as férias = 800; FGTS 64.
        new("02 1ª parcela com o adiantamento pago nas férias", Employee(First, 2400m, vacationAdvance: 400m), L(("13PARC1", 800m), ("FGTS", 64m)), 0m, 64m, 0m, 800m),
        // 6.000 o ano inteiro, 1ª parcela de 3.000 paga: INSS 641,51 e IRRF 385,10 sobre o 13º inteiro (como a Mensal de 6.000); FGTS sobre
        // 3.000 = 240; encargos 28,8% de 6.000 = 1.728.
        new("03 2ª parcela com INSS e IRRF do 13º inteiro", Employee(Second, 6000m, advancePaid: 3000m),
            L(("13SAL", 6000m), ("13ADIANT", 3000m), ("INSS", 641.51m), ("IRRF", 385.10m), ("FGTS", 240m)), 6000m, 240m, 1728m, 1973.39m),
        // Admitido em 10/06 (7 avos): 3.000 × 7/12 = 1.750; horas extras e DSR de jul, ago e out (600) ÷ 7 meses × 7/12 = 50; 13º de 1.800;
        // 1ª parcela 875; INSS 137,69 (1.621 × 7,5% + 179 × 9%); IRRF zero; FGTS sobre 925 = 74.
        new("04 2ª parcela com médias das horas extras", Employee(Second, 3000m, new DateOnly(2026, 6, 10), advancePaid: 875m,
                variables: [new(7, 200m), new(8, 100m), new(10, 300m)]),
            L(("13SAL", 1750m), ("13MED", 50m), ("13ADIANT", 875m), ("INSS", 137.69m), ("FGTS", 74m)), 1800m, 74m, 518.40m, 787.31m),
        // Março com 17 faltas: 14 dias trabalhados, o avo cai (11 avos); 2.400 × 11/12 = 2.200; sem 1ª parcela; INSS 173,69; FGTS 176.
        new("05 faltas derrubam um avo", Employee(Second, 2400m, absences: [new(3, 17)]), L(("13SAL", 2200m), ("INSS", 173.69m), ("FGTS", 176m)), 2200m, 176m, 633.60m, 2026.31m),
        // Afastado pela previdência de 01/03 a 30/04: março conta (15 dias pela empresa + 16 pela previdência = 15 trabalhados), abril não.
        new("06 afastado pela previdência", Employee(Second, 2400m, leaves: [new(new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 30), AbsenceReason.MedicalCertificate)]),
            L(("13SAL", 2200m), ("INSS", 173.69m), ("FGTS", 176m)), 2200m, 176m, 633.60m, 2026.31m),
    ];

    /// <summary>CA 4: cada linha, base e líquido do dataset sintético bate centavo a centavo.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Dataset_bate_centavo_a_centavo(string name)
    {
        var c = Dataset.Single(x => x.Name == name);
        var r = Calc(c.Input);
        r.Issues.Should().NotContain(i => i.Blocking, name);
        Lines(r).Should().Equal(c.Lines, name);
        r.InssBase.Should().Be(c.InssBase, name);
        r.FgtsAmount.Should().Be(c.Fgts, name);
        r.EmployerCharges.Should().Be(c.Employer, name);
        r.Totals.Net.Should().Be(c.Net, name);
        r.Lines.Should().OnlyContain(l => l.Calculated && l.Memory.Count > 0, "toda linha tem a memória (CA 2)");
    }

    /// <summary>CA 3: o mês conta com 15 dias ou mais trabalhados; faltas e dias pela previdência saem; o parâmetro muda o mínimo e o acidente.</summary>
    [Theory]
    [InlineData("2026-06-16", null, 15, 7)]   // junho com 15 dias conta: jun a dez
    [InlineData("2026-06-17", null, 15, 6)]   // junho com 14 dias não conta
    [InlineData("2020-01-02", "2026-09-14", 15, 8)] // desligado em 14/09: setembro com 14 dias não conta
    [InlineData("2020-01-02", "2026-09-15", 15, 9)]
    [InlineData("2026-06-17", null, 14, 7)]   // mínimo de 14 dias pelo parâmetro
    public void Avos_pelo_vinculo_e_pelo_minimo_de_dias(string admission, string? termination, int minDays, int expected)
    {
        var months = ThirteenthCalculator.ThirteenthMonths(2026, 12, DateOnly.Parse(admission, System.Globalization.CultureInfo.InvariantCulture),
            termination is null ? null : DateOnly.Parse(termination, System.Globalization.CultureInfo.InvariantCulture), [], [], 15, ThirteenthParameters.Default with { MonthMinDays = minDays });
        months.Count(m => m.Counts).Should().Be(expected);
        months.Should().HaveCount(12);
    }

    [Fact]
    public void Acidente_de_trabalho_pelo_parametro_e_memoria_mes_a_mes()
    {
        IReadOnlyList<PayrollCalcLeave> accident = [new(new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 30), AbsenceReason.WorkAccident)];
        ThirteenthCalculator.ThirteenthMonths(2026, 12, new DateOnly(2020, 1, 2), null, accident, [], 15, ThirteenthParameters.Default).Count(m => m.Counts).Should().Be(11);
        ThirteenthCalculator.ThirteenthMonths(2026, 12, new DateOnly(2020, 1, 2), null, accident, [], 15, ThirteenthParameters.Default with { LeaveRule = ThirteenthLeaveRule.SicknessOnly })
            .Count(m => m.Counts).Should().Be(12, "com \"Só doença\", o acidente conta como trabalhado");
        var april = ThirteenthCalculator.ThirteenthMonths(2026, 12, new DateOnly(2020, 1, 2), null, accident, [new(4, 3)], 15, ThirteenthParameters.Default).Single(m => m.Month == 4);
        april.Should().Be(new ThirteenthMonth(4, 30, 0, 30, 0, false), "as faltas nunca passam dos dias que sobram");
        var memory = Calc(Employee(Second, 2400m, absences: [new(3, 17)])).Lines.Single(l => l.Code == "13SAL").Memory;
        memory.Should().Contain("mar/2026: 31 dias no vínculo − 17 faltas = 14 dias trabalhados → não conta (menos de 15 dias).");
        memory.Should().Contain("Total: 11/12 avos.");
    }

    /// <summary>CA 1: a 1ª parcela usa os avos do ano projetados (o ano inteiro é a metade da remuneração); pelo parâmetro, só os avos até o mês.</summary>
    [Fact]
    public void Avos_da_primeira_parcela_pelo_parametro()
    {
        ThirteenthCalculator.LastMonth(First, 11).Should().Be(11);
        ThirteenthCalculator.LastMonth(Second, 11).Should().Be(12);
        Lines(Calc(Employee(First, 3000m)))["13PARC1"].Should().Be(1500m, "no ano inteiro, a metade da remuneração");
        var months = ThirteenthCalculator.MonthsOf(Employee(First, 3000m, absences: [new(3, 17)]));
        months.Should().HaveCount(12);
        months.Single(m => m.Month == 12).Projected.Should().BeTrue();
        months.Count(m => m.Counts).Should().Be(11, "março caiu pelas faltas; dezembro é projetado");
        var upToMonth = ThirteenthParameters.Default with { FirstAvos = ThirteenthFirstAvos.UpToMonth };
        // Admitido em 10/06: só até novembro são 6 avos → 3.000 × 6/12 = 1.500; metade 750.
        Lines(Calc(Employee(First, 3000m, new DateOnly(2026, 6, 10), parameters: upToMonth)))["13PARC1"].Should().Be(750m);
    }

    /// <summary>CA 2: sem 1ª parcela, a 2ª sai inteira com o aviso; sem avos, o funcionário trava ("Sem avos no ano"); sem médias pelo parâmetro.</summary>
    [Fact]
    public void Sem_adiantamento_sem_avos_e_sem_medias()
    {
        var noAdvance = Calc(Employee(Second, 2400m));
        Lines(noAdvance).Should().NotContainKey("13ADIANT");
        noAdvance.Issues.Should().Contain(i => i.Code == "no_advance" && !i.Blocking);
        Calc(Employee(Second, 3000m, new DateOnly(2026, 12, 20))).Issues.Should().ContainSingle(i => i.Code == ThirteenthCalculator.NoAvosCode && i.Blocking);
        var withoutAverages = Calc(Employee(Second, 3000m, variables: [new(5, 600m)], parameters: ThirteenthParameters.Default with { Averages = ThirteenthAveragesRule.None }));
        Lines(withoutAverages).Should().NotContainKey("13MED");
        var paidWithVacation = Calc(Employee(First, 2400m, vacationAdvance: 1500m));
        paidWithVacation.Lines.Should().BeEmpty("a 1ª parcela já saiu inteira com as férias");
        paidWithVacation.Issues.Should().Contain(i => i.Code == "paid_with_vacation");
    }

    /// <summary>Médias: só as regras das horas da FLH-02b (horas extras, adicional noturno e DSR); o banco de horas pago e o salário ficam fora.</summary>
    [Theory]
    [InlineData(PayrollCalculation.OvertimeWorkday, true)]
    [InlineData(PayrollCalculation.OvertimeRestDay, true)]
    [InlineData(PayrollCalculation.OvertimeHoliday, true)]
    [InlineData(PayrollCalculation.NightShift, true)]
    [InlineData(PayrollCalculation.Dsr, true)]
    [InlineData(PayrollCalculation.HourBankPayout, false)]
    [InlineData(PayrollCalculation.BaseSalary, false)]
    [InlineData(PayrollCalculation.None, false)]
    public void Variaveis_das_medias(PayrollCalculation calculation, bool expected) =>
        ThirteenthCalculator.IsVariable(PayrollItemKind.Earning, calculation).Should().Be(expected);

    /// <summary>Determinismo (ADR 0039 §2): o mesmo retrato dá o mesmo resultado e volta igual do JSON.</summary>
    [Fact]
    public void Retrato_volta_igual_e_da_o_mesmo_resultado()
    {
        var e = Employee(Second, 3000m, new DateOnly(2026, 6, 10), advancePaid: 875m, variables: [new(7, 200m)], absences: [new(8, 2)]);
        var json = ThirteenthCalculator.WriteSnapshot(e);
        var back = ThirteenthCalculator.ReadSnapshot(json)!;
        ThirteenthCalculator.WriteSnapshot(back).Should().Be(json);
        Calc(back).Totals.Should().Be(Calc(e).Totals);
        ThirteenthCalculator.ReadSnapshot("{\"baseSalary\":") .Should().BeNull();
    }

    /// <summary>CA 7: desde janeiro, as duas parcelas por pessoa pelo salário atual × avos projetados; a parcela fechada e o desligado saem.</summary>
    [Fact]
    public void Estimativa_do_13o_desde_janeiro()
    {
        var ana = Guid.NewGuid();
        var bia = Guid.NewGuid();
        var caio = Guid.NewGuid();
        var forecast = new ThirteenthForecast(
            [new(ana, 3000m, new DateOnly(2020, 1, 2), null), new(bia, 2000m, new DateOnly(2020, 1, 2), new DateOnly(2027, 10, 31)), new(caio, 1200m, new DateOnly(2027, 6, 10), null)], 11, 12, 15);
        var lines = ThirteenthCalculator.ForecastLines(forecast, [2027], []);
        lines.Should().ContainSingle(l => l.EmployeeId == ana && l.Kind == PayrollForecastKind.Thirteenth1 && l.Period == new DateOnly(2027, 11, 1) && l.Amount == 1500m);
        lines.Should().ContainSingle(l => l.EmployeeId == ana && l.Kind == PayrollForecastKind.Thirteenth2 && l.Period == new DateOnly(2027, 12, 1) && l.Amount == 1500m);
        lines.Should().NotContain(l => l.EmployeeId == bia, "desligada antes de novembro: o proporcional sai na rescisão");
        lines.Where(l => l.EmployeeId == caio).Select(l => l.Amount).Should().Equal(350m, 350m);
        ThirteenthCalculator.ForecastLines(forecast, [2027], [(PayrollRunType.Thirteenth1, 2027)]).Should().NotContain(l => l.Kind == PayrollForecastKind.Thirteenth1);

        // Em janeiro (a última Mensal fechada é a de dezembro), a janela é de 2027 e o 13º de 2027 já aparece.
        var input = new ForecastInput(new DateOnly(2026, 12, 1), Guid.NewGuid(), 3, [new ForecastEmployee(ana, 2500m, 3000m, null)], [], [], [], forecast);
        PayrollForecastRules.Plan(input).Where(l => l.Kind is PayrollForecastKind.Thirteenth1 or PayrollForecastKind.Thirteenth2 && l.EmployeeId == ana).Select(l => l.Period)
            .Should().Equal(new DateOnly(2027, 11, 1), new DateOnly(2027, 12, 1));
        PayrollForecastRules.Plan(input with { Thirteenth = null }).Should().NotContain(l => l.Kind == PayrollForecastKind.Thirteenth1, "hr.forecast_13th desligado");
        ThirteenthCalculator.ForecastDescription(PayrollRunType.Thirteenth2, 2027, "M001", "Ana").Should().Be("Estimativa do 13º 2ª parcela de 2027 — M001");
    }
}
