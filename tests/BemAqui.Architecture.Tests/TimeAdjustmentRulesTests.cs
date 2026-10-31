using BemAqui.BuildingBlocks.Attachments;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// RH-01b: regras do ajuste do ponto sem banco: o dia pela jornada (CA 3), as datas (CA 4), a lista única de ausências e a migração dos
/// tipos da CAD-04c1 (CA 7), os textos (CA 10, 17 e 19), a segregação com o Proprietário único (CA 13) e o dono de anexo do documento (CA 8).
/// </summary>
public class TimeAdjustmentRulesTests
{
    private static string Hr => Path.Combine(Solution.RootDirectory, "src", "Modules", "HR");

    [Fact]
    public void Dia_pela_jornada_com_intervalo_ao_meio_dia()
    {
        IReadOnlyList<ScheduleDay> days = [new(1, "08:00", "17:48", 60), new(6, "08:00", "12:00", 0), new(2, "13:00", "22:00", 60)];
        TimeAdjustmentRules.ScheduleEntries(days, new DateOnly(2026, 10, 19)).Should().Equal(
            (TimeEntryType.In, new TimeOnly(8, 0)), (TimeEntryType.BreakStart, new TimeOnly(12, 0)), (TimeEntryType.BreakEnd, new TimeOnly(13, 0)), (TimeEntryType.Out, new TimeOnly(17, 48)));
        TimeAdjustmentRules.ScheduleEntries(days, new DateOnly(2026, 10, 24)).Should().Equal((TimeEntryType.In, new TimeOnly(8, 0)), (TimeEntryType.Out, new TimeOnly(12, 0)));
        // Turno da tarde: 12:00 não cabe, o intervalo fica na metade do dia trabalhado (13:00 + 4 h).
        TimeAdjustmentRules.ScheduleEntries(days, new DateOnly(2026, 10, 20)).Select(e => e.Time).Should().Equal(new TimeOnly(13, 0), new TimeOnly(17, 0), new TimeOnly(18, 0), new TimeOnly(22, 0));
        TimeAdjustmentRules.ScheduleEntries(days, new DateOnly(2026, 10, 25)).Should().BeEmpty("domingo fora da jornada");
        TimeAdjustmentRules.ScheduleEntries(null, new DateOnly(2026, 10, 19)).Should().BeEmpty();
    }

    [Fact]
    public void Data_no_futuro_ou_fora_do_periodo_em_aberto_e_recusada()
    {
        var today = new DateOnly(2026, 10, 27);
        var admission = new DateOnly(2025, 1, 10);
        TimeAdjustmentRules.DateError(today.AddDays(1), today, null, admission, null).Should().Be("Não é possível lançar marcação no futuro.");
        TimeAdjustmentRules.DateError(new DateOnly(2026, 9, 30), today, new DateOnly(2026, 10, 1), admission, null).Should().Be("Data fora do período em aberto.");
        TimeAdjustmentRules.DateError(new DateOnly(2025, 1, 9), today, null, admission, null).Should().Be("Data fora do período em aberto.", "antes da admissão");
        TimeAdjustmentRules.DateError(new DateOnly(2026, 10, 20), today, null, admission, new DateOnly(2026, 10, 15)).Should().Be("Data fora do período em aberto.", "depois do desligamento");
        TimeAdjustmentRules.DateError(today, today, new DateOnly(2026, 10, 1), admission, null).Should().BeNull();
        var now = new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero);
        TimeAdjustmentRules.IsFuture(now.AddMinutes(1), now).Should().BeTrue();
        TimeAdjustmentRules.IsFuture(now, now).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("curta", false)]
    [InlineData("Esqueci de marcar a saída", true)]
    public void Justificativa_de_10_a_200_caracteres(string? text, bool ok)
    {
        (TimeAdjustmentRules.JustificationError(text) is null).Should().Be(ok);
        TimeAdjustmentRules.JustificationError(new string('a', 201)).Should().Contain("Não informe CID nem diagnóstico.");
    }

    [Fact]
    public void Lista_unica_de_ausencias_com_uso_e_saude()
    {
        AbsenceReasons.All.Select(r => r.Label).Should().Equal(
            "Atestado médico", "Acidente de trabalho", "Maternidade", "Paternidade", "Serviço militar", "Declaração de comparecimento",
            "Folga compensada pelo banco de horas", "Falta justificada", "Falta não justificada");
        AbsenceReasons.All.Where(r => r.IsHealth).Select(r => r.Code).Should().Equal(AbsenceReason.MedicalCertificate, AbsenceReason.WorkAccident);
        AbsenceReasons.ForLeave(AbsenceReason.Maternity).Should().BeTrue();
        AbsenceReasons.ForAbsence(AbsenceReason.Maternity).Should().BeFalse();
        AbsenceReasons.ForLeave(AbsenceReason.AttendanceNote).Should().BeFalse();
        AbsenceReasons.ForAbsence(AbsenceReason.UnjustifiedAbsence).Should().BeTrue();
        AbsenceReasons.Info(AbsenceReason.UnjustifiedAbsence).Justifies.Should().BeFalse("falta não justificada é só registro");
        AbsenceReasons.Info(AbsenceReason.TimeBankOff).Justifies.Should().BeFalse("folga compensada desconta do banco (RH-01c CA 10)");
        Enum.GetValues<AbsenceReason>().Should().OnlyContain(r => r.ToString().Length <= 20, "a coluna leave_type tem 20 caracteres");
    }

    [Theory]
    [InlineData("Illness", AbsenceReason.MedicalCertificate)]
    [InlineData("Other", AbsenceReason.JustifiedAbsence)]
    [InlineData("WorkAccident", AbsenceReason.WorkAccident)]
    [InlineData("Maternity", AbsenceReason.Maternity)]
    [InlineData("Paternity", AbsenceReason.Paternity)]
    [InlineData("Military", AbsenceReason.Military)]
    public void Tipos_de_afastamento_da_CAD_04c1_migram_sem_perda(string legacy, AbsenceReason expected)
    {
        var reason = AbsenceReasons.FromLegacyLeaveType(legacy);
        reason.Should().Be(expected);
        AbsenceReasons.ForLeave(reason).Should().BeTrue("todo afastamento antigo continua válido como afastamento");
    }

    [Fact]
    public void Migracao_atualiza_os_dois_tipos_renomeados()
    {
        var migration = Directory.GetFiles(Path.Combine(Solution.RootDirectory, "src", "Host", "BemAqui.Host", "Persistence", "Migrations"), "*_TimeAdjustmentsRh01b.cs").Single();
        var text = File.ReadAllText(migration);
        text.Should().Contain("SET leave_type = 'MedicalCertificate' WHERE leave_type = 'Illness'").And.Contain("SET leave_type = 'JustifiedAbsence' WHERE leave_type = 'Other'")
            .And.Contain("SET previous_leave_type = 'MedicalCertificate' WHERE previous_leave_type = 'Illness'").And.NotContain("DropTable(\n                name: \"time_entries\"");
    }

    [Fact]
    public void Textos_do_que_muda_e_das_notificacoes()
    {
        var local = new DateTimeOffset(2026, 10, 21, 17, 50, 0, TimeSpan.FromHours(-3));
        TimeAdjustmentRules.Summary(TimeAdjustmentKind.AddEntry, TimeEntryType.Out, local, null, null, true).Should().Be("Incluir saída às 17:50");
        TimeAdjustmentRules.Summary(TimeAdjustmentKind.VoidEntry, TimeEntryType.In, local.AddHours(-10).AddMinutes(-38), null, null, true).Should().Be("Desconsiderar entrada às 07:12");
        TimeAdjustmentRules.Summary(TimeAdjustmentKind.Absence, null, null, 240, AbsenceReason.MedicalCertificate, true).Should().Be("Abonar 4 h: Atestado médico");
        TimeAdjustmentRules.Summary(TimeAdjustmentKind.Absence, null, null, 240, AbsenceReason.MedicalCertificate, false).Should().Be("Abonar 4 h: Ausência justificada");
        TimeAdjustmentRules.Summary(TimeAdjustmentKind.Absence, null, null, null, AbsenceReason.JustifiedAbsence, true).Should().Be("Abonar o dia: Falta justificada");
        TimeAdjustmentRules.Duration(90).Should().Be("1 h 30 min");
        TimeAdjustmentRules.ApproverNotice("Ana Souza").Should().Be("Ajuste de ponto de Ana Souza para aprovar");
        TimeAdjustmentRules.ApprovedNotice(new DateOnly(2026, 10, 21)).Should().Be("Seu ajuste de 21/10/2026 foi aprovado.");
        TimeAdjustmentRules.VoidedNote("Carlos", local, "marcação em dobro").Should().Be("Desconsiderada por Carlos em 21/10/2026: marcação em dobro");
        TimeAdjustmentRules.DedupKey(Guid.Empty).Should().Be("time-adjustment:" + Guid.Empty);
    }

    [Fact]
    public void Trabalhado_do_dia_pelos_pares_fechados()
    {
        var day = new DateTimeOffset(2026, 10, 21, 11, 0, 0, TimeSpan.Zero);
        TimeAdjustmentRules.WorkedMinutes([(day, TimeEntryType.In), (day.AddHours(4), TimeEntryType.BreakStart), (day.AddHours(5), TimeEntryType.BreakEnd), (day.AddHours(9).AddMinutes(48), TimeEntryType.Out)])
            .Should().Be(528);
        TimeAdjustmentRules.WorkedMinutes([(day, TimeEntryType.In), (day.AddHours(4), TimeEntryType.BreakStart), (day.AddHours(5), TimeEntryType.BreakEnd)]).Should().Be(240, "o par aberto não conta");
    }

    [Fact]
    public void Quem_pede_ou_e_dono_das_marcacoes_nao_aprova_salvo_o_Proprietario_unico()
    {
        var requester = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        TimeAdjustmentRules.MayDecide([requester], requester, noOtherEligible: false).Should().BeFalse();
        TimeAdjustmentRules.MayDecide([requester], requester, noOtherEligible: true).Should().BeTrue("Proprietário único, com a marca autoaprovado");
        TimeAdjustmentRules.MayDecide([requester], other, noOtherEligible: false).Should().BeTrue();
        TimeAdjustmentRules.OwnMessage.Should().Be("Você não pode aprovar um ajuste seu.");
    }

    /// <summary>CA 8: o dono TimeAdjustmentDocument lê e grava com hr.view_health, pdf, png e jpg até 10 MB, confere o pedido, audita o download e fecha a rota genérica.</summary>
    [Fact]
    public void Dono_TimeAdjustmentDocument_com_as_regras_da_ADR()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        BemAqui.HR.Infrastructure.HrInfrastructure.AddHrInfrastructure(services);
        var rules = services.Select(d => d.ImplementationInstance).OfType<AttachmentOwnerRegistration>().Single(r => r.OwnerType == "TimeAdjustmentDocument").Rules;
        rules.Module.Should().Be("hr");
        rules.ReadAction.Should().Be("view_health");
        rules.WriteAction.Should().Be("view_health");
        rules.ContentTypes.Should().BeEquivalentTo(["application/pdf", "image/png", "image/jpeg"]);
        rules.MaxSize.Should().Be(10 * 1024 * 1024);
        rules.OwnerExists.Should().NotBeNull();
        rules.CanAttach.Should().NotBeNull("só a rota do pedido grava, conferindo o registro");
        rules.AuditReads.Should().BeTrue();
    }

    /// <summary>CA 6 e 18: as rotas do ajuste têm permissão (RH) ou o vínculo (autoatendimento), lançar e pedir com Idempotency-Key, nenhuma no PIN e nenhuma edita nem apaga.</summary>
    [Fact]
    public void Rotas_do_ajuste_com_permissao_ou_vinculo_e_sem_editar_nem_apagar()
    {
        var endpoints = File.ReadAllText(Path.Combine(Hr, "BemAqui.HR.Endpoints", "HrEndpoints.TimeAdjustments.cs"));
        var statements = endpoints.Split(';').Where(s => s.Contains(".WithName(", StringComparison.Ordinal)).ToList();
        foreach (var statement in statements)
        {
            (statement.Contains(".RequirePermission(\"hr.", StringComparison.Ordinal) ^ statement.Contains(".RequireActiveEmployeeLink(", StringComparison.Ordinal)).Should().BeTrue(statement);
        }

        foreach (var name in new[] { "CreateEmployeeTimeAdjustment", "CreateEmployeeTimeAdjustmentDay", "CreateMyTimeAdjustment" })
        {
            statements.Single(s => s.Contains($"\"{name}\"", StringComparison.Ordinal)).Should().Contain(".RequireIdempotencyKey()", name);
        }

        foreach (var name in new[] { "ApproveTimeAdjustment", "RejectTimeAdjustment", "ApproveTimeAdjustments" })
        {
            statements.Single(s => s.Contains($"\"{name}\"", StringComparison.Ordinal)).Should().Contain("RequirePermission(\"hr.approve\")", name);
        }

        foreach (var name in new[] { "AttachTimeAdjustmentDocument", "DownloadTimeAdjustmentDocument" })
        {
            statements.Single(s => s.Contains($"\"{name}\"", StringComparison.Ordinal)).Should().Contain("RequirePermission(\"hr.view_health\")", name);
        }

        endpoints.Should().NotContain("AllowPinSession").And.NotContain("MapPut").And.NotContain("MapDelete").And.NotContain("MapPatch");
    }
}
