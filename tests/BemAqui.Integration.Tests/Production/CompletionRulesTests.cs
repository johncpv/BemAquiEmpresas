using BemAqui.Commercial.Application;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PRD-06a1, regras sem banco: "Pronta para concluir" gravada (CA 1), bloqueios da conclusão (CA 4), última OS de fabricação do projeto
/// sem contar as canceladas (CA 2 e 7), estorno nas etapas (CA 10), motivo do estorno e os textos do aviso e do registro manual.
/// </summary>
public class CompletionRulesTests
{
    private const ProductionStepStatus D = ProductionStepStatus.Done;

    private static CompletionStep S(string name, ProductionStepStatus? status, bool quality = false, bool awaiting = false) => new(name, status, quality, awaiting);

    [Fact]
    public void Todas_as_etapas_concluidas_deixam_a_OS_pronta_para_concluir_e_a_concluida_nao_muda()
    {
        LaborRules.OrderStatus(ProductionOrderStatus.Quality, [new StepState(D, false), new StepState(D, true)]).Should().Be(ProductionOrderStatus.ReadyToComplete, "CA 1: aprovada a última qualidade");
        LaborRules.OrderStatus(ProductionOrderStatus.InProgress, [new StepState(D, false)]).Should().Be(ProductionOrderStatus.ReadyToComplete, "CA 1: última etapa concluída");
        LaborRules.OrderStatus(ProductionOrderStatus.Completed, [new StepState(D, false)]).Should().Be(ProductionOrderStatus.Completed);
    }

    [Fact]
    public void Bloqueios_da_conclusao_dizem_o_motivo()
    {
        CompletionRules.Blocker(ProductionOrderStatus.ReadyToComplete, [S("Corte", D), S("Qualidade", D, quality: true)], null).Should().BeNull("CA 1: pronta para concluir");
        CompletionRules.Blocker(ProductionOrderStatus.Quality, [S("Corte", D), S("Qualidade", ProductionStepStatus.Ready, quality: true)], null).Should().Be("Falta aprovar a Qualidade.");
        CompletionRules.Blocker(ProductionOrderStatus.InProgress, [S("Corte", D), S("Montagem", ProductionStepStatus.InProgress)], "Montagem").Should().Be("Há apontamento aberto na etapa Montagem.");
        CompletionRules.Blocker(ProductionOrderStatus.InProgress, [S("Corte", D), S("Montagem", ProductionStepStatus.Ready)], null).Should().Be("Falta concluir a etapa Montagem.");
        CompletionRules.Blocker(ProductionOrderStatus.InProgress, [S("Solda", ProductionStepStatus.InProgress, awaiting: true)], null).Should().Be("Falta aprovar a etapa Solda.");
        CompletionRules.Blocker(ProductionOrderStatus.Cancelled, [S("Corte", D)], null).Should().Be("A OS foi cancelada.");
        CompletionRules.Blocker(ProductionOrderStatus.Completed, [S("Corte", D)], null).Should().Be("A OS já foi concluída.");
        CompletionRules.Blocker(ProductionOrderStatus.Open, [], null).Should().Be("A OS não tem etapas. Escolha o roteiro.");
    }

    [Fact]
    public void Ultima_OS_de_fabricacao_nao_conta_as_canceladas()
    {
        var me = Guid.NewGuid();
        CompletionRules.IsLastOrder(me, [(me, ProductionOrderStatus.ReadyToComplete)]).Should().BeTrue();
        CompletionRules.IsLastOrder(me, [(me, ProductionOrderStatus.ReadyToComplete), (Guid.NewGuid(), ProductionOrderStatus.Cancelled), (Guid.NewGuid(), ProductionOrderStatus.Completed)])
            .Should().BeTrue("CA 7: cancelada não conta");
        CompletionRules.IsLastOrder(me, [(me, ProductionOrderStatus.ReadyToComplete), (Guid.NewGuid(), ProductionOrderStatus.InProgress)]).Should().BeFalse();
    }

    [Fact]
    public void Projeto_fabricado_so_com_todas_as_OS_de_fabricacao_concluidas()
    {
        ProjectFabricationRules.AllCompleted([ProductionOrderState.Completed, ProductionOrderState.Completed]).Should().BeTrue("CA 7");
        ProjectFabricationRules.AllCompleted([ProductionOrderState.Completed, ProductionOrderState.ReadyToComplete]).Should().BeFalse();
        ProjectFabricationRules.AllCompleted([]).Should().BeFalse("sem OS, o registro é manual");
        ProjectFabricationRules.ReversedNotice("OS-2026-00041", "Fabricação concluída (40%)")
            .Should().Be("Conclusão da OS-2026-00041 estornada. A parcela Fabricação concluída (40%) já liberada continua em aberto.");
        ProjectFabricationRules.ReversedWithoutReleaseNotice("OS-2026-00041")
            .Should().Be("Conclusão da OS-2026-00041 estornada. Nenhuma parcela tinha sido liberada: não há nada a ajustar no financeiro.");
        ProjectService.ByOrdersMessage(2).Should().Be("A fabricação deste projeto é concluída pelas OS (2 abertas).");
        ProjectService.ByOrdersMessage(1).Should().Be("A fabricação deste projeto é concluída pelas OS (1 aberta).");
        ProjectService.ReceivedMessage("Fabricação concluída (40%)").Should().Be("A parcela Fabricação concluída (40%) já foi recebida. Fale com o Financeiro antes de estornar.");
    }

    [Fact]
    public void Estorno_volta_a_ultima_etapa_e_a_OS_fica_em_andamento_ou_em_qualidade()
    {
        CompletionRules.ReversedStatus([new StepState(D, false), new StepState(D, false)]).Should().Be(ProductionOrderStatus.InProgress, "CA 10");
        CompletionRules.ReversedStatus([new StepState(D, false), new StepState(D, true)]).Should().Be(ProductionOrderStatus.Quality, "a última de qualidade volta à inspeção");
        CompletionRules.ReversalReason("  Medida errada na cabine  ").Should().Be("Medida errada na cabine");
        CompletionRules.ReversalReason(" ").Should().BeNull();
        CompletionRules.ReversalReason(new string('x', 501)).Should().BeNull();
    }
}