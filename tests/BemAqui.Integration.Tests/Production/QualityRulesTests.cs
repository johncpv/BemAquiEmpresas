using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PRD-05, regras sem banco: etapa decidível, retorno sugerido e opções (CA 6), aprovação e reprovação nas etapas, nunca
/// <c>Completed</c> (CA 3, 6, 7), situação da OS em qualidade (CA 2), segregação por tentativa com "Autoinspeção" (CA 5) e os textos.
/// </summary>
public class QualityRulesTests
{
    private const ProductionStepStatus W = ProductionStepStatus.Waiting;
    private const ProductionStepStatus R = ProductionStepStatus.Ready;
    private const ProductionStepStatus D = ProductionStepStatus.Done;
    private const ProductionStepStatus I = ProductionStepStatus.InProgress;

    private static QualityStepState S(ProductionStepStatus status, bool quality = false) => new(status, quality);

    [Theory]
    [InlineData(ProductionOrderStatus.Quality, ProductionStepStatus.Ready, true, true)]
    [InlineData(ProductionOrderStatus.Quality, ProductionStepStatus.InProgress, true, true)]
    [InlineData(ProductionOrderStatus.Quality, ProductionStepStatus.Paused, true, true)]
    [InlineData(ProductionOrderStatus.InProgress, ProductionStepStatus.Waiting, true, false)]
    [InlineData(ProductionOrderStatus.InProgress, ProductionStepStatus.Rejected, true, false)]
    [InlineData(ProductionOrderStatus.InProgress, ProductionStepStatus.Done, true, false)]
    [InlineData(ProductionOrderStatus.Cancelled, ProductionStepStatus.Ready, true, false)]
    [InlineData(ProductionOrderStatus.Quality, ProductionStepStatus.Ready, false, false)]
    public void Etapa_de_qualidade_decidivel(ProductionOrderStatus order, ProductionStepStatus step, bool quality, bool expected) =>
        QualityRules.IsDecidable(order, step, quality).Should().Be(expected);

    [Fact]
    public void Retorno_sugerido_e_a_etapa_produtiva_imediatamente_anterior()
    {
        // C → M → Q1 → P → Q2
        var steps = new[] { S(D), S(D), S(D, true), S(D), S(R, true) };
        QualityRules.ReturnOptions(steps, 4).Should().Equal(3, 1, 0);
        QualityRules.SuggestedReturn(steps, 4).Should().Be(3);
        QualityRules.ReturnOptions(steps, 2).Should().Equal(1, 0);
        QualityRules.SuggestedReturn([S(R, true)], 0).Should().Be(-1, "sem etapa anterior");
    }

    [Fact]
    public void Reprovar_volta_a_etapa_escolhida_e_as_seguintes_esperam()
    {
        var steps = new[] { S(D), S(D), S(D), S(I, true), S(W) };
        var result = QualityRules.ApplyRejection(steps, 3, 0);
        result.Should().Equal(R, W, W, ProductionStepStatus.Rejected, W);
        QualityRules.PlanRejection(steps, 3, 0).BackToWaiting.Should().Equal(1, 2);
        QualityRules.ApplyRejection(steps, 3, 2).Should().Equal(D, D, R, ProductionStepStatus.Rejected, W);
        QualityRules.ReadyToComplete(result).Should().BeFalse();
    }

    [Fact]
    public void Reprovar_antes_de_outra_qualidade_zera_o_checklist_dela_tambem()
    {
        var steps = new[] { S(D), S(D, true), S(D), S(R, true) };
        var plan = QualityRules.PlanRejection(steps, 3, 0);
        plan.BackToWaiting.Should().Equal(1, 2);
        plan.QualityStepsReset.Should().BeEquivalentTo([3, 1]);
    }

    [Fact]
    public void Retorno_posterior_ou_de_qualidade_e_recusado()
    {
        var steps = new[] { S(D), S(D, true), S(D), S(R, true), S(W) };
        var later = () => QualityRules.PlanRejection(steps, 3, 4);
        later.Should().Throw<ArgumentOutOfRangeException>();
        var quality = () => QualityRules.PlanRejection(steps, 3, 1);
        quality.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Aprovar_libera_a_proxima_ou_deixa_pronta_para_concluir_nunca_concluida()
    {
        var middle = QualityRules.ApplyApproval([S(D), S(R, true), S(W)], 1);
        middle.Should().Equal(D, D, R);
        QualityRules.ReadyToComplete(middle).Should().BeFalse();

        var last = QualityRules.ApplyApproval([S(D), S(D), S(I, true)], 2);
        last.Should().Equal(D, D, D);
        QualityRules.ReadyToComplete(last).Should().BeTrue("D12: Pronta para concluir");
        LaborRules.OrderStatus(ProductionOrderStatus.Quality, last.Select(s => new StepState(s, false)).ToList())
            .Should().Be(ProductionOrderStatus.InProgress, "a conclusão é explícita (PRD-06a1), nunca Completed");
    }

    [Fact]
    public void Tentativa_nova_a_qualidade_rejeitada_volta_a_ficar_pronta_quando_o_retrabalho_chega()
    {
        // Depois da reprovação: M pronta, Q rejeitada. Concluir M deixa a mesma Q pronta (sem etapa nova) e a OS em Qualidade.
        var afterRework = QualityRules.ApplyApproval([S(D), S(ProductionStepStatus.Rejected, true)], 0);
        afterRework.Should().Equal(D, R);
        LaborRules.OrderStatus(ProductionOrderStatus.InProgress, [new StepState(D, false), new StepState(R, true)]).Should().Be(ProductionOrderStatus.Quality);
        LaborRules.OrderStatus(ProductionOrderStatus.InProgress, [new StepState(R, false), new StepState(ProductionStepStatus.Rejected, true)])
            .Should().Be(ProductionOrderStatus.InProgress, "a OS volta a InProgress na reprovação");
    }

    [Fact]
    public void OS_fica_em_qualidade_com_a_inspecao_em_andamento_ou_pausada()
    {
        LaborRules.OrderStatus(ProductionOrderStatus.Quality, [new StepState(D, false), new StepState(I, true)]).Should().Be(ProductionOrderStatus.Quality);
        LaborRules.OrderStatus(ProductionOrderStatus.Quality, [new StepState(D, false), new StepState(ProductionStepStatus.Paused, true)]).Should().Be(ProductionOrderStatus.Quality);
    }

    [Fact]
    public void Segregacao_por_tentativa_com_autoinspecao_sem_outro_aprovador()
    {
        var inspector = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var operatorUser = Guid.CreateVersion7();
        QualityRules.Segregation(inspector, [operatorUser], [inspector, other]).Should().Be(QualitySegregation.Allowed, "não apontou a tentativa");
        QualityRules.Segregation(inspector, [inspector], [inspector, other]).Should().Be(QualitySegregation.Forbidden, "há outro aprovador elegível");
        QualityRules.Segregation(inspector, [inspector, other], [inspector, other]).Should().Be(QualitySegregation.SelfInspection, "o outro também apontou");
        QualityRules.Segregation(inspector, [inspector], [inspector]).Should().Be(QualitySegregation.SelfInspection, "único aprovador");
    }

    [Fact]
    public void Textos_dos_dialogos_e_da_notificacao()
    {
        QualityRules.ApprovalImpact("Embalagem").Should().Be("A OS vai para Embalagem.");
        QualityRules.ApprovalImpact(null).Should().Be("A OS fica pronta para concluir.");
        QualityRules.RejectionImpact("Montagem", 0).Should().Be("Volta para Montagem.");
        QualityRules.RejectionImpact("Montagem", 1).Should().Be("Volta para Montagem; 1 etapa volta a esperar.");
        QualityRules.RejectionImpact("Corte", 2).Should().Be("Volta para Corte; 2 etapas voltam a esperar.");
        QualityRules.DraftDescription(["Prumo", "Painel riscado"]).Should().Be("Não conforme: Prumo; Painel riscado.");
        QualityRules.DraftDescription([]).Should().BeEmpty();
        QualityRules.Excerpt(new string('a', 80)).Should().HaveLength(60).And.EndWith("...");
        QualityRules.AgeDays(new DateOnly(2026, 10, 1), null, new DateOnly(2026, 10, 22)).Should().Be(21);
        QualityRules.AgeDays(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 22)).Should().Be(4);
    }

    [Fact]
    public void Texto_obrigatorio_com_limite()
    {
        QualityRules.Text("  ok  ", "description", 10, "a descrição", required: true).Value.Should().Be("ok");
        QualityRules.Text(" ", "description", 10, "a descrição", required: true).Error!.Message.Should().Be("Informe a descrição.");
        QualityRules.Text(null, "cause", 10, "a causa", required: false).Value.Should().BeNull();
        QualityRules.Text(new string('x', 11), "cause", 10, "a causa", required: false).Error!.Message.Should().Be("A causa tem até 10 caracteres.");
    }
}