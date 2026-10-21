using BemAqui.Engineering.Application;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Engineering;

/// <summary>ENG-01 CA 3 a 6: regras do roteiro sem banco (limites, letra, qualidade e sequência).</summary>
public class RoutingRulesTests
{
    private static readonly Guid Center = Guid.CreateVersion7();

    private static RoutingStepInput Step(string name, string code, bool quality = false, string[]? checklist = null, decimal hours = 1m) =>
        new(null, name, code, Center, hours, quality, false, checklist ?? []);

    [Fact]
    public void Sequencia_e_regravada_de_1_a_n_e_a_letra_vira_maiuscula()
    {
        var (steps, errors) = RoutingRules.Validate("Cabine", null, [Step(" Corte ", "c"), Step("Montagem", "M", checklist: ["  ", "Fixar guias", ""])]);
        errors.Should().BeEmpty();
        steps.Select(s => (s.Sequence, s.Name, s.Code)).Should().Equal((1, "Corte", "C"), (2, "Montagem", "M"));
        steps[1].Checklist.Should().ContainSingle().Which.Should().Be(new BemAqui.Engineering.Contracts.RoutingChecklistItem(1, "Fixar guias"), "linhas vazias saem e o checklist é numerado");
    }

    [Fact]
    public void Letra_repetida_ou_fora_de_A_a_Z_e_primeira_de_qualidade_dao_erro_na_linha()
    {
        var (_, errors) = RoutingRules.Validate("Cabine", null, [Step("Qualidade", "Q", quality: true), Step("Corte", "Ç"), Step("Quadro", "q"), Step("Inspeção", "I", quality: true), Step("Final", "F", quality: true)]);
        errors["steps[0].isQualityStep"].Should().Equal("A primeira etapa não pode ser de qualidade.");
        errors["steps[1].code"].Should().Equal("Informe a letra da etapa, de A a Z.");
        errors["steps[2].code"].Should().Equal("A letra Q já está na etapa 1.");
        errors["steps[4].isQualityStep"].Should().Equal("Duas etapas de qualidade não podem ficar seguidas.");
        errors.Should().NotContainKey("steps[3].isQualityStep");
    }

    [Fact]
    public void Limites_de_etapas_checklist_nome_e_horas()
    {
        var thirty = Enumerable.Range(0, 30).Select(i => Step("Etapa " + i, ((char)('A' + (i % 26))).ToString())).ToList();
        RoutingRules.Validate("X", null, thirty).Errors.Keys.Should().OnlyContain(k => k.EndsWith(".code", StringComparison.Ordinal), "30 etapas cabem; só as letras repetem");
        RoutingRules.Validate("X", null, [.. thirty, Step("Mais", "Z")]).Errors["steps"].Should().Equal("Inclua no máximo 30 etapas.");
        RoutingRules.Validate("X", null, []).Errors["steps"].Should().Equal("Inclua ao menos uma etapa.");
        RoutingRules.Validate(new string('a', 101), new string('b', 61), [Step("Corte", "C")]).Errors.Keys.Should().BeEquivalentTo(["name", "productType"]);
        RoutingRules.Validate("X", null, [Step("Corte", "C", checklist: Enumerable.Range(0, 31).Select(i => "Item " + i).ToArray())]).Errors.Should().ContainKey("steps[0].checklist");
        RoutingRules.Validate("X", null, [Step("Corte", "C", checklist: [new string('x', 121)])]).Errors.Should().ContainKey("steps[0].checklist[0]");
        RoutingRules.Validate("X", null, [Step("Corte", "C", hours: -1m)]).Errors.Should().ContainKey("steps[0].standardHours");
        RoutingRules.Validate("X", null, [Step(new string('n', 61), "C")]).Errors.Should().ContainKey("steps[0].name");
    }

    [Theory]
    [InlineData("Expedição", "E")]
    [InlineData("ótima montagem", "O")]
    [InlineData("  corte", "C")]
    [InlineData("3D", "D")]
    [InlineData("123", null)]
    public void Letra_sugerida_pela_inicial_sem_acento(string name, string? letter) => RoutingRules.SuggestLetter(name).Should().Be(letter);

    [Fact]
    public void Centro_obrigatorio_em_cada_etapa()
    {
        var (steps, errors) = RoutingRules.Validate("X", null, [new RoutingStepInput(null, "Corte", "C", null, 0, false, false, [])]);
        RoutingRules.RequireWorkCenters(steps, errors);
        errors["steps[0].workCenterId"].Should().Equal("Escolha o centro de trabalho da etapa.");
    }
}