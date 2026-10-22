using BemAqui.BuildingBlocks.Authorization;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>Regra do Proprietário único (PRD-03a CA 21, ADR 0023 §5): sem outro aprovador elegível, a autoaprovação passa.</summary>
public class SoleOwnerRuleTests
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid Other = Guid.CreateVersion7();
    private static readonly Guid Third = Guid.CreateVersion7();

    [Fact]
    public void So_passa_quando_nenhum_outro_aprovador_fica_fora_dos_envolvidos()
    {
        SoleOwnerRule.NoOtherEligible([Me], Me, [Me]).Should().BeTrue("o Proprietário único");
        SoleOwnerRule.NoOtherEligible([], Me, [Me]).Should().BeTrue();
        SoleOwnerRule.NoOtherEligible([Me, Other], Me, [Me]).Should().BeFalse("outro aprovador elegível");
        SoleOwnerRule.NoOtherEligible([Me, Other], Me, [Me, Other]).Should().BeTrue("o outro também apontou na etapa");
        SoleOwnerRule.NoOtherEligible([Me, Other, Third], Me, [Me, Other]).Should().BeFalse();
        SoleOwnerRule.SelfApprovedLabel.Should().Be("Autoaprovado");
    }
}