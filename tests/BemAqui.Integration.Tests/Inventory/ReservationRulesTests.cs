using BemAqui.Inventory.Application;
using BemAqui.Inventory.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>
/// EST-02, regras sem banco: alvo e falta por local (CA 1, 3 a 6), revisão sem descer abaixo do baixado (CA 8), ordem das faltas ao
/// completar (CA 11), convergência pela fonte em qualquer ordem de eventos (CA 2) e a mensagem do 409 com reservado (CA 13).
/// </summary>
public class ReservationRulesTests
{
    private static readonly Guid Alm = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private static ReservationPlan Plan(decimal target, IReadOnlyList<ReservationSlot> current, params (Guid Location, decimal Available)[] stock) =>
        ReservationPlanner.Plan(target, current, stock.Select(s => s.Location).ToList(), stock.ToDictionary(s => s.Location, s => s.Available), Alm);

    private static ReservationSlot Slot(ReservationPlan plan, Guid location) => plan.Slots.Single(s => s.LocationId == location);

    [Fact]
    public void Alvo_coberto_reserva_tudo_no_local_padrao()
    {
        var plan = Plan(10m, [], (Alm, 10m));
        Slot(plan, Alm).Should().Be(new ReservationSlot(Alm, 10m, 10m, 0m), "CA 3: requested 10 e quantity 10");
        plan.Shortage.Should().Be(0m);
    }

    [Fact]
    public void Disponivel_menor_reserva_o_possivel_e_registra_a_falta()
    {
        var plan = Plan(10m, [], (Alm, 6m));
        Slot(plan, Alm).Should().Be(new ReservationSlot(Alm, 10m, 6m, 0m), "CA 4: falta 4 = requested − quantity");
        plan.Shortage.Should().Be(4m);
    }

    [Fact]
    public void Sem_saldo_a_falta_existe_mesmo_assim()
    {
        var plan = Plan(10m, []);
        Slot(plan, Alm).Should().Be(new ReservationSlot(Alm, 10m, 0m, 0m), "CA 5");
        plan.Shortage.Should().Be(10m);
    }

    [Fact]
    public void Varios_locais_reserva_primeiro_no_padrao_e_depois_na_ordem_uma_por_local()
    {
        var plan = Plan(10m, [], (Alm, 4m), (B, 3m), (C, 5m));
        plan.Slots.Select(s => (s.LocationId, s.Quantity)).Should().Equal((Alm, 4m), (B, 3m), (C, 3m));
        plan.Shortage.Should().Be(0m, "CA 6");
        var short_ = Plan(10m, [], (Alm, 1m), (B, 2m));
        Slot(short_, Alm).Requested.Should().Be(8m, "a falta fica no local padrão: 1 reservado + 7 de falta");
        Slot(short_, B).Should().Be(new ReservationSlot(B, 2m, 2m, 0m));
    }

    [Fact]
    public void Revisao_que_aumenta_tenta_reservar_mais_e_a_que_diminui_libera()
    {
        var first = Plan(10m, [], (Alm, 20m));
        var up = Plan(14m, first.Slots, (Alm, 2m));
        Slot(up, Alm).Should().Be(new ReservationSlot(Alm, 14m, 12m, 0m), "CA 8: mais 4 pedidos, 2 possíveis, falta 2");
        up.Shortage.Should().Be(2m);
        var down = Plan(7m, up.Slots, (Alm, 0m));
        Slot(down, Alm).Should().Be(new ReservationSlot(Alm, 7m, 7m, 0m), "libera o excedente e a falta zera");
        Plan(0m, down.Slots).Slots.Single().Should().Be(new ReservationSlot(Alm, 0m, 0m, 0m), "linha removida libera tudo");
    }

    [Fact]
    public void Revisao_abaixo_do_baixado_nao_desce_abaixo_do_baixado()
    {
        ReservationSlot[] current = [new(Alm, 10m, 4m, 6m)];
        var plan = Plan(5m, current);
        Slot(plan, Alm).Should().Be(new ReservationSlot(Alm, 6m, 0m, 6m), "CA 8: nunca abaixo do baixado");
        plan.OverIssued.Should().Be(1m, "Baixado acima da BOM: 1");
    }

    [Fact]
    public void Liberacao_tira_primeiro_dos_locais_de_menor_prioridade()
    {
        ReservationSlot[] current = [new(Alm, 4m, 4m, 0m), new(B, 3m, 3m, 0m), new(C, 3m, 3m, 0m)];
        var plan = ReservationPlanner.Plan(5m, current, [Alm, B, C], new Dictionary<Guid, decimal>(), Alm);
        plan.Slots.Select(s => (s.LocationId, s.Quantity)).Should().Equal((Alm, 4m), (B, 1m), (C, 0m));
    }

    [Fact]
    public void Completar_a_falta_com_material_que_chega_em_outro_local_move_a_falta()
    {
        ReservationSlot[] current = [new(Alm, 10m, 6m, 0m)];
        var plan = ReservationPlanner.Plan(10m, current, [C], new Dictionary<Guid, decimal> { [C] = 3m }, Alm);
        Slot(plan, Alm).Should().Be(new ReservationSlot(Alm, 7m, 6m, 0m), "CA 11: a falta que sobra continua no padrão");
        Slot(plan, C).Should().Be(new ReservationSlot(C, 3m, 3m, 0m));
        plan.Shortage.Should().Be(1m);
    }

    [Fact]
    public void Ordem_das_faltas_primeiro_a_OS_de_origem_depois_a_BOM_mais_antiga()
    {
        var older = Guid.CreateVersion7();
        var newer = Guid.CreateVersion7();
        var origin = Guid.CreateVersion7();
        var noDate = Guid.CreateVersion7();
        var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        ReservationPlanner.CompletionOrder([(newer, at.AddDays(2)), (noDate, null), (older, at), (origin, at.AddDays(5))], origin)
            .Should().Equal(origin, older, newer, noDate);
        ReservationPlanner.CompletionOrder([(newer, at.AddDays(2)), (older, at)], null).Should().Equal([older, newer], "sem OS de origem, a BOM liberada há mais tempo");
    }

    [Fact]
    public void Alvo_soma_as_linhas_do_item_e_zera_com_a_OS_fechada()
    {
        var item = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var lines = new[] { (item, 2.5m), (other, 1m), (item, 1.25m) };
        ReservationPlanner.Targets(lines, orderClosed: false).Should().BeEquivalentTo(new Dictionary<Guid, decimal> { [item] = 3.75m, [other] = 1m });
        ReservationPlanner.Targets(lines, orderClosed: true).Values.Should().AllBeEquivalentTo(0m, "CA 1: OS cancelada ou concluída tem alvo 0");
    }

    /// <summary>
    /// CA 2: o resultado depende só do alvo da fonte. Qualquer sequência de reconciliações (BomReleased, BomRevised, Cancelled, Completed
    /// em todas as ordens, com duplicadas), cada uma lendo a fonte final, termina no mesmo estado de uma reconciliação só.
    /// </summary>
    [Theory]
    [InlineData(14, false)]
    [InlineData(14, true)]
    [InlineData(7, false)]
    public void Qualquer_ordem_de_eventos_termina_no_alvo_da_fonte(int revisedTarget, bool cancelled)
    {
        decimal finalTarget = cancelled ? 0m : revisedTarget;
        var expected = Plan(finalTarget, [], (Alm, 9m), (B, 3m)).Slots.Where(s => s.Requested > 0 || s.Quantity > 0).ToList();
        string[] events = ["BomReleased", "BomRevised", "ProductionOrderCancelled", "BomReleased"];
        foreach (var permutation in Permutations(events))
        {
            IReadOnlyList<ReservationSlot> state = [];
            var stock = new Dictionary<Guid, decimal> { [Alm] = 9m, [B] = 3m };
            foreach (var _ in permutation)
            {
                // A reconciliação ignora o evento e lê a fonte: o alvo é sempre o da revisão vigente (ou 0 se cancelada).
                var reservedBefore = state.ToDictionary(s => s.LocationId, s => s.Quantity);
                var available = stock.ToDictionary(kv => kv.Key, kv => kv.Value - reservedBefore.GetValueOrDefault(kv.Key));
                state = ReservationPlanner.Plan(finalTarget, state, [Alm, B], available, Alm).Slots;
            }

            state.Where(s => s.Requested > 0 || s.Quantity > 0).Should().BeEquivalentTo(expected, string.Join(" → ", permutation));
        }
    }

    [Fact]
    public void Mensagem_do_409_mostra_o_disponivel_e_o_reservado()
    {
        StockLedger.Reserved(6m, 4m, "UN").Message.Should().Be("Há 6,0000 UN disponível; 4,0000 UN está reservado para OS.");
        StockLedger.Reserved(6m, 4m, "UN").Code.Should().Be(StockLedger.InsufficientStockCode);
    }

    private static IEnumerable<string[]> Permutations(string[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Length; i++)
        {
            foreach (var rest in Permutations([.. items[..i], .. items[(i + 1)..]]))
            {
                yield return [items[i], .. rest];
            }
        }
    }
}
