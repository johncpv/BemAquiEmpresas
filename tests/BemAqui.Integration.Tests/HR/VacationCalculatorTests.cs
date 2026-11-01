using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-03 CA 6, 7 e 10: o motor da folha de férias, sem banco. <b>Dataset sintético</b> (ADR 0039 §4): funcionários fictícios, sem nome nem
/// CPF, com o resultado calculado à mão pelas tabelas de 2026 (pacote <c>hr-payroll-tables-2026</c>: INSS 7,5% até 1.621,00, 9% até
/// 2.902,84, 12% até 4.354,27 e 14% até 8.475,55, somado e arredondado no total; IRRF da Lei 15.191/2025 com a redução da Lei 15.270/2025;
/// salário mínimo 1.621,00; FGTS 8%) e os parâmetros pré-configurados (média de 12 meses, INSS rateado pelas competências do gozo, IRRF
/// em separado, abono e o seu 1/3 sem incidência pelas marcas do seed). Nenhum holerite real.
/// </summary>
public class VacationCalculatorTests
{
    private static readonly IReadOnlyList<PayrollCalcItem> Items =
        PayrollItemService.Standards.Select((s, i) => new PayrollCalcItem(new Guid(i + 1, 0, 0, new byte[8]), s.Code, s.Name, s.Kind, s.Calc, s.Inss, s.Fgts, s.Irrf, s.DeductsIrrf)).ToList();

    private static readonly IReadOnlyList<PayrollCalcTable> Tables2026 = PayrollTaxPackages.All.Single(p => p.Package == 2026).Tables
        .Select(t => new PayrollCalcTable(Guid.NewGuid(), t.Kind, t.ValidFrom!.Value, PayrollTaxTableStatus.Unconfirmed, t.Rounding ?? PayrollRounding.AwayFromZero, t.Content!)).ToList();

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static VacationCalcSnapshot Employee(decimal salary, DateOnly start, int days, int sold = 0, decimal variables = 0m, int? insalubrity = null, DateOnly? concession = null,
        int dependents = 0, VacationTaxation taxation = VacationTaxation.SplitByMonth) =>
        new(salary, D(2020, 1, 2), insalubrity, false, dependents, D(2025, 1, 2), D(2026, 1, 1), concession ?? D(2027, 1, 1), start, days, sold, false, start.AddDays(-2),
            variables > 0m ? Enumerable.Range(0, 12).Select(i => new VacationCalcVariable(D(2025, 1, 1).AddMonths(i), variables / 12m)).ToList() : [],
            PayrollCalcParameters.Default, VacationParameters.Default with { Taxation = taxation });

    private static VacationCalcResult Calc(VacationCalcSnapshot e)
    {
        var inss = Tables2026.Single(t => t.Kind == PayrollTaxTableKind.Inss);
        var months = VacationPolicy.DaysByMonth(e.Start, e.Start.AddDays(e.Days - 1)).ToDictionary(m => m.Month, _ => inss);
        return VacationCalculator.Calculate(new VacationCalcInput(e, Items, Tables2026, months));
    }

    private static Dictionary<string, decimal> L(params (string Code, decimal Amount)[] lines) => lines.ToDictionary(l => l.Code, l => l.Amount);

    /// <summary>Caso do dataset: as linhas esperadas (código → valor), a base do INSS, o FGTS e o líquido.</summary>
    public sealed record Case(string Name, VacationCalcSnapshot Input, IReadOnlyDictionary<string, decimal> Lines, decimal InssBase, decimal Fgts, decimal Net);

    public static TheoryData<string> Names => new(Dataset.Select(c => c.Name));

    private static readonly IReadOnlyList<Case> Dataset =
    [
        // 3.000 × 30/30 = 3.000 + 1/3 de 1.000 = 4.000 → INSS 121,575 + 115,3656 + 131,6592 = 368,5998 → 368,60; IRRF zerado pela redução.
        new("01 férias integrais", Employee(3000m, D(2026, 6, 1), 30), L(("FERIAS", 3000m), ("FER13", 1000m), ("INSS", 368.60m), ("FGTS", 320m)), 4000m, 320m, 3631.40m),
        // 6.000 + 2.000 = 8.000 → INSS 921,5144 → 921,51; IRRF: 7.078,49 × 27,5% − 908,73 = 1.037,85 (acima de 7.350, sem redução; o simplificado daria 1.124,29).
        new("02 férias integrais com IRRF", Employee(6000m, D(2026, 6, 1), 30), L(("FERIAS", 6000m), ("FER13", 2000m), ("INSS", 921.51m), ("IRRF", 1037.85m), ("FGTS", 640m)), 8000m, 640m, 6040.64m),
        // 20 dias + 10 vendidos: 2.000 + 666,67; abono 1.000 + 333,33 fora das bases; INSS sobre 2.666,67 = 215,6853 → 215,69.
        new("03 abono pecuniário", Employee(3000m, D(2026, 6, 1), 20, sold: 10), L(("FERIAS", 2000m), ("FER13", 666.67m), ("ABONO", 1000m), ("ABONO13", 333.33m), ("INSS", 215.69m), ("FGTS", 213.33m)),
            2666.67m, 213.33m, 3784.31m),
        // Variáveis de 3.600 nos 12 meses: média 300; 3.000 + 300 + 1/3 de 1.100 = 4.400 → INSS 417,5144 → 417,51.
        new("04 médias de horas extras", Employee(3000m, D(2026, 6, 1), 30, variables: 3600m), L(("FERIAS", 3000m), ("FERMED", 300m), ("FER13", 1100m), ("INSS", 417.51m), ("FGTS", 352m)),
            4400m, 352m, 3982.49m),
        // 16/06 a 15/07: 15 dias em cada competência, base 2.000 em cada → INSS 155,685 → 155,69 duas vezes = 311,38 (inteiro daria 368,60).
        new("05 gozo que atravessa o mês (INSS rateado)", Employee(3000m, D(2026, 6, 16), 30), L(("FERIAS", 3000m), ("FER13", 1000m), ("INSS", 311.38m), ("FGTS", 320m)), 4000m, 320m, 3688.62m),
        // Primeira parte do fracionamento: 14 dias → 1.400 + 466,67 = 1.866,67 → INSS 143,6853 → 143,69.
        new("06 fracionada, 14 dias", Employee(3000m, D(2026, 6, 1), 14), L(("FERIAS", 1400m), ("FER13", 466.67m), ("INSS", 143.69m), ("FGTS", 149.33m)), 1866.67m, 149.33m, 1722.98m),
        // Depois de faltas (24 dias de direito) e com insalubridade 20% do mínimo (324,20): 2.324,20 × 24/30 = 1.859,36 + 619,79 = 2.479,15 →
        // INSS 121,575 + 858,15 × 9% (77,2335) = 198,8085 → 198,81.
        new("07 com faltas (24 dias) e insalubridade", Employee(2000m, D(2026, 6, 1), 24, insalubrity: 20), L(("FERIAS", 1859.36m), ("FER13", 619.79m), ("INSS", 198.81m), ("FGTS", 198.33m)),
            2479.15m, 198.33m, 2280.34m),
        // Concessivo terminou em 10/06: 20 dos 30 dias em dobro → 3.000 × 50/30 = 5.000 + 1.666,67 = 6.666,67 → INSS 734,85; IRRF pela legal com a
        // redução: 5.931,82 × 27,5% − 908,73 = 722,5205 − (978,62 − 0,133145 × 6.666,67) = 631,53.
        new("08 depois do concessivo (em dobro)", Employee(3000m, D(2026, 6, 1), 30, concession: D(2026, 6, 10)), L(("FERIAS", 5000m), ("FER13", 1666.67m), ("INSS", 734.85m), ("IRRF", 631.53m), ("FGTS", 533.33m)),
            6666.67m, 533.33m, 5300.29m),
    ];

    /// <summary>CA 7: cada linha, base e líquido do dataset sintético bate centavo a centavo, com a memória em toda linha.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Dataset_bate_centavo_a_centavo(string name)
    {
        var c = Dataset.Single(x => x.Name == name);
        var r = Calc(c.Input).Payslip;
        r.Issues.Should().NotContain(i => i.Blocking, name);
        r.Lines.GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount)).Should().Equal(c.Lines, name);
        r.InssBase.Should().Be(c.InssBase, name);
        r.FgtsAmount.Should().Be(c.Fgts, name);
        r.Totals.Net.Should().Be(c.Net, name);
        r.EmployerCharges.Should().Be(0m, "os encargos das férias entram na folha mensal");
        r.Lines.Should().OnlyContain(l => l.Calculated && l.Memory.Count > 0, "toda linha tem a memória (CA 6)");
    }

    /// <summary>CA 6: a divisão do INSS por competência (para a mensal compensar) e a alternativa do INSS inteiro na competência do início.</summary>
    [Fact]
    public void Divisao_do_inss_por_competencia()
    {
        var split = Calc(Employee(3000m, D(2026, 6, 16), 30)).Split;
        split.Should().Equal(new VacationMonthSplit(D(2026, 6, 1), 15, 2000m, 155.69m, 2000m), new VacationMonthSplit(D(2026, 7, 1), 15, 2000m, 155.69m, 2000m));
        var whole = Calc(Employee(3000m, D(2026, 6, 16), 30, taxation: VacationTaxation.StartMonth));
        whole.Split.Should().ContainSingle().Which.Should().Be(new VacationMonthSplit(D(2026, 6, 1), 30, 4000m, 368.60m, 4000m));
        whole.Payslip.Lines.Single(l => l.Code == "INSS").Amount.Should().Be(368.60m);
    }

    /// <summary>CA 6: sem a rubrica de 1/3, a linha não sai e o aviso diz qual falta; sem a tabela do INSS do mês, trava.</summary>
    [Fact]
    public void Rubrica_e_tabela_faltando()
    {
        var withoutThird = Items.Where(i => i.Calculation != PayrollCalculation.VacationThird).ToList();
        var e = Employee(3000m, D(2026, 6, 1), 30);
        var r = VacationCalculator.Calculate(new VacationCalcInput(e, withoutThird, Tables2026, new Dictionary<DateOnly, PayrollCalcTable> { [D(2026, 6, 1)] = Tables2026.Single(t => t.Kind == PayrollTaxTableKind.Inss) }));
        r.Payslip.Issues.Should().Contain(i => i.Code == "item_missing" && !i.Blocking);
        var blocked = VacationCalculator.Calculate(new VacationCalcInput(e, Items, Tables2026, new Dictionary<DateOnly, PayrollCalcTable>()));
        blocked.Payslip.Issues.Should().ContainSingle(i => i.Blocking).Which.Message.Should().Be("Falta a tabela INSS para junho/2026.");
    }

    /// <summary>
    /// CA 10: na folha mensal, os dias de férias não entram como salário de novo; a parte das férias entra na base do INSS e do FGTS e o INSS
    /// já retido no recibo é descontado. 3.000, férias de 16 a 30/06 (base 2.000, INSS retido 155,69): salário 15 dias = 1.500; INSS sobre 3.500
    /// = 308,5998 → 308,60 − 155,69 = 152,91; FGTS 8% de 3.500 = 280; encargos 28,8% de 3.500 = 1.008.
    /// </summary>
    [Fact]
    public void Mensal_com_ferias_nao_paga_de_novo_e_compensa_o_inss()
    {
        var snapshot = new PayrollCalcSnapshot(3000m, D(2020, 1, 2), null, true, 220m, null, false, false, [], [], true, 0, 0, [], PayrollCalcParameters.Default,
            [new PayrollCalcVacation(D(2026, 6, 16), D(2026, 6, 30), 15, 2000m, 155.69m, 2000m, true)]);
        var r = PayrollCalculator.Calculate(new PayrollCalcInput(D(2026, 6, 1), snapshot, Items, Tables2026));
        r.Lines.Single(l => l.Code == "SAL").Amount.Should().Be(1500m);
        r.Lines.Single(l => l.Code == "SAL").Memory.Should().Contain("Menos 15 dias de férias, pagos no recibo de férias.");
        r.InssBase.Should().Be(3500m);
        r.Lines.Single(l => l.Code == "INSS").Amount.Should().Be(152.91m);
        r.FgtsAmount.Should().Be(280m);
        r.EmployerCharges.Should().Be(1008m);
        r.Lines.Should().NotContain(l => l.Code == "IRRF", "o IRRF das férias foi em separado e o salário do mês fica isento");
        r.Totals.Net.Should().Be(1347.09m);

        // Férias programadas sem cálculo: os dias saem do salário e o aviso pede o cálculo, sem travar.
        var pending = PayrollCalculator.Calculate(new PayrollCalcInput(D(2026, 6, 1), snapshot with { Vacations = [new(D(2026, 6, 16), D(2026, 6, 30), 15, 0m, 0m, 0m, false)] }, Items, Tables2026));
        pending.Issues.Should().Contain(i => i.Code == "vacation_not_calculated" && !i.Blocking);
        pending.Lines.Single(l => l.Code == "SAL").Amount.Should().Be(1500m);
    }

    /// <summary>Retrato (ADR 0039 §2): sem férias no mês, o retrato da mensal não ganha o campo (o cálculo de antes continua "atualizado").</summary>
    [Fact]
    public void Retrato_da_mensal_sem_ferias_nao_muda()
    {
        var snapshot = new PayrollCalcSnapshot(3000m, D(2020, 1, 2), null, true, 220m, null, false, false, [], [], true, 0, 0, [], PayrollCalcParameters.Default);
        PayrollCalculator.WriteSnapshot(snapshot).Should().NotContain("vacations");
        VacationCalculator.ReadSnapshot(VacationCalculator.WriteSnapshot(Employee(3000m, D(2026, 6, 1), 30)))!.Days.Should().Be(30);
    }
}
