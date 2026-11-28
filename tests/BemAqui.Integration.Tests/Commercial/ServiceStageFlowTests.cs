using BemAqui.Commercial.Application;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>EXE-03b: máquina da etapa, permissão para concluir (com e sem marco) e autoaprovação nos três modos (sem banco).</summary>
public class ServiceStageFlowTests
{
    [Theory]
    [InlineData(ProjectStageStatus.Ready, StageCommand.Start, true)]
    [InlineData(ProjectStageStatus.Waiting, StageCommand.Start, false)]
    [InlineData(ProjectStageStatus.InProgress, StageCommand.Start, false)]
    [InlineData(ProjectStageStatus.InProgress, StageCommand.Mark, true)]
    [InlineData(ProjectStageStatus.Ready, StageCommand.Mark, false)]
    [InlineData(ProjectStageStatus.Done, StageCommand.Mark, false)]
    [InlineData(ProjectStageStatus.InProgress, StageCommand.Complete, true)]
    [InlineData(ProjectStageStatus.AwaitingApproval, StageCommand.Complete, false)]
    [InlineData(ProjectStageStatus.AwaitingApproval, StageCommand.Approve, true)]
    [InlineData(ProjectStageStatus.Done, StageCommand.Approve, false)]
    public void Maquina_da_etapa(ProjectStageStatus status, StageCommand command, bool allowed) =>
        ServiceStageFlow.Allows(status, command).Should().Be(allowed);

    [Fact]
    public void Concluir_vai_a_aprovacao_so_quando_exige_e_nao_foi_aprovada_na_hora()
    {
        ServiceStageFlow.AfterComplete(requiresApproval: false, approvedNow: false).Should().Be(ProjectStageStatus.Done);
        ServiceStageFlow.AfterComplete(requiresApproval: true, approvedNow: false).Should().Be(ProjectStageStatus.AwaitingApproval);
        ServiceStageFlow.AfterComplete(requiresApproval: true, approvedNow: true).Should().Be(ProjectStageStatus.Done);
        ServiceStageFlow.ChecklistDone([StageChecklistResult.Done, StageChecklistResult.NotApplicable]).Should().BeTrue();
        ServiceStageFlow.ChecklistDone([StageChecklistResult.Done, null]).Should().BeFalse();
        ServiceStageFlow.ChecklistDone([]).Should().BeTrue();
    }

    [Theory]
    [InlineData(StageRole.Execution, false, "edit", false)]
    [InlineData(StageRole.Execution, true, "approve", true)]
    [InlineData(StageRole.Delivery, false, "approve", true)]
    [InlineData(StageRole.Acceptance, false, "register_acceptance", true)]
    public void Permissao_para_concluir_pelo_papel_e_pelo_marco(StageRole role, bool milestone, string action, bool guarded)
    {
        ServiceStageFlow.CompletionAction(role, milestone).Should().Be(action);
        ServiceStageFlow.Guarded(role, milestone).Should().Be(guarded);
        if (role == StageRole.Execution && milestone)
        {
            ServiceStageFlow.CompletionRefusal(role, milestone).Should().Be("Esta etapa libera parcela: quem conclui são usuários com permissão de aprovar projetos.");
        }
    }

    [Theory]
    [InlineData(false, false, 2, SelfApprovalMode.Never, true, false)]
    [InlineData(true, true, 1, SelfApprovalMode.Never, true, true)]
    [InlineData(true, true, 2, SelfApprovalMode.Never, false, false)]
    [InlineData(true, true, 2, SelfApprovalMode.OwnerOnly, true, true)]
    [InlineData(true, false, 2, SelfApprovalMode.OwnerOnly, false, false)]
    [InlineData(true, false, 2, SelfApprovalMode.AnyApprover, true, true)]
    public void Autoaprovacao_nos_tres_modos(bool completer, bool owner, int owners, SelfApprovalMode mode, bool allowed, bool self) =>
        StageApprovalRules.CanApprove(completer, owner, owners, mode).Should().Be((allowed, self));

    [Fact]
    public void Parametro_ausente_vale_so_o_proprietario_e_desconhecido_vale_nunca()
    {
        CommercialParametersService.StageModeOf(null).Should().Be(SelfApprovalMode.OwnerOnly);
        CommercialParametersService.StageModeOf(0m).Should().Be(SelfApprovalMode.Never);
        CommercialParametersService.StageModeOf(2m).Should().Be(SelfApprovalMode.AnyApprover);
        CommercialParametersService.StageModeOf(-1m).Should().Be(SelfApprovalMode.Never);
    }
}
