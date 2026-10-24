using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.Production.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PRD-06a2, produto acabado na conclusão da OS (movimenta estoque, Testcontainers obrigatório): sem produto (CA 1); diálogo com e sem
/// <c>projects.view_cost</c> e o mesmo custo na entrada (CA 2 e 3); entrada <c>Production</c> com séries D15, custo médio, lotes consumidos e
/// reservas liberadas na mesma transação (CA 4, 5 e 8) e a falha desfazendo tudo; fora do custo do projeto (CA 6); custo zero (CA 7); OS
/// concluídas antes sem entrada (CA 9); estorno com e sem série expedida e sem saldo (CA 10); três camadas e IDOR (CA 12); conclusão
/// concorrente (CA 13); DTOs sem custo (CA 14); histórico e linha do tempo (CA 15). Cada teste cria a própria OS, itens e locais e filtra
/// pelos ids dele.
/// </summary>
public partial class FabricationTests
{
    private static string Year => BusinessTime.Today(TimeProvider.System).Year.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// OS de fabricação de <paramref name="quantity"/> unidades com o produto (com série se <paramref name="serial"/>), BOM liberada de uma
    /// chapa com reserva, 4 KG baixados pela requisição (R$ 100,00 cada) e as etapas concluídas pelo supervisor: pronta para concluir.
    /// </summary>
    private async Task<(HttpClient Owner, Guid Order, Guid Project, Guid Product, Guid Steel)> FinishedDatasetAsync(bool serial = true, decimal quantity = 2m, bool issue = true)
    {
        var (owner, order, project, steel, line) = await ReservedOrderAsync(perUnit: 6m, stock: 20m, unitCost: 100m);
        var product = (await OrderAsync(order)).ProductId!.Value;
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE inventory.items SET tracks_serial = {serial} WHERE id = {product}"));
        if (quantity != 2m)
        {
            await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET quantity = {quantity} WHERE id = {order}"));
        }

        if (issue)
        {
            await CreatedAsync(await IssueAsync(owner, order, null, ReqLine(line, 4m)));
        }

        await FinishStepsAsync(owner, order);
        (await OrderAsync(order)).Status.Should().Be(ProductionOrderStatus.ReadyToComplete);
        return (owner, order, project, product, steel);
    }

    /// <summary>Apontamento fechado com custo direto no banco (o cálculo do custo tem os testes da PRD-04).</summary>
    private async Task LaborCostAsync(Guid order, decimal cost) => await WithDbAsync(async db =>
    {
        var step = await db.ProductionOrderSteps.Where(s => s.OrderId == order).OrderBy(s => s.Sequence).Select(s => s.Id).FirstAsync();
        var end = DateTimeOffset.UtcNow.AddHours(-1);
        db.LaborEntries.Add(new LaborEntry
        {
            OrderId = order, OrderStepId = step, EmployeeId = Guid.CreateVersion7(), StartedAt = end.AddHours(-2), EndedAt = end, CloseKind = LaborCloseKind.Supervisor, Cost = cost,
            CostVersion = 1, HourlyCostSnapshot = cost / 2m, CreatedAt = end,
        });
        return await db.SaveChangesAsync();
    });

    /// <summary>Material com lote baixado direto pelo motor para a OS (como a requisição faria), numa transação própria.</summary>
    private async Task<Guid> IssueLotAsync(Guid order, Guid project, string lotNumber, decimal quantity, decimal unitCost)
    {
        var item = await MaterialAsync("Cabo " + Guid.NewGuid().ToString("N")[..4]);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE inventory.items SET tracks_lot = true WHERE id = {item}"));
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        var location = new Location { Code = Unique("L"), Name = "Local " + Guid.NewGuid().ToString("N")[..4], CreatedAt = DateTimeOffset.UtcNow };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        var stock = scope.ServiceProvider.GetRequiredService<IStockMovements>();
        var lot = await stock.EnsureLotAsync(item, lotNumber);
        await using var transaction = await db.Database.BeginTransactionAsync();
        (await stock.PostAsync(new StockPosting(StockMovementType.Receipt, item, location.Id, lot, quantity, unitCost, StockSources.GoodsReceipt, Guid.NewGuid()))).IsSuccess.Should().BeTrue();
        (await stock.PostAsync(new StockPosting(StockMovementType.Issue, item, location.Id, lot, -quantity, null, StockSources.Requisition, Guid.NewGuid(), ProjectId: project, ProductionOrderId: order)))
            .IsSuccess.Should().BeTrue();
        await transaction.CommitAsync();
        return lot;
    }

    private static async Task<HttpResponseMessage> CompleteWithAsync(HttpClient client, Guid order, uint version, object? extra = null) =>
        await client.SendAsync(Post($"{Base}/{order}/complete", extra ?? new { version, confirmZeroCost = false }));

    private async Task<List<StockMovement>> ProductionMovementsAsync(Guid order) =>
        await WithDbAsync(db => db.StockMovements.AsNoTracking().Where(m => m.SourceId == order && m.Type == StockMovementType.Production).OrderBy(m => m.PostedSeq).ToListAsync());

    private async Task<List<Serial>> SerialsAsync(Guid order) =>
        await WithDbAsync(db => db.Serials.AsNoTracking().Where(s => s.ProductionOrderId == order).OrderBy(s => s.SerialNumber).ToListAsync());

    private async Task<List<SerialComponent>> ComponentsAsync(Guid order) =>
        await WithDbAsync(db => db.SerialComponents.AsNoTracking().Where(c => c.ProductionOrderId == order).ToListAsync());

    [Fact]
    public async Task Sem_item_de_estoque_do_produto_a_conclusao_pede_o_item_e_o_atalho_liga_o_produto()
    {
        var (owner, order, _, _) = await BomOrderAsync(quantity: 1m, withProduct: false);
        await FinishStepsAsync(owner, order);
        var detail = await GetOrderAsync(owner, order);
        detail.GetProperty("completionBlocker").GetString().Should().Be("Escolha o item de estoque do produto antes de concluir.", "CA 1: botão desabilitado com o motivo");
        var preview = await OkAsync(await owner.GetAsync($"{Base}/{order}/completion"));
        preview.GetProperty("canComplete").GetBoolean().Should().BeFalse();

        var refused = await CompleteWithAsync(owner, order, Version(detail), new { version = Version(detail), confirmZeroCost = true });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "CA 1");
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be("product_required");
        problem.GetProperty("detail").GetString().Should().Be("Escolha o item de estoque do produto antes de concluir.");
        (await OrderAsync(order)).Status.Should().Be(ProductionOrderStatus.ReadyToComplete);

        // Atalho: "Escolher item do produto" com production.edit; serviço ou matéria-prima não servem.
        var options = await OkAsync(await owner.GetAsync($"{Base}/{order}/product-options?q=Elevador"));
        options.ValueKind.Should().Be(JsonValueKind.Array);
        var raw = await MaterialAsync("Chapa avulsa");
        (await owner.PutAsJsonAsync($"{Base}/{order}/product", new { itemId = raw, version = Version(detail) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var (_, viewer) = await ActorAsync("production:view,approve");
        var product = await ProductAsync("Cabina avulsa");
        (await viewer.PutAsJsonAsync($"{Base}/{order}/product", new { itemId = product, version = Version(detail) })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem production.edit");
        var linked = await OkAsync(await owner.PutAsJsonAsync($"{Base}/{order}/product", new { itemId = product, version = Version(detail) }));
        linked.GetProperty("productId").GetGuid().Should().Be(product);
        linked.GetProperty("completionBlocker").ValueKind.Should().Be(JsonValueKind.Null);
        await OkAsync(await CompleteWithAsync(owner, order, Version(linked), new { version = Version(linked), confirmZeroCost = true }));
        (await ProductionMovementsAsync(order)).Should().ContainSingle().Which.ItemId.Should().Be(product);
    }

    [Fact]
    public async Task Conclusao_lanca_a_entrada_com_series_custo_lotes_e_reservas_na_mesma_transacao()
    {
        var (owner, order, project, product, steel) = await FinishedDatasetAsync(serial: true, quantity: 2m);
        await LaborCostAsync(order, 200m);
        var lot = await IssueLotAsync(order, project, "LT-" + Guid.NewGuid().ToString("N")[..6], 2m, 50m);
        var code = await WithDbAsync(db => db.Items.Where(i => i.Id == product).Select(i => i.Code).SingleAsync());
        (await ReservationsOfAsync(order)).Should().Contain(r => r.ItemId == steel && r.Status == ReservationState.Active && r.Quantity > 0, "sobra reserva antes de concluir");

        // CA 2 e 3: o diálogo com projects.view_cost lê o custo em DTO separado; sem a ação, 403 e nenhum custo no impacto.
        var (_, supervisor) = await ActorAsync("production:view,approve");
        var preview = await OkAsync(await supervisor.GetAsync($"{Base}/{order}/completion"));
        preview.GetProperty("stockNotice").ValueKind.Should().Be(JsonValueKind.Null, "a linha 'ainda não entra no estoque' sai");
        var stock = preview.GetProperty("stock");
        stock.GetProperty("enabled").GetBoolean().Should().BeTrue();
        stock.GetProperty("quantity").GetDecimal().Should().Be(2m);
        stock.GetProperty("tracksSerial").GetBoolean().Should().BeTrue();
        stock.GetProperty("suggestedSerials").EnumerateArray().Select(s => s.GetString()).Should().Equal($"{code}-{Year}-00001", $"{code}-{Year}-00002");
        stock.GetProperty("suggestedLocationId").ValueKind.Should().Be(JsonValueKind.String, "local padrão sugerido");
        stock.GetProperty("reservationsToRelease").EnumerateArray().Should().Contain(r => r.GetProperty("itemId").GetGuid() == steel);
        stock.GetProperty("zeroCost").GetBoolean().Should().BeFalse();
        stock.EnumerateObject().Select(p => p.Name).Where(n => n.Contains("cost", StringComparison.OrdinalIgnoreCase) && n != "zeroCost").Should().BeEmpty("CA 14");
        (await supervisor.GetAsync($"{Base}/{order}/completion/cost")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "custo só com projects.view_cost");
        var cost = await OkAsync(await owner.GetAsync($"{Base}/{order}/completion/cost"));
        cost.GetProperty("labor").GetDecimal().Should().Be(200m);
        cost.GetProperty("material").GetDecimal().Should().Be(500m, "4 × 100 da requisição + 2 × 50 do lote");
        cost.GetProperty("total").GetDecimal().Should().Be(700m);
        cost.GetProperty("unit").GetDecimal().Should().Be(350m);

        // CA 5: quem conclui sem projects.view_cost usa o mesmo custo (CA 3).
        var done = await OkAsync(await CompleteWithAsync(supervisor, order, Version(preview), new { version = Version(preview), locationId = (Guid?)null, serialNumbers = (string[]?)null, confirmZeroCost = false }));
        done.GetProperty("status").GetString().Should().Be("Completed");
        var entry = (await ProductionMovementsAsync(order)).Should().ContainSingle().Subject;
        entry.Quantity.Should().Be(2m);
        entry.UnitCost.Should().Be(350m);
        entry.TotalCost.Should().Be(700m);
        entry.Leg.Should().Be(1);
        (entry.SourceType, entry.ProjectId, entry.ProductionOrderId).Should().Be((StockSources.ProductionOrder, project, order));
        done.GetProperty("entryMovementId").GetGuid().Should().Be(entry.Id);
        (await WithDbAsync(db => db.Items.AsNoTracking().Where(i => i.Id == product).Select(i => i.AverageCost).SingleAsync())).Should().Be(350m, "CA 5: custo médio recalculado");

        var serials = await SerialsAsync(order);
        serials.Select(s => s.SerialNumber).Should().Equal($"{code}-{Year}-00001", $"{code}-{Year}-00002");
        serials.Should().OnlyContain(s => s.Status == SerialState.InStock && s.ProjectId == project && s.LocationId == entry.LocationId && s.EntryMovementId == entry.Id);
        var components = await ComponentsAsync(order);
        components.Should().Contain(c => c.ItemId == steel && c.LotId == null && c.Quantity == 4m, "CA 8: material sem lote pelo item");
        components.Should().Contain(c => c.LotId == lot && c.Quantity == 2m, "CA 8: o lote consumido");
        components.Should().OnlyContain(c => c.SerialId == null && c.ReversedAt == null, "cada série herda os lotes da OS inteira");
        (await ReservationsOfAsync(order)).Where(r => r.Status == ReservationState.Active).Should().BeEmpty("CA 5: reservas restantes liberadas (alvo 0)");
        (await OutboxAsync<ProductionOrderCompleted>(order)).Should().Be(1);
        var payload = (await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type == typeof(ProductionOrderCompleted).FullName).ToListAsync()))
            .Select(m => JsonSerializer.Deserialize<ProductionOrderCompleted>(m.Payload)!).Single(e => e.ProductionOrderId == order);
        (payload.ProductId, payload.UnitCost, payload.LocationId).Should().Be((product, 350m, entry.LocationId));
        payload.SerialNumbers.Should().Equal(serials.Select(s => s.SerialNumber));

        // CA 11: a aba Séries da OS e a do item.
        var tab = await OkAsync(await supervisor.GetAsync($"{Inventory}/production-orders/{order}/serials"));
        tab.GetProperty("entryMovementNumber").GetString().Should().Be(entry.Number);
        tab.GetProperty("serials").GetArrayLength().Should().Be(2);
        tab.GetProperty("consumedLots").GetArrayLength().Should().Be(2);
        var itemTab = await OkAsync(await owner.GetAsync($"{Inventory}/items/{product}/serials"));
        itemTab.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("productionOrderNumber").GetString()).Should().OnlyContain(n => n == done.GetProperty("number").GetString());
        var costs = await OkAsync(await owner.GetAsync($"{Base}/{order}/costs"));
        costs.GetProperty("finishedGoods").GetProperty("unitCost").GetDecimal().Should().Be(350m, "aba Custos com projects.view_cost");
    }

    [Fact]
    public async Task Falha_no_estoque_desfaz_tudo_e_a_OS_continua_pronta_para_concluir()
    {
        var (owner, order, _, product, _) = await FinishedDatasetAsync(serial: true, quantity: 2m);
        var taken = "JA-" + Guid.NewGuid().ToString("N")[..6];
        await WithDbAsync(async db =>
        {
            db.Serials.Add(new Serial { ItemId = product, SerialNumber = taken, Status = SerialState.InStock, CreatedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        });
        var reservedBefore = (await ReservationsOfAsync(order)).Sum(r => r.Quantity);
        var version = await OrderVersionAsync(order);

        var refused = await CompleteWithAsync(owner, order, version, new { version, serialNumbers = new[] { taken, "NOVA-1" }, confirmZeroCost = false });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "CA 4: série repetida no item");
        (await JsonAsync(refused)).GetProperty("detail").GetString().Should().Be($"O número de série {taken} já existe neste item.");
        (await OrderAsync(order)).Status.Should().Be(ProductionOrderStatus.ReadyToComplete, "CA 5: qualquer falha desfaz tudo");
        (await ProductionMovementsAsync(order)).Should().BeEmpty();
        (await SerialsAsync(order)).Should().BeEmpty();
        (await ComponentsAsync(order)).Should().BeEmpty();
        (await ReservationsOfAsync(order)).Sum(r => r.Quantity).Should().Be(reservedBefore, "a liberação das reservas também voltou");
        (await OutboxAsync<ProductionOrderCompleted>(order)).Should().Be(0);

        // Editadas, sem repetir: entram como digitadas.
        var ok = await OkAsync(await CompleteWithAsync(owner, order, version, new { version, serialNumbers = new[] { "ED-" + taken, "NOVA-" + taken }, confirmZeroCost = false }));
        ok.GetProperty("status").GetString().Should().Be("Completed");
        (await SerialsAsync(order)).Select(s => s.SerialNumber).Should().BeEquivalentTo(["ED-" + taken, "NOVA-" + taken]);
    }

    [Fact]
    public async Task Entrada_do_produto_acabado_fica_fora_do_custo_do_projeto()
    {
        var (owner, order, project, _, _) = await FinishedDatasetAsync(serial: false);
        await OkAsync(await CompleteWithAsync(owner, order, await OrderVersionAsync(order)));
        await DrainAsync();

        (await RealizedMaterialAsync(owner, project)).Should().Be(400m, "CA 6: só a baixa (4 × 100) entra; a entrada Production não");
        var entry = (await ProductionMovementsAsync(order)).Single();
        (await OutboxAsync<StockIssued>(entry.Id)).Should().Be(0);
        (await OutboxAsync<StockReturned>(entry.Id)).Should().Be(0);
        (await WithDbAsync(db => db.ProjectCosts.AsNoTracking().Where(c => c.ProjectId == project).ToListAsync())).Should().NotContain(c => c.SourceId == entry.Id);
    }

    [Fact]
    public async Task Custo_zerado_pede_confirmacao_explicita()
    {
        var (owner, order, _, product, _) = await FinishedDatasetAsync(serial: false, issue: false);
        var preview = await OkAsync(await owner.GetAsync($"{Base}/{order}/completion"));
        preview.GetProperty("stock").GetProperty("zeroCost").GetBoolean().Should().BeTrue("CA 7");

        var refused = await CompleteWithAsync(owner, order, Version(preview));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(refused)).GetProperty("errors").GetProperty("confirmZeroCost").EnumerateArray().Single().GetString()
            .Should().StartWith("Custo da OS zerado: o produto entra no estoque com custo 0.");
        (await OrderAsync(order)).Status.Should().Be(ProductionOrderStatus.ReadyToComplete);

        await OkAsync(await CompleteWithAsync(owner, order, Version(preview), new { version = Version(preview), confirmZeroCost = true }));
        var entry = (await ProductionMovementsAsync(order)).Single();
        (entry.UnitCost, entry.TotalCost).Should().Be((0m, 0m));
        (await SerialsAsync(order)).Should().BeEmpty("sem controle de série, a entrada é por quantidade");
        (await WithDbAsync(db => db.StockPositions.AsNoTracking().Where(p => p.ItemId == product).SumAsync(p => p.QuantityOnHand))).Should().Be(2m);
    }

    [Fact]
    public async Task OS_concluida_antes_do_controle_de_estoque_fica_sem_entrada_e_a_reconclusao_segue_a_regra_nova()
    {
        var (owner, order, _, _, _) = await FinishedDatasetAsync(serial: false);
        // Concluída pela PRD-06a1 (sem estoque): a migration marca a OS.
        var now = DateTimeOffset.UtcNow;
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Completed', actual_end = {now} WHERE id = {order}"));
        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync(ProductionDataFixes.CompletedWithoutStock));
        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync(ProductionDataFixes.CompletedWithoutStock));

        (await OrderAsync(order)).CompletedWithoutStock.Should().BeTrue("CA 9");
        var detail = await GetOrderAsync(owner, order);
        detail.GetProperty("completedWithoutStock").GetBoolean().Should().BeTrue("a OS mostra o texto informativo");
        (await ProductionMovementsAsync(order)).Should().BeEmpty("sem recomposição de estoque");
        (await OkAsync(await owner.GetAsync($"{Inventory}/production-orders/{order}/serials"))).GetProperty("entryMovementId").ValueKind.Should().Be(JsonValueKind.Null);

        // O estorno não mexe no estoque; a reconclusão entra no estoque pela regra desta story.
        var reversed = await OkAsync(await ReverseOrderAsync(owner, order, Version(detail)));
        reversed.GetProperty("completedWithoutStock").GetBoolean().Should().BeFalse();
        (await ProductionMovementsAsync(order)).Should().BeEmpty();
        await FinishStepsAsync(owner, order);
        await OkAsync(await CompleteWithAsync(owner, order, await OrderVersionAsync(order)));
        (await ProductionMovementsAsync(order)).Should().ContainSingle();
    }

    [Fact]
    public async Task Estorno_lanca_o_inverso_cancela_as_series_e_a_reconclusao_usa_perna_nova()
    {
        var (owner, order, _, product, steel) = await FinishedDatasetAsync(serial: true, quantity: 2m);
        await OkAsync(await CompleteWithAsync(owner, order, await OrderVersionAsync(order)));
        var entry = (await ProductionMovementsAsync(order)).Single();
        var preview = await OkAsync(await owner.GetAsync($"{Base}/{order}/completion"));
        preview.GetProperty("stock").GetProperty("entryMovementNumber").GetString().Should().Be(entry.Number);
        preview.GetProperty("stock").GetProperty("serialsToCancel").GetInt32().Should().Be(2);

        var reversed = await OkAsync(await ReverseOrderAsync(owner, order, Version(preview)));
        reversed.GetProperty("status").GetString().Should().Be("InProgress");
        reversed.GetProperty("entryMovementId").ValueKind.Should().Be(JsonValueKind.Null);
        var inverse = await WithDbAsync(db => db.StockMovements.AsNoTracking().SingleAsync(m => m.ReversesMovementId == entry.Id));
        (inverse.Quantity, inverse.UnitCost).Should().Be((-2m, entry.UnitCost), "CA 10: movimento inverso pelo custo da entrada");
        (await SerialsAsync(order)).Should().OnlyContain(s => s.Status == SerialState.Cancelled && s.CancelledAt != null);
        (await ComponentsAsync(order)).Should().OnlyContain(c => c.ReversedAt != null, "o vínculo de lotes fica estornado");
        (await WithDbAsync(db => db.StockPositions.AsNoTracking().Where(p => p.ItemId == product).SumAsync(p => p.QuantityOnHand))).Should().Be(0m);
        await DrainAsync();
        (await ReservationsOfAsync(order)).Should().Contain(r => r.ItemId == steel && r.Status == ReservationState.Active, "CA 10: reserva reconciliada a partir da BOM");

        await FinishStepsAsync(owner, order);
        await OkAsync(await CompleteWithAsync(owner, order, await OrderVersionAsync(order)));
        var entries = await ProductionMovementsAsync(order);
        entries.Where(m => m.ReversesMovementId == null).Select(m => m.Leg).Should().Equal(1, 2);
        (await SerialsAsync(order)).Count(s => s.Status == SerialState.InStock).Should().Be(2, "séries novas, sem repetir as canceladas");
        (await SerialsAsync(order)).Select(s => s.SerialNumber).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Estorno_recusado_com_serie_expedida_e_sem_saldo()
    {
        var (owner, order, _, _, _) = await FinishedDatasetAsync(serial: true, quantity: 2m);
        await OkAsync(await CompleteWithAsync(owner, order, await OrderVersionAsync(order)));
        var shipped = (await SerialsAsync(order))[0].Id;
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE inventory.serials SET status = 'Shipped' WHERE id = {shipped}"));

        var preview = await OkAsync(await owner.GetAsync($"{Base}/{order}/completion"));
        preview.GetProperty("canReverse").GetBoolean().Should().BeFalse();
        preview.GetProperty("reverseBlocker").GetString().Should().Be("O produto já foi expedido.");
        var refused = await ReverseOrderAsync(owner, order, Version(preview));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 10");
        Code(await JsonAsync(refused)).Should().Be("serial_shipped");
        (await OrderAsync(order)).Status.Should().Be(ProductionOrderStatus.Completed, "nada grava");
        (await SerialsAsync(order)).Count(s => s.Status == SerialState.InStock).Should().Be(1);

        // Sem série: o produto saiu do estoque por outro caminho; o estorno fica sem saldo.
        var (owner2, plain, _, product, _) = await FinishedDatasetAsync(serial: false);
        await OkAsync(await CompleteWithAsync(owner2, plain, await OrderVersionAsync(plain)));
        var entry = (await ProductionMovementsAsync(plain)).Single();
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await scope.ServiceProvider.GetRequiredService<IStockMovements>().PostAsync(new StockPosting(StockMovementType.Issue, product, entry.LocationId, null, -1m, null, StockSources.ManualPosting, Guid.NewGuid())))
                .IsSuccess.Should().BeTrue();
            await transaction.CommitAsync();
        }

        var noStock = await ReverseOrderAsync(owner2, plain, await OrderVersionAsync(plain));
        noStock.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(noStock)).Should().Be("insufficient_stock");
        (await OrderAsync(plain)).Status.Should().Be(ProductionOrderStatus.Completed);
        (await WithDbAsync(db => db.StockMovements.AsNoTracking().AnyAsync(m => m.ReversesMovementId == entry.Id))).Should().BeFalse();
    }

    [Fact]
    public async Task Series_tem_as_tres_camadas_e_IDOR()
    {
        var (owner, order, _, product, _) = await FinishedDatasetAsync(serial: true, quantity: 1m);
        await OkAsync(await CompleteWithAsync(owner, order, await OrderVersionAsync(order)));
        var serial = (await SerialsAsync(order)).Single().Id;
        var (_, productionViewer) = await ActorAsync("production:view");
        (await productionViewer.GetAsync($"{Inventory}/production-orders/{order}/serials")).StatusCode.Should().Be(HttpStatusCode.OK, "CA 11: production.view");
        (await productionViewer.GetAsync($"{Inventory}/production-orders/{order}/serials/{serial}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await productionViewer.GetAsync($"{Inventory}/items/{product}/serials")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "aba do item com inventory.view");
        (await ReverseOrderAsync(productionViewer, order, await OrderVersionAsync(order))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "estornar sem production.approve");
        var (_, nobody) = await ActorAsync(null);
        var forbidden = await nobody.GetAsync($"{Inventory}/production-orders/{order}/serials");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(forbidden)).Should().Be("forbidden");

        // IDOR: série de outra OS e item que não existe.
        var (otherOwner, other, _, _, _) = await FinishedDatasetAsync(serial: false);
        (await otherOwner.GetAsync($"{Inventory}/production-orders/{other}/serials/{serial}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"{Inventory}/items/{Guid.CreateVersion7()}/serials")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"{Inventory}/production-orders/{Guid.CreateVersion7()}/serials")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await SetModuleReleasedAsync("inventory", false);
        try
        {
            var blocked = await owner.GetAsync($"{Inventory}/production-orders/{order}/serials");
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(blocked)).Should().Be("module_not_released");
        }
        finally
        {
            await SetModuleReleasedAsync("inventory", true);
        }

        await SetModuleReleasedAsync("production", false);
        try
        {
            var blocked = await owner.GetAsync($"{Base}/{order}/completion");
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(blocked)).Should().Be("module_not_released");
        }
        finally
        {
            await SetModuleReleasedAsync("production", true);
        }
    }

    [Fact]
    public async Task Duas_conclusoes_juntas_lancam_uma_entrada_e_as_series_nao_repetem()
    {
        var (owner, order, _, _, _) = await FinishedDatasetAsync(serial: true, quantity: 3m);
        var (_, other) = await ActorAsync(Group.OwnerSeedKey);
        var version = await OrderVersionAsync(order);
        var results = await Task.WhenAll(CompleteWithAsync(owner, order, version), CompleteWithAsync(other, order, version));

        results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict], "CA 13: xmin");
        (await ProductionMovementsAsync(order)).Should().ContainSingle("a chave de idempotência e o xmin seguram a entrada");
        var serials = await SerialsAsync(order);
        serials.Should().HaveCount(3);
        serials.Select(s => s.SerialNumber).Should().OnlyHaveUniqueItems();
        (await OutboxAsync<ProductionOrderCompleted>(order)).Should().Be(1);
    }

    [Fact]
    public async Task DTOs_da_OS_e_das_series_nao_trazem_custo_sem_a_permissao()
    {
        var (owner, order, _, _, _) = await FinishedDatasetAsync(serial: true, quantity: 1m);
        await LaborCostAsync(order, 80m);
        await OkAsync(await CompleteWithAsync(owner, order, await OrderVersionAsync(order)));
        var (_, viewer) = await ActorAsync("production:view");
        var texts = new[]
        {
            (await viewer.GetAsync($"{Base}/{order}")).Content.ReadAsStringAsync(),
            (await viewer.GetAsync($"{Inventory}/production-orders/{order}/serials")).Content.ReadAsStringAsync(),
        };
        foreach (var body in await Task.WhenAll(texts))
        {
            body.Should().NotContainAny(["unitCost", "averageCost", "totalCost", "\"cost\""], "CA 14: DTO sem custo");
        }

        (await viewer.GetAsync($"{Base}/{order}/costs")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "custo só com projects.view_cost");
    }

    [Fact]
    public async Task Historico_e_linha_do_tempo_mostram_unidades_series_e_custo_so_com_permissao()
    {
        var (owner, order, project, _, _) = await FinishedDatasetAsync(serial: true, quantity: 2m);
        await OkAsync(await CompleteWithAsync(owner, order, await OrderVersionAsync(order)));
        var serials = (await SerialsAsync(order)).Select(s => s.SerialNumber).ToList();
        var number = (await OrderAsync(order)).Number;

        var history = await TextsAsync(owner, $"{Base}/{order}/history?pageSize=100");
        history.Should().Contain("Completed", "CA 15: a conclusão no histórico da OS");
        var timeline = await TextsAsync(owner, $"/api/v1/projects/{project}/timeline");
        timeline.Should().Contain($"{number} concluída: 2 unidades no estoque ({serials[0]}, {serials[1]}) · custo unitário R$ ");
        var (_, blind) = await ActorAsync("projects:view;production:view");
        var blindTimeline = await TextsAsync(blind, $"/api/v1/projects/{project}/timeline");
        blindTimeline.Should().Contain($"{number} concluída: 2 unidades no estoque ({serials[0]}, {serials[1]})").And.NotContain("custo unitário", "sem projects.view_cost");
        var kardex = await OkAsync(await owner.GetAsync($"{Inventory}/movements?productionOrderId={order}&type=Production"));
        kardex.GetProperty("items").EnumerateArray().Should().ContainSingle(m => m.GetProperty("type").GetString() == "Production", "o item mostra a entrada no kardex");
    }
}
