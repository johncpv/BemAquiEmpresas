using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-05 CA 2 a 5: o motor da rescisão, sem banco. <b>Dataset sintético</b> (ADR 0039 §4): funcionários fictícios, sem nome nem CPF, salário
/// de 3.000, desligamento em 15/10/2026 (quinta-feira) e as tabelas do pacote de 2026 (INSS 7,5% até 1.621,00 e 9% até 2.902,84, somado e
/// arredondado no total; IRRF da Lei 15.191/2025 com a redução da Lei 15.270/2025; FGTS 8%; encargos 28,8%), a matriz da CLT e os demais
/// parâmetros pré-configurados. Saldo de salário: 15 dias do mês comercial = 1.500,00 (INSS 112,50, FGTS 120,00). Admissão em 04/03/2024
/// (2 anos completos: aviso de 36 dias), o 1º período aquisitivo gozado, o 2º (até 03/03/2026) vencido no concessivo e o 3º em aquisição.
/// Nenhum holerite real.
/// </summary>
public class TerminationCalculatorTests
{
    private static readonly IReadOnlyList<PayrollCalcItem> Items =
        PayrollItemService.Standards.Select((s, i) => new PayrollCalcItem(new Guid(i + 1, 0, 0, new byte[8]), s.Code, s.Name, s.Kind, s.Calc, s.Inss, s.Fgts, s.Irrf, s.DeductsIrrf)).ToList();

    private static readonly IReadOnlyList<PayrollCalcTable> Tables2026 = PayrollTaxPackages.All.Single(p => p.Package == 2026).Tables
        .Select(t => new PayrollCalcTable(Guid.NewGuid(), t.Kind, t.ValidFrom!.Value, PayrollTaxTableStatus.Unconfirmed, t.Rounding ?? PayrollRounding.AwayFromZero, t.Content!)).ToList();

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static readonly DateOnly Date = D(2026, 10, 15);

    private static readonly DateOnly Veteran = D(2024, 3, 4);

    private static TerminationCalcSnapshot Snap(TerminationKind kind, TerminationNoticeKind notice, DateOnly? admission = null, decimal? fgts = null, DateOnly? contractEnd = null,
        bool firstTaken = true, PayrollCalcHourBank? bank = null, TerminationParameters? parameters = null) =>
        new(kind, notice, Date, contractEnd, fgts, fgts is null ? null : D(2026, 10, 10),
            new PayrollCalcSnapshot(3000m, admission ?? Veteran, null, true, 220m, null, false, false, [], [], true, 0, 0, [], PayrollCalcParameters.Default, null, null, bank),
            [], [], firstTaken && (admission ?? Veteran) == Veteran ? [new TerminationVacationTaken(1, 30, 0)] : [], [],
            new TerminationThirteenthSnapshot([], [], [], 0m, 0m, ThirteenthParameters.Default), VacationParameters.Default, parameters ?? TerminationParameters.Default);

    private static TerminationCalcResult Calc(TerminationCalcSnapshot s) => TerminationCalculator.Calculate(new TerminationCalcInput(s, Items, Tables2026));

    private static decimal Line(TerminationCalcResult r, string code) => r.Payslip.Lines.Where(l => l.Code == code).Sum(l => l.Amount);

    private static Dictionary<string, decimal> L(params (string Code, decimal Amount)[] lines) => lines.ToDictionary(l => l.Code, l => l.Amount);

    /// <summary>Caso do dataset: as linhas esperadas (código → soma), o líquido, o FGTS (do mês + rescisório), a multa e o custo total.</summary>
    public sealed record Case(string Name, TerminationCalcSnapshot Input, IReadOnlyDictionary<string, decimal> Lines, decimal Net, decimal Fgts, decimal Fine, decimal TotalCost);

    public static TheoryData<string> Names => new(Dataset.Select(c => c.Name));

    private static readonly IReadOnlyList<Case> Dataset =
    [
        // Aviso 36 dias: 3.000 × 36/30 = 3.600 (só FGTS); projeção até 20/11. Vencidas do 2º período 3.000; proporcionais 9/12 (04/03 a 20/11:
        // 8 meses + 17 dias) = 2.250; 1/3 = 1.750. 13º: jan a out + 20 dias de nov = 11/12 = 2.750 → INSS 121,575 + 1.129 × 9% = 223,19; IRRF
        // zero. FGTS rescisório (3.600 + 2.750) × 8% = 508. Multa 40% × (10.000 + 120 + 508) = 4.251,20. Encargos 28,8% × (1.500 + 2.750) = 1.224.
        new("01 sem justa causa, aviso indenizado", Snap(TerminationKind.WithoutCause, TerminationNoticeKind.Indemnified, fgts: 10000m),
            L(("SAL", 1500m), ("AVISOIND", 3600m), ("FERVENC", 3000m), ("FERPROP", 2250m), ("FERRESC13", 1750m), ("13SAL", 2750m), ("INSS", 335.69m), ("FGTS", 628m), ("MULTAFGTS", 4251.20m)),
            14514.31m, 628m, 4251.20m, 20953.20m),
        // Aviso trabalhado: sem linha do aviso nem projeção; proporcionais 7/12 (12 dias em outubro não contam) = 1.750, 1/3 = 1.583,33; 13º 10/12 =
        // 2.500 → INSS 200,69. FGTS rescisório 200. Multa 40% × (10.000 + 120 + 200) = 4.128.
        new("02 sem justa causa, aviso trabalhado", Snap(TerminationKind.WithoutCause, TerminationNoticeKind.Worked, fgts: 10000m),
            L(("SAL", 1500m), ("FERVENC", 3000m), ("FERPROP", 1750m), ("FERRESC13", 1583.33m), ("13SAL", 2500m), ("INSS", 313.19m), ("FGTS", 320m), ("MULTAFGTS", 4128m)),
            10020.14m, 320m, 4128m, 15933.33m),
        // Pedido de demissão sem cumprir o aviso: desconto de 30 dias (3.000, fora das bases); sem multa.
        new("03 pedido de demissão, aviso não cumprido", Snap(TerminationKind.Resignation, TerminationNoticeKind.Indemnified),
            L(("SAL", 1500m), ("FERVENC", 3000m), ("FERPROP", 1750m), ("FERRESC13", 1583.33m), ("13SAL", 2500m), ("AVISODESC", 3000m), ("INSS", 313.19m), ("FGTS", 320m)),
            7020.14m, 320m, 0m, 11805.33m),
        // Justa causa: saldo e vencidas com 1/3; sem proporcionais, 13º, aviso nem multa.
        new("04 justa causa", Snap(TerminationKind.WithCause, TerminationNoticeKind.Waived),
            L(("SAL", 1500m), ("FERVENC", 3000m), ("FERRESC13", 1000m), ("INSS", 112.50m), ("FGTS", 120m)), 5387.50m, 120m, 0m, 6052m),
        // Acordo (art. 484-A): metade do aviso, 18 dias = 1.800, projeção até 02/11 (8/12 nas férias: 1º/11 e 2/11 completam 30 dias; 2 dias em
        // novembro não contam no 13º: 10/12). FGTS rescisório (1.800 + 2.500) × 8% = 344; multa 20% × 10.464 = 2.092,80.
        new("05 acordo entre as partes", Snap(TerminationKind.MutualAgreement, TerminationNoticeKind.Indemnified, fgts: 10000m),
            L(("SAL", 1500m), ("AVISOIND", 1800m), ("FERVENC", 3000m), ("FERPROP", 2000m), ("FERRESC13", 1666.67m), ("13SAL", 2500m), ("INSS", 313.19m), ("FGTS", 464m), ("MULTAFGTS", 2092.80m)),
            12153.48m, 464m, 2092.80m, 16175.47m),
        // Experiência no prazo (18/07 a 15/10, 90 dias): proporcionais 3/12 = 750 + 250; 13º 3/12 (julho com 14 dias não conta) = 750 → INSS 56,25.
        new("06 término da experiência no prazo", Snap(TerminationKind.ContractEnd, TerminationNoticeKind.Waived, admission: D(2026, 7, 18)),
            L(("SAL", 1500m), ("FERPROP", 750m), ("FERRESC13", 250m), ("13SAL", 750m), ("INSS", 168.75m), ("FGTS", 180m)), 3081.25m, 180m, 0m, 4078m),
        // Experiência de 01/08 a 29/10 terminada pela empresa em 15/10: metade dos 14 dias restantes = 700 (sem bases); multa 40% × (500 + 120 + 60).
        new("07 término antecipado pela empresa (art. 479)", Snap(TerminationKind.EarlyEndByEmployer, TerminationNoticeKind.Waived, admission: D(2026, 8, 1), fgts: 500m, contractEnd: D(2026, 10, 29)),
            L(("SAL", 1500m), ("IND479", 700m), ("FERPROP", 750m), ("FERRESC13", 250m), ("13SAL", 750m), ("INSS", 168.75m), ("FGTS", 180m), ("MULTAFGTS", 272m)), 3781.25m, 180m, 272m, 5050m),
        // Pelo empregado (art. 480): o desconto da metade dos dias restantes, sem multa.
        new("08 término antecipado pelo empregado (art. 480)", Snap(TerminationKind.EarlyEndByEmployee, TerminationNoticeKind.Waived, admission: D(2026, 8, 1), contractEnd: D(2026, 10, 29)),
            L(("SAL", 1500m), ("DESC480", 700m), ("FERPROP", 750m), ("FERRESC13", 250m), ("13SAL", 750m), ("INSS", 168.75m), ("FGTS", 180m)), 2381.25m, 180m, 0m, 4078m),
        // 1º período não gozado e o concessivo terminou em 03/03/2026: em dobro (6.000) + o 2º (3.000) = 9.000; 1/3 de (9.000 + 2.250) = 3.750.
        new("09 férias vencidas em dobro", Snap(TerminationKind.WithoutCause, TerminationNoticeKind.Indemnified, fgts: 10000m, firstTaken: false),
            L(("SAL", 1500m), ("AVISOIND", 3600m), ("FERVENC", 9000m), ("FERPROP", 2250m), ("FERRESC13", 3750m), ("13SAL", 2750m), ("INSS", 335.69m), ("FGTS", 628m), ("MULTAFGTS", 4251.20m)),
            22514.31m, 628m, 4251.20m, 28953.20m),
    ];

    /// <summary>CA 5: cada linha, o líquido, o FGTS, a multa e o custo total do dataset sintético batem centavo a centavo, com a memória em toda linha.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Dataset_sintetico_bate_centavo_a_centavo(string name)
    {
        var c = Dataset.Single(x => x.Name == name);
        var r = Calc(c.Input);

        r.Payslip.Issues.Should().NotContain(i => i.Blocking);
        r.Payslip.Lines.GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount)).Should().BeEquivalentTo(c.Lines, name);
        r.Payslip.Totals.Net.Should().Be(c.Net, name);
        r.Payslip.FgtsAmount.Should().Be(c.Fgts, name);
        r.Summary.Fine.Should().Be(c.Fine, name);
        r.Summary.TotalCost.Should().Be(c.TotalCost, name);
        r.Summary.PaymentDeadline.Should().Be(D(2026, 10, 25), "10 dias corridos do desligamento (hr.termination_payment_days)");
        r.Payslip.Lines.Where(l => l.Calculated).Should().OnlyContain(l => l.Memory.Count > 0, "toda linha tem a memória");
        r.Payslip.Lines.Single(l => l.Code == "SAL").Name.Should().Be("Saldo de salário");
    }

    /// <summary>CA 2: a matriz pré-configurada pela CLT e o aviso proporcional (Lei 12.506/2011) vêm dos parâmetros.</summary>
    [Fact]
    public void Matriz_e_aviso_proporcional_vem_dos_parametros()
    {
        var p = TerminationParameters.Default;
        p.Row(TerminationKind.WithoutCause).Should().Be(new TerminationMatrixRow(TerminationKind.WithoutCause, TerminationNoticeRule.Full, TerminationVacationRule.ExpiredAndProportional, true, 40m));
        p.Row(TerminationKind.MutualAgreement).FgtsFinePct.Should().Be(20m);
        p.Row(TerminationKind.WithCause).Should().Match<TerminationMatrixRow>(r => !r.Thirteenth && r.Vacation == TerminationVacationRule.ExpiredOnly && r.FgtsFinePct == 0m);
        TerminationPolicy.NoticeDays(D(2025, 10, 16), Date, p).Should().Be(30, "menos de um ano completo");
        TerminationPolicy.NoticeDays(Veteran, Date, p).Should().Be(36);
        TerminationPolicy.NoticeDays(D(1990, 1, 1), Date, p).Should().Be(90, "o máximo");

        // A matriz trocada pelo tenant vale (a multa do acordo em 30% e sem 13º, só para o teste).
        var custom = p with { Matrix = [.. p.Matrix.Where(r => r.Kind != TerminationKind.MutualAgreement), new(TerminationKind.MutualAgreement, TerminationNoticeRule.Full, TerminationVacationRule.ExpiredAndProportional, false, 30m)] };
        var r2 = Calc(Snap(TerminationKind.MutualAgreement, TerminationNoticeKind.Indemnified, fgts: 10000m, parameters: custom));
        Line(r2, "AVISOIND").Should().Be(3600m);
        Line(r2, "13SAL").Should().Be(0m);
        r2.Summary.FinePct.Should().Be(30m);
    }

    /// <summary>CA 3: o saldo positivo do banco de horas é pago como extra de dia útil com o DSR; o negativo só é descontado com o parâmetro.</summary>
    [Fact]
    public void Banco_de_horas_na_rescisao()
    {
        // 10 h × (3.000 ÷ 220 h) × 1,5 = 204,55; DSR 204,55 ÷ 27 × 4 = 30,30; INSS do mês sobre 1.734,85 = 131,82.
        var positive = Calc(Snap(TerminationKind.WithoutCause, TerminationNoticeKind.Indemnified, fgts: 0m, bank: new PayrollCalcHourBank(600, 600, "acerto na rescisão")));
        Line(positive, "BHPAGO").Should().Be(204.55m);
        Line(positive, "DSR").Should().Be(30.30m);
        positive.Payslip.Lines.First(l => l.Code == "INSS").Amount.Should().Be(131.82m);

        // −5 h sem o acordo que permite: nenhuma linha; com o parâmetro, 5 h × 3.000 ÷ 220 = 68,18.
        Line(Calc(Snap(TerminationKind.WithoutCause, TerminationNoticeKind.Indemnified, fgts: 0m, bank: new PayrollCalcHourBank(-300, 0, "acerto na rescisão"))), "BHDESC").Should().Be(0m);
        Line(Calc(Snap(TerminationKind.WithoutCause, TerminationNoticeKind.Indemnified, fgts: 0m, bank: new PayrollCalcHourBank(-300, -300, "acerto na rescisão"))), "BHDESC").Should().Be(68.18m);
    }

    /// <summary>CA 4: sem o saldo do FGTS, a multa não sai (o serviço recusa o cálculo com 422; a simulação avisa).</summary>
    [Fact]
    public void Sem_saldo_do_FGTS_a_multa_nao_sai()
    {
        var r = Calc(Snap(TerminationKind.WithoutCause, TerminationNoticeKind.Indemnified));
        Line(r, "MULTAFGTS").Should().Be(0m);
        r.Payslip.Issues.Should().Contain(i => i.Code == TerminationPolicy.FgtsBalanceCode && !i.Blocking);
    }

    /// <summary>Os avos das férias proporcionais (CLT art. 146): a fração de 15 dias ou mais conta; nunca mais de 12.</summary>
    [Theory]
    [InlineData(2026, 3, 4, 2026, 11, 20, 9)]
    [InlineData(2026, 3, 4, 2026, 10, 15, 7)]
    [InlineData(2026, 7, 18, 2026, 10, 15, 3)]
    [InlineData(2026, 1, 1, 2026, 1, 14, 0)]
    [InlineData(2025, 1, 1, 2026, 6, 30, 12)]
    public void Avos_das_ferias(int y1, int m1, int d1, int y2, int m2, int d2, int expected) =>
        TerminationPolicy.Avos(D(y1, m1, d1), D(y2, m2, d2)).Should().Be(expected);
}
