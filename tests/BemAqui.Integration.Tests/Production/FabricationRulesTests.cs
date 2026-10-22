using BemAqui.Commercial.Contracts;
using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>PRD-01, regras sem banco: quais itens geram OS (CA 1 a 3), sugestão do roteiro (CA 4), início da OS (CA 8, 17) e etapas (CA 7).</summary>
public class FabricationRulesTests
{
    private static readonly Guid A = Guid.CreateVersion7();
    private static readonly Guid B = Guid.CreateVersion7();
    private static readonly Guid C = Guid.CreateVersion7();

    [Theory]
    [InlineData(ProjectItemKind.Equipment, false, FabricationDecision.Generate)]
    [InlineData(ProjectItemKind.Equipment, true, FabricationDecision.Never)]
    [InlineData(ProjectItemKind.Product, false, FabricationDecision.Manual)]
    [InlineData(ProjectItemKind.Product, true, FabricationDecision.Never)]
    [InlineData(ProjectItemKind.Service, false, FabricationDecision.Never)]
    [InlineData(null, false, FabricationDecision.Manual)]
    [InlineData(null, null, FabricationDecision.Manual)]
    [InlineData(ProjectItemKind.Equipment, null, FabricationDecision.Manual)]
    public void Quais_itens_geram_OS_pelo_evento(ProjectItemKind? kind, bool? onboarding, FabricationDecision expected) =>
        FabricationRules.FromEvent(kind, onboarding).Should().Be(expected);

    [Theory]
    [InlineData(ProjectItemKind.Product, false, "Planned", null)]
    [InlineData(ProjectItemKind.Equipment, false, "InProgress", null)]
    [InlineData(ProjectItemKind.Service, false, "Planned", "item_no_fabrication")]
    [InlineData(ProjectItemKind.Equipment, true, "Planned", "onboarding_no_fabrication")]
    [InlineData(ProjectItemKind.Equipment, false, "Fabricated", "project_already_fabricated")]
    [InlineData(ProjectItemKind.Equipment, false, "Installing", "project_already_fabricated")]
    [InlineData(ProjectItemKind.Equipment, false, "Delivered", "project_already_fabricated")]
    [InlineData(ProjectItemKind.Equipment, false, "Cancelled", "project_cancelled")]
    public void Gerar_OS_manual(ProjectItemKind kind, bool onboarding, string status, string? code)
    {
        var item = new ProjectItemInfo(A, B, 1, "Item", 1m, null, kind, onboarding, null);
        var project = new ProjectSummary(B, "PRJ-2026-0001", "Obra", C, C, status, null);
        FabricationRules.CanGenerate(item, project)?.Code.Should().Be(code);
        if (code is null)
        {
            FabricationRules.CanGenerate(item, project).Should().BeNull();
        }
    }

    [Fact]
    public void Sugestao_do_roteiro_na_ordem_do_CA_4()
    {
        FabricationRules.PickRouting(A, B, C, [A, B, C]).Should().Be(A, "o do item do projeto vem primeiro");
        FabricationRules.PickRouting(null, B, C, [A, B, C]).Should().Be(B, "depois o vinculado ao produto");
        FabricationRules.PickRouting(A, null, C, [B, C]).Should().Be(C, "roteiro inativo do item não vale; o último usado com o produto");
        FabricationRules.PickRouting(null, null, null, [A]).Should().Be(A, "o único ativo");
        FabricationRules.PickRouting(null, null, null, [A, B]).Should().BeNull("sem regra, sem roteiro");
        FabricationRules.PickRouting(null, null, null, []).Should().BeNull();
    }

    [Fact]
    public void A_OS_comeca_com_a_primeira_etapa_iniciada_ou_fora_de_aberta()
    {
        FabricationRules.HasStarted(ProductionOrderStatus.Open, [ProductionStepStatus.Ready, ProductionStepStatus.Waiting]).Should().BeFalse();
        FabricationRules.HasStarted(ProductionOrderStatus.Open, []).Should().BeFalse();
        FabricationRules.HasStarted(ProductionOrderStatus.Open, [ProductionStepStatus.InProgress]).Should().BeTrue();
        FabricationRules.HasStarted(ProductionOrderStatus.Open, [ProductionStepStatus.Done, ProductionStepStatus.Ready]).Should().BeTrue();
        FabricationRules.HasStarted(ProductionOrderStatus.InProgress, [ProductionStepStatus.Ready]).Should().BeTrue();
        FabricationRules.CurrentIndex([]).Should().Be(-1);
        FabricationRules.CurrentIndex([ProductionStepStatus.Done, ProductionStepStatus.Ready, ProductionStepStatus.Waiting]).Should().Be(1);
        FabricationRules.CurrentIndex([ProductionStepStatus.Done, ProductionStepStatus.Done]).Should().Be(1);
        FabricationRules.InitialStatus(0).Should().Be(ProductionStepStatus.Ready);
        FabricationRules.InitialStatus(3).Should().Be(ProductionStepStatus.Waiting);
    }

    [Fact]
    public void Etapas_com_as_regras_do_roteiro()
    {
        static FabricationStepInput S(string name, string code, Guid? center, bool quality = false, decimal hours = 1m, string[]? checklist = null) =>
            new(null, name, code, center, hours, quality, false, checklist ?? []);

        var (steps, errors) = FabricationRules.Validate([S(" Corte ", "c", A, checklist: ["  Medir ", ""]), S("Qualidade", "Q", A, quality: true, hours: 1.23456m)]);
        errors.Should().BeEmpty();
        steps.Select(s => (s.Sequence, s.Name, s.Code)).Should().Equal((1, "Corte", "C"), (2, "Qualidade", "Q"));
        steps[0].Checklist.Should().Equal("Medir");
        steps[1].StandardHours.Should().Be(1.2346m);

        FabricationRules.Validate([]).Errors.Should().ContainKey("steps");
        FabricationRules.Validate([S("Corte", "C", A), S("Corte 2", "C", A)]).Errors["steps[1].code"].Should().Equal("A letra C já está na etapa 1.");
        FabricationRules.Validate([S("Corte", "1", A)]).Errors.Should().ContainKey("steps[0].code");
        FabricationRules.Validate([S("Qualidade", "Q", A, quality: true)]).Errors.Should().ContainKey("steps[0].isQualityStep");
        FabricationRules.Validate([S("Corte", "C", A), S("Q1", "Q", A, quality: true), S("Q2", "R", A, quality: true)]).Errors["steps[2].isQualityStep"]
            .Should().Equal("Duas etapas de qualidade não podem ficar seguidas.");
        FabricationRules.Validate([S("Corte", "C", null)]).Errors.Should().ContainKey("steps[0].workCenterId");
        FabricationRules.Validate([S("Corte", "C", A, hours: -1)]).Errors.Should().ContainKey("steps[0].standardHours");
        FabricationRules.Validate([S(new string('x', 61), "C", A)]).Errors.Should().ContainKey("steps[0].name");
        FabricationRules.Validate([S("Corte", "C", A, checklist: [new string('x', 121)])]).Errors.Should().ContainKey("steps[0].checklist[0]");
        FabricationRules.Validate(Enumerable.Range(0, 31).Select(i => S("E" + i, ((char)('A' + (i % 26))).ToString(), A)).ToList()).Errors.Should().ContainKey("steps");
    }
}