using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>EXE-03a: regras puras da cópia das etapas e da etapa atual (sem banco).</summary>
public class ServiceStagePlanTests
{
    private static readonly StageTemplateStepInfo[] Steps =
    [
        new("K", "Kickoff", StageRole.Execution, 2m, null, false, ["Ata"]),
        new("T", "Treinamento", StageRole.Execution, 1.5m, null, true, []),
        new("G", "Go-live", StageRole.Delivery, 0.3333m, null, false, []),
        new("A", "Aceite", StageRole.Acceptance, 0m, null, false, []),
    ];

    [Fact]
    public void Copia_em_planejado_primeira_pronta_e_horas_pela_quantidade()
    {
        var copy = ServiceStagePlan.Copy(Steps, 3m, ProjectStatus.Planned, null, null);
        copy.Select(c => c.Status).Should().Equal(ProjectStageStatus.Ready, ProjectStageStatus.Waiting, ProjectStageStatus.Waiting, ProjectStageStatus.Waiting);
        copy.Select(c => c.PlannedHours).Should().Equal(6m, 4.5m, 0.9999m, 0m);
        copy.Select(c => c.Sequence).Should().Equal(1, 2, 3, 4);
        copy[0].Checklist.Should().Equal("Ata");
        ServiceStagePlan.PlannedHours(2m, 0m).Should().Be(2m, "sem item, × 1");
    }

    [Fact]
    public void Copia_em_entregue_conclui_execucao_e_entrega_e_o_aceite_so_com_aceite_vigente()
    {
        var delivered = new DateOnly(2026, 11, 20);
        var semAceite = ServiceStagePlan.Copy(Steps, 1m, ProjectStatus.Delivered, delivered, null);
        semAceite.Select(c => c.Status).Should().Equal(ProjectStageStatus.Done, ProjectStageStatus.Done, ProjectStageStatus.Done, ProjectStageStatus.Ready);
        semAceite.Take(3).Should().OnlyContain(c => c.CompletedOn == delivered);
        semAceite[3].CompletedOn.Should().BeNull();

        var accepted = new DateOnly(2026, 11, 25);
        var comAceite = ServiceStagePlan.Copy(Steps, 1m, ProjectStatus.Delivered, delivered, accepted);
        comAceite[3].Status.Should().Be(ProjectStageStatus.Done);
        comAceite[3].CompletedOn.Should().Be(accepted);
    }

    [Fact]
    public void Etapa_atual_e_a_primeira_nao_concluida_ou_a_ultima()
    {
        ServiceStagePlan.Current([]).Should().BeNull();
        ServiceStagePlan.Current([(2, "T", "Treinamento", ProjectStageStatus.Waiting), (1, "K", "Kickoff", ProjectStageStatus.Done)])
            .Should().Be(new ProjectCurrentStage("T", "Treinamento", 2, 2));
        ServiceStagePlan.Current([(1, "K", "Kickoff", ProjectStageStatus.Done), (2, "A", "Aceite", ProjectStageStatus.Done)])
            .Should().Be(new ProjectCurrentStage("A", "Aceite", 2, 2));
        ServiceStagePlan.AcceptsStages(ProjectStatus.Closed).Should().BeFalse();
        ServiceStagePlan.AcceptsStages(ProjectStatus.Cancelled).Should().BeFalse();
        ServiceStagePlan.AcceptsStages(ProjectStatus.Delivered).Should().BeTrue();
    }
}
