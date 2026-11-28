using BemAqui.Commercial.Application;
using BemAqui.Commercial.Application.Documents;
using BemAqui.Commercial.Application.Exports;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>EXE-01b: regras puras das telas e documentos do projeto de serviço (rótulos por tipo, etapa na exportação e no PDF do resumo).</summary>
public class ServiceScreenRulesTests
{
    /// <summary>Espelho de <c>projectStages(kind)</c> em <c>src/Web/src/pages/contracts/contracts.ts</c> (o teste do front confere os mesmos textos).</summary>
    [Fact]
    public void Etapas_do_back_batem_com_o_espelho_do_front()
    {
        ProjectStageMap.ServiceLetters.Should().Equal("P", "I", "E", "A", "C");
        ProjectStageMap.FabricationLetters.Should().Equal("P", "E", "F", "I", "T", "C");
        new[] { ProjectStatus.Planned, ProjectStatus.InProgress, ProjectStatus.Delivered, ProjectStatus.Closed }
            .Select(s => ProjectStageMap.For(ExecutionKind.Service, s, false, true).Label).Should().Equal("Planejado", "Em execução", "Entregue", "Encerrado");
        ProjectStageMap.For(ExecutionKind.Service, ProjectStatus.Delivered, true, true).Label.Should().Be("Aceito");
        new[] { ProjectStatus.Planned, ProjectStatus.InProgress, ProjectStatus.Fabricated, ProjectStatus.Installing, ProjectStatus.Delivered, ProjectStatus.Closed }
            .Select(s => ProjectStageMap.For(ExecutionKind.Fabrication, s).Label).Should().Equal("Planejado", "Em produção", "Fabricado", "Instalando", "Entregue", "Encerrado");
    }

    [Fact]
    public void Rotulos_do_marco_mudam_so_no_servico()
    {
        MilestoneEventLabels.Label(TriggerEvent.TechnicalAcceptance, ExecutionKind.Fabrication).Should().Be("Aceite técnico");
        MilestoneEventLabels.Label(TriggerEvent.TechnicalAcceptance, ExecutionKind.Service).Should().Be("Aceite do cliente");
        MilestoneEventLabels.Label(TriggerEvent.FabricationDone, ExecutionKind.Service).Should().Be("Conclusão da execução (antes: Fabricação concluída)");
        MilestoneEventLabels.Lower(TriggerEvent.FabricationDone, ExecutionKind.Service).Should().Be("conclusão da execução (antes: fabricação concluída)");
        ContractPdf.Due(MilestoneTrigger.Event, TriggerEvent.TechnicalAcceptance, null, 10).Should().Be("10 dias após o aceite técnico", "fábrica sem mudança");
        ContractPdf.Due(MilestoneTrigger.Event, TriggerEvent.TechnicalAcceptance, null, 10, ExecutionKind.Service).Should().Be("10 dias após o aceite do cliente");
        ContractPdf.Due(MilestoneTrigger.Event, TriggerEvent.FabricationDone, null, 0, ExecutionKind.Service).Should().Be("Após a conclusão da execução (antes: fabricação concluída)");
    }

    [Fact]
    public void Exportacao_usa_P_I_E_A_C_no_servico_e_o_de_sempre_na_fabricacao()
    {
        CommercialExports.Stage(ProjectStatus.InProgress, ProjectStageMap.For(ExecutionKind.Service, ProjectStatus.InProgress)).Should().Be("I · Em execução");
        CommercialExports.Stage(ProjectStatus.Delivered, ProjectStageMap.For(ExecutionKind.Service, ProjectStatus.Delivered, true, true)).Should().Be("A · Aceito");
        CommercialExports.Stage(ProjectStatus.InProgress, ProjectStageMap.For(ExecutionKind.Fabrication, ProjectStatus.InProgress)).Should().Be("E · Em produção");
        CommercialExports.Stage(ProjectStatus.Delivered, ProjectStageMap.For(ExecutionKind.Fabrication, ProjectStatus.Delivered)).Should().Be("T · Entregue");
        CommercialExports.Stage(ProjectStatus.Cancelled, ProjectStageMap.For(ExecutionKind.Service, ProjectStatus.Cancelled)).Should().Be("Cancelado");
    }

    [Fact]
    public void Pdf_do_resumo_do_servico_traz_so_a_data_do_aceite_e_as_letras_do_tipo()
    {
        var today = new DateOnly(2026, 11, 28);
        var acceptance = new ProjectAcceptanceView(Guid.NewGuid(), 1, AcceptanceKind.Manual, today, DateTimeOffset.UtcNow, null, "Quem registrou", "Joana Prado", "Diretora", null, null);
        var service = Project(ExecutionKind.Service, ProjectStatus.Delivered) with
        {
            AcceptedOn = today, Acceptance = acceptance, AcceptanceRequired = true,
            Stage = ProjectStageMap.For(ExecutionKind.Service, ProjectStatus.Delivered, true, true),
        };
        var model = ProjectSummaryPdf.Build(service, null, [], null, null, today);
        model.Letters.Should().Equal("P", "I", "E", "A", "C");
        model.StageIndex.Should().Be(3);
        model.StatusLabel.Should().Be("Aceito");
        model.AcceptedOn.Should().Be(today);
        model.Orders.Should().BeNull("serviço não tem OS de fabricação");
        System.Text.Json.JsonSerializer.Serialize(model).Should().NotContain("Joana").And.NotContain("Diretora", "LGPD: quem aceitou não sai no PDF");

        var fabrication = ProjectSummaryPdf.Build(Project(ExecutionKind.Fabrication, ProjectStatus.InProgress), null, [], null, null, today);
        fabrication.Letters.Should().Equal(ProjectSummaryPdf.Letters);
        fabrication.StatusLabel.Should().Be("Em produção");
        fabrication.AcceptedOn.Should().BeNull();
        fabrication.Orders.Should().NotBeNull();
    }

    private static ProjectDetail Project(ExecutionKind kind, ProjectStatus status) => new(
        Guid.NewGuid(), "PRJ-2026-0001", "Implantação", status, Guid.NewGuid(), "Cliente", Guid.NewGuid(), "CTR-2026-0001", null, null, null, null, null, null, null,
        DateTimeOffset.UtcNow, [], 1, ExecutionKind: kind);
}
