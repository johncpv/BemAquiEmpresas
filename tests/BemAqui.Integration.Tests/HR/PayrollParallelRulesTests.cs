using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>FLH-06: regras sem banco da conferência paralela (comparação com tolerância zero, R$ e %, de-para, seguidas aceitas e anonimização).</summary>
public class PayrollParallelRulesTests
{
    private static readonly Guid Ana = Guid.CreateVersion7();
    private static readonly Guid Bia = Guid.CreateVersion7();

    private static PayrollParallelLine L(Guid who, string code, decimal amount, PayrollItemKind kind = PayrollItemKind.Earning) => new(who, code, code, kind, amount);

    private static PayrollParallelMonth M(int month, PayrollParallelStatus status, int acceptedDay = 1) =>
        new(new DateOnly(2026, month, 1), status, status == PayrollParallelStatus.Accepted ? new DateTimeOffset(2026, 11, acceptedDay, 12, 0, 0, TimeSpan.Zero) : null);

    /// <summary>CA 1 e 3: por funcionário e rubrica, a diferença em R$ e em % sobre a contadora (4 casas, "—" sem ela) e tolerância zero.</summary>
    [Fact]
    public void Compara_por_funcionario_e_rubrica_com_tolerancia_zero()
    {
        var rows = PayrollParallelRules.Compare(
            [L(Ana, "SAL", 3000m), L(Ana, "INSS", 254.97m, PayrollItemKind.Deduction), L(Bia, "SAL", 2000m), L(Bia, "SAL", 500m)],
            [L(Ana, "SAL", 3000m), L(Ana, "INSS", 254.98m, PayrollItemKind.Deduction), L(Bia, "SAL", 2500m), L(Bia, "HE50", 120m)]);

        rows.Should().HaveCount(4);
        var inss = rows.Single(r => r.EmployeeId == Ana && r.Code == "INSS");
        inss.Difference.Should().Be(0.01m, "um centavo já é diferença");
        inss.Equal.Should().BeFalse();
        inss.DifferencePct.Should().Be(0.0039m, "0,01 sobre 254,97, com 4 casas");
        rows.Single(r => r.EmployeeId == Bia && r.Code == "SAL").Equal.Should().BeTrue("as linhas do mesmo código somam");
        var extra = rows.Single(r => r.Code == "HE50");
        extra.Entered.Should().Be(0m);
        extra.DifferencePct.Should().BeNull("sem valor da contadora, o % é \"—\"");
        PayrollParallelRules.CanAccept(rows).Should().BeFalse();
        PayrollParallelRules.Net(rows.Where(r => r.EmployeeId == Ana), false).Should().Be(2745.03m);
        PayrollParallelRules.Net(rows.Where(r => r.EmployeeId == Ana), true).Should().Be(2745.02m);
        PayrollParallelRules.Pct(-200m, 10m).Should().Be(5m, "o % é sobre o valor absoluto da contadora");

        var equal = PayrollParallelRules.Compare([L(Ana, "SAL", 3000m)], [L(Ana, "SAL", 3000m)]);
        PayrollParallelRules.CanAccept(equal).Should().BeTrue();
    }

    /// <summary>CA 1: o código da contadora passa pelo de-para quando difere do sistema; o informativo fica fora do líquido.</summary>
    [Fact]
    public void De_para_liga_o_codigo_da_contadora_ao_do_sistema()
    {
        var mappings = new Dictionary<string, string> { ["001"] = "SAL" };
        var rows = PayrollParallelRules.Compare([L(Ana, "001", 3000m), L(Ana, "FGTS", 240m, PayrollItemKind.Information)], [L(Ana, "SAL", 3000m), L(Ana, "FGTS", 240m, PayrollItemKind.Information)], mappings);
        rows.Should().HaveCount(2).And.OnlyContain(r => r.Equal);
        rows.Select(r => r.Code).Should().BeEquivalentTo(["SAL", "FGTS"]);
        PayrollParallelRules.Net(rows, true).Should().Be(3000m);
        PayrollParallelRules.Compare([L(Ana, "001", 3000m)], [L(Ana, "SAL", 3000m)]).Should().HaveCount(2, "sem o de-para, os códigos não se encontram");
    }

    /// <summary>CA 5 e 7: 3 seguidas aceitas; a em conferência no topo não quebra; reprovada, lacuna e aceite antes da volta zeram a contagem.</summary>
    [Fact]
    public void Conta_as_competencias_seguidas_aceitas()
    {
        PayrollParallelRules.ConsecutiveAccepted([M(6, PayrollParallelStatus.Accepted), M(7, PayrollParallelStatus.Accepted), M(8, PayrollParallelStatus.Accepted)])
            .Should().Equal(new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 1));
        PayrollParallelRules.FromPeriod([new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 1)]).Should().Be(new DateOnly(2026, 9, 1), "a calculada vale a partir da competência seguinte");

        PayrollParallelRules.ConsecutiveAccepted([M(6, PayrollParallelStatus.Accepted), M(7, PayrollParallelStatus.Accepted), M(8, PayrollParallelStatus.Open)])
            .Should().HaveCount(2, "a competência em conferência no topo não conta nem quebra");
        PayrollParallelRules.ConsecutiveAccepted([M(6, PayrollParallelStatus.Accepted), M(7, PayrollParallelStatus.Accepted), M(8, PayrollParallelStatus.Rejected)])
            .Should().BeEmpty("uma divergência zera a contagem");
        PayrollParallelRules.ConsecutiveAccepted([M(5, PayrollParallelStatus.Accepted), M(6, PayrollParallelStatus.Rejected), M(7, PayrollParallelStatus.Accepted), M(8, PayrollParallelStatus.Accepted)])
            .Should().HaveCount(2, "a reprovada no meio quebra a sequência");
        PayrollParallelRules.ConsecutiveAccepted([M(5, PayrollParallelStatus.Accepted), M(7, PayrollParallelStatus.Accepted), M(8, PayrollParallelStatus.Accepted)])
            .Should().HaveCount(2, "falta junho: não são seguidas");
        PayrollParallelRules.ConsecutiveAccepted([M(6, PayrollParallelStatus.Accepted, 2), M(7, PayrollParallelStatus.Accepted, 3), M(8, PayrollParallelStatus.Accepted, 20)],
                new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero))
            .Should().Equal([new DateOnly(2026, 8, 1)], "depois da volta para a lançada, só o aceito depois conta");
    }

    [Fact]
    public void Tipo_da_conferencia_junta_as_parcelas_do_13_e_deixa_o_adiantamento_de_fora()
    {
        PayrollParallelRules.TypeOf(PayrollRunType.Thirteenth1).Should().Be(PayrollParallelType.Thirteenth);
        PayrollParallelRules.TypeOf(PayrollRunType.Thirteenth2).Should().Be(PayrollParallelType.Thirteenth);
        PayrollParallelRules.TypeOf(PayrollRunType.Advance).Should().BeNull();
        PayrollParallelRules.TypeOf(PayrollRunType.Monthly).Should().Be(PayrollParallelType.Monthly);
    }

    /// <summary>CA 2 e 9: o retrato do caso de correção perde o nascimento do dependente (vira a idade), o motivo do avulso e o do banco.</summary>
    [Fact]
    public void Caso_de_correcao_sai_sem_nascimento_nem_motivo()
    {
        var period = new DateOnly(2026, 5, 1);
        var snapshot = new PayrollCalcSnapshot(3000m, new DateOnly(2020, 2, 3), null, true, 220m, null, false, false,
            [new PayrollCalcDependent(new DateOnly(2013, 7, 19), true, true, true)], [], true, 0, 0,
            [new PayrollCalcManualLine(Guid.CreateVersion7(), null, 100m, "Prêmio da Maria Fictícia")], PayrollCalcParameters.Default, HourBank: new PayrollCalcHourBank(60, 60, "Pedido do João"));
        var clean = PayrollParallelRules.Anonymize(snapshot, period);
        clean.Dependents.Single().BirthDate.Should().Be(new DateOnly(2014, 1, 1), "12 anos em 31/05/2026, como o nascimento real");
        clean.Manual.Single().Note.Should().BeEmpty();
        clean.HourBank!.DecisionNote.Should().BeNull();
        var json = PayrollParallelRules.WriteCase(new PayrollCorrectionCase(1, "2026-05", PayrollCalculator.Version, [], clean, []));
        json.Should().NotContain("2013-07-19").And.NotContain("Maria").And.NotContain("João");
        PayrollParallelRules.ReadCase(json)!.Inputs!.BaseSalary.Should().Be(3000m);
    }
}
