using BemAqui.BuildingBlocks.Modules;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Identity.Application;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>EXE-01a: regras puras do projeto de serviço (tipo, etapas, transições, marcos, conclusão, 2FA e o título que volta no desfazer).</summary>
public class ServiceExecutionRulesTests
{
    [Fact]
    public void Tipo_e_servico_so_com_o_recurso_e_todos_os_itens_de_servico()
    {
        ExecutionKindRule.For(true, [QuoteItemKind.Service, QuoteItemKind.Service]).Should().Be(ExecutionKind.Service);
        ExecutionKindRule.For(true, [QuoteItemKind.Service, QuoteItemKind.Product]).Should().Be(ExecutionKind.Fabrication, "misto é fabricação");
        ExecutionKindRule.For(true, []).Should().Be(ExecutionKind.Fabrication, "sem itens é fabricação");
        ExecutionKindRule.For(false, [QuoteItemKind.Service]).Should().Be(ExecutionKind.Fabrication, "sem o recurso (Konebras) nada muda");
    }

    [Fact]
    public void Recurso_do_projeto_de_servico_e_so_do_perfil_Servicos()
    {
        BusinessProfileCatalog.Features[BusinessProfileCatalog.ServiceExecutionFeature].Should().Equal(BusinessProfileCatalog.Services);
        BusinessProfileCatalog.HasFeature([BusinessProfileCatalog.Factory], BusinessProfileCatalog.ServiceExecutionFeature).Should().BeFalse();
    }

    [Theory]
    [InlineData(ProjectStatus.Planned, false, false, "P", "Planejado", true)]
    [InlineData(ProjectStatus.InProgress, false, true, "I", "Em execução", false)]
    [InlineData(ProjectStatus.Delivered, false, true, "E", "Entregue", false)]
    [InlineData(ProjectStatus.Delivered, true, true, "A", "Aceito", false)]
    [InlineData(ProjectStatus.Delivered, true, false, "A", "Aceito", false)]
    [InlineData(ProjectStatus.Closed, false, false, "C", "Encerrado", true)]
    [InlineData(ProjectStatus.Cancelled, false, false, null, "Cancelado", true)]
    public void Etapas_do_servico_P_I_E_A_C_com_A_dispensado(ProjectStatus status, bool accepted, bool required, string? letter, string label, bool waived)
    {
        var stage = ProjectStageMap.For(ExecutionKind.Service, status, accepted, required);
        stage.Letters.Should().Equal("P", "I", "E", "A", "C");
        stage.Letter.Should().Be(letter);
        stage.Label.Should().Be(label);
        stage.AcceptanceWaived.Should().Be(waived);
    }

    [Fact]
    public void Etapas_da_fabricacao_continuam_P_E_F_I_T_C()
    {
        ProjectStageMap.For(ExecutionKind.Fabrication, ProjectStatus.Installing).Should().BeEquivalentTo(new { Index = 3, Label = "Instalando", Letter = "I" });
        ProjectStageMap.For(ExecutionKind.Fabrication, ProjectStatus.InProgress).Label.Should().Be("Em produção");
        ProjectStageMap.For(ExecutionKind.Fabrication, ProjectStatus.Cancelled).Index.Should().Be(-1);
        ProjectStageMap.FabricationLetters.Should().Equal("P", "E", "F", "I", "T", "C");
    }

    [Theory]
    [InlineData(ProjectStatus.Planned, ServiceAction.Start, false, true)]
    [InlineData(ProjectStatus.InProgress, ServiceAction.Start, false, false)]
    [InlineData(ProjectStatus.Planned, ServiceAction.Deliver, false, true)]
    [InlineData(ProjectStatus.InProgress, ServiceAction.Deliver, false, true)]
    [InlineData(ProjectStatus.Delivered, ServiceAction.Deliver, false, false)]
    [InlineData(ProjectStatus.Delivered, ServiceAction.Accept, false, true)]
    [InlineData(ProjectStatus.Delivered, ServiceAction.Accept, true, false)]
    [InlineData(ProjectStatus.InProgress, ServiceAction.Accept, false, false)]
    [InlineData(ProjectStatus.Delivered, ServiceAction.RevertAcceptance, true, true)]
    [InlineData(ProjectStatus.Delivered, ServiceAction.RevertAcceptance, false, false)]
    [InlineData(ProjectStatus.Closed, ServiceAction.RevertAcceptance, true, false)]
    [InlineData(ProjectStatus.Cancelled, ServiceAction.Deliver, false, false)]
    public void Tabela_de_transicoes_do_servico(ProjectStatus from, ServiceAction action, bool accepted, bool allowed) =>
        ServiceTransitions.Allows(from, action, accepted).Should().Be(allowed);

    [Fact]
    public void Fora_da_tabela_a_mensagem_diz_a_situacao_e_a_acao() =>
        ServiceTransitions.Refusal(ProjectStatus.InProgress, false, ServiceAction.Accept).Should().Be("Este projeto está em execução e não pode registrar o aceite do cliente.");

    [Fact]
    public void Marco_de_fabricacao_em_servico_e_recusado_no_orcamento_aprovacao_e_aditivo_mas_o_legado_passa()
    {
        (TriggerEvent?, string)[] milestones = [(TriggerEvent.Signature, "Sinal"), (TriggerEvent.FabricationDone, "Fabricação")];
        foreach (var mode in new[] { MilestoneValidationMode.Quote, MilestoneValidationMode.Approval, MilestoneValidationMode.Amendment })
        {
            var violation = MilestoneTriggerPolicy.Validate(ExecutionKind.Service, milestones, mode);
            violation.Should().NotBeNull();
            violation!.Field.Should().Be("milestones[1].triggerEvent");
            violation.Message.Should().Be("Marco 2 (Fabricação): projeto de serviço não tem fabricação. Troque o gatilho para entrega, aceite do cliente ou data.");
        }

        MilestoneTriggerPolicy.Validate(ExecutionKind.Service, milestones, MilestoneValidationMode.Legacy).Should().BeNull();
        MilestoneTriggerPolicy.Validate(ExecutionKind.Fabrication, milestones, MilestoneValidationMode.Approval).Should().BeNull();
    }

    [Fact]
    public void Aceite_e_exigido_so_no_servico_com_marco_de_aceite()
    {
        CompletionRules.RequiresAcceptance(ExecutionKind.Service, [TriggerEvent.Signature, TriggerEvent.TechnicalAcceptance]).Should().BeTrue();
        CompletionRules.RequiresAcceptance(ExecutionKind.Service, [TriggerEvent.Delivered, null]).Should().BeFalse();
        CompletionRules.RequiresAcceptance(ExecutionKind.Fabrication, [TriggerEvent.TechnicalAcceptance]).Should().BeFalse();
    }

    [Theory]
    [InlineData("projects.approve", true)]
    [InlineData("finance.pay", true)]
    [InlineData("projects.register_acceptance", true)]
    [InlineData("projects.edit", false)]
    public void Login_exige_2FA_de_quem_aprova_paga_ou_registra_aceite(string permission, bool requires) =>
        ApproverRequiresTwoFactorRule.Requires(permission).Should().Be(requires);

    [Fact]
    public void Registrar_aceite_e_acao_do_catalogo_nunca_dada_a_aplicativo_nem_por_PIN()
    {
        ModuleCatalog.IsKnownPermission("projects.register_acceptance").Should().BeTrue();
        ModuleCatalog.TwoFactorActions.Should().Contain("projects.register_acceptance");
        ConnectedAppScopes.IsNeverGranted("projects.register_acceptance").Should().BeTrue();
        BemAqui.Identity.Application.Shopfloor.PinProfile.IsSensitive("projects.register_acceptance").Should().BeTrue();
    }

    [Fact]
    public void So_o_titulo_aberto_sem_baixa_e_sem_boleto_volta_a_previsto()
    {
        AcceptanceRevertedConsumer.CanReturn(new Receivable { Status = ReceivableStatus.Open }).Should().BeTrue();
        AcceptanceRevertedConsumer.CanReturn(new Receivable { Status = ReceivableStatus.Open, BillingSlipId = Guid.NewGuid() }).Should().BeFalse();
        AcceptanceRevertedConsumer.CanReturn(new Receivable { Status = ReceivableStatus.PartiallyPaid }).Should().BeFalse();
        AcceptanceRevertedConsumer.CanReturn(new Receivable { Status = ReceivableStatus.Cancelled }).Should().BeFalse();
    }
}
