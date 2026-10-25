using BemAqui.Engineering.Application;
using BemAqui.Engineering.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Engineering;

/// <summary>ENG-02, regras sem banco: necessidade com perda e conversão (CA 3), item repetido, diferença por revisão (CA 13), bloqueio por sequência (CA 16).</summary>
public class BomRulesTests
{
    private static readonly Guid Kg = Guid.CreateVersion7();
    private static readonly Guid Grams = Guid.CreateVersion7();
    private static readonly Guid Item = Guid.CreateVersion7();

    /// <summary>Conversão de teste: 1 g = 0,001 kg, arredondando uma vez no fim como o Estoque (CAD-02b).</summary>
    private sealed class GramsToKg : IUnitConversion
    {
        public Task<Result<decimal>> ConvertAsync(Guid itemId, decimal quantity, Guid fromUnitId, Guid toUnitId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Result<decimal>>(fromUnitId == Grams && toUnitId == Kg
                ? Math.Round(quantity * 0.001m, 4, MidpointRounding.AwayFromZero)
                : Error.Conflict("unit_conversion_missing", "Sem conversão."));
    }

    private static BomLineInput Line(Guid? item = null, decimal quantity = 1m, decimal scrap = 0m, string? notes = null) => new(null, item ?? Guid.CreateVersion7(), quantity, null, scrap, notes);

    [Fact]
    public async Task Necessidade_e_quantidade_por_unidade_vezes_a_OS_com_perda_e_arredonda_no_fim()
    {
        var conversion = new GramsToKg();
        (await BomRules.RequirementAsync(conversion, Item, 2.5m, 3m, 10m, Kg, Kg)).Value.Should().Be(8.25m, "2,5 × 3 × 1,10");
        (await BomRules.RequirementAsync(conversion, Item, 0.3333m, 7m, 3m, Kg, Kg)).Value.Should().Be(2.4031m, "2,403093 arredonda em 4 casas só no fim");
        (await BomRules.RequirementAsync(conversion, Item, 250m, 2m, 5m, Grams, Kg)).Value.Should().Be(0.525m, "250 g × 2 × 1,05 = 525 g = 0,525 kg, convertido da conta crua");
        var missing = await BomRules.RequirementAsync(conversion, Item, 1m, 1m, 0m, Guid.CreateVersion7(), Kg);
        missing.IsFailure.Should().BeTrue();
        missing.Error!.Code.Should().Be("unit_conversion_missing");
        BomRules.RawRequirement(1.5m, 2m, 10m).Should().Be(3.3m);
    }

    [Fact]
    public void Mesmo_item_nao_se_repete_e_os_limites_da_linha_valem()
    {
        var item = Guid.CreateVersion7();
        var (lines, errors) = BomRules.Validate([Line(item), Line(item), Line(quantity: 0), Line(quantity: 1.23456m), Line(scrap: 101m), Line(scrap: -1m), Line(notes: new string('x', 201))]);
        errors["lines[1].itemId"].Should().Equal("O item já está na linha 1.");
        errors.Should().ContainKeys("lines[2].quantity", "lines[3].quantity", "lines[4].scrapPct", "lines[5].scrapPct", "lines[6].notes");
        errors.Should().NotContainKey("lines[0].itemId");
        lines.Select(l => l.Sequence).Should().Equal(1, 2, 3, 4, 5, 6, 7);

        var (ok, none) = BomRules.Validate([Line(notes: "  Cortar no sentido da fibra  ", scrap: 2.5m)]);
        none.Should().BeEmpty();
        ok[0].Notes.Should().Be("Cortar no sentido da fibra");
        BomRules.Validate([]).Errors.Should().BeEmpty("BOM vazia é válida: OS só de mão de obra (CA 9)");
        BomRules.Validate([Line(Guid.Empty)]).Errors.Should().ContainKey("lines[0].itemId");
    }

    [Fact]
    public void Diferenca_por_item_entre_revisoes_da_a_reservar_a_mais_e_a_liberar()
    {
        var (a, b, c, d) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var diff = BomRules.Diff([(a, 10m), (b, 4m), (c, 2m)], [(a, 12m), (b, 1m), (d, 5m), (c, 2m)]).ToDictionary(x => x.ItemId);
        diff[a].Should().Match<BomItemDiff>(x => x.Change == BomChange.Changed && x.MoreToReserve == 2m && x.ToRelease == 0m);
        diff[b].Should().Match<BomItemDiff>(x => x.Change == BomChange.Changed && x.MoreToReserve == 0m && x.ToRelease == 3m);
        diff[c].Change.Should().Be(BomChange.Unchanged);
        diff[d].Should().Match<BomItemDiff>(x => x.Change == BomChange.Added && x.Before == 0m && x.After == 5m);

        var removed = BomRules.Diff([(a, 3m)], []).Single();
        removed.Change.Should().Be(BomChange.Removed);
        removed.ToRelease.Should().Be(3m);
    }

    [Fact]
    public void Evento_da_revisao_leva_a_necessidade_anterior_e_o_item_que_saiu_com_zero()
    {
        var (bom, previous) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (a, b, c) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var oldB = new BomLine { BomId = previous, ItemId = b, RequiredQuantity = 4m };
        var lines = BomService.RevisedLines(
            [new BomLine { BomId = bom, ItemId = a, RequiredQuantity = 12m }, new BomLine { BomId = bom, ItemId = c, RequiredQuantity = 1m }],
            [new BomLine { BomId = previous, ItemId = a, RequiredQuantity = 10m }, oldB]);
        lines.Should().HaveCount(3);
        lines.Single(l => l.ItemId == a).Should().Match<BemAqui.Engineering.Contracts.BomRevisedLine>(l => l.RequiredQty == 12m && l.PreviousRequiredQty == 10m);
        lines.Single(l => l.ItemId == c).PreviousRequiredQty.Should().Be(0m, "item novo");
        lines.Single(l => l.ItemId == b).Should().Match<BemAqui.Engineering.Contracts.BomRevisedLine>(l => l.RequiredQty == 0m && l.PreviousRequiredQty == 4m && l.BomLineId == oldB.Id);
    }

    [Fact]
    public void Aviso_de_baixado_acima_da_nova_necessidade_e_falta_pelo_disponivel()
    {
        BomRules.IssuedAboveWarning(8m, 5.5m, "Chapa 2 mm", "kg").Should()
            .Be("Já foram baixados 8 kg de Chapa 2 mm; a nova necessidade é 5,5 kg. Devolva a diferença ao estoque pela requisição.");
        BomRules.IssuedAboveWarning(5m, 5m, "Chapa", "kg").Should().BeNull();
        BomRules.Shortage(10m, 4m).Should().Be(6m);
        BomRules.Shortage(3m, 4m).Should().Be(0m);
        BomRules.Shortage(3m, -1m).Should().Be(3m, "disponível negativo conta como zero");
    }

    [Fact]
    public void Item_da_linha_nao_e_servico_nem_o_produto_e_o_novo_precisa_estar_ativo()
    {
        ItemSummary Item(ItemType type = ItemType.RawMaterial, bool active = true, bool deleted = false, Guid? id = null) =>
            new(id ?? Guid.CreateVersion7(), "MP-1", "Chapa", type, Kg, "KG", null, false, false, null, active, deleted);
        var product = Guid.CreateVersion7();
        BomRules.ItemProblem(Item(), product, false).Should().BeNull();
        BomRules.ItemProblem(null, product, false).Should().Be("Escolha um item do estoque.");
        BomRules.ItemProblem(Item(deleted: true), product, true).Should().Be("Escolha um item do estoque.");
        BomRules.ItemProblem(Item(ItemType.Service), product, false).Should().StartWith("Serviço não entra na BOM");
        BomRules.ItemProblem(Item(ItemType.Finished, id: product), product, false).Should().Be("O produto da OS não entra na própria BOM.");
        BomRules.ItemProblem(Item(active: false), product, false).Should().Be("Escolha um item ativo do estoque.");
        BomRules.ItemProblem(Item(active: false), product, true).Should().BeNull("o item que ficou inativo depois da cópia continua, marcado para revisão (CA 5)");
    }

    [Fact]
    public void OS_concluida_ou_cancelada_nao_muda_a_BOM()
    {
        ProductionOrderInfo Order(ProductionOrderState state) => new(Guid.CreateVersion7(), "OS-2026-00041", ProductionOrderKind.Fabrication, Guid.CreateVersion7(), Guid.CreateVersion7(), null, 1m, state, null, null);
        BomRules.Closed(Order(ProductionOrderState.Cancelled))!.Message.Should().Be("A OS OS-2026-00041 está cancelada: a BOM não muda.");
        BomRules.Closed(Order(ProductionOrderState.Completed))!.Message.Should().Be("A OS OS-2026-00041 está concluída: a BOM não muda.");
        BomRules.Closed(Order(ProductionOrderState.Completed))!.Type.Should().Be(ErrorType.Conflict);
        BomRules.Closed(Order(ProductionOrderState.InProgress)).Should().BeNull();
        BomRules.Closed(Order(ProductionOrderState.ReadyToComplete)).Should().BeNull();
    }

    [Fact]
    public void Bloqueio_vale_da_segunda_etapa_em_diante_e_nao_interrompe_a_etapa_ja_iniciada()
    {
        BomGateRules.Applies(0, alreadyStarted: false).Should().BeFalse("a primeira etapa começa sem BOM (D12)");
        BomGateRules.Applies(1, alreadyStarted: false).Should().BeTrue();
        BomGateRules.Applies(4, alreadyStarted: false).Should().BeTrue();
        BomGateRules.Applies(1, alreadyStarted: true).Should().BeFalse("a etapa em andamento no deploy não é interrompida");
        BomGateRules.NotReleased().Code.Should().Be("bom_not_released");
        BomGateRules.NotReleased().Message.Should().Be("A BOM desta OS ainda não foi liberada. Fale com a Engenharia.");
    }

    [Fact]
    public void OS_vai_parar_sem_BOM_com_a_etapa_atual_depois_da_primeira()
    {
        ProductionOrderInfo Order(int? sequence, bool firstDone, ProductionOrderState state = ProductionOrderState.InProgress) =>
            new(Guid.CreateVersion7(), "OS-1", ProductionOrderKind.Fabrication, Guid.CreateVersion7(), Guid.CreateVersion7(), null, 1m, state, null, null,
                CurrentStepSequence: sequence, FirstStepDone: firstDone, StepCount: 3);
        BomService.WillStop(Order(2, true), released: false).Should().BeTrue();
        BomService.WillStop(Order(1, false, ProductionOrderState.Open), released: false).Should().BeFalse("a primeira etapa ainda não terminou");
        BomService.WillStop(Order(2, true), released: true).Should().BeFalse();
        BomService.WillStop(Order(null, true, ProductionOrderState.ReadyToComplete), released: false).Should().BeFalse("não há mais etapa para começar");
    }

    private sealed class FakeBalances(params StockAvailability[] rows) : IStockBalances
    {
        public Task<IReadOnlyList<StockAvailability>> GetAsync(IReadOnlyCollection<Guid> itemIds, Guid? locationId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockAvailability>>(rows.Where(r => itemIds.Contains(r.ItemId)).ToList());
    }

    /// <summary>Reservas da OS como o <see cref="IStockReservations"/> da EST-02 devolveria.</summary>
    private sealed class FakeReservations(params StockReservationInfo[] rows) : IStockReservations
    {
        public Task<IReadOnlyList<StockReservationInfo>> ForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockReservationInfo>>(rows.Where(r => r.ProductionOrderId == productionOrderId).ToList());

        public Task<IReadOnlyList<StockShortageInfo>> OpenShortagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StockShortageInfo>>([]);

        public Task ReleaseForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Guid>> OrdersCompletedByEntriesAsync(IReadOnlyCollection<string> movementNumbers, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class FakeCosts : IItemCosts
    {
        public Task<IReadOnlyDictionary<Guid, decimal>> AverageCostsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(itemIds.ToDictionary(i => i, _ => 12.5m));
    }

    [Fact]
    public async Task Saldo_da_BOM_soma_os_locais_do_IStockBalances_e_o_baixado_vem_das_reservas_da_EST02()
    {
        var (a, b, order) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var reservations = new FakeReservations(
            new StockReservationInfo(Guid.CreateVersion7(), order, a, Guid.CreateVersion7(), null, 1, 5m, 2m, 3m, 0m, ReservationState.Active, false),
            new StockReservationInfo(Guid.CreateVersion7(), order, a, Guid.CreateVersion7(), null, 1, 1m, 0m, 1m, 0m, ReservationState.Consumed, false),
            new StockReservationInfo(Guid.CreateVersion7(), Guid.CreateVersion7(), b, Guid.CreateVersion7(), null, 1, 9m, 0m, 9m, 0m, ReservationState.Consumed, false));
        var stock = new StockBalancesBomStock(new FakeBalances(
            new StockAvailability(a, Guid.CreateVersion7(), 10m, 2m, 8m), new StockAvailability(a, Guid.CreateVersion7(), 3m, 0m, 3m), new StockAvailability(b, Guid.CreateVersion7(), 1m, 1m, 0m)), new FakeCosts(), reservations);
        stock.Connected.Should().BeTrue();
        stock.ReservationsConnected.Should().BeTrue("a EST-02 liga o \"vai reservar\" e o baixado");
        var positions = await stock.PositionsAsync([a, b, Guid.CreateVersion7()]);
        positions[a].Should().Be(new BomStockPosition(13m, 2m, 11m));
        positions[b].Should().Be(new BomStockPosition(1m, 1m, 0m));
        positions.Should().HaveCount(2, "item sem posição fica fora (vale zero)");
        (await stock.PositionsAsync([])).Should().BeEmpty();
        (await stock.AverageCostsAsync([a]))[a].Should().Be(12.5m);
        (await stock.IssuedForOrderAsync(order)).Should().BeEquivalentTo(new Dictionary<Guid, decimal> { [a] = 4m }, "baixado da OS somado nos locais, só desta OS");
        (await stock.IssuedForOrderAsync(Guid.CreateVersion7())).Should().BeEmpty();
        (await new DisconnectedBomStock().IssuedForOrderAsync(order)).Should().BeNull();
    }

    [Fact]
    public void Maquina_de_estados_da_revisao()
    {
        var bom = new Bom { ProductionOrderId = Guid.CreateVersion7(), Revision = 0 };
        bom.IsDraft.Should().BeTrue();
        var by = Guid.CreateVersion7();
        bom.Release(DateTimeOffset.UtcNow, by);
        bom.Status.Should().Be(BomStatus.Released);
        bom.ReleasedBy.Should().Be(by);
        var again = () => bom.Release(DateTimeOffset.UtcNow, by);
        again.Should().Throw<InvalidOperationException>("liberada não é liberada de novo");
        bom.Supersede();
        bom.Status.Should().Be(BomStatus.Superseded);
        var twice = () => bom.Supersede();
        twice.Should().Throw<InvalidOperationException>();
        BomRules.Label(BomStatus.Superseded).Should().Be("Substituída");
    }
}
