using BemAqui.Commercial.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Application;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PRD-06a2, regras sem banco: custo apurado e unitário da OS (CA 3), série no formato D15 e a conferência das séries editadas (CA 4), a
/// perna da entrada na chave de idempotência (CA 13 e reconclusão) e o texto da linha do tempo (CA 15).
/// </summary>
public class FinishedGoodsRulesTests
{
    [Fact]
    public void Custo_apurado_soma_horas_e_material_e_o_unitario_tem_6_casas()
    {
        var cost = CompletionRules.Cost(labor: 1200.50m, material: 3800.25m, quantity: 3m);
        cost.Total.Should().Be(5000.75m, "CA 3: horas + material baixado − devolvido");
        cost.Unit.Should().Be(1666.916667m, "CA 3: total ÷ quantidade, 6 casas AwayFromZero");
        CompletionRules.Cost(0m, 0m, 2m).Should().Be(new OrderCost(0m, 0m, 0m, 0m), "CA 7: custo zerado");
        CompletionRules.Cost(10m, -2m, 1m).Total.Should().Be(8m, "devolução maior que a baixa num item reduz o material");
        var act = () => CompletionRules.Cost(1m, 1m, 0m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Serie_segue_o_formato_D15_com_5_digitos()
    {
        SerialRules.Format("ELV-0001", 2026, 1).Should().Be("ELV-0001-2026-00001", "CA 4: {código}-{AAAA}-{seq}");
        SerialRules.Format(" PA-7 ", 2027, 123456).Should().Be("PA-7-2027-123456", "acima de 5 dígitos a sequência continua");
        SerialRules.Units(3m).Should().Be(3);
        SerialRules.Units(2.5m).Should().BeNull("série é por unidade inteira");
        SerialRules.Units(0m).Should().BeNull();
    }

    [Fact]
    public void Series_editadas_sao_uma_por_unidade_sem_vazio_e_sem_repetir()
    {
        SerialRules.Invalid(["A-1", "A-2"], 2).Should().BeNull();
        SerialRules.Invalid(["A-1"], 2).Should().Be("Informe 2 números de série, um por unidade.");
        SerialRules.Invalid(["A-1", " "], 2).Should().Be("Número de série vazio.");
        SerialRules.Invalid(["A-1", "A-1 "], 2).Should().Be("O número de série A-1 se repete.", "CA 4: único na lista");
        SerialRules.Invalid([new string('x', 61)], 1).Should().Be("Número de série com até 60 caracteres.");
    }

    [Fact]
    public void Perna_da_entrada_so_avanca_depois_do_estorno()
    {
        CompletionRules.EntryLeg(null, false).Should().Be(1, "primeira conclusão: (ProductionOrder, osId, 1, Production)");
        CompletionRules.EntryLeg(1, false).Should().Be(1, "sem estorno, a mesma perna: a segunda chamada devolve o mesmo movimento (CA 13)");
        CompletionRules.EntryLeg(1, true).Should().Be(2, "reconclusão depois do estorno lança uma perna nova");
    }

    [Fact]
    public void Linha_do_tempo_diz_as_unidades_as_series_e_o_custo_so_com_permissao()
    {
        var entry = new FinishedGoodsEntry(Guid.NewGuid(), "MOV-2026-000010", Guid.NewGuid(), Guid.NewGuid(), 2m, 2500.5m, 5001m, DateTimeOffset.UtcNow, false, 1);
        ProjectService.FinishedGoodsText(entry, ["ELV-2026-00001", "ELV-2026-00002"], viewCost: false)
            .Should().Be(": 2 unidades no estoque (ELV-2026-00001, ELV-2026-00002)", "CA 15: sem projects.view_cost, sem custo");
        ProjectService.FinishedGoodsText(entry, [], viewCost: true).Should().Be(": 2 unidades no estoque · custo unitário R$ 2.500,50");
        ProjectService.FinishedGoodsText(entry with { Quantity = 1m }, [], viewCost: false).Should().Be(": 1 unidade no estoque");
        var many = Enumerable.Range(1, 7).Select(i => $"S-{i}").ToList();
        ProjectService.FinishedGoodsText(entry, many, viewCost: false).Should().EndWith("(S-1, S-2, S-3, S-4, S-5 e mais 2)");
    }

    [Fact]
    public void Textos_da_conclusao_com_estoque()
    {
        CompletionRules.ProductRequired.Should().Be("Escolha o item de estoque do produto antes de concluir.", "CA 1");
        CompletionRules.ZeroCostWarning.Should().Be("Custo da OS zerado: o produto entra no estoque com custo 0.", "CA 7");
        CompletionRules.CompletedMessage.Should().Be("OS concluída. Produto acabado no estoque.", "CA 5");
        CompletionRules.CompletedBeforeStock.Should().Be("Concluída antes do controle de estoque: o produto não entrou no estoque.", "CA 9");
        CompletionRules.ProductShipped.Should().Be("O produto já foi expedido.", "CA 10");
    }
}
