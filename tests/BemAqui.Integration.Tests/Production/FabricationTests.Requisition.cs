using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

public abstract partial class FabricationTestBase
{
    protected const string Inventory = "/api/v1/inventory";

    /// <summary>
    /// OS com BOM liberada de um item de estoque (necessidade = <paramref name="perUnit"/> × 2), estoque recebido a <paramref name="unitCost"/>
    /// num local novo e as reservas já feitas (consumidor do BomReleased pela inbox). Devolve o dono, a OS, o projeto, o item e a reserva.
    /// </summary>
    protected async Task<(HttpClient Owner, Guid Order, Guid Project, Guid Item, JsonElement Line)> ReservedOrderAsync(decimal perUnit = 6m, decimal stock = 20m, decimal unitCost = 100m)
    {
        var (owner, order, _, _) = await BomOrderAsync(quantity: 2m);
        var (_, engineer) = await ActorAsync("engineering");
        var steel = await MaterialAsync("Chapa " + Guid.NewGuid().ToString("N")[..4]);
        await ReceiveStockAsync(steel, stock, unitCost);
        await ReleasedBomAsync(engineer, order, BomLine(steel, perUnit));
        await DrainOutboxAsync();
        var draft = await OkAsync(await owner.GetAsync($"{Inventory}/production-orders/{order}/requisitions/draft"));
        var line = draft.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("itemId").GetGuid() == steel);
        return (owner, order, (await OrderAsync(order)).ProjectId, steel, line);
    }

    protected static object ReqLine(JsonElement draftLine, decimal quantity, string? overReason = null, Guid? lot = null) => new
    {
        reservationId = draftLine.GetProperty("reservationId").GetGuid(), itemId = draftLine.GetProperty("itemId").GetGuid(),
        locationId = draftLine.GetProperty("locationId").GetGuid(), lotId = lot, quantity, overReason,
    };

    protected static Task<HttpResponseMessage> IssueAsync(HttpClient client, Guid order, Guid? key = null, params object[] lines) =>
        client.SendAsync(Post($"{Inventory}/production-orders/{order}/requisitions", new { lines }, key));

    protected static async Task<JsonElement> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    protected async Task<List<StockMovement>> MovementsOfAsync(Guid order) =>
        await WithDbAsync(db => db.StockMovements.AsNoTracking().Where(m => m.ProductionOrderId == order).OrderBy(m => m.PostedSeq).ToListAsync());

    protected async Task<List<Reservation>> ReservationsOfAsync(Guid order) =>
        await WithDbAsync(db => db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == order).ToListAsync());

    protected async Task<List<ProjectCost>> MaterialLedgerAsync(Guid project) =>
        await WithDbAsync(db => db.ProjectCosts.AsNoTracking().Where(c => c.ProjectId == project && (c.Source == ProjectCostSource.StockIssue || c.Source == ProjectCostSource.StockReturn))
            .OrderBy(c => c.RecordedAt).ToListAsync());

    protected static async Task<decimal> RealizedMaterialAsync(HttpClient owner, Guid project)
    {
        var costs = await OkAsync(await owner.GetAsync($"/api/v1/projects/{project}/costs"));
        return costs.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("costType").GetString() == "Material").GetProperty("realized").GetDecimal();
    }

    protected sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>
/// EST-03, requisição de material da OS e devolução, com PostgreSQL real (movimenta estoque e custo): BOM liberada pela ENG-02, reserva
/// pela EST-02, baixa síncrona pelo motor com a reserva consumida e o evento na mesma transação (CA 1, 2), excedente e material extra
/// (CA 3), aprovação acima de R$ 1.000,00 (CA 4), pedido do operador (CA 5 a 7), razão do projeto pelo consumidor (CA 8, 9), Custos da
/// OS (CA 10), recomposição (CA 11), devolução pelo custo original (CA 12), OS cancelada (CA 13), estorno (CA 14), três camadas e IDOR
/// (CA 15), concorrência (CA 16) e histórico (CA 17). Cada teste cria a própria OS, os itens e os locais e filtra pelos ids dele.
/// </summary>
public partial class FabricationOrdersTests
{
    [Fact]
    public async Task Baixa_sincrona_consome_a_reserva_grava_o_movimento_e_o_evento_na_mesma_transacao()
    {
        var (owner, order, project, steel, line) = await ReservedOrderAsync();
        line.GetProperty("reserved").GetDecimal().Should().Be(12m, "o diálogo traz todo o reservado a baixar (CA 1)");
        var (_, warehouse) = await ActorAsync("warehouse");
        var key = Guid.NewGuid();

        var created = await CreatedAsync(await IssueAsync(warehouse, order, key, ReqLine(line, 12m)));
        var again = await IssueAsync(warehouse, order, key, ReqLine(line, 12m));

        created.GetProperty("status").GetString().Should().Be("Completed");
        created.GetProperty("number").GetString().Should().MatchRegex(@"^REQ-\d{4}-\d{4}$", "D15");
        (await JsonAsync(again)).GetProperty("id").GetGuid().Should().Be(created.GetProperty("id").GetGuid(), "Idempotency-Key repetida devolve a mesma requisição");
        var movements = await MovementsOfAsync(order);
        movements.Should().ContainSingle();
        (movements[0].Type, movements[0].Quantity, movements[0].SourceType, movements[0].ProjectId, movements[0].TotalCost)
            .Should().Be((StockMovementType.Issue, -12m, StockSources.Requisition, (Guid?)project, -1200m));
        var reservations = await ReservationsOfAsync(order);
        (reservations.Sum(r => r.Quantity), reservations.Sum(r => r.QuantityIssued)).Should().Be((0m, 12m));
        reservations.Should().OnlyContain(r => r.Status == ReservationState.Consumed, "baixou tudo o que a reserva pedia");
        var position = await WithDbAsync(db => db.StockPositions.AsNoTracking().SingleAsync(p => p.ItemId == steel));
        (position.QuantityOnHand, position.QuantityReserved).Should().Be((8m, 0m), "baixou o físico e o reservado da posição");
        (await OutboxAsync<StockIssued>(movements[0].Id)).Should().Be(1, "o StockIssued vai para o outbox na mesma transação");
    }

    [Fact]
    public async Task Dataset_fixo_baixa_de_1200_e_devolucao_de_200_dao_realizado_de_material_1000()
    {
        var (owner, order, project, steel, line) = await ReservedOrderAsync(unitCost: 100m);
        var issue = await CreatedAsync(await IssueAsync(owner, order, null, ReqLine(line, 12m)));
        // O custo médio muda depois da baixa: a devolução sai pelo custo da baixa original (ponto 58).
        await ReceiveStockAsync(steel, 10m, 160m);
        var issueLine = issue.GetProperty("lines")[0].GetProperty("id").GetGuid();

        var returned = await CreatedAsync(await owner.SendAsync(Post($"{Inventory}/production-orders/{order}/returns",
            new { locationId = line.GetProperty("locationId").GetGuid(), lines = new[] { new { issueLineId = issueLine, quantity = 2m } } })));
        await DrainAsync();

        returned.GetProperty("kind").GetString().Should().Be("Return");
        var back = (await MovementsOfAsync(order)).Single(m => m.Type == StockMovementType.Return);
        (back.Quantity, back.UnitCost, back.TotalCost).Should().Be((2m, 100m, 200m));
        var ledger = await MaterialLedgerAsync(project);
        ledger.Select(c => (c.Source, c.Amount, c.Seq, c.IsBackfill)).Should().Equal((ProjectCostSource.StockIssue, 1200m, 1, false), (ProjectCostSource.StockReturn, -200m, 1, false));
        (await RealizedMaterialAsync(owner, project)).Should().Be(1000m, "a baixa líquida de devolução (CA 9)");
        var cost = await OkAsync(await owner.GetAsync($"{Inventory}/production-orders/{order}/material-cost"));
        cost.GetProperty("total").GetDecimal().Should().Be(1000m, "a aba Custos da OS lê o motor (CA 10)");
        var reservations = await ReservationsOfAsync(order);
        reservations.Sum(r => r.QuantityIssued).Should().Be(10m);
        reservations.Sum(r => r.Quantity).Should().Be(2m, "a BOM ainda pede 12: a devolução volta a reservar (reconciliação da EST-02)");
    }

    [Fact]
    public async Task Excedente_exige_motivo_vai_ate_o_disponivel_e_material_extra_baixa_do_disponivel()
    {
        var (owner, order, _, _, line) = await ReservedOrderAsync(stock: 15m);
        var extra = await MaterialAsync("Parafuso " + Guid.NewGuid().ToString("N")[..4]);
        await ReceiveStockAsync(extra, 4m, 2m);
        var extraStock = await OkAsync(await owner.GetAsync($"{Inventory}/requisitions/item-stock/{extra}"));
        var extraLocation = extraStock.GetProperty("locations").EnumerateArray().First(l => l.GetProperty("onHand").GetDecimal() > 0).GetProperty("locationId").GetGuid();

        var noReason = await IssueAsync(owner, order, null, ReqLine(line, 14m));
        noReason.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "acima do reservado pede motivo (CA 3)");
        var tooMuch = await IssueAsync(owner, order, null, ReqLine(line, 16m, "Perda no corte"));
        tooMuch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(tooMuch)).Should().Be(StockLedger.InsufficientStockCode, "acima do disponível (15 − 12 reservados = 3 livres)");
        (await MovementsOfAsync(order)).Should().BeEmpty("nada é gravado na recusa");

        var created = await CreatedAsync(await IssueAsync(owner, order, null, ReqLine(line, 14m, "Perda no corte"),
            new { reservationId = (Guid?)null, itemId = extra, locationId = extraLocation, lotId = (Guid?)null, quantity = 1m, overReason = "Fixação extra" }));
        var lines = created.GetProperty("lines").EnumerateArray().ToList();
        lines.Single(l => l.GetProperty("itemId").GetGuid() == line.GetProperty("itemId").GetGuid()).GetProperty("overQuantity").GetDecimal().Should().Be(2m);
        lines.Single(l => l.GetProperty("itemId").GetGuid() == extra).GetProperty("isExtra").GetBoolean().Should().BeTrue();
        (await MovementsOfAsync(order)).Should().HaveCount(2).And.OnlyContain(m => m.Type == StockMovementType.Issue);
        (await MovementsOfAsync(order)).Single(m => m.ItemId == extra).Reason.Should().Be("Consumo acima da BOM: Fixação extra");
    }

    [Fact]
    public async Task Acima_de_1000_vai_para_aprovacao_sem_baixar_e_outra_pessoa_aprova_sem_editar()
    {
        var (owner, order, _, _, line) = await ReservedOrderAsync(stock: 40m, unitCost: 100m);
        var (warehouseId, warehouse) = await ActorAsync("warehouse");
        var (approverId, approver) = await ActorAsync(Group.OwnerSeedKey);

        var sent = await CreatedAsync(await IssueAsync(warehouse, order, null, ReqLine(line, 23m, "Refazer painel")));

        sent.GetProperty("status").GetString().Should().Be("PendingApproval", "excedente 11 × R$ 100,00 = R$ 1.100,00");
        sent.GetProperty("overBomValue").GetDecimal().Should().Be(1100m);
        (await MovementsOfAsync(order)).Should().BeEmpty("enviar para aprovação não baixa nada");
        var id = sent.GetProperty("id").GetGuid();
        var notices = await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == approverId && n.Type == MaterialRequisitionService.ApprovalType).ToListAsync());
        notices.Should().ContainSingle(n => n.Link.EndsWith(id.ToString(), StringComparison.Ordinal)).Which.Title.Should().Contain("aguardando aprovação: R$ 1.100,00 acima da BOM.");

        var edit = await approver.SendAsync(Post($"{Inventory}/requisitions/{id}/approve", new { comment = (string?)null, version = Version(sent), quantity = 1m }));
        edit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o aprovador decide sobre o que foi enviado");
        (await warehouse.SendAsync(Post($"{Inventory}/requisitions/{id}/approve", new { version = Version(sent) }))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem inventory.approve");
        (await approver.PostAsJsonAsync($"{Inventory}/requisitions/{id}/reject", new { comment = "", version = Version(sent) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "reprovar exige comentário");

        var approved = await OkAsync(await approver.SendAsync(Post($"{Inventory}/requisitions/{id}/approve", new { comment = "Ok", version = Version(sent) })));
        approved.GetProperty("status").GetString().Should().Be("Completed");
        approved.GetProperty("decidedByName").GetString().Should().NotBeNullOrEmpty();
        (await MovementsOfAsync(order)).Should().ContainSingle().Which.Quantity.Should().Be(-23m);
        var warehouseNotices = await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == warehouseId && n.Type == MaterialRequisitionService.DecisionType).ToListAsync());
        warehouseNotices.Should().Contain(n => n.Title.Contains("aprovada", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Autor_nao_aprova_a_propria_requisicao_quando_ha_outro_aprovador()
    {
        var (owner, order, _, _, line) = await ReservedOrderAsync(stock: 40m, unitCost: 100m);
        await ActorAsync(Group.OwnerSeedKey);
        var sent = await CreatedAsync(await IssueAsync(owner, order, null, ReqLine(line, 23m, "Refazer painel")));

        var own = await owner.SendAsync(Post($"{Inventory}/requisitions/{sent.GetProperty("id").GetGuid()}/approve", new { version = Version(sent) }));

        own.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(own)).Should().Be("own_requisition");
    }

    [Fact]
    public async Task Pedido_do_operador_nao_baixa_avisa_o_almoxarifado_e_e_confirmado_ou_recusado()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync(2);
        var (_, engineer) = await ActorAsync("engineering");
        var steel = await MaterialAsync("Cabo " + Guid.NewGuid().ToString("N")[..4]);
        await ReceiveStockAsync(steel, 10m, 10m);
        await ReleasedBomAsync(engineer, orders[0], BomLine(steel, 3m));
        await DrainOutboxAsync();
        var (warehouseId, warehouse) = await ActorAsync("warehouse");
        var (_, _, operatorClient, operatorName) = await OperatorAsync(mine);
        var ready = await StepIdAsync(orders[0], 1);
        var waiting = await StepIdAsync(orders[0], 2);

        var material = await OkAsync(await operatorClient.GetAsync($"{Shopfloor}/steps/{ready}/material"));
        material.GetProperty("bomReleased").GetBoolean().Should().BeTrue();
        material.GetProperty("items")[0].GetProperty("itemId").GetGuid().Should().Be(steel);
        (await operatorClient.SendAsync(Post($"{Shopfloor}/steps/{waiting}/material-requests", new { lines = new[] { new { itemId = steel, quantity = 1m } } })))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "etapa que não está pronta nem em andamento");
        (await operatorClient.SendAsync(Post($"{Shopfloor}/steps/{ready}/material-requests", new { lines = new[] { new { itemId = steel, quantity = 1m, lotId = Guid.NewGuid() } } })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "o pedido só aceita item e quantidade");
        var request = await OkAsync(await operatorClient.SendAsync(Post($"{Shopfloor}/steps/{ready}/material-requests", new { lines = new[] { new { itemId = steel, quantity = 6m } } })));

        request.GetProperty("state").GetString().Should().Be("Waiting");
        (await MovementsOfAsync(orders[0])).Should().BeEmpty("o pedido do operador nunca baixa (CA 6)");
        var id = request.GetProperty("id").GetGuid();
        var notice = await WithDbAsync(db => db.Notifications.AsNoTracking().SingleAsync(n => n.UserId == warehouseId && n.Link == MaterialRequisitionService.Link + id));
        notice.Title.Should().Be($"Material pedido para a {(await OrderAsync(orders[0])).Number}, etapa Corte, por {operatorName}.");

        var detail = await OkAsync(await warehouse.GetAsync($"{Inventory}/requisitions/{id}"));
        detail.GetProperty("status").GetString().Should().Be("Requested");
        detail.GetProperty("canConfirm").GetBoolean().Should().BeTrue();
        var draft = await OkAsync(await warehouse.GetAsync($"{Inventory}/production-orders/{orders[0]}/requisitions/draft"));
        var line = draft.GetProperty("lines")[0];
        var confirmed = await OkAsync(await warehouse.SendAsync(Post($"{Inventory}/requisitions/{id}/confirm", new { lines = new[] { ReqLine(line, 3m) }, version = Version(detail) })));
        confirmed.GetProperty("status").GetString().Should().Be("Completed");
        confirmed.GetProperty("completedByName").GetString().Should().NotBeNullOrEmpty();
        (await MovementsOfAsync(orders[0])).Should().ContainSingle().Which.Quantity.Should().Be(-3m, "o almoxarife ajustou a quantidade ao reservado");
        var after = await OkAsync(await operatorClient.GetAsync($"{Shopfloor}/steps/{ready}/material"));
        after.GetProperty("requests")[0].GetProperty("state").GetString().Should().Be("Separated");

        var second = await OkAsync(await operatorClient.SendAsync(Post($"{Shopfloor}/steps/{ready}/material-requests", new { lines = new[] { new { itemId = steel, quantity = 1m } } })));
        var secondDetail = await OkAsync(await warehouse.GetAsync($"{Inventory}/requisitions/{second.GetProperty("id").GetGuid()}"));
        await OkAsync(await warehouse.PostAsJsonAsync($"{Inventory}/requisitions/{second.GetProperty("id").GetGuid()}/decline", new { reason = "Já separado no carrinho", version = Version(secondDetail) }));
        var declined = await OkAsync(await operatorClient.GetAsync($"{Shopfloor}/steps/{ready}/material"));
        var row = declined.GetProperty("requests").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == second.GetProperty("id").GetGuid());
        (row.GetProperty("state").GetString(), row.GetProperty("declineReason").GetString()).Should().Be(("Declined", "Já separado no carrinho"));
    }

    [Fact]
    public async Task Pedido_do_operador_recusa_OS_cancelada()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(mine);
        var ready = await StepIdAsync(orders[0], 1);
        await WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == orders[0]).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, ProductionOrderStatus.Cancelled)));

        (await operatorClient.SendAsync(Post($"{Shopfloor}/steps/{ready}/material-requests", new { lines = new[] { new { itemId = Guid.NewGuid(), quantity = 1m } } })))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "OS cancelada sai da fila (D11)");
    }

    [Fact]
    public async Task Consumidor_do_Commercial_e_idempotente_e_aceita_o_estorno_antes_do_lancamento()
    {
        var (_, order, _, _) = await BomOrderAsync();
        var project = (await OrderAsync(order)).ProjectId;
        var movement = Guid.CreateVersion7();
        var item = Guid.NewGuid();
        async Task Handle(Func<MaterialCostConsumer, Task> action)
        {
            await using var scope = await ScopeAsync();
            await action(scope.ServiceProvider.GetRequiredService<MaterialCostConsumer>());
        }

        var at = DateTimeOffset.UtcNow;
        await Handle(c => c.HandleAsync(new StockIssued(movement, StockSources.Requisition, Guid.NewGuid(), 2, project, order, item, 3m, 300m, 0m, at), default));
        await Handle(c => c.HandleAsync(new StockIssued(movement, StockSources.Requisition, Guid.NewGuid(), 1, project, order, item, 3m, 0m, 300m, at), default));
        await Handle(c => c.HandleAsync(new StockIssued(movement, StockSources.Requisition, Guid.NewGuid(), 2, project, order, item, 3m, 300m, 0m, at), default));

        var rows = await WithDbAsync(db => db.ProjectCosts.AsNoTracking().Where(c => c.SourceId == movement).ToListAsync());
        rows.Should().ContainSingle("o lançamento que chegou depois do estorno e o repetido não gravam");
        rows.Sum(r => r.Amount).Should().Be(0m);
        rows[0].CostType.Should().Be(BemAqui.SharedKernel.CostType.Material);
    }

    [Fact]
    public async Task Recomposicao_grava_a_saida_com_projeto_anterior_uma_vez_mesmo_com_o_consumidor_ao_vivo()
    {
        var (owner, order, project, steel, line) = await ReservedOrderAsync(unitCost: 50m);
        await CreatedAsync(await IssueAsync(owner, order, null, ReqLine(line, 2m)));
        var movement = (await MovementsOfAsync(order)).Single();

        async Task<int> BackfillAsync()
        {
            await using var scope = await ScopeAsync();
            var later = new FixedClock(DateTimeOffset.UtcNow.AddHours(2));
            var backfill = new MaterialCostBackfill(scope.ServiceProvider.GetRequiredService<ICommercialDb>(), scope.ServiceProvider.GetRequiredService<IStockMovements>(),
                scope.ServiceProvider.GetRequiredService<TenantContextHolder>(), later);
            return await backfill.RunAsync();
        }

        await Task.WhenAll(BackfillAsync(), BackfillAsync());
        await DrainAsync();
        await BackfillAsync();

        var rows = await WithDbAsync(db => db.ProjectCosts.AsNoTracking().Where(c => c.SourceId == movement.Id).ToListAsync());
        rows.Should().ContainSingle("duas recomposições e o consumidor ao vivo gravam uma vez só (ADR 0025 §3)");
        rows[0].Amount.Should().Be(100m);
        // Quem grava primeiro (a recomposição ou o consumidor ao vivo) depende da ordem de chegada: o teste não supõe a ordem; a marca
        // "Calculado na implantação" acompanha a linha gravada.
        var costs = await OkAsync(await owner.GetAsync($"/api/v1/projects/{project}/costs"));
        costs.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("costType").GetString() == "Material").GetProperty("includesBackfill").GetBoolean()
            .Should().Be(rows[0].IsBackfill);
        steel.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Estorno_pelo_autor_ate_o_limite_tira_o_custo_e_acima_dele_precisa_de_outra_pessoa()
    {
        var (owner, order, project, _, line) = await ReservedOrderAsync(unitCost: 100m);
        var (_, warehouse) = await ActorAsync("warehouse");
        var small = await CreatedAsync(await IssueAsync(warehouse, order, null, ReqLine(line, 5m)));
        var big = await CreatedAsync(await IssueAsync(warehouse, order, null, ReqLine(line, 11m, "Painel refeito")));

        var reversed = await OkAsync(await warehouse.SendAsync(Post($"{Inventory}/requisitions/{small.GetProperty("id").GetGuid()}/reverse", new { reason = "Lançada na OS errada", version = Version(small) })));
        var denied = await warehouse.SendAsync(Post($"{Inventory}/requisitions/{big.GetProperty("id").GetGuid()}/reverse", new { reason = "Errada", version = Version(big) }));

        reversed.GetProperty("status").GetString().Should().Be("Reversed", "R$ 500,00, pelo autor, em menos de 24 h");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, "R$ 1.100,00 passa do limite: precisa de outra pessoa com inventory.approve");
        Code(await JsonAsync(denied)).Should().Be("reverse_needs_approver");
        (await ReservationsOfAsync(order)).Sum(r => r.Quantity).Should().Be(1m, "a reserva volta pela reconciliação: a BOM pede 12, há 11 baixados");
        await OkAsync(await owner.SendAsync(Post($"{Inventory}/requisitions/{big.GetProperty("id").GetGuid()}/reverse", new { reason = "Errada", version = Version(big) })));
        await DrainAsync();

        var ledger = await MaterialLedgerAsync(project);
        ledger.Where(c => c.Seq == 2).Select(c => c.Amount).Should().BeEquivalentTo([-500m, -1100m], "o estorno (valor novo 0) tira o custo do projeto");
        ledger.Sum(c => c.Amount).Should().Be(0m);
        (await ReservationsOfAsync(order)).Sum(r => r.Quantity).Should().Be(12m);
        (await MovementsOfAsync(order)).Where(m => m.ReversesMovementId != null).Should().HaveCount(2, "movimentos inversos pelo motor");
    }

    [Fact]
    public async Task Requisicao_com_devolucao_nao_se_estorna_e_OS_cancelada_mantem_a_perda_no_projeto()
    {
        var (owner, order, _, _, line) = await ReservedOrderAsync();
        var issue = await CreatedAsync(await IssueAsync(owner, order, null, ReqLine(line, 4m)));
        var issueLine = issue.GetProperty("lines")[0].GetProperty("id").GetGuid();
        await CreatedAsync(await owner.SendAsync(Post($"{Inventory}/production-orders/{order}/returns",
            new { locationId = line.GetProperty("locationId").GetGuid(), lines = new[] { new { issueLineId = issueLine, quantity = 1m } } })));

        var blocked = await owner.SendAsync(Post($"{Inventory}/requisitions/{issue.GetProperty("id").GetGuid()}/reverse", new { reason = "Errada", version = Version(issue) }));
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(blocked)).Should().Be("requisition_has_returns");

        var open = await owner.SendAsync(Post($"{Inventory}/production-orders/{order}/losses", new { reason = "Sucata", lines = new[] { new { issueLineId = issueLine, quantity = 1m } } }));
        open.StatusCode.Should().Be(HttpStatusCode.Conflict, "só a OS cancelada registra perda");
        await WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == order).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, ProductionOrderStatus.Cancelled)));
        var tooMuch = await owner.SendAsync(Post($"{Inventory}/production-orders/{order}/losses", new { reason = "Sucata", lines = new[] { new { issueLineId = issueLine, quantity = 4m } } }));
        tooMuch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "só o baixado e não devolvido (3)");
        var loss = await CreatedAsync(await owner.SendAsync(Post($"{Inventory}/production-orders/{order}/losses", new { reason = "Sucata", lines = new[] { new { issueLineId = issueLine, quantity = 3m } } })));
        loss.GetProperty("kind").GetString().Should().Be("Loss");
        loss.GetProperty("rejectReason").GetString().Should().Be("Sucata");
        var tab = await OkAsync(await owner.GetAsync($"{Inventory}/production-orders/{order}/requisitions"));
        tab.GetProperty("orderCancelled").GetBoolean().Should().BeTrue();
        tab.GetProperty("returnable").GetArrayLength().Should().Be(0, "o que sobrou virou perda");
        (await MovementsOfAsync(order)).Should().HaveCount(2, "a perda não movimenta estoque");
    }

    [Fact]
    public async Task Tres_camadas_IDOR_e_lote_de_outro_item()
    {
        var (owner, order, _, steel, line) = await ReservedOrderAsync();
        var (otherOwner, otherOrder, _, _, _) = await ReservedOrderAsync();
        var (_, viewer) = await ActorAsync("production:view");
        var (_, nobody) = await ActorAsync(null);
        var created = await CreatedAsync(await IssueAsync(owner, order, null, ReqLine(line, 1m)));
        var id = created.GetProperty("id").GetGuid();
        var otherDraft = await OkAsync(await otherOwner.GetAsync($"{Inventory}/production-orders/{otherOrder}/requisitions/draft"));
        var otherReq = await CreatedAsync(await IssueAsync(otherOwner, otherOrder, null, ReqLine(otherDraft.GetProperty("lines")[0], 1m)));

        (await IssueAsync(viewer, order, null, ReqLine(line, 1m))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "baixar exige inventory.create");
        var noGroup = await nobody.GetAsync($"{Inventory}/requisitions/{id}");
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.GetAsync($"{Inventory}/production-orders/{otherOrder}/requisitions/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "requisição de outra OS");
        (await owner.GetAsync($"{Inventory}/requisitions/{id}/lines/{otherReq.GetProperty("lines")[0].GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "linha de outra requisição");
        (await owner.GetAsync($"{Inventory}/production-orders/{order}/requisitions/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        Guid otherLot;
        await using (var scope = await ScopeAsync())
        {
            var other = await MaterialAsync("Outro " + Guid.NewGuid().ToString("N")[..4]);
            otherLot = await scope.ServiceProvider.GetRequiredService<IStockMovements>().EnsureLotAsync(other, "LT-" + Guid.NewGuid().ToString("N")[..4]);
        }

        (await IssueAsync(owner, order, null, ReqLine(line, 1m, lot: otherLot))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "lote de outro item");
        steel.Should().NotBeEmpty();

        await SetModuleReleasedAsync("inventory", false);
        try
        {
            var blocked = await owner.GetAsync($"{Inventory}/requisitions/{id}");
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(blocked)).Should().Be("module_not_released");
        }
        finally
        {
            await SetModuleReleasedAsync("inventory", true);
        }
    }

    [Fact]
    public async Task Duas_baixas_simultaneas_da_mesma_reserva_nunca_passam_do_reservado_mais_disponivel()
    {
        var (owner, order, project, steel, line) = await ReservedOrderAsync(stock: 12m);
        var (_, warehouse) = await ActorAsync("warehouse");

        // Com o motivo nas duas, a segunda passa da validação em qualquer ordem e para na trava, com a reserva já baixada: os 12 viram
        // excedente. Sem disponível, 409; ou, como o excedente (R$ 1.200,00) passa do limite, fica PendingApproval sem baixar (CA 4).
        var responses = await Task.WhenAll(IssueAsync(owner, order, null, ReqLine(line, 12m, "Corrida")), IssueAsync(warehouse, order, null, ReqLine(line, 12m, "Corrida")));

        var outcomes = new List<string>();
        foreach (var response in responses)
        {
            outcomes.Add(response.StatusCode == HttpStatusCode.Created ? (await JsonAsync(response)).GetProperty("status").GetString()! : response.StatusCode.ToString());
        }

        outcomes.Should().ContainSingle(o => o == "Completed", "só uma baixa acontece");
        outcomes.Should().Contain(o => o == nameof(HttpStatusCode.Conflict) || o == "PendingApproval", "a outra não baixa nada");
        (await MovementsOfAsync(order)).Sum(m => m.Quantity).Should().Be(-12m);
        var position = await WithDbAsync(db => db.StockPositions.AsNoTracking().SingleAsync(p => p.ItemId == steel));
        (position.QuantityOnHand, position.QuantityReserved).Should().Be((0m, 0m));
        await DrainAsync();
        (await MaterialLedgerAsync(project)).Sum(c => c.Amount).Should().Be(1200m, "o custo não entra duas vezes");
    }

    [Fact]
    public async Task Confirmacao_simultanea_do_mesmo_pedido_baixa_uma_vez()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, engineer) = await ActorAsync("engineering");
        var steel = await MaterialAsync("Trilho " + Guid.NewGuid().ToString("N")[..4]);
        await ReceiveStockAsync(steel, 10m, 10m);
        await ReleasedBomAsync(engineer, orders[0], BomLine(steel, 2m));
        await DrainOutboxAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(mine);
        var ready = await StepIdAsync(orders[0], 1);
        var request = await OkAsync(await operatorClient.SendAsync(Post($"{Shopfloor}/steps/{ready}/material-requests", new { lines = new[] { new { itemId = steel, quantity = 2m } } })));
        var (_, a) = await ActorAsync("warehouse");
        var (_, b) = await ActorAsync("warehouse");
        var id = request.GetProperty("id").GetGuid();
        var detail = await OkAsync(await a.GetAsync($"{Inventory}/requisitions/{id}"));
        var line = (await OkAsync(await a.GetAsync($"{Inventory}/production-orders/{orders[0]}/requisitions/draft"))).GetProperty("lines")[0];

        var responses = await Task.WhenAll(
            a.SendAsync(Post($"{Inventory}/requisitions/{id}/confirm", new { lines = new[] { ReqLine(line, 2m) }, version = Version(detail) })),
            b.SendAsync(Post($"{Inventory}/requisitions/{id}/confirm", new { lines = new[] { ReqLine(line, 2m) }, version = Version(detail) })));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict], "xmin da requisição (CA 16)");
        (await MovementsOfAsync(orders[0])).Should().ContainSingle();
    }

    [Fact]
    public async Task Historico_da_requisicao_e_linha_do_tempo_do_projeto()
    {
        var (owner, order, project, _, line) = await ReservedOrderAsync(unitCost: 100m);
        var created = await CreatedAsync(await IssueAsync(owner, order, null, ReqLine(line, 12m)));
        await DrainAsync();

        var history = await OkAsync(await owner.GetAsync($"{Inventory}/requisitions/{created.GetProperty("id").GetGuid()}/history"));
        history.GetProperty("total").GetInt32().Should().BeGreaterThan(0, "auditoria da requisição e das linhas");
        created.GetProperty("requestedByName").GetString().Should().NotBeNullOrEmpty();
        created.GetProperty("lines")[0].GetProperty("movementNumber").GetString().Should().StartWith("MOV-");
        var timeline = await OkAsync(await owner.GetAsync($"/api/v1/projects/{project}/timeline"));
        var number = (await OrderAsync(order)).Number;
        timeline.EnumerateArray().Select(e => e.GetProperty("title").GetString()).Should().Contain($"Material baixado para a {number}: R$ 1.200,00");
    }
}