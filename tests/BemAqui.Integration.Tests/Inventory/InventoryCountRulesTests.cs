using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>
/// EST-05, regras sem banco: a diferença com os movimentos durante a contagem (CA 5), os não contados (CA 9), a recontagem por outra
/// pessoa (CA 8), os envolvidos no ajuste (CA 11), a contagem abaixo do reservado (decisão do PO de 23/10/2026), o número INV (D15) e o
/// DTO da contagem cega sem saldo nem custo (CA 15).
/// </summary>
public class InventoryCountRulesTests
{
    private static readonly Guid A = Guid.CreateVersion7();

    [Fact]
    public void Diferenca_desconta_os_movimentos_entre_a_foto_e_a_contagem()
    {
        // Foto 10; saíram 3 antes de contar (sistema na contagem 7); contado 6: falta 1, não 4.
        var result = InventoryCountRules.Resolve(10m, 6m, 7m, 2m, UncountedPolicy.KeepSystem);
        result.MovementDelta.Should().Be(-3m, "CA 5: movimento do período");
        result.Difference.Should().Be(-1m, "contado − (foto + movimentos)");
        result.Counted.Should().Be(6m);
        InventoryCountRules.Resolve(10m, 10m, 10m, 25m, UncountedPolicy.Zero).Difference.Should().Be(0m, "movimento depois da contagem da linha não conta");
        InventoryCountRules.Difference(12.00005m, 10m, 0m).Should().Be(2.0001m, "4 casas AwayFromZero");
        InventoryCountRules.MovementDelta(5m, 8m).Should().Be(3m);
    }

    [Fact]
    public void Nao_contados_viram_zero_ou_mantem_o_saldo()
    {
        var zero = InventoryCountRules.Resolve(10m, null, null, 8m, UncountedPolicy.Zero);
        zero.Counted.Should().Be(0m);
        zero.Difference.Should().Be(-8m, "CA 9: zero sobre o saldo de agora (foto 10 − 2 que saíram)");
        zero.MovementDelta.Should().Be(-2m);
        var keep = InventoryCountRules.Resolve(10m, null, null, 8m, UncountedPolicy.KeepSystem);
        keep.Counted.Should().BeNull();
        keep.Difference.Should().Be(0m, "manter o saldo do sistema não gera diferença");
        InventoryCountRules.UncountedQuestion(3).Should().Be("Considerar 3 itens não contados como zero?");
        InventoryCountRules.UncountedQuestion(1).Should().Be("Considerar 1 item não contado como zero?");
        InventoryCountRules.PolicyLabel(UncountedPolicy.KeepSystem).Should().Be("Manter o saldo do sistema");
    }

    [Fact]
    public void Recontagem_so_por_outra_pessoa()
    {
        var first = Guid.CreateVersion7();
        InventoryCountRules.MayRecount(first, first).Should().BeFalse("CA 8");
        InventoryCountRules.MayRecount(first, Guid.CreateVersion7()).Should().BeTrue();
        InventoryCountRules.MayRecount(null, first).Should().BeTrue();
        InventoryCountRules.RecountByOtherMessage.Should().Be("A recontagem precisa ser feita por outra pessoa.");
    }

    [Fact]
    public void Quem_abriu_ou_contou_nao_aprova_salvo_o_Proprietario_unico()
    {
        var opener = Guid.CreateVersion7();
        var counter = Guid.CreateVersion7();
        var approver = Guid.CreateVersion7();
        var involved = new[] { opener, counter };
        StockAdjustmentRules.MayDecide(involved, counter, decidingAlone: false).Should().BeFalse("CA 11");
        StockAdjustmentRules.MayDecide(involved, opener, decidingAlone: true).Should().BeTrue("Proprietário único: Autoaprovado");
        StockAdjustmentRules.MayDecide(involved, approver, decidingAlone: false).Should().BeTrue();
        SoleOwnerRule.NoOtherEligible([counter, approver], counter, involved).Should().BeFalse("há outro aprovador fora do inventário");
        SoleOwnerRule.NoOtherEligible([counter, opener], counter, involved).Should().BeTrue("os dois aprovadores contaram ou abriram");
    }

    [Fact]
    public void Contagem_abaixo_do_reservado_nao_e_recusa_no_ajuste_do_inventario()
    {
        var line = new[] { new AdjustmentLineInput(A, null, null, -6m) };
        var balance = new[] { new AdjustmentBalance(10m, 7m, 10m) };
        StockAdjustmentRules.Evaluate(StockAdjustmentMode.Delta, line, balance, ["UN"]).Refusal.Should().NotBeNull("o ajuste manual continua recusado");
        StockAdjustmentRules.Evaluate(StockAdjustmentMode.Delta, line, balance, ["UN"], releasesReservations: true).Refusal.Should().BeNull("a aprovação reduz as reservas antes");
        StockAdjustmentRules.Evaluate(StockAdjustmentMode.Delta, [new AdjustmentLineInput(A, null, null, -11m)], balance, ["UN"], releasesReservations: true).Refusal!.Message
            .Should().Contain("abaixo de zero", "o lote negativo continua recusado");
        StockAdjustmentRules.ReservationExcess(4m, 7m).Should().Be(3m);
        StockAdjustmentRules.ReservationExcess(8m, 7m).Should().Be(0m);
        StockAdjustmentRules.ReservationExcess(-1m, 7m).Should().Be(7m, "nunca reduz mais que o reservado");
    }

    [Fact]
    public void Reduz_primeiro_a_BOM_liberada_mais_recentemente()
    {
        var old = Guid.CreateVersion7();
        var recent = Guid.CreateVersion7();
        var undated = Guid.CreateVersion7();
        var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        ReservationPlanner.ReductionOrder([(old, at), (undated, null), (recent, at.AddDays(5))]).Should().Equal(recent, old, undated);
    }

    [Fact]
    public void Numero_INV_textos_e_ordem_por_valor()
    {
        NumberFormats.Defaults[InventoryCountRules.DocType].Format(2026, 1).Should().Be("INV-2026-0001", "D15");
        NumberFormats.Defaults.Values.Select(f => f.Prefix).Should().OnlyHaveUniqueItems();
        InventoryCountRules.Progress(3, 10).Should().Be("3 de 10 itens contados");
        InventoryCountRules.StatusLabel(InventoryCountStatus.Counting).Should().Be("Em contagem");
        var ordered = InventoryCountRules.ByValue([(Seq: 1, Value: 10m, Qty: 1m), (Seq: 2, Value: -50m, Qty: -2m), (Seq: 3, Value: 10m, Qty: 3m)], x => x.Value, x => x.Qty, x => x.Seq);
        ordered.Select(x => x.Seq).Should().Equal(2, 3, 1);
    }

    [Fact]
    public void Contagem_cega_sem_saldo_movimento_diferenca_nem_custo()
    {
        string[] forbidden = ["SystemQty", "SystemQtyAtCount", "MovementDelta", "Difference", "UnitCost", "AverageCost", "Value", "Cost"];
        foreach (var type in new[] { typeof(InventoryCountingLine), typeof(InventoryCountingView), typeof(InventoryCountDetail), typeof(InventoryCountSummary) })
        {
            type.GetProperties().Select(p => p.Name).Should().NotContain(n => forbidden.Any(f => n.Contains(f, StringComparison.Ordinal)), $"CA 15: {type.Name} vai para quem só conta");
        }
    }
}
