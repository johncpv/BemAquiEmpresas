using BemAqui.BuildingBlocks.Messaging;
using BemAqui.HR.Application;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>CAD-04c1 CA 1, 2, 6 e 7: regras do afastamento sem banco (situação, sobreposição e eventos uma vez só).</summary>
public class EmployeeLeaveRulesTests
{
    private static readonly DateOnly Start = new(2026, 10, 10);
    private static readonly DateOnly End = new(2026, 10, 20);

    private sealed class Recorder : IEventPublisher
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public void Publish<TEvent>(TEvent integrationEvent, Guid? correlationId = null) where TEvent : IIntegrationEvent => Events.Add(integrationEvent);
    }

    [Theory]
    [InlineData("2026-10-09", false)]
    [InlineData("2026-10-10", true)]
    [InlineData("2026-10-15", true)]
    [InlineData("2026-10-20", true)]
    [InlineData("2026-10-21", false)]
    public void Vigente_entre_o_inicio_e_o_fim_previsto_inclusive(string day, bool expected) =>
        EmployeeLeaveRules.Covers(Start, End, DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(expected);

    [Fact]
    public void Situacao_calculada_com_desligado_prevalecendo()
    {
        EmployeeLeaveRules.Situation(EmployeeStatus.Active, onLeaveToday: false).Should().Be(EmployeeStatus.Active);
        EmployeeLeaveRules.Situation(EmployeeStatus.Active, onLeaveToday: true).Should().Be(EmployeeStatus.OnLeave);
        EmployeeLeaveRules.Situation(EmployeeStatus.Terminated, onLeaveToday: true).Should().Be(EmployeeStatus.Terminated, "Desligado prevalece sobre Afastado");
        EmployeeLeaveRules.Situation(EmployeeStatus.Terminated, onLeaveToday: false).Should().Be(EmployeeStatus.Terminated);
    }

    [Theory]
    [InlineData("2026-10-01", "2026-10-09", false)]
    [InlineData("2026-10-01", "2026-10-10", true)]
    [InlineData("2026-10-12", "2026-10-14", true)]
    [InlineData("2026-10-20", "2026-10-30", true)]
    [InlineData("2026-10-21", "2026-10-30", false)]
    public void Sobreposicao_com_um_dia_em_comum(string start, string end, bool expected) =>
        EmployeeLeaveRules.Overlaps(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), Start, End).Should().Be(expected);

    [Fact]
    public void Eventos_saem_no_inicio_e_no_dia_seguinte_ao_fim_uma_vez_so()
    {
        var publisher = new Recorder();
        var leave = new EmployeeHistoryEntry { EmployeeId = Guid.CreateVersion7(), Kind = EmployeeHistoryKind.Leave, EffectiveDate = Start, LeaveEndDate = End, LeaveType = EmployeeLeaveType.Illness, Reason = "Repouso" };
        var now = DateTimeOffset.UtcNow;

        LeaveEvents.PublishDue(publisher, leave, Start.AddDays(-1), now).Should().BeFalse("início futuro: nada sai no registro");
        LeaveEvents.PublishDue(publisher, leave, Start, now).Should().BeTrue();
        publisher.Events.Should().ContainSingle().Which.Should().Be(new EmployeeLeaveStarted(leave.EmployeeId, Start, End));
        LeaveEvents.PublishDue(publisher, leave, End, now).Should().BeFalse("no último dia ainda está afastado, e o começo já saiu");
        LeaveEvents.PublishDue(publisher, leave, End.AddDays(1), now).Should().BeTrue();
        LeaveEvents.PublishDue(publisher, leave, End.AddDays(2), now).Should().BeFalse("segunda execução não duplica");
        publisher.Events.Should().HaveCount(2).And.ContainEquivalentOf(new EmployeeLeaveEnded(leave.EmployeeId, Start, End));
        leave.StartPublishedAt.Should().NotBeNull();
        leave.EndPublishedAt.Should().NotBeNull();
    }

    [Fact]
    public void Afastamento_todo_no_passado_publica_os_dois_eventos_de_uma_vez()
    {
        var publisher = new Recorder();
        var leave = new EmployeeHistoryEntry { EmployeeId = Guid.CreateVersion7(), Kind = EmployeeHistoryKind.Leave, EffectiveDate = Start, LeaveEndDate = End };
        LeaveEvents.PublishDue(publisher, leave, End.AddDays(5), DateTimeOffset.UtcNow).Should().BeTrue();
        publisher.Events.Select(e => e.GetType().Name).Should().Equal(nameof(EmployeeLeaveStarted), nameof(EmployeeLeaveEnded));
    }
}
