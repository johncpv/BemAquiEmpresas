using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>PNL-01b: regras puras do Painel da TV (nome curto, operador pelo primeiro nome e inicial, ordem dos cartões e dos atrasos).</summary>
public class KioskPanelRulesTests
{
    [Theory]
    [InlineData("Residencial Aurora", "Residencial Aurora")]
    [InlineData("  Torre Norte  ", "Torre Norte")]
    [InlineData("Edifício Residencial Jardim das Palmeiras", "Edifício Residencial Jardim…")]
    [InlineData(null, "")]
    public void Nome_curto_do_projeto_corta_em_28_caracteres(string? name, string expected)
    {
        var shortName = KioskPanelRules.ShortName(name);

        shortName.Should().Be(expected);
        shortName.Length.Should().BeLessThanOrEqualTo(KioskPanelRules.ProjectNameLength);
    }

    [Theory]
    [InlineData("João Carlos da Silva", "João S.")]
    [InlineData("maria souza", "maria S.")]
    [InlineData("Ana", "Ana")]
    [InlineData("  ", "Operador")]
    public void Operador_pelo_primeiro_nome_e_inicial_do_sobrenome(string name, string expected) =>
        KioskPanelRules.FirstNameAndInitial(name).Should().Be(expected);

    [Fact]
    public void Varios_operadores_mostram_o_primeiro_e_mais_n()
    {
        KioskPanelRules.Operators([]).Should().BeNull();
        KioskPanelRules.Operators(["João Silva"]).Should().Be("João S.");
        KioskPanelRules.Operators(["João Silva", "Maria Souza", "Pedro Alves"]).Should().Be("João S. +2");
        KioskPanelRules.Operators(["João Silva", "João Santos"]).Should().Be("João S.", "duas pessoas com o mesmo nome curto aparecem uma vez");
    }

    private static KioskCard Card(string number, DeliveryRisk risk, int? delay = null, DateOnly? end = null) =>
        new(Guid.CreateVersion7(), number, "PRJ-1", "Projeto", "M", "M", "Montagem", null, null, null, 0m, 1m, end, risk, delay);

    [Fact]
    public void Cartoes_atrasados_primeiro_depois_em_risco_depois_pelo_fim_previsto()
    {
        var d = new DateOnly(2026, 11, 10);
        var cards = new[]
        {
            Card("OS-5", DeliveryRisk.None, end: d.AddDays(3)), Card("OS-4", DeliveryRisk.None, end: d), Card("OS-3", DeliveryRisk.AtRisk, end: d.AddDays(9)),
            Card("OS-2", DeliveryRisk.Late, 2), Card("OS-1", DeliveryRisk.Late, 7), Card("OS-6", DeliveryRisk.None),
        };

        KioskPanelRules.Ordered(cards).Select(c => c.Number).Should().Equal("OS-1", "OS-2", "OS-3", "OS-4", "OS-5", "OS-6");
    }

    [Fact]
    public void Dias_do_atraso_e_do_risco()
    {
        var promised = new DateOnly(2026, 11, 10);

        KioskPanelRules.Days(DeliveryRisk.Late, 5, promised, null).Should().Be(5);
        KioskPanelRules.Days(DeliveryRisk.AtRisk, null, promised, promised.AddDays(3)).Should().Be(3, "em risco: quanto a previsão passa do prometido");
        KioskPanelRules.Days(DeliveryRisk.AtRisk, null, promised, null).Should().Be(0);
        KioskPanelRules.Days(DeliveryRisk.None, null, promised, promised).Should().Be(0);
    }

    [Fact]
    public void Atrasos_do_maior_para_o_menor_com_as_atrasadas_antes_das_em_risco()
    {
        KioskDelayRow Row(string number, DeliveryRisk risk, int days) => new(Guid.CreateVersion7(), number, "PRJ-1", "Projeto", "M", "Montagem", null, risk, days, null, null);

        KioskPanelRules.Ordered([Row("OS-3", DeliveryRisk.AtRisk, 9), Row("OS-2", DeliveryRisk.Late, 1), Row("OS-1", DeliveryRisk.Late, 6)])
            .Select(r => r.Number).Should().Equal("OS-1", "OS-2", "OS-3");
    }
}
