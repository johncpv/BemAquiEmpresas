using BemAqui.Host.Persistence;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>EST-02 CA 1 a 12 e 16: reconciliação pela fonte, posição agregada, concorrência, revisão, cancelamento, entrada e recomposição.</summary>
public partial class ReservationTests
{
    [Fact]
    public async Task Alvo_coberto_reserva_na_posicao_na_mesma_transacao()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 10m);
        var order = Order();
        Release(order, 1, (item, 10m));
        await ReconcileAsync(order);

        var reservation = (await ReservationsAsync(order)).Should().ContainSingle().Subject;
        reservation.Status.Should().Be(ReservationState.Active);
        (reservation.RequestedQuantity, reservation.Quantity, reservation.LocationId).Should().Be((10m, 10m, alm), "CA 3");
        (await PositionAsync(item, alm)).Should().Be((10m, 10m), "o reservado da posição sobe 10");
    }

    [Fact]
    public async Task Disponivel_parcial_reserva_o_possivel_e_avisa_a_falta()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 6m);
        var order = Order();
        var bom = Release(order, 1, (item, 10m));
        await ReconcileAsync(order);

        var reservation = (await ReservationsAsync(order)).Single();
        (reservation.RequestedQuantity, reservation.Quantity).Should().Be((10m, 6m), "CA 4: falta 4");
        var events = await ShortageEventsAsync(order);
        events.Should().ContainSingle().Which.Should().Be(new StockShortageChanged(item, order, Orders[order].ProjectId, bom.Lines[0].BomLineId, 4m));
    }

    [Fact]
    public async Task Sem_saldo_a_falta_nasce_mesmo_assim()
    {
        var item = await ItemAsync();
        var order = Order();
        Release(order, 1, (item, 10m));
        await ReconcileAsync(order);

        var reservation = (await ReservationsAsync(order)).Single();
        (reservation.RequestedQuantity, reservation.Quantity, reservation.Shortage).Should().Be((10m, 0m, 10m), "CA 5");
        (await ShortageEventsAsync(order)).Single().ShortQty.Should().Be(10m);
    }

    [Fact]
    public async Task Item_com_lote_reserva_por_item_e_local_sem_lote()
    {
        var (item, alm) = (await ItemAsync(tracksLot: true), await DefaultLocationAsync());
        Guid lot;
        await using (var scope = await ScopeAsync())
        {
            lot = await scope.ServiceProvider.GetRequiredService<IStockMovements>().EnsureLotAsync(item, Unique("LT"));
        }

        await ReceiveAsync(item, alm, 5m, lot: lot);
        var order = Order();
        Release(order, 1, (item, 3m));
        await ReconcileAsync(order);
        (await PositionAsync(item, alm)).Reserved.Should().Be(3m, "CA 3: o lote é escolhido na requisição (EST-03)");
    }

    [Fact]
    public async Task Varios_locais_reserva_primeiro_no_padrao_depois_em_ordem_de_codigo()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        var (first, second) = (await LocationAsync("RA"), await LocationAsync("RB"));
        await ReceiveAsync(item, alm, 4m);
        await ReceiveAsync(item, second, 5m);
        await ReceiveAsync(item, first, 3m);
        var order = Order();
        Release(order, 1, (item, 10m));
        await ReconcileAsync(order);

        var rows = await ReservationsAsync(order);
        rows.ToDictionary(r => r.LocationId, r => r.Quantity).Should().BeEquivalentTo(new Dictionary<Guid, decimal> { [alm] = 4m, [first] = 3m, [second] = 3m }, "CA 6");
        rows.Sum(r => r.Shortage).Should().Be(0m);
    }

    [Fact]
    public async Task Dez_liberacoes_paralelas_nunca_reservam_acima_do_fisico()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 12m);
        var orders = Enumerable.Range(0, 10).Select(_ => Order()).ToList();
        foreach (var order in orders)
        {
            Release(order, 1, (item, 2m));
        }

        await Task.WhenAll(orders.Select(o => Task.Run(() => ReconcileAsync(o))));

        var rows = await WithDbAsync(db => db.Reservations.AsNoTracking().Where(r => r.ItemId == item).ToListAsync());
        rows.Sum(r => r.Quantity).Should().Be(12m, "CA 7: a soma reservada nunca passa do físico");
        rows.Sum(r => r.Shortage).Should().Be(8m, "quem chegou depois reservou o que sobrou, com a falta certa");
        (await PositionAsync(item, alm)).Should().Be((12m, 12m));
    }

    [Fact]
    public async Task Duas_reservas_concorrentes_do_mesmo_item_dividem_o_saldo()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 10m);
        var (a, b) = (Order(), Order());
        Release(a, 1, (item, 10m));
        Release(b, 1, (item, 10m));

        await Task.WhenAll(Task.Run(() => ReconcileAsync(a)), Task.Run(() => ReconcileAsync(b)));

        var rows = await WithDbAsync(db => db.Reservations.AsNoTracking().Where(r => r.ItemId == item).ToListAsync());
        rows.Sum(r => r.Quantity).Should().Be(10m);
        rows.Sum(r => r.Shortage).Should().Be(10m, "uma reserva tudo, a outra fica com a falta inteira");
    }

    [Fact]
    public async Task Revisao_aumenta_diminui_remove_e_acrescenta_linha()
    {
        var (item, other, alm) = (await ItemAsync(), await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 12m);
        await ReceiveAsync(other, alm, 5m);
        var order = Order();
        Release(order, 1, (item, 10m));
        await ReconcileAsync(order);

        Release(order, 2, (item, 14m));
        await ReconcileAsync(order, "Evento BomRevised · mensagem 2");
        var up = (await ReservationsAsync(order)).Single(r => r.ItemId == item);
        (up.RequestedQuantity, up.Quantity, up.BomRevision).Should().Be((14m, 12m, 2), "CA 8: o possível, com falta do resto");

        Release(order, 3, (item, 7m), (other, 2m));
        await ReconcileAsync(order, "Evento BomRevised · mensagem 3");
        var rows = await ReservationsAsync(order);
        rows.Single(r => r.ItemId == item).Quantity.Should().Be(7m, "libera 5");
        rows.Single(r => r.ItemId == other).Quantity.Should().Be(2m, "linha nova reserva como no CA 3");
        (await PositionAsync(item, alm)).Reserved.Should().Be(7m);

        Release(order, 4, (other, 2m));
        await ReconcileAsync(order, "Evento BomRevised · mensagem 4");
        var removed = (await ReservationsAsync(order)).Single(r => r.ItemId == item);
        (removed.Status, removed.Quantity).Should().Be((ReservationState.Released, 0m), "linha removida libera a reserva inteira");
        (await PositionAsync(item, alm)).Reserved.Should().Be(0m);
    }

    [Fact]
    public async Task Revisao_abaixo_do_baixado_mostra_baixado_acima_da_BOM()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 10m);
        var order = Order();
        Release(order, 1, (item, 10m));
        await ReconcileAsync(order);
        // A EST-03 baixará pela requisição; aqui simula 6 já baixados (a requisição consome o reservado na mesma transação).
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE inventory.reservations SET quantity_issued = 6, quantity = 4 WHERE production_order_id = {order}; UPDATE inventory.stock_positions SET quantity_reserved = 4, quantity_on_hand = 4 WHERE item_id = {item}"));

        Release(order, 2, (item, 5m));
        await ReconcileAsync(order);
        var row = (await ReservationsAsync(order)).Single();
        (row.Quantity, row.QuantityIssued, row.RequestedQuantity).Should().Be((0m, 6m, 6m), "CA 8: nunca abaixo do baixado");
        await using var scope = await ScopeAsync();
        var view = (await scope.ServiceProvider.GetRequiredService<StockReservationService>().MaterialAsync(order)).Value;
        view.Lines.Single().OverIssued.Should().Be(1m, "Baixado acima da BOM: 1");
    }

    [Fact]
    public async Task OS_cancelada_libera_tudo_e_zera_as_faltas()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 6m);
        var order = Order();
        Release(order, 1, (item, 10m));
        await ReconcileAsync(order);

        SetStatus(order, ProductionOrderState.Cancelled);
        await ReconcileAsync(order, "Evento ProductionOrderCancelled · mensagem x");

        (await ReservationsAsync(order)).Should().OnlyContain(r => r.Status == ReservationState.Released && r.Quantity == 0m && r.Shortage == 0m, "CA 9");
        (await PositionAsync(item, alm)).Reserved.Should().Be(0m);
        (await ShortageEventsAsync(order)).Last().ShortQty.Should().Be(0m, "StockShortageChanged com shortQty 0");
    }

    [Fact]
    public async Task OS_concluida_reconcilia_com_alvo_zero_e_a_liberacao_sincrona_exige_transacao()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 10m);
        var (old, current) = (Order(), Order());
        Release(old, 1, (item, 4m));
        Release(current, 1, (item, 4m));
        await ReconcileAsync(old);
        await ReconcileAsync(current);

        // PRD-06a1 (antes da PRD-06a2): a conclusão não libera; o consumidor de ProductionOrderCompleted libera o que houver.
        SetStatus(old, ProductionOrderState.Completed);
        await ReconcileAsync(old, "Evento ProductionOrderCompleted · mensagem y");
        (await ReservationsAsync(old)).Should().OnlyContain(r => r.Status == ReservationState.Released, "CA 10");

        // PRD-06a2: a conclusão libera de forma síncrona, na transação dela; o consumidor depois não muda nada.
        await using (var scope = await ScopeAsync())
        {
            var reservations = scope.ServiceProvider.GetRequiredService<IStockReservations>();
            var withoutTransaction = () => reservations.ReleaseForOrderAsync(current);
            await withoutTransaction.Should().ThrowAsync<InvalidOperationException>("exige transação aberta, como o PostAsync");
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await reservations.ReleaseForOrderAsync(current);
            await reservations.ReleaseForOrderAsync(current);
            await transaction.CommitAsync();
        }

        SetStatus(current, ProductionOrderState.Completed);
        var changes = await WithDbAsync(db => db.ReservationChanges.CountAsync(c => c.ProductionOrderId == current));
        await ReconcileAsync(current, "Evento ProductionOrderCompleted · mensagem z");
        (await WithDbAsync(db => db.ReservationChanges.CountAsync(c => c.ProductionOrderId == current))).Should().Be(changes, "o consumidor só reconcilia e não muda nada");
        (await PositionAsync(item, alm)).Reserved.Should().Be(0m);
    }

    /// <summary>CA 2: as permutações dos quatro gatilhos (liberação, revisão, cancelamento e a liberação repetida) terminam no alvo da fonte.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Eventos_fora_de_ordem_duplicados_e_de_revisao_antiga_terminam_no_alvo_da_fonte(bool cancelled)
    {
        string[] triggers = ["BomReleased", "BomRevised", "ProductionOrderCancelled", "BomReleased"];
        foreach (var permutation in Permutations(triggers).Take(12))
        {
            var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
            await ReceiveAsync(item, alm, 9m);
            var order = Order(ProductionOrderState.InProgress);
            Release(order, 1, (item, 10m));
            foreach (var trigger in permutation)
            {
                // A fonte muda quando o fato acontece; a mensagem pode chegar antes ou depois das outras. Depois da revisão 2, a
                // mensagem da revisão 1 (BomReleased) é ignorada porque o consumidor lê a fonte.
                if (trigger == "BomRevised" && Boms[order].Revision == 1)
                {
                    Release(order, 2, (item, 14m));
                }

                if (trigger == "ProductionOrderCancelled" && cancelled)
                {
                    SetStatus(order, ProductionOrderState.Cancelled);
                }

                await ReconcileAsync(order, "Evento " + trigger + " · mensagem " + Guid.NewGuid());
            }

            var rows = await ReservationsAsync(order);
            var reason = string.Join(" → ", permutation);
            if (cancelled)
            {
                rows.Should().OnlyContain(r => r.Quantity == 0m && r.Status != ReservationState.Active, reason);
            }
            else
            {
                (rows.Sum(r => r.Quantity), rows.Sum(r => r.Shortage), rows.Max(r => r.BomRevision)).Should().Be((9m, 5m, 2), reason);
            }

            (await PositionAsync(item, alm)).Reserved.Should().Be(cancelled ? 0m : 9m, reason);
        }
    }

    [Fact]
    public async Task Entrada_completa_as_faltas_primeiro_da_OS_de_origem_depois_da_BOM_mais_antiga()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        var (older, origin) = (Order(), Order());
        var at = DateTimeOffset.UtcNow.AddDays(-2);
        ReleaseAt(older, 1, at, (item, 4m));
        ReleaseAt(origin, 1, at.AddHours(5), (item, 4m));
        await ReconcileAsync(older);
        await ReconcileAsync(origin);

        await ReceiveAsync(item, alm, 5m, originOrder: origin);
        (await ReservationsAsync(origin)).Sum(r => r.Quantity).Should().Be(4m, "CA 11: a OS que originou a compra primeiro");
        (await ReservationsAsync(older)).Sum(r => r.Quantity).Should().Be(1m, "o resto vai para a BOM liberada há mais tempo");

        var elsewhere = await LocationAsync();
        await ReceiveAsync(item, elsewhere, 10m);
        var rows = await ReservationsAsync(older);
        rows.Sum(r => r.Quantity).Should().Be(4m, "material em outro local também completa a falta");
        rows.Sum(r => r.Shortage).Should().Be(0m);
        (await PositionAsync(item, elsewhere)).Reserved.Should().Be(3m);
        (await WithDbAsync(db => db.ReservationChanges.AsNoTracking().Where(c => c.ProductionOrderId == older && c.Kind == ReservationChangeKind.Completed).Select(c => c.Origin).ToListAsync()))
            .Should().OnlyContain(o => o.StartsWith("Entrada MOV-", StringComparison.Ordinal), "CA 18: origem \"Entrada {MOV}\"");
        (await ShortageEventsAsync(older)).Last().ShortQty.Should().Be(0m);
    }

    [Fact]
    public async Task Liberacao_de_uma_OS_completa_a_falta_de_outra()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 5m);
        var (first, second) = (Order(), Order());
        Release(first, 1, (item, 5m));
        Release(second, 1, (item, 5m));
        await ReconcileAsync(first);
        await ReconcileAsync(second);
        (await ReservationsAsync(second)).Sum(r => r.Shortage).Should().Be(5m);

        SetStatus(first, ProductionOrderState.Cancelled);
        await ReconcileAsync(first, "Evento ProductionOrderCancelled · mensagem c");
        (await ReservationsAsync(second)).Sum(r => r.Quantity).Should().Be(5m, "o disponível liberado completa a falta das outras OS");
    }

    [Fact]
    public async Task Recomposicao_idempotente_com_o_consumidor_ao_vivo()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 3m);
        var (pending, live) = (Order(), Order());
        Release(pending, 1, (item, 2m));
        Release(live, 1, (item, 2m));
        await WithDbAsync(db => db.InventoryParameters.ExecuteUpdateAsync(s => s.SetProperty(p => p.ReservationsBackfilledAt, (DateTimeOffset?)null)));

        async Task BackfillAsync()
        {
            await using var scope = await ScopeAsync();
            await scope.ServiceProvider.GetRequiredService<ReservationBackfill>().RunAsync();
        }

        await Task.WhenAll(Task.Run(BackfillAsync), Task.Run(() => ReconcileAsync(live)));
        await BackfillAsync();

        var rows = await WithDbAsync(db => db.Reservations.AsNoTracking().Where(r => r.ItemId == item).ToListAsync());
        rows.Should().HaveCount(2, "CA 12: uma reserva por OS, gravada uma vez só");
        (rows.Sum(r => r.Quantity), rows.Sum(r => r.Shortage)).Should().Be((3m, 1m));
        rows.Single(r => r.ProductionOrderId == pending).Should().Match<Reservation>(r => r.IsBackfill && r.Origin == ReservationOrigins.Backfill, "marca \"Calculado na implantação\"");
        (await WithDbAsync(db => db.InventoryParameters.AsNoTracking().Select(p => p.ReservationsBackfilledAt).FirstAsync())).Should().NotBeNull();
        ((await ShortageEventsAsync(pending)).Count + (await ShortageEventsAsync(live)).Count).Should().Be(1, "o aviso da falta sai uma vez");
    }

    [Fact]
    public async Task Contrato_devolve_reservas_e_faltas_em_lote_sem_custo()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 2m);
        var order = Order();
        var bom = Release(order, 1, (item, 5m));
        await ReconcileAsync(order);

        await using var scope = await ScopeAsync();
        var contract = scope.ServiceProvider.GetRequiredService<IStockReservations>();
        var mine = await contract.ForOrderAsync(order);
        mine.Should().ContainSingle().Which.Should().Match<StockReservationInfo>(r => r.Requested == 5m && r.Reserved == 2m && r.Shortage == 3m && r.BomLineId == bom.Lines[0].BomLineId);
        (await contract.OpenShortagesAsync()).Where(s => s.ProductionOrderId == order).Should().ContainSingle().Which.ShortQty.Should().Be(3m, "CA 16");
        typeof(StockReservationInfo).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("Cost", StringComparison.Ordinal), "sem dado de custo");
        var balance = (await scope.ServiceProvider.GetRequiredService<IStockBalances>().GetAsync([item], alm)).Single();
        (balance.OnHand, balance.Reserved, balance.Available).Should().Be((2m, 2m, 0m), "CA 13: o IStockBalances devolve o reservado da posição");
    }

    [Fact]
    public async Task Servico_na_BOM_nao_reserva()
    {
        var service = await ItemAsync(type: ItemType.Service);
        var order = Order();
        Release(order, 1, (service, 3m));
        await ReconcileAsync(order);
        (await ReservationsAsync(order)).Should().BeEmpty();
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
