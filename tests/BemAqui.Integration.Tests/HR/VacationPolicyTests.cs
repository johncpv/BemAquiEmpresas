using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-03: as regras de férias sem banco, pelos parâmetros pré-configurados pela CLT (<see cref="VacationParameters.Default"/>): faltas e dias
/// de direito (CA 1), afastamentos que reiniciam o período (CA 2), situação, alerta de vencimento e o concessivo (CA 1 e 3) e a validação da
/// programação (CA 4). Datas fictícias.
/// </summary>
public class VacationPolicyTests
{
    private static readonly VacationParameters P = VacationParameters.Default;

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    /// <summary>CA 1: art. 130 — 30 dias até 5 faltas, 24 de 6 a 14, 18 de 15 a 23, 12 de 24 a 32 e nenhum acima de 32.</summary>
    [Theory]
    [InlineData(0, 30)]
    [InlineData(5, 30)]
    [InlineData(6, 24)]
    [InlineData(14, 24)]
    [InlineData(15, 18)]
    [InlineData(23, 18)]
    [InlineData(24, 12)]
    [InlineData(32, 12)]
    [InlineData(33, 0)]
    public void Dias_de_direito_pelas_faltas(int absences, int days) => VacationPolicy.EntitledDays(absences, P.AbsenceBrackets).Should().Be(days);

    /// <summary>CA 1: períodos de 12 meses desde a admissão, o concessivo de 12 meses depois e as faltas do período.</summary>
    [Fact]
    public void Periodos_desde_a_admissao_com_as_faltas()
    {
        var absences = Enumerable.Range(0, 7).Select(i => D(2025, 6, 2).AddDays(i)).ToList();
        var plans = VacationPolicy.Plan(new VacationPlanInput(D(2024, 3, 1), null, D(2026, 11, 1), [], absences, P));
        plans.Select(p => (p.Start, p.End)).Should().Equal((D(2024, 3, 1), D(2025, 2, 28)), (D(2025, 3, 1), D(2026, 2, 28)), (D(2026, 3, 1), D(2027, 2, 28)));
        plans[0].EntitledDays.Should().Be(30);
        plans[0].ConcessionEnd.Should().Be(D(2026, 2, 28));
        plans[1].Absences.Should().Be(7);
        plans[1].EntitledDays.Should().Be(24);
        plans[1].Memory.Should().Contain("7 faltas injustificadas: 24 dias de direito (parâmetro hr.vacation_absence_brackets).");
    }

    /// <summary>
    /// CA 2: afastamento pela previdência por mais de 180 dias (depois dos 15 pagos pela empresa) encerra o período sem direito e o novo começa
    /// no retorno, com a memória "Período reiniciado em {data}: {motivo}".
    /// </summary>
    [Fact]
    public void Afastamento_pela_previdencia_reinicia_o_periodo()
    {
        var leave = new VacationLeaveInput(D(2024, 3, 1), D(2024, 10, 31), AbsenceReason.MedicalCertificate);
        var plans = VacationPolicy.Plan(new VacationPlanInput(D(2024, 1, 1), null, D(2026, 1, 15), [leave], [], P));
        plans[0].Start.Should().Be(D(2024, 1, 1));
        plans[0].End.Should().Be(D(2024, 9, 12), "a previdência paga desde 16/03; o 181º dia é 12/09");
        plans[0].EntitledDays.Should().Be(0);
        plans[0].RestartedOn.Should().Be(D(2024, 11, 1));
        plans[0].Memory.Should().Contain(m => m.StartsWith("Período reiniciado em 01/11/2024: afastamento pela previdência por mais de 180 dias", StringComparison.Ordinal));
        plans[1].Start.Should().Be(D(2024, 11, 1));
        plans[1].End.Should().Be(D(2025, 10, 31));
        plans[1].EntitledDays.Should().Be(30);
        VacationPolicy.StatusOf(plans[0].End, plans[0].ConcessionEnd, true, 0, D(2026, 1, 15)).Should().Be(VacationPeriodStatus.Closed);
    }

    /// <summary>CA 2: afastamentos que não passam de 180 dias somados não reiniciam; a licença remunerada acima de 30 dias reinicia.</summary>
    [Fact]
    public void Soma_dos_afastamentos_e_licenca_remunerada()
    {
        // 99 + 70 dias pela previdência (depois dos 15 de cada um): 169 ≤ 180, o período segue.
        List<VacationLeaveInput> leaves = [new(D(2024, 2, 1), D(2024, 5, 24), AbsenceReason.WorkAccident), new(D(2024, 7, 1), D(2024, 9, 23), AbsenceReason.MedicalCertificate)];
        VacationPolicy.Plan(new VacationPlanInput(D(2024, 1, 1), null, D(2025, 3, 1), leaves, [], P))[0].RestartReason.Should().BeNull();

        var paid = new VacationLeaveInput(D(2024, 4, 1), D(2024, 5, 15), AbsenceReason.JustifiedAbsence);
        var plans = VacationPolicy.Plan(new VacationPlanInput(D(2024, 1, 1), null, D(2025, 3, 1), [paid], [], P));
        plans[0].RestartReason.Should().StartWith("licença remunerada por mais de 30 dias");
        plans[0].RestartedOn.Should().Be(D(2024, 5, 16));
        plans[1].Start.Should().Be(D(2024, 5, 16));

        // Afastamento em aberto que passa do limite: o próximo período só começa no retorno.
        var open = new VacationLeaveInput(D(2024, 3, 1), null, AbsenceReason.MedicalCertificate);
        VacationPolicy.Plan(new VacationPlanInput(D(2024, 1, 1), null, D(2025, 6, 1), [open], [], P)).Should().ContainSingle().Which.RestartedOn.Should().BeNull();
    }

    /// <summary>CA 1 e 3: em aquisição, a gozar, "Vence em" a 60 dias, vencido (o concessivo passou) e encerrado sem saldo.</summary>
    [Fact]
    public void Situacao_vencimento_e_prazo_do_dobro()
    {
        var end = D(2025, 2, 28);
        var concession = VacationPolicy.ConcessionEnd(end);
        concession.Should().Be(D(2026, 2, 28));
        VacationPolicy.StatusOf(end, concession, false, 30, D(2025, 2, 28)).Should().Be(VacationPeriodStatus.Accruing);
        VacationPolicy.StatusOf(end, concession, false, 30, D(2025, 6, 1)).Should().Be(VacationPeriodStatus.Available);
        VacationPolicy.StatusLabel(VacationPeriodStatus.Available, concession, D(2025, 6, 1)).Should().Be("A gozar");
        VacationPolicy.StatusLabel(VacationPeriodStatus.Available, concession, D(2025, 12, 30)).Should().Be("Vence em 28/02/2026", "a 60 dias do fim");
        VacationPolicy.DueSoon(VacationPeriodStatus.Available, concession, D(2025, 12, 29)).Should().BeFalse("a 61 dias ainda não é alerta");
        VacationPolicy.StatusOf(end, concession, false, 30, D(2026, 3, 1)).Should().Be(VacationPeriodStatus.Expired);
        VacationPolicy.StatusLabel(VacationPeriodStatus.Expired, concession, D(2026, 3, 1)).Should().Be("Vencido");
        VacationPolicy.StatusOf(end, concession, false, 0, D(2026, 3, 1)).Should().Be(VacationPeriodStatus.Closed);
        VacationPolicy.StatusOf(D(2026, 2, 28), D(2027, 2, 28), false, 30, D(2026, 3, 10), termination: D(2026, 1, 31)).Should().Be(VacationPeriodStatus.Closed, "o proporcional do desligado é da FLH-05");
    }

    private static readonly VacationPeriodView Period = new(VacationPeriodStatus.Available, D(2026, 2, 28), 30, 30, D(2027, 2, 28));

    private static bool Sundays(DateOnly d) => d.DayOfWeek == DayOfWeek.Sunday || d == D(2026, 6, 4);

    private static VacationCheck Check(VacationRequest request, IReadOnlyList<VacationPart>? parts = null, VacationPeriodView? period = null, DateOnly? today = null) =>
        VacationPolicy.Validate(period ?? Period, parts ?? [], parts ?? [], request, Sundays, today ?? D(2026, 4, 1), null, P);

    /// <summary>CA 4: o fracionamento, o abono e o início nos 2 dias antes de feriado ou repouso, com a mensagem e o valor do parâmetro.</summary>
    [Theory]
    [InlineData(2026, 6, 15, 10, 0, "days", "O primeiro período precisa ter pelo menos 14 dias.")]
    [InlineData(2026, 6, 15, 20, 11, "soldDays", "O abono pecuniário é de até 10 dias (1/3 dos 30 dias de direito).")]
    [InlineData(2026, 6, 15, 31, 0, "days", "O período tem 30 dias de saldo.")]
    [InlineData(2026, 6, 15, 27, 0, "days", "Sobrariam 3 dias: cada período precisa ter pelo menos 5 dias.")]
    [InlineData(2026, 6, 13, 30, 0, "startsOn", "As férias não podem começar nos 2 dias antes de feriado ou do repouso semanal (14/06/2026).")]
    [InlineData(2026, 6, 2, 30, 0, "startsOn", "As férias não podem começar nos 2 dias antes de feriado ou do repouso semanal (04/06/2026).")]
    [InlineData(2026, 2, 16, 30, 0, "startsOn", "As férias deste período começam a partir de 01/03/2026.")]
    public void Validacao_da_programacao(int y, int m, int d, int days, int sold, string field, string message)
    {
        var check = Check(new VacationRequest(D(y, m, d), days, sold));
        check.Field.Should().Be(field);
        check.Error.Should().Be(message);
    }

    /// <summary>CA 4: a terceira parte fecha o período; a segunda e a terceira com pelo menos 5 dias; o máximo de 3 partes.</summary>
    [Fact]
    public void Fracionamento_em_tres_partes()
    {
        List<VacationPart> parts = [new(D(2026, 6, 15), 14, 0)];
        var after = Period with { Balance = 16 };
        Check(new VacationRequest(D(2026, 7, 20), 4, 0), parts, after).Error.Should().Be("Cada período precisa ter pelo menos 5 dias.");
        Check(new VacationRequest(D(2026, 7, 20), 11, 0), parts, after).Error.Should().BeNull("sobram 5 dias para a terceira parte");
        List<VacationPart> two = [.. parts, new(D(2026, 7, 20), 11, 0)];
        Check(new VacationRequest(D(2026, 9, 14), 4, 0), two, Period with { Balance = 5 }).Error.Should().Be("Cada período precisa ter pelo menos 5 dias.");
        Check(new VacationRequest(D(2026, 9, 14), 5, 0), two, Period with { Balance = 5 }).Error.Should().BeNull();
        List<VacationPart> three = [.. two, new(D(2026, 9, 14), 5, 0)];
        Check(new VacationRequest(D(2026, 11, 9), 5, 0), three, Period with { Balance = 5 }).Error.Should().Be("As férias podem ser divididas em até 3 períodos.");
        Check(new VacationRequest(D(2026, 6, 20), 16, 0), parts, after).Error.Should().Be("Já há férias programadas de 15/06/2026 a 28/06/2026.");
    }

    /// <summary>CA 4 e 5: em aquisição recusa; o aviso de 30 dias e os dias depois do concessivo (em dobro) avisam sem travar.</summary>
    [Fact]
    public void Avisos_do_prazo_do_aviso_e_do_dobro()
    {
        Check(new VacationRequest(D(2026, 6, 15), 30, 0), period: Period with { Status = VacationPeriodStatus.Accruing }).Error.Should().StartWith("O período aquisitivo ainda não terminou");
        var ok = Check(new VacationRequest(D(2026, 4, 13), 30, 0));
        ok.Error.Should().BeNull();
        ok.Warnings.Should().ContainSingle(w => w.StartsWith("O aviso de férias precisa sair 30 dias antes do início", StringComparison.Ordinal));
        var late = Check(new VacationRequest(D(2027, 2, 15), 30, 0), today: D(2026, 12, 1));
        late.Error.Should().BeNull();
        late.Warnings.Should().ContainSingle().Which.Should().Be("Férias depois do fim do período concessivo (28/02/2027): 16 dias são pagos em dobro (CLT art. 137).");
    }

    /// <summary>Propositivo: o início sugerido é o primeiro dia depois do aviso que não cai em repouso nem nos 2 dias antes dele.</summary>
    [Fact]
    public void Sugestao_do_inicio_depois_do_aviso()
    {
        // Hoje 01/05/2026: + 30 dias = 31/05 (domingo, repouso) → 01/06 (segunda): 02/06 e 03/06 não são feriado nem repouso, então é o
        // primeiro permitido (o feriado de 04/06 está a 3 dias).
        var s = VacationPolicy.Suggest(D(2026, 5, 1), D(2026, 2, 28), 30, Sundays, P);
        s.Start.Should().Be(D(2026, 6, 1));
        s.Days.Should().Be(30);
        VacationPolicy.Blocked(s.Start, Sundays).Should().BeNull();
        VacationPolicy.PaymentDue(D(2026, 6, 1), P).Should().Be(D(2026, 5, 30));
    }

    [Fact]
    public void Dias_por_competencia() =>
        VacationPolicy.DaysByMonth(D(2026, 6, 16), D(2026, 7, 15)).Should().Equal((D(2026, 6, 1), 15), (D(2026, 7, 1), 15));
}
