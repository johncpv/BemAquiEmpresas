using System.Text.Json;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-02a: o motor da folha mensal calculada, sem banco. <b>Dataset sintético</b> (ADR 0039 §4): funcionários fictícios, sem nome nem CPF,
/// com o resultado calculado à mão pela legislação vigente de 2026 (as tabelas do pacote <c>hr-payroll-tables-2026</c>: INSS da Portaria
/// MPS/MF 13/2026, IRRF da Lei 15.191/2025 com a redução da Lei 15.270/2025, salário mínimo 1.621,00, salário-família 67,54 até 1.980,38,
/// FGTS 8%, vale-transporte 6% e encargos de Lucro Presumido 28,8%) e revisado; competência de março de 2026 (31 dias), parâmetros
/// pré-configurados. Nenhum holerite real.
/// </summary>
public class PayrollCalculatorTests
{
    private static readonly DateOnly March = new(2026, 3, 1);

    private static readonly Guid Premio = Guid.Parse("0f000000-0000-0000-0000-000000000001");

    /// <summary>As rubricas do seed (com a regra e as marcas de base propostas) e um prêmio avulso (provento com as três bases).</summary>
    private static readonly IReadOnlyList<PayrollCalcItem> Items =
    [
        .. PayrollItemService.Standards.Select((s, i) => new PayrollCalcItem(new Guid(i + 1, 0, 0, new byte[8]), s.Code, s.Name, s.Kind, s.Calc, s.Inss, s.Fgts, s.Irrf, s.DeductsIrrf)),
        new PayrollCalcItem(Premio, "PREMIO", "Prêmio", PayrollItemKind.Earning, PayrollCalculation.None, true, true, true, false),
    ];

    private static Guid ItemId(string code) => Items.Single(i => i.Code == code).Id;

    private static IReadOnlyList<PayrollCalcTable> Tables2026 { get; } = PayrollTaxPackages.All.Single(p => p.Package == 2026).Tables
        .Select(t => new PayrollCalcTable(Guid.NewGuid(), t.Kind, t.ValidFrom!.Value, PayrollTaxTableStatus.Unconfirmed, t.Rounding ?? PayrollRounding.AwayFromZero, t.Content!)).ToList();

    private static PayrollCalcSnapshot Employee(decimal salary, DateOnly? admission = null, DateOnly? termination = null, decimal hours = 220m, int? insalubrity = null, bool hazard = false,
        bool vt = false, IReadOnlyList<PayrollCalcDependent>? dependents = null, IReadOnlyList<PayrollCalcLeave>? leaves = null, int absence = 0, int late = 0,
        IReadOnlyList<PayrollCalcManualLine>? manual = null, PayrollCalcParameters? parameters = null, bool position = true) =>
        new(salary, admission ?? new DateOnly(2020, 1, 2), termination, position, hours, insalubrity, hazard, vt, dependents ?? [], leaves ?? [], true, absence, late, manual ?? [],
            parameters ?? PayrollCalcParameters.Default);

    private static PayrollCalcResult Calc(PayrollCalcSnapshot snapshot, DateOnly? period = null) =>
        PayrollCalculator.Calculate(new PayrollCalcInput(period ?? March, snapshot, Items, Tables2026));

    private static decimal Line(PayrollCalcResult r, string code) => r.Lines.Where(l => l.Code == code).Sum(l => l.Amount);

    /// <summary>Caso do dataset: as linhas esperadas (código → valor), o INSS, o IRRF, o FGTS do mês, os encargos e o líquido.</summary>
    public sealed record Case(string Name, PayrollCalcSnapshot Input, IReadOnlyDictionary<string, decimal> Lines, decimal InssBase, decimal Fgts, decimal Employer, decimal Net);

    private static Dictionary<string, decimal> L(params (string Code, decimal Amount)[] lines) => lines.ToDictionary(l => l.Code, l => l.Amount);

    /// <summary>
    /// O dataset (CA 11), cada valor calculado à mão; a regra está no nome do caso. INSS: 7,5% até 1.621,00; 9% até 2.902,84; 12% até
    /// 4.354,27; 14% até 8.475,55; soma das faixas arredondada no total. IRRF: a menor entre a dedução legal e o simplificado (607,20).
    /// </summary>
    public static TheoryData<string> Names => new(Dataset.Select(c => c.Name));

    private static readonly IReadOnlyList<Case> Dataset =
    [
        // 2.000 → INSS 121,575 + 379 × 9% = 155,685 → 155,69; IRRF zero; FGTS 160; encargos 28,8% = 576.
        new("01 mês cheio, abaixo da isenção", Employee(2000m), L(("SAL", 2000m), ("INSS", 155.69m), ("FGTS", 160m)), 2000m, 160m, 576m, 1844.31m),
        // 6.000 → INSS 641,5144 → 641,51; legal: 5.358,49 × 27,5% − 908,73 = 564,85 − redução (978,62 − 0,133145 × 6.000 = 179,75) = 385,10;
        // simplificado daria 394,54 (o exemplo da Receita).
        new("02 redução do IRRF, dedução legal melhor", Employee(6000m), L(("SAL", 6000m), ("INSS", 641.51m), ("IRRF", 385.10m), ("FGTS", 480m)), 6000m, 480m, 1728m, 4973.39m),
        // 10.000 → INSS no teto 988,09; IRRF sem redução (acima de 7.350): 9.011,91 × 27,5% − 908,73 = 1.569,545 → 1.569,55.
        new("03 teto do INSS, sem redução", Employee(10000m), L(("SAL", 10000m), ("INSS", 988.09m), ("IRRF", 1569.55m), ("FGTS", 800m)), 10000m, 800m, 2880m, 7442.36m),
        // Admitido em 11/03: 20 de 30 dias (mês comercial) × 3.000 = 2.000.
        new("04 admissão no mês", Employee(3000m, admission: new DateOnly(2026, 3, 11)), L(("SAL", 2000m), ("INSS", 155.69m), ("FGTS", 160m)), 2000m, 160m, 576m, 1844.31m),
        // Desligado em 20/03: 20 dias × 4.500 = 3.000; INSS 248,5998 → 248,60; IRRF zerado pela redução.
        new("05 desligamento no mês", Employee(4500m, termination: new DateOnly(2026, 3, 20)), L(("SAL", 3000m), ("INSS", 248.60m), ("FGTS", 240m)), 3000m, 240m, 864m, 2751.40m),
        // Hora = 2.200 ÷ 220 = 10: faltas 8 h = 80, atrasos 1:30 h = 15; base 2.105 → INSS 165,135 → 165,14.
        new("06 falta e atraso do espelho", Employee(2200m, absence: 480, late: 90), L(("SAL", 2200m), ("FALTAS", 80m), ("ATRASOS", 15m), ("INSS", 165.14m), ("FGTS", 168.40m)), 2105m, 168.40m, 606.24m, 1939.86m),
        // Doença desde 24/02: a empresa paga até 10/03 (15 dias); de 11 a 31/03, 20 dias pela previdência: salário 10 dias = 1.000.
        new("07 afastamento por doença acima de 15 dias", Employee(3000m, leaves: [new(new DateOnly(2026, 2, 24), new DateOnly(2026, 3, 31), AbsenceReason.MedicalCertificate)]),
            L(("SAL", 1000m), ("AFAST", 2000m), ("INSS", 75m), ("FGTS", 80m)), 1000m, 80m, 288m, 925m),
        // Acidente desde 01/03: 15 dias pela empresa e 15 pela previdência; o FGTS fica sobre os 30 dias (2.400 × 8% = 192).
        new("08 acidente de trabalho mantém o FGTS", Employee(2400m, leaves: [new(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), AbsenceReason.WorkAccident)]),
            L(("SAL", 1200m), ("AFAST", 1200m), ("INSS", 90m), ("FGTS", 192m)), 1200m, 192m, 345.60m, 1110m),
        // Insalubridade 20% do mínimo = 324,20 × periculosidade 30% de 2.000 = 600: vale a periculosidade; INSS 209,685 → 209,69.
        new("09 insalubridade e periculosidade sem acúmulo", Employee(2000m, insalubrity: 20, hazard: true), L(("SAL", 2000m), ("PERIC", 600m), ("INSS", 209.69m), ("FGTS", 208m)), 2600m, 208m, 748.80m, 2390.31m),
        // 1.700 ≤ 1.980,38: dois filhos com os documentos (67,54 × 2 = 135,08); o de 2011 passou dos 14; o de 2022 sem documentos; VT 6% = 102.
        new("10 salário-família e vale-transporte", Employee(1700m, vt: true, dependents:
            [new(new DateOnly(2018, 5, 10), false, true, true), new(new DateOnly(2020, 1, 1), true, true, true), new(new DateOnly(2011, 2, 15), false, true, true), new(new DateOnly(2022, 7, 7), false, true, false)]),
            L(("SAL", 1700m), ("SALFAM", 135.08m), ("VT", 102m), ("INSS", 128.69m), ("FGTS", 136m)), 1700m, 136m, 489.60m, 1604.39m),
        // VT 6% de 2.500 = 150, limitado ao fornecido no mês (120); INSS 200,685 → 200,69.
        new("11 vale-transporte limitado ao fornecido", Employee(2500m, vt: true, manual: [new(ItemId("VTFORN"), null, 120m, "Recarga do cartão de março")]),
            L(("SAL", 2500m), ("VTFORN", 120m), ("VT", 120m), ("INSS", 200.69m), ("FGTS", 200m)), 2500m, 200m, 720m, 2179.31m),
        // 5.500: INSS 571,51 < 607,20 → simplificado: 4.892,80 × 27,5% − 908,73 = 436,79 − redução 246,3225 = 190,47 (legal daria 200,28).
        new("12 desconto simplificado melhor", Employee(5500m), L(("SAL", 5500m), ("INSS", 571.51m), ("IRRF", 190.47m), ("FGTS", 440m)), 5500m, 440m, 1584m, 4738.02m),
        // 7.000 com 2 dependentes e pensão judicial 500 (deduz da base): legal 781,51 + 379,18 + 500 = 1.660,69 → 559,58 − redução 46,605 = 512,98.
        new("13 dependentes e pensão judicial", Employee(7000m, dependents: [new(new DateOnly(2015, 1, 1), true, false, false), new(new DateOnly(2016, 1, 1), true, false, false)],
                manual: [new(ItemId("PENSAO"), null, 500m, "Pensão alimentícia, ofício 123/2026")]),
            L(("SAL", 7000m), ("PENSAO", 500m), ("INSS", 781.51m), ("IRRF", 512.98m), ("FGTS", 560m)), 7000m, 560m, 2016m, 5205.51m),
        // Prêmio avulso de 400 entra nas bases: 5.200 → INSS 529,51; simplificado 4.592,80 × 22,5% − 675,49 = 357,89 − 286,266 = 71,62.
        new("14 prêmio avulso nas bases", Employee(4800m, manual: [new(Premio, null, 400m, "Prêmio de produtividade de março")]),
            L(("SAL", 4800m), ("PREMIO", 400m), ("INSS", 529.51m), ("IRRF", 71.62m), ("FGTS", 416m)), 5200m, 416m, 1497.60m, 4598.87m),
    ];

    /// <summary>CA 11: cada linha, base e líquido do dataset sintético bate centavo a centavo.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Dataset_bate_centavo_a_centavo(string name)
    {
        var c = Dataset.Single(x => x.Name == name);
        var r = Calc(c.Input);
        r.Issues.Should().NotContain(i => i.Blocking, name);
        r.Lines.GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount)).Should().Equal(c.Lines, name);
        r.InssBase.Should().Be(c.InssBase, name);
        r.FgtsAmount.Should().Be(c.Fgts, name);
        r.EmployerCharges.Should().Be(c.Employer, name);
        r.Totals.Net.Should().Be(c.Net, name);
        r.Lines.Where(l => l.Calculated).Should().OnlyContain(l => l.Memory.Count > 0, "toda linha calculada tem a memória (CA 12)");
        r.TableIds.Should().HaveCount(7, "o holerite guarda as sete versões de tabela usadas");
    }

    [Fact]
    public void Dataset_tem_doze_ou_mais_funcionarios_e_cobre_cada_regra() =>
        Dataset.Should().HaveCountGreaterThanOrEqualTo(12);

    /// <summary>CA 2: proporcional pelo mês comercial (pré-configurado) ou pelos dias do mês.</summary>
    [Theory]
    [InlineData(SalaryProration.CommercialMonth, "2026-03-11", null, 2000, "20 dias")]
    [InlineData(SalaryProration.CalendarDays, "2026-03-11", null, 2032.26, "21 dias")]
    [InlineData(SalaryProration.CommercialMonth, "2020-01-02", "2026-03-31", 3000, "30 dias")]
    [InlineData(SalaryProration.CommercialMonth, "2026-03-31", null, 100, "1 dias")]
    [InlineData(SalaryProration.CalendarDays, "2020-01-02", "2026-03-15", 1451.61, "15 dias")]
    public void Salario_proporcional_pelo_parametro(SalaryProration proration, string admission, string? termination, decimal expected, string reference)
    {
        var r = Calc(Employee(3000m, DateOnly.Parse(admission, System.Globalization.CultureInfo.InvariantCulture), termination is null ? null : DateOnly.Parse(termination, System.Globalization.CultureInfo.InvariantCulture),
            parameters: PayrollCalcParameters.Default with { Proration = proration }));
        var salary = r.Lines.Single(l => l.Code == "SAL");
        salary.Amount.Should().Be(expected);
        salary.Reference.Should().Be(reference);
    }

    /// <summary>CA 2 em fevereiro: o mês comercial paga 30 dias no mês cheio de 28.</summary>
    [Fact]
    public void Fevereiro_cheio_paga_o_mes_comercial() =>
        Line(Calc(Employee(3000m), new DateOnly(2026, 2, 1)), "SAL").Should().Be(3000m);

    /// <summary>CA 3: os dias pagos pela empresa vêm do parâmetro; o acidente sem o FGTS mantido usa só o salário do mês.</summary>
    [Fact]
    public void Afastamento_pelo_parametro_e_fgts_do_acidente()
    {
        var leave = new PayrollCalcLeave(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), AbsenceReason.WorkAccident);
        var tenDays = Calc(Employee(3000m, leaves: [leave], parameters: PayrollCalcParameters.Default with { SickLeaveCompanyDays = 10 }));
        Line(tenDays, "SAL").Should().Be(1000m, "10 dias pela empresa");
        Line(tenDays, "AFAST").Should().Be(2000m);
        tenDays.FgtsBase.Should().Be(3000m, "o FGTS continua sobre os 30 dias");
        var noFgts = Calc(Employee(3000m, leaves: [leave], parameters: PayrollCalcParameters.Default with { AccidentKeepsFgts = false }));
        noFgts.FgtsBase.Should().Be(1500m);
        Line(Calc(Employee(3000m, leaves: [leave with { Reason = AbsenceReason.Maternity }])), "SAL").Should().Be(3000m, "só doença e acidente mudam o salário nesta story");
        Line(Calc(Employee(3000m, leaves: [leave with { End = new DateOnly(2026, 3, 10), Reason = AbsenceReason.MedicalCertificate }])), "AFAST").Should().Be(0m, "até 15 dias, a empresa paga");
    }

    /// <summary>CA 4: base no salário base pelo parâmetro; com o acúmulo ligado, saem os dois; a memória diz qual foi escolhido.</summary>
    [Fact]
    public void Adicionais_pela_base_e_pelo_acumulo()
    {
        var both = Calc(Employee(2000m, insalubrity: 40, hazard: true));
        Line(both, "INSAL").Should().Be(648.40m, "40% do mínimo (648,40) é maior que 30% de 2.000");
        Line(both, "PERIC").Should().Be(0m);
        both.Lines.Single(l => l.Code == "INSAL").Memory.Should().Contain(m => m.Contains("vale o maior, a insalubridade", StringComparison.Ordinal));
        var accumulate = Calc(Employee(2000m, insalubrity: 40, hazard: true, parameters: PayrollCalcParameters.Default with { AllowancesAccumulate = true }));
        (Line(accumulate, "INSAL") + Line(accumulate, "PERIC")).Should().Be(1248.40m);
        Line(Calc(Employee(2000m, insalubrity: 10, parameters: PayrollCalcParameters.Default with { InsalubrityBase = InsalubrityBase.BaseSalary })), "INSAL").Should().Be(200m);
        Line(Calc(Employee(3000m, admission: new DateOnly(2026, 3, 16), insalubrity: 20)), "INSAL").Should().Be(162.10m, "proporcional aos 15 dias");
    }

    /// <summary>CA 5: o divisor do parâmetro vence as horas do cargo; sem jornada, o funcionário fica sem cálculo e os outros seguem.</summary>
    [Fact]
    public void Faltas_pelo_divisor_e_sem_jornada()
    {
        Line(Calc(Employee(2000m, hours: 200m, absence: 60, parameters: PayrollCalcParameters.Default with { HourValueDivisor = 220m })), "FALTAS").Should().Be(9.09m);
        Line(Calc(Employee(2000m, hours: 200m, absence: 60)), "FALTAS").Should().Be(10m);
        var blocked = Calc(Employee(2000m, hours: 0m, late: 30));
        blocked.Lines.Should().BeEmpty();
        blocked.Issues.Should().ContainSingle(i => i.Blocking && i.Message.StartsWith("Sem jornada", StringComparison.Ordinal));
        Calc(Employee(2000m, position: false)).Issues.Should().ContainSingle(i => i.Blocking && i.Code == "no_position");
        Calc(Employee(2000m, hours: 0m)).Issues.Should().NotContain(i => i.Blocking, "sem falta nem atraso, a jornada não faz falta");
    }

    /// <summary>CA 6: idade pelo parâmetro, a marca de documentos e o limite da remuneração; aviso "documentos pendentes" sem travar.</summary>
    [Fact]
    public void Salario_familia_idade_documentos_e_limite()
    {
        var child = new PayrollCalcDependent(new DateOnly(2012, 3, 20), false, true, true);
        Line(Calc(Employee(1500m, dependents: [child])), "SALFAM").Should().Be(67.54m, "faz 14 anos em março: ainda recebe no mês");
        Line(Calc(Employee(1500m, dependents: [child], parameters: PayrollCalcParameters.Default with { FamilyAllowanceMaxAge = 13 })), "SALFAM").Should().Be(0m);
        var noDocs = Calc(Employee(1500m, dependents: [child with { FamilyAllowanceDocsOk = false }]));
        Line(noDocs, "SALFAM").Should().Be(0m);
        noDocs.Issues.Should().ContainSingle(i => !i.Blocking && i.Message == "documentos do salário-família pendentes.");
        Line(Calc(Employee(1980.38m, dependents: [child])), "SALFAM").Should().Be(67.54m, "no limite ainda recebe");
        Line(Calc(Employee(1980.39m, dependents: [child])), "SALFAM").Should().Be(0m);
        Line(Calc(Employee(1500m, admission: new DateOnly(2026, 3, 16), dependents: [child])), "SALFAM").Should().Be(33.77m, "proporcional no mês da admissão");
    }

    /// <summary>CA 9 e ponto em aberto 1: a redução pelo rendimento bruto (exemplos da Receita) ou pela base, conforme o parâmetro.</summary>
    [Fact]
    public void Irrf_reducao_pelo_rendimento_do_parametro()
    {
        var gross = Calc(Employee(6000m));
        Line(gross, "IRRF").Should().Be(385.10m);
        var irrf = gross.Lines.Single(l => l.Code == "IRRF");
        irrf.Memory.Should().Contain(m => m.StartsWith("Escolhido: dedução legal", StringComparison.Ordinal));
        irrf.Memory.Should().Contain(m => m.Contains("o imposto seria R$ 394,54", StringComparison.Ordinal), "a memória mostra o imposto da outra opção");
        // Pela base (5.358,49): redução 978,62 − 0,133145 × 5.358,49 = 265,1652 → 564,85475 − 265,1652 = 299,69.
        Line(Calc(Employee(6000m, parameters: PayrollCalcParameters.Default with { ReductionIncome = IrrfReductionIncome.CalculationBase })), "IRRF").Should().Be(299.69m);
        PayrollCalculator.IrrfOption(Official(PayrollTaxTableKind.Irrf).Irrf!, PayrollRounding.AwayFromZero, 6000m, 607.20m, IrrfReductionIncome.GrossTaxable).Final.Should().Be(394.54m, "o exemplo da Receita");
        PayrollCalculator.IrrfOption(Official(PayrollTaxTableKind.Irrf).Irrf!, PayrollRounding.AwayFromZero, 4500m, 607.20m, IrrfReductionIncome.GrossTaxable).Final.Should().Be(0m, "o exemplo da Receita");
    }

    /// <summary>CA 8: o INSS segue a regra de arredondamento da tabela.</summary>
    [Fact]
    public void Inss_pelo_arredondamento_da_tabela()
    {
        var tables = Tables2026.Select(t => t.Kind == PayrollTaxTableKind.Inss ? t with { Rounding = PayrollRounding.Truncate } : t).ToList();
        PayrollCalculator.Calculate(new PayrollCalcInput(March, Employee(2000m), Items, tables)).Inss.Should().Be(155.68m, "155,685 truncado");
    }

    /// <summary>Sem a tabela de algum tipo, o motor não calcula (o serviço recusa antes com o 422 da FLH-01).</summary>
    [Fact]
    public void Sem_tabela_trava()
    {
        var r = PayrollCalculator.Calculate(new PayrollCalcInput(March, Employee(2000m), Items, Tables2026.Where(t => t.Kind != PayrollTaxTableKind.Fgts).ToList()));
        r.Issues.Should().ContainSingle(i => i.Blocking && i.Message == "Falta a tabela FGTS para março/2026.");
    }

    /// <summary>
    /// CA 12 (propriedade): para retratos ao acaso, calcular de novo o mesmo retrato (inclusive depois de gravar e ler o JSON) com a mesma
    /// versão do motor dá exatamente o mesmo resultado.
    /// </summary>
    [Fact]
    public void Propriedade_mesmo_retrato_mesmo_resultado()
    {
        var random = new Random(20261101);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        for (var i = 0; i < 300; i++)
        {
            var dependents = Enumerable.Range(0, random.Next(4)).Select(_ => new PayrollCalcDependent(new DateOnly(2005 + random.Next(20), 1 + random.Next(12), 1 + random.Next(28)), random.Next(2) == 0, random.Next(2) == 0, random.Next(2) == 0)).ToList();
            var leaves = random.Next(3) == 0 ? [new PayrollCalcLeave(new DateOnly(2026, 2, 1).AddDays(random.Next(50)), new DateOnly(2026, 3, 1).AddDays(random.Next(40)), random.Next(2) == 0 ? AbsenceReason.MedicalCertificate : AbsenceReason.WorkAccident)] : new List<PayrollCalcLeave>();
            var manual = random.Next(2) == 0 ? [new PayrollCalcManualLine(Premio, null, random.Next(1, 100_000) / 100m, "Prêmio de teste")] : new List<PayrollCalcManualLine>();
            var snapshot = Employee(random.Next(100_000, 2_000_000) / 100m, admission: new DateOnly(2026, 3, 1).AddDays(random.Next(-400, 25)), insalubrity: random.Next(4) switch { 0 => 10, 1 => 20, 2 => 40, _ => null },
                hazard: random.Next(2) == 0, vt: random.Next(2) == 0, dependents: dependents, leaves: leaves, absence: random.Next(3) * 60, late: random.Next(3) * 15, manual: manual,
                parameters: PayrollCalcParameters.Default with { Proration = random.Next(2) == 0 ? SalaryProration.CommercialMonth : SalaryProration.CalendarDays, AllowancesAccumulate = random.Next(2) == 0 });
            var first = JsonSerializer.Serialize(Calc(snapshot), json);
            var again = JsonSerializer.Serialize(Calc(snapshot), json);
            var stored = PayrollCalculator.ReadSnapshot(PayrollCalculator.WriteSnapshot(snapshot))!;
            var reread = JsonSerializer.Serialize(Calc(stored), json);
            again.Should().Be(first, $"caso {i}");
            reread.Should().Be(first, $"caso {i} depois de gravar o retrato");
            var result = Calc(snapshot);
            result.Totals.Net.Should().Be(result.Totals.Gross - result.Totals.Deductions);
            result.Lines.Should().OnlyContain(l => l.Amount > 0m && decimal.Round(l.Amount, 2) == l.Amount, "dinheiro em 2 casas");
            result.Irrf.Should().BeGreaterThanOrEqualTo(0m, "o IRRF nunca é negativo");
        }
    }

    /// <summary>CA 14: a diferença da retificação por rubrica (o provento que diminuiu vira desconto).</summary>
    [Fact]
    public void Diferenca_da_retificacao_por_rubrica()
    {
        PayrollCalculationRules.Difference(PayrollItemKind.Earning, 1000m, 1100m).Should().Be((PayrollItemKind.Earning, 100m, false));
        PayrollCalculationRules.Difference(PayrollItemKind.Earning, 1000m, 900m).Should().Be((PayrollItemKind.Deduction, 100m, true));
        PayrollCalculationRules.Difference(PayrollItemKind.Deduction, 100m, 80m).Should().Be((PayrollItemKind.Earning, 20m, true));
        PayrollCalculationRules.Difference(PayrollItemKind.Information, 80m, 70m).Should().Be((PayrollItemKind.Information, 10m, true));
        PayrollCalculationRules.Difference(PayrollItemKind.Earning, 10m, 10m).Should().BeNull();
    }

    private static PayrollTaxContent Official(PayrollTaxTableKind kind) => Tables2026.Single(t => t.Kind == kind).Content;
}
