using System.Globalization;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.HR.Application;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Infrastructure;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>CAD-04c1b: regras de editar, encerrar antes e cancelar o afastamento, sem banco, e a ação hr.view_health.</summary>
public class EmployeeLeaveChangeRulesTests
{
    private static readonly DateOnly Start = new(2026, 10, 10);
    private static readonly DateOnly End = new(2026, 10, 20);

    private static DateOnly D(string day) => DateOnly.Parse(day, CultureInfo.InvariantCulture);

    private sealed class Recorder : IEventPublisher
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public void Publish<TEvent>(TEvent integrationEvent, Guid? correlationId = null) where TEvent : IIntegrationEvent => Events.Add(integrationEvent);
    }

    [Theory]
    [InlineData("2026-10-09", false, EmployeeLeaveState.Scheduled)]
    [InlineData("2026-10-10", false, EmployeeLeaveState.Current)]
    [InlineData("2026-10-20", false, EmployeeLeaveState.Current)]
    [InlineData("2026-10-21", false, EmployeeLeaveState.Ended)]
    [InlineData("2026-10-09", true, EmployeeLeaveState.Cancelled)]
    public void Estado_do_afastamento_no_dia(string today, bool cancelled, EmployeeLeaveState expected) =>
        EmployeeLeaveRules.State(Start, End, cancelled, D(today)).Should().Be(expected);

    [Theory]
    [InlineData(AbsenceReason.MedicalCertificate, true)]
    [InlineData(AbsenceReason.WorkAccident, true)]
    [InlineData(AbsenceReason.Maternity, false)]
    [InlineData(AbsenceReason.Paternity, false)]
    [InlineData(AbsenceReason.Military, false)]
    [InlineData(AbsenceReason.JustifiedAbsence, false)]
    public void Doenca_e_acidente_sao_dado_de_saude(AbsenceReason type, bool health) => EmployeeLeaveRules.IsHealth(type).Should().Be(health);

    [Fact]
    public void Tipo_e_motivo_de_qualquer_afastamento_so_com_view_health()
    {
        EmployeeLeaveRules.ShowsDetails(canViewHealth: true).Should().BeTrue();
        EmployeeLeaveRules.ShowsDetails(canViewHealth: false).Should().BeFalse("PO 22/10/2026: nenhum tipo sem a ação, para não deixar inferir o de saúde");
    }

    [Fact]
    public void Editar_so_o_previsto_ou_o_em_curso()
    {
        var today = D("2026-10-15");
        EmployeeLeaveRules.EditError(EmployeeLeaveState.Scheduled, D("2026-11-01"), D("2026-11-05"), today).Should().BeNull("previsto: muda tudo");
        EmployeeLeaveRules.EditError(EmployeeLeaveState.Scheduled, D("2026-10-12"), D("2026-10-18"), today).Should().BeNull("previsto pode passar a começar antes de hoje");
        EmployeeLeaveRules.EditError(EmployeeLeaveState.Current, D("2026-10-08"), D("2026-10-25"), today).Should().BeNull("em curso: corrige o início para trás e estende o fim");
        EmployeeLeaveRules.EditError(EmployeeLeaveState.Current, today, today, today).Should().BeNull("início e fim hoje ainda valem");
        Field(EmployeeLeaveRules.EditError(EmployeeLeaveState.Current, D("2026-10-16"), D("2026-10-25"), today)).Should().Be("startDate", "o começo já foi publicado");
        Field(EmployeeLeaveRules.EditError(EmployeeLeaveState.Current, Start, D("2026-10-14"), today)).Should().Be("expectedEndDate", "terminar antes é pelo encerramento");
        EmployeeLeaveRules.EditError(EmployeeLeaveState.Ended, Start, End, today)!.Should().Match<Error>(e => e.Type == ErrorType.Conflict && e.Code == EmployeeLeaveRules.EndedCode);
        EmployeeLeaveRules.EditError(EmployeeLeaveState.Cancelled, Start, End, today)!.Should().Match<Error>(e => e.Type == ErrorType.Conflict && e.Code == EmployeeLeaveRules.CancelledCode);
    }

    [Theory]
    [InlineData("2026-10-15", "2026-10-15", null)]
    [InlineData("2026-10-15", "2026-10-11", null)]
    [InlineData("2026-10-20", "2026-10-20", null)]
    [InlineData("2026-10-15", "2026-10-10", "returnDate")]
    [InlineData("2026-10-15", "2026-10-16", "returnDate")]
    [InlineData("2026-10-20", "2026-10-21", "returnDate")]
    public void Encerrar_antes_com_a_volta_entre_o_inicio_e_o_fim_ate_hoje(string today, string back, string? field)
    {
        var error = EmployeeLeaveRules.EndError(EmployeeLeaveState.Current, Start, End, D(back), D(today));
        if (field is null)
        {
            error.Should().BeNull();
        }
        else
        {
            Field(error).Should().Be(field);
        }
    }

    [Fact]
    public void Encerrar_recusa_sem_volta_e_fora_do_em_curso()
    {
        Field(EmployeeLeaveRules.EndError(EmployeeLeaveState.Current, Start, End, null, D("2026-10-15"))).Should().Be("returnDate");
        EmployeeLeaveRules.EndError(EmployeeLeaveState.Scheduled, Start, End, D("2026-10-12"), D("2026-10-09"))!.Code.Should().Be(EmployeeLeaveRules.NotStartedCode);
        EmployeeLeaveRules.EndError(EmployeeLeaveState.Ended, Start, End, D("2026-10-12"), D("2026-10-25"))!.Code.Should().Be(EmployeeLeaveRules.EndedCode);
        EmployeeLeaveRules.EndError(EmployeeLeaveState.Cancelled, Start, End, D("2026-10-12"), D("2026-10-09"))!.Code.Should().Be(EmployeeLeaveRules.CancelledCode);
    }

    [Fact]
    public void Cancelar_so_o_que_nao_comecou_e_com_motivo()
    {
        EmployeeLeaveRules.CancelError(EmployeeLeaveState.Scheduled, "Lançado em duplicidade").Should().BeNull();
        Field(EmployeeLeaveRules.CancelError(EmployeeLeaveState.Scheduled, "  ")).Should().Be("reason");
        Field(EmployeeLeaveRules.CancelError(EmployeeLeaveState.Scheduled, new string('x', 201))).Should().Be("reason");
        EmployeeLeaveRules.CancelError(EmployeeLeaveState.Current, "Errado")!.Code.Should().Be(EmployeeLeaveRules.StartedCode, "já iniciado só se encerra");
        EmployeeLeaveRules.CancelError(EmployeeLeaveState.Ended, "Errado")!.Code.Should().Be(EmployeeLeaveRules.StartedCode);
        EmployeeLeaveRules.CancelError(EmployeeLeaveState.Cancelled, "De novo")!.Code.Should().Be(EmployeeLeaveRules.CancelledCode);
    }

    [Fact]
    public void Encerrar_antes_publica_o_fim_uma_vez_e_o_job_nao_repete()
    {
        var publisher = new Recorder();
        var leave = new EmployeeHistoryEntry { EmployeeId = Guid.CreateVersion7(), Kind = EmployeeHistoryKind.Leave, EffectiveDate = Start, LeaveEndDate = End };
        var today = D("2026-10-15");
        LeaveEvents.PublishDue(publisher, leave, today, DateTimeOffset.UtcNow).Should().BeTrue("o começo sai no registro");

        // Volta hoje: o último dia afastado foi ontem, então o fim sai no próprio encerramento.
        leave.LeaveEndDate = today.AddDays(-1);
        LeaveEvents.PublishDue(publisher, leave, today, DateTimeOffset.UtcNow).Should().BeTrue();
        publisher.Events.Last().Should().Be(new EmployeeLeaveEnded(leave.EmployeeId, Start, today.AddDays(-1)));
        LeaveEvents.PublishDue(publisher, leave, today.AddDays(1), DateTimeOffset.UtcNow).Should().BeFalse("o job do dia seguinte não publica de novo");
        LeaveEvents.PublishDue(publisher, leave, End.AddDays(1), DateTimeOffset.UtcNow).Should().BeFalse("nem no dia seguinte ao fim previsto original");
        publisher.Events.OfType<EmployeeLeaveEnded>().Should().ContainSingle();
    }

    [Fact]
    public void Cancelado_nunca_publica()
    {
        var publisher = new Recorder();
        var leave = new EmployeeHistoryEntry { EmployeeId = Guid.CreateVersion7(), Kind = EmployeeHistoryKind.Leave, EffectiveDate = Start, LeaveEndDate = End, CancelledAt = DateTimeOffset.UtcNow };
        LeaveEvents.PublishDue(publisher, leave, End.AddDays(5), DateTimeOffset.UtcNow).Should().BeFalse();
        publisher.Events.Should().BeEmpty();
    }

    [Fact]
    public void Edicao_que_traz_o_inicio_para_hoje_publica_o_comeco_na_hora()
    {
        var publisher = new Recorder();
        var leave = new EmployeeHistoryEntry { EmployeeId = Guid.CreateVersion7(), Kind = EmployeeHistoryKind.Leave, EffectiveDate = Start, LeaveEndDate = End };
        var today = D("2026-10-05");
        LeaveEvents.PublishDue(publisher, leave, today, DateTimeOffset.UtcNow).Should().BeFalse("previsto");
        leave.EffectiveDate = today;
        LeaveEvents.PublishDue(publisher, leave, today, DateTimeOffset.UtcNow).Should().BeTrue();
        publisher.Events.Should().ContainSingle().Which.Should().Be(new EmployeeLeaveStarted(leave.EmployeeId, today, End));
    }

    [Fact]
    public void Acao_view_health_no_catalogo_reservada_ao_Proprietario_e_com_seed_patch_do_RH()
    {
        ModuleCatalog.IsKnownPermission("hr.view_health").Should().BeTrue("ação específica do HR (ADR 0003, adendo de 21/10/2026)");
        ModuleCatalog.Find("hr")!.Specific.Select(a => a.Code).Should().Equal("view_sensitive", "view_health");
        GroupService.IsSensitive("hr.view_health").Should().BeTrue("só o Proprietário concede (ADR 0008)");
        var patch = SeedPatchSeeder.Patches.Single(p => p.Key == "hr-view-health");
        var grant = patch.Grants.Should().ContainSingle("o grupo RH do seed recebe a ação; o Proprietário já tem tudo").Which;
        (grant.SeedKey, grant.Module).Should().Be(("hr", "hr"));
        grant.Actions.Should().Equal("view_health");
        var keys = SeedPatchSeeder.Patches.Select(p => p.Key).ToList();
        keys.IndexOf("hr-view-health").Should().BeGreaterThan(keys.IndexOf("production-quality"), "patch novo entra depois dos que já foram aplicados");
    }

    private static string? Field(Error? error) => error?.Fields?.Keys.Single();
}
