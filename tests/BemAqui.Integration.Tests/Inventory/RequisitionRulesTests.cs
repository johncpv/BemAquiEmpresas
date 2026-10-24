using BemAqui.Inventory.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>
/// EST-03, regras sem banco: sugestão de lote por validade e entrada (CA 1, ponto 56), divisão em lotes, excedente sobre a reserva e
/// material extra (CA 3, ponto 55), regra de valor da aprovação (CA 4), o que ainda volta ao estoque (CA 12) e os limites do estorno
/// pelo autor (CA 14).
/// </summary>
public class RequisitionRulesTests
{
    private static readonly DateTimeOffset Jan = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    private static LotStock Lot(string number, DateOnly? expiry, DateTimeOffset? receivedAt, decimal onHand) => new(Guid.NewGuid(), number, expiry, receivedAt, onHand);

    [Fact]
    public void Sugere_a_validade_mais_proxima_e_sem_validade_por_ultimo()
    {
        var lots = new[]
        {
            Lot("L-SEM", null, Jan.AddDays(-90), 5m),
            Lot("L-DEZ", new DateOnly(2026, 12, 1), Jan, 5m),
            Lot("L-NOV", new DateOnly(2026, 11, 1), Jan.AddDays(5), 5m),
        };

        RequisitionRules.SuggestionOrder(lots).Select(l => l.LotNumber).Should().Equal("L-NOV", "L-DEZ", "L-SEM");
    }

    [Fact]
    public void Empatado_ou_sem_validade_vai_o_mais_antigo_pela_entrada_e_lote_vazio_fica_fora()
    {
        var expiry = new DateOnly(2026, 11, 1);
        var lots = new[]
        {
            Lot("L-NOVO", expiry, Jan.AddDays(10), 5m),
            Lot("L-VELHO", expiry, Jan, 5m),
            Lot("L-ZERADO", expiry, Jan.AddDays(-30), 0m),
            Lot("S-2", null, Jan.AddDays(3), 1m),
            Lot("S-1", null, Jan.AddDays(1), 1m),
        };

        RequisitionRules.SuggestionOrder(lots).Select(l => l.LotNumber).Should().Equal("L-VELHO", "L-NOVO", "S-1", "S-2");
    }

    [Fact]
    public void Divide_a_linha_nos_lotes_quando_o_sugerido_nao_basta()
    {
        var lots = new[] { Lot("A", new DateOnly(2026, 11, 1), Jan, 3m), Lot("B", new DateOnly(2026, 12, 1), Jan, 10m) };

        var picks = RequisitionRules.SplitByLots(lots, 5m);

        picks.Select(p => (p.LotNumber, p.Quantity, p.Suggested)).Should().Equal(("A", 3m, true), ("B", 2m, false));
        RequisitionRules.SplitByLots(lots, 2m).Should().ContainSingle().Which.Quantity.Should().Be(2m);
        RequisitionRules.SplitByLots(lots, 20m).Sum(p => p.Quantity).Should().Be(13m, "o que não cabe nos lotes fica de fora (o servidor recusa com 409)");
    }

    [Fact]
    public void Excedente_sobre_a_reserva_e_material_extra()
    {
        var reservation = Guid.NewGuid();
        var item = Guid.NewGuid();
        var extra = Guid.NewGuid();
        RequisitionDemand[] lines = [new(reservation, item, 4m), new(reservation, item, 3m), new(null, extra, 2m)];

        var excess = RequisitionRules.Excess(lines, new Dictionary<Guid, decimal> { [reservation] = 6m });

        excess.Select(e => (e.FromReservation, e.Over)).Should().Equal((4m, 0m), (2m, 1m), (0m, 2m));
    }

    [Fact]
    public void Valor_acima_da_BOM_pelo_custo_medio_e_aprovacao_acima_do_limite()
    {
        var reservation = Guid.NewGuid();
        var item = Guid.NewGuid();
        var extra = Guid.NewGuid();
        RequisitionDemand[] lines = [new(reservation, item, 10m), new(null, extra, 1m)];
        var excess = RequisitionRules.Excess(lines, new Dictionary<Guid, decimal> { [reservation] = 8m });

        var value = RequisitionRules.OverBomValue(lines, excess, new Dictionary<Guid, decimal> { [item] = 450.555m, [extra] = 99.99m });

        value.Should().Be(1001.10m, "2 × 450,555 = 901,11 mais 1 × 99,99");
        RequisitionRules.NeedsApproval(1000m, 1000m).Should().BeFalse("até R$ 1.000,00 baixa direto");
        RequisitionRules.NeedsApproval(1000.01m, 1000m).Should().BeTrue();
        RequisitionRules.OverReasonText("perda no corte").Should().Be("Consumo acima da BOM: perda no corte");
    }

    [Fact]
    public void Devolve_ate_o_baixado_e_nao_devolvido()
    {
        RequisitionRules.Returnable(10m, 3m, 2m).Should().Be(5m);
        RequisitionRules.Returnable(10m, 8m, 4m).Should().Be(0m, "nunca negativo");
    }

    [Fact]
    public void Autor_estorna_ate_o_limite_e_em_24_horas()
    {
        var now = Jan.AddHours(30);
        RequisitionRules.AuthorMayReverse(800m, Jan.AddHours(10), now, 1000m).Should().BeTrue();
        RequisitionRules.AuthorMayReverse(1200m, Jan.AddHours(10), now, 1000m).Should().BeFalse("acima do limite");
        RequisitionRules.AuthorMayReverse(800m, Jan, now, 1000m).Should().BeFalse("depois de 24 h");
    }
}
