using BemAqui.Commercial.Application;
using BemAqui.Commercial.Contracts;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>EXE-02 CA 3 e 5: regras do modelo de etapas sem banco, com o erro na linha certa.</summary>
public class StageTemplateRulesTests
{
    private static StageStepInput Step(string code, StageRole role, decimal hours = 1m, string? name = null) =>
        new(null, name ?? "Etapa " + code, code, role, hours, null, false, []);

    private static Dictionary<string, string[]> Errors(IReadOnlyList<StageStepInput> steps, IReadOnlyList<SuggestedMilestoneInput>? milestones = null) =>
        StageTemplateRules.Validate(new StageTemplateInput("Modelo", null, steps, milestones)).Errors;

    private static readonly StageStepInput[] Valid =
        [Step("K", StageRole.Execution), Step("L", StageRole.Execution), Step("G", StageRole.Delivery), Step("A", StageRole.Acceptance)];

    [Fact]
    public void Modelo_valido_passa_e_normaliza_letra_horas_e_checklist()
    {
        var (steps, milestones, errors) = StageTemplateRules.Validate(new StageTemplateInput(" Implantação ", null,
            [new StageStepInput(null, " Kickoff ", "k", StageRole.Execution, 1.23456m, null, true, ["  Item  ", " ", "Outro"]), Step("G", StageRole.Delivery)],
            [new SuggestedMilestoneInput(50m, SuggestedMilestoneTrigger.Signature, "Z", 0), new SuggestedMilestoneInput(50m, SuggestedMilestoneTrigger.StageCompleted, "k", 5)]));
        errors.Should().BeEmpty();
        steps[0].Code.Should().Be("K");
        steps[0].Name.Should().Be("Kickoff");
        steps[0].StandardHoursPerUnit.Should().Be(1.2346m);
        steps[0].Checklist.Should().Equal("Item", "Outro");
        steps.Select(s => s.Sequence).Should().Equal(1, 2);
        milestones[0].StageCode.Should().BeNull("letra só vale no gatilho por etapa");
        milestones[1].StageCode.Should().Be("K");
    }

    [Fact]
    public void Sem_execucao_recusa_no_campo_das_etapas()
    {
        Errors([Step("G", StageRole.Delivery)]).Should().ContainKey("steps");
        Errors([]).Should().ContainKey("steps");
    }

    [Fact]
    public void Duas_entregas_ou_dois_aceites_recusam_na_linha_repetida()
    {
        Errors([Step("A", StageRole.Execution), Step("B", StageRole.Delivery), Step("C", StageRole.Delivery)]).Should().ContainKey("steps[2].role");
        Errors([Step("A", StageRole.Execution), Step("B", StageRole.Delivery), Step("C", StageRole.Acceptance), Step("D", StageRole.Acceptance)])
            .Should().ContainKey("steps[3].role");
    }

    [Fact]
    public void Aceite_exige_entrega_antes_e_e_a_ultima()
    {
        Errors([Step("A", StageRole.Execution), Step("C", StageRole.Acceptance)])["steps[1].role"][0].Should().Contain("depois de uma etapa de Entrega");
        Errors([Step("A", StageRole.Execution), Step("C", StageRole.Acceptance), Step("D", StageRole.Delivery)])["steps[1].role"][0].Should().Contain("depois de uma etapa de Entrega");
        // Aceite antes de outra etapa: Execução depois da Entrega também é apontada na linha dela.
        var errors = Errors([Step("A", StageRole.Execution), Step("B", StageRole.Delivery), Step("C", StageRole.Acceptance), Step("D", StageRole.Execution)]);
        errors["steps[2].role"][0].Should().Contain("última");
        errors.Should().ContainKey("steps[3].role");
    }

    [Fact]
    public void Execucao_depois_da_entrega_recusa_na_linha()
    {
        Errors([Step("A", StageRole.Execution), Step("B", StageRole.Delivery), Step("C", StageRole.Execution)])["steps[2].role"][0]
            .Should().Be("Depois da Entrega só pode vir o Aceite do cliente.");
        Errors([Step("A", StageRole.Execution), Step("B", StageRole.Delivery)]).Should().BeEmpty("Aceite é opcional");
    }

    [Fact]
    public void Letras_unicas_de_A_a_Z_horas_nao_negativas_e_limites_do_checklist()
    {
        var errors = Errors([Step("A", StageRole.Execution), Step("a", StageRole.Execution), Step("1", StageRole.Execution), Step("B", StageRole.Execution, -1m),
            new StageStepInput(null, "Longa", "C", StageRole.Execution, 0m, null, false, [new string('x', 201)]),
            new StageStepInput(null, "Muitos", "D", StageRole.Execution, 0m, null, false, Enumerable.Range(1, 31).Select(i => "Item " + i).ToList())]);
        errors["steps[1].code"][0].Should().Contain("já está na etapa 1");
        errors.Should().ContainKeys("steps[2].code", "steps[3].standardHoursPerUnit", "steps[4].checklist[0]", "steps[5].checklist");
    }

    [Fact]
    public void Marcos_sugeridos_somam_100_e_o_gatilho_por_etapa_so_aponta_para_execucao()
    {
        Errors(Valid, [new(50m, SuggestedMilestoneTrigger.Signature, null, 0), new(40m, SuggestedMilestoneTrigger.TechnicalAcceptance, null, 0)])
            .Should().ContainKey("suggestedMilestones");
        Errors(Valid, [new(50m, SuggestedMilestoneTrigger.Signature, null, 0), new(50m, SuggestedMilestoneTrigger.StageCompleted, "G", 0)])
            .Should().ContainKey("suggestedMilestones[1].stageCode", "a Entrega usa o gatilho próprio");
        Errors(Valid, [new(50m, SuggestedMilestoneTrigger.Signature, null, 0), new(50m, SuggestedMilestoneTrigger.StageCompleted, "Z", 0)])
            .Should().ContainKey("suggestedMilestones[1].stageCode");
        Errors(Valid, [new(100m, SuggestedMilestoneTrigger.Delivered, null, 400)]).Should().ContainKey("suggestedMilestones[0].daysAfterEvent");
        Errors(Valid, [new(0m, SuggestedMilestoneTrigger.Signature, null, 0), new(100m, SuggestedMilestoneTrigger.Delivered, null, 0)])
            .Should().ContainKey("suggestedMilestones[0].percent");
        Errors(Valid, []).Should().BeEmpty("sem marco sugerido é aceito");
    }

    [Fact]
    public void Marco_no_aceite_exige_a_etapa_de_aceite()
    {
        Errors([Step("A", StageRole.Execution), Step("B", StageRole.Delivery)], [new(100m, SuggestedMilestoneTrigger.TechnicalAcceptance, null, 0)])
            .Should().ContainKey("suggestedMilestones[0].trigger");
    }

    [Fact]
    public void Os_quatro_modelos_do_pacote_passam_nas_regras()
    {
        foreach (var seed in StageTemplateService.Seeds)
        {
            var input = new StageTemplateInput(seed.Name, null,
                seed.Steps.Select(s => new StageStepInput(null, s.Name, s.Code, s.Role, s.Hours, null, false, s.Checklist)).ToList(),
                seed.Milestones.Select(m => new SuggestedMilestoneInput(m.Percent, m.Trigger, m.StageCode, m.DaysAfterEvent)).ToList());
            StageTemplateRules.Validate(input).Errors.Should().BeEmpty(seed.Name);
        }
    }

    [Fact]
    public void Segmento_fora_do_perfil_servicos_recusa()
    {
        StageTemplateRules.Validate(new StageTemplateInput("Modelo", "bakery", Valid, null)).Errors.Should().ContainKey("segment");
        StageTemplateRules.Validate(new StageTemplateInput("Modelo", "workshop", Valid, null)).Errors.Should().BeEmpty();
    }
}
