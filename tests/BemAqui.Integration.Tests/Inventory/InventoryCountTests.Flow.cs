using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

public partial class InventoryCountTests
{
    [Fact]
    public async Task Abrir_tira_a_foto_e_o_segundo_no_mesmo_local_e_409()
    {
        var (_, owner) = await ActorAsync("owner");
        var location = await LocationAsync();
        var item = await ItemAsync();
        var zero = await ItemAsync();
        await MoveAsync(item, location, 10m);
        await MoveAsync(zero, location, 2m);
        await MoveAsync(zero, location, -2m);

        var count = await OkAsync(await owner.SendAsync(Command(Counts, Open(location))), HttpStatusCode.Created);
        count.GetProperty("number").GetString().Should().MatchRegex(@"^INV-\d{4}-\d{4}$", "D15");
        count.GetProperty("status").GetString().Should().Be("Counting");
        count.GetProperty("lineCount").GetInt32().Should().Be(1, "o escopo padrão é itens com saldo no local");
        count.GetProperty("progress").GetString().Should().Be("0 de 1 item contado");
        var lines = await WithDbAsync(db => db.InventoryCountLines.AsNoTracking().Where(l => l.CountId == Id(count)).ToListAsync());
        lines.Should().ContainSingle(l => l.ItemId == item && l.SystemQty == 10m, "CA 1: o saldo do sistema na abertura");

        var again = await owner.SendAsync(Command(Counts, Open(location)));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var name = await WithDbAsync(db => db.Locations.Where(l => l.Id == location).Select(l => l.Name).SingleAsync());
        Detail(await JsonAsync(again)).Should().Be($"Já há um inventário aberto em {name}: {count.GetProperty("number").GetString()}.", "CA 2");

        // Com movimento: o item zerado que já teve movimento entra.
        var other = await LocationAsync();
        await MoveAsync(zero, other, 1m);
        await MoveAsync(zero, other, -1m);
        var withMovement = await OkAsync(await owner.SendAsync(Command(Counts, Open(other, withMovement: true))), HttpStatusCode.Created);
        withMovement.GetProperty("lineCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Dois_abrir_ao_mesmo_tempo_no_mesmo_local_so_um_nasce()
    {
        var (_, owner) = await ActorAsync("owner");
        var location = await LocationAsync();
        var item = await ItemAsync();
        await MoveAsync(item, location, 5m);
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => owner.SendAsync(Command(Counts, Open(location)))));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict, "CA 2: 409, nunca 500");
    }

    [Fact]
    public async Task Contagem_cega_nao_traz_saldo_movimento_diferenca_nem_custo()
    {
        var (_, owner) = await ActorAsync("owner");
        var (counterId, counter) = await ActorAsync("warehouse");
        var location = await LocationAsync();
        var item = await ItemAsync();
        await MoveAsync(item, location, 7m, 33m);
        var count = await OkAsync(await owner.SendAsync(Command(Counts, Open(location))), HttpStatusCode.Created);

        var raw = await (await counter.GetAsync($"{Counts}/{Id(count)}/counting")).Content.ReadAsStringAsync();
        raw.Should().NotContainAny(["systemQty", "movementDelta", "difference", "unitCost", "averageCost", "\"value\""], "CA 15: contagem cega e nenhum custo");
        var counting = JsonDocument.Parse(raw).RootElement;
        var line = LineOf(counting, item);
        var saved = await OkAsync(await counter.SendAsync(CountRequest(Id(count), line, 6m)));
        saved.GetProperty("countedQty").GetDecimal().Should().Be(6m);
        saved.GetProperty("countedByMe").GetBoolean().Should().BeTrue("CA 3: cada linha grava quem contou");
        saved.GetProperty("countedAt").ValueKind.Should().Be(JsonValueKind.String);
        (await WithDbAsync(db => db.InventoryCountLines.AsNoTracking().SingleAsync(l => l.Id == Id(line)))).CountedBy.Should().Be(counterId);

        // O contador não vê as divergências (inventory.adjust).
        (await counter.GetAsync($"{Counts}/{Id(count)}/divergences")).StatusCode.Should().Be(HttpStatusCode.OK, "o grupo do almoxarifado tem inventory.adjust");
        var (_, viewer) = await ActorAsync("view:inventory");
        (await viewer.GetAsync($"{Counts}/{Id(count)}/divergences")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var viewerRaw = await (await viewer.GetAsync($"{Counts}/{Id(count)}/counting")).Content.ReadAsStringAsync();
        viewerRaw.Should().NotContain("systemQty");
    }

    [Fact]
    public async Task Movimento_durante_a_contagem_entra_na_diferenca()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, counter) = await ActorAsync("warehouse");
        var location = await LocationAsync();
        var item = await ItemAsync();
        await MoveAsync(item, location, 10m);
        var count = await OkAsync(await owner.SendAsync(Command(Counts, Open(location))), HttpStatusCode.Created);
        await MoveAsync(item, location, -3m);
        var counting = await OkAsync(await counter.GetAsync($"{Counts}/{Id(count)}/counting"));
        await OkAsync(await counter.SendAsync(CountRequest(Id(count), LineOf(counting, item), 6m)));
        await MoveAsync(item, location, 4m);

        var divergences = await OkAsync(await owner.GetAsync($"{Counts}/{Id(count)}/divergences"));
        var line = divergences.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("itemId").GetGuid() == item);
        line.GetProperty("movementDelta").GetDecimal().Should().Be(-3m, "CA 5: só os movimentos entre a foto e a contagem da linha");
        line.GetProperty("hadMovement").GetBoolean().Should().BeTrue("a tela mostra Teve movimento durante a contagem");
        line.GetProperty("difference").GetDecimal().Should().Be(-1m, "6 − (10 − 3)");
        line.GetProperty("systemQtyNow").GetDecimal().Should().Be(11m);
        line.GetProperty("after").GetDecimal().Should().Be(10m);
    }

    [Fact]
    public async Task Ajuste_e_estorno_manual_ficam_bloqueados_no_local_em_contagem_e_liberados_ao_cancelar()
    {
        var (_, owner) = await ActorAsync("owner");
        var location = await LocationAsync();
        var item = await ItemAsync();
        await MoveAsync(item, location, 10m);
        var count = await OkAsync(await owner.SendAsync(Command(Counts, Open(location))), HttpStatusCode.Created);

        var blocked = await owner.SendAsync(Command(Adjustments, new { locationId = location, mode = "Delta", reason = "Loss", reasonDetail = (string?)null, lines = new[] { new { itemId = item, lotId = (Guid?)null, quantity = -1m, unitId = (Guid?)null } } }));
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(blocked);
        Code(problem).Should().Be(LocationCountLocks.Code, "CA 6");
        Detail(problem).Should().EndWith($"{count.GetProperty("number").GetString()}. Ajustes e estornos manuais ficam bloqueados até fechar ou cancelar.");
        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<ILocationCountLock>().FindAsync([location]))!.CountId.Should().Be(Id(count), "o estorno manual consulta o mesmo bloqueio");
        }

        // Entradas e saídas seguem (ponto 91).
        await MoveAsync(item, location, 1m);

        (await owner.SendAsync(Command($"{Counts}/{Id(count)}/cancel", new { reason = " ", version = Version(count) }))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var cancelled = await OkAsync(await owner.SendAsync(Command($"{Counts}/{Id(count)}/cancel", new { reason = "Contagem errada", version = Version(count) })));
        cancelled.GetProperty("status").GetString().Should().Be("Cancelled", "CA 13");
        cancelled.GetProperty("adjustmentId").ValueKind.Should().Be(JsonValueKind.Null);
        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<ILocationCountLock>().FindAsync([location])).Should().BeNull("o local é desbloqueado");
        }

        (await owner.SendAsync(Command(Adjustments, new { locationId = location, mode = "Delta", reason = "Loss", reasonDetail = (string?)null, lines = new[] { new { itemId = item, lotId = (Guid?)null, quantity = -1m, unitId = (Guid?)null } } })))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Recontagem_pela_mesma_pessoa_e_recusada_e_a_outra_substitui_a_diferenca()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, first) = await ActorAsync("warehouse");
        var (_, second) = await ActorAsync("warehouse");
        var location = await LocationAsync();
        var item = await ItemAsync();
        await MoveAsync(item, location, 10m);
        var count = await OpenAndCountAsync(owner, first, location, item, 4m);
        var divergences = await OkAsync(await owner.GetAsync($"{Counts}/{Id(count)}/divergences"));
        var original = divergences.GetProperty("lines").EnumerateArray().Single();
        original.GetProperty("canRequestRecount").GetBoolean().Should().BeTrue();

        var recount = await OkAsync(await owner.SendAsync(Command($"{Counts}/{Id(count)}/recount", new { lineIds = new[] { Id(original) }, version = Version(count) })));
        recount.GetProperty("lines").EnumerateArray().Should().BeEmpty("CA 7: a recontagem substitui a anterior; sem contar ainda, não há diferença");

        var mine = await OkAsync(await first.GetAsync($"{Counts}/{Id(count)}/counting"));
        mine.GetProperty("lines").EnumerateArray().Should().NotContain(l => l.GetProperty("itemId").GetGuid() == item, "CA 8: a tela de recontagem só lista o que ele não contou");
        var theirs = await OkAsync(await second.GetAsync($"{Counts}/{Id(count)}/counting"));
        var line = LineOf(theirs, item);
        line.GetProperty("isRecount").GetBoolean().Should().BeTrue();
        var refused = await first.SendAsync(CountRequest(Id(count), line, 9m));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Detail(await JsonAsync(refused)).Should().Be("A recontagem precisa ser feita por outra pessoa.", "CA 8, conferido no servidor");

        await OkAsync(await second.SendAsync(CountRequest(Id(count), line, 9m)));
        var after = await OkAsync(await owner.GetAsync($"{Counts}/{Id(count)}/divergences"));
        var recounted = after.GetProperty("lines").EnumerateArray().Single();
        recounted.GetProperty("difference").GetDecimal().Should().Be(-1m, "a recontagem vale na diferença");
        recounted.GetProperty("previousCountedQty").GetDecimal().Should().Be(4m, "o histórico das duas fica");
        (await WithDbAsync(db => db.InventoryCountLines.AsNoTracking().CountAsync(l => l.CountId == Id(count) && l.ItemId == item))).Should().Be(2);
    }

    [Fact]
    public async Task Fechar_gera_um_ajuste_Delta_pendente_e_a_aprovacao_aplica_so_a_diferenca()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, counter) = await ActorAsync("warehouse");
        var (_, approver) = await ActorAsync("owner");
        var location = await LocationAsync();
        var item = await ItemAsync();
        var untouched = await ItemAsync();
        await MoveAsync(item, location, 10m);
        await MoveAsync(untouched, location, 3m);
        var count = await OpenAndCountAsync(owner, counter, location, item, 8m);

        var missing = await owner.SendAsync(Command($"{Counts}/{Id(count)}/close", new { uncountedPolicy = (string?)null, version = Version(count) }));
        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Detail(await JsonAsync(missing)).Should().Be("Considerar 1 item não contado como zero?", "CA 9");

        var closed = await CloseAsync(owner, count, "KeepSystem");
        closed.GetProperty("status").GetString().Should().Be("Closed", "CA 10");
        closed.GetProperty("uncountedPolicy").GetString().Should().Be("KeepSystem", "a escolha fica no inventário");
        closed.GetProperty("adjustmentStatus").GetString().Should().Be("Pending");
        var adjustmentId = closed.GetProperty("adjustmentId").GetGuid();
        var adjustment = await OkAsync(await approver.GetAsync($"{Adjustments}/{adjustmentId}"));
        adjustment.GetProperty("mode").GetString().Should().Be("Delta");
        adjustment.GetProperty("origin").GetString().Should().Be("InventoryCount");
        adjustment.GetProperty("reason").GetString().Should().Be("Count");
        adjustment.GetProperty("originId").GetGuid().Should().Be(Id(count));
        adjustment.GetProperty("lines").EnumerateArray().Should().ContainSingle().Which.GetProperty("deltaQty").GetDecimal().Should().Be(-2m, "só a divergência");
        (await StateAsync(item, location)).OnHand.Should().Be(10m, "o saldo só muda na aprovação");
        (await WithDbAsync(db => db.Notifications.AsNoTracking().AnyAsync(n => n.Type == StockAdjustmentService.ApprovalType && n.Link == StockAdjustmentService.Link + adjustmentId))).Should().BeTrue("aprovadores avisados");
        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<ILocationCountLock>().FindAsync([location])).Should().BeNull("fechado, o local é desbloqueado");
        }

        await MoveAsync(item, location, 5m);
        var approved = await OkAsync(await approver.SendAsync(Command($"{Adjustments}/{adjustmentId}/approve", new { comment = (string?)null, version = Version(adjustment) })));
        approved.GetProperty("status").GetString().Should().Be("Approved");
        (await StateAsync(item, location)).OnHand.Should().Be(13m, "CA 10: 15 + (−2), sem gravar o contado por cima");
        var detail = await OkAsync(await owner.GetAsync($"{Counts}/{Id(count)}"));
        detail.GetProperty("adjustmentSituation").GetString().Should().Be($"Ajuste {adjustment.GetProperty("number").GetString()} aprovado", "CA 12");
    }

    [Fact]
    public async Task Quem_abriu_ou_contou_nao_aprova_salvo_o_Proprietario_unico()
    {
        var (_, opener) = await ActorAsync("owner");
        var (counterId, counter) = await ActorAsync("owner");
        var (_, outsider) = await ActorAsync("owner");
        var location = await LocationAsync();
        var item = await ItemAsync();
        await MoveAsync(item, location, 10m);
        var count = await OpenAndCountAsync(opener, counter, location, item, 9m);
        var closed = await CloseAsync(opener, count);
        var adjustmentId = closed.GetProperty("adjustmentId").GetGuid();
        var adjustment = await OkAsync(await counter.GetAsync($"{Adjustments}/{adjustmentId}"));
        adjustment.GetProperty("canDecide").GetBoolean().Should().BeFalse();
        adjustment.GetProperty("decideBlockedReason").GetString().Should().Be("Quem abriu ou contou o inventário não aprova o ajuste dele.");

        var refused = await counter.SendAsync(Command($"{Adjustments}/{adjustmentId}/approve", new { comment = (string?)null, version = Version(adjustment) }));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "CA 11: quem contou é envolvido");
        (await opener.SendAsync(Command($"{Adjustments}/{adjustmentId}/approve", new { comment = (string?)null, version = Version(adjustment) }))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "quem abriu e fechou também");

        await AloneAsync(counterId, async () =>
        {
            var self = await OkAsync(await counter.SendAsync(Command($"{Adjustments}/{adjustmentId}/approve", new { comment = (string?)null, version = Version(adjustment) })));
            self.GetProperty("selfApproved").GetBoolean().Should().BeTrue("Proprietário único: Autoaprovado");
        });
        (await StateAsync(item, location)).OnHand.Should().Be(9m);
        _ = outsider;
    }

    [Fact]
    public async Task Reprovado_reabre_a_contagem_e_volta_a_bloquear_o_local()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, counter) = await ActorAsync("warehouse");
        var (_, approver) = await ActorAsync("owner");
        var location = await LocationAsync();
        var item = await ItemAsync();
        await MoveAsync(item, location, 10m);
        var count = await OpenAndCountAsync(owner, counter, location, item, 7m);
        var closed = await CloseAsync(owner, count);
        (await owner.SendAsync(Command($"{Counts}/{Id(count)}/reopen", new { version = Version(closed) }))).StatusCode.Should().Be(HttpStatusCode.Conflict, "pendente não reabre");

        var adjustmentId = closed.GetProperty("adjustmentId").GetGuid();
        var adjustment = await OkAsync(await approver.GetAsync($"{Adjustments}/{adjustmentId}"));
        await OkAsync(await approver.SendAsync(Command($"{Adjustments}/{adjustmentId}/reject", new { comment = "Conte de novo a prateleira 3", version = Version(adjustment) })));
        var rejected = await OkAsync(await owner.GetAsync($"{Counts}/{Id(count)}"));
        rejected.GetProperty("adjustmentSituation").GetString().Should().EndWith("reprovado", "CA 12");
        rejected.GetProperty("canReopen").GetBoolean().Should().BeTrue();

        var reopened = await OkAsync(await owner.SendAsync(Command($"{Counts}/{Id(count)}/reopen", new { version = Version(rejected) })));
        reopened.GetProperty("status").GetString().Should().Be("Counting");
        reopened.GetProperty("reopenCount").GetInt32().Should().Be(1);
        await using var scope = await ScopeAsync();
        (await scope.ServiceProvider.GetRequiredService<ILocationCountLock>().FindAsync([location]))!.CountId.Should().Be(Id(count), "volta a bloquear o local (CA 6)");
    }

    [Fact]
    public async Task Contagem_abaixo_do_reservado_reduz_as_reservas_mais_recentes_e_gera_a_falta()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, counter) = await ActorAsync("warehouse");
        var (approverId, approver) = await ActorAsync("owner");
        var location = await DefaultLocationAsync();
        var item = await ItemAsync();
        await MoveAsync(item, location, 10m);
        var older = await ReserveAsync(item, 4m, DateTimeOffset.UtcNow.AddDays(-3));
        var newer = await ReserveAsync(item, 4m, DateTimeOffset.UtcNow.AddMinutes(-5));
        (await StateAsync(item, location)).Reserved.Should().Be(8m);

        var count = await OpenAndCountAsync(owner, counter, location, item, 5m);
        var closed = await CloseAsync(owner, count, "KeepSystem");
        var adjustmentId = closed.GetProperty("adjustmentId").GetGuid();
        var adjustment = await OkAsync(await approver.GetAsync($"{Adjustments}/{adjustmentId}"));
        adjustment.GetProperty("impact").GetProperty("refusal").ValueKind.Should().Be(JsonValueKind.Null, "a contagem abaixo do reservado não é recusa no ajuste do inventário");
        adjustment.GetProperty("impact").GetProperty("reservationNote").GetString().Should().Contain("reduz 3,0000 UN das reservas");

        await OkAsync(await approver.SendAsync(Command($"{Adjustments}/{adjustmentId}/approve", new { comment = (string?)null, version = Version(adjustment) })));
        (await StateAsync(item, location)).Should().Be((5m, 5m), "o reservado desce até o físico");
        var reservations = await WithDbAsync(db => db.Reservations.AsNoTracking().Where(r => r.ItemId == item && r.LocationId == location).ToListAsync());
        reservations.Single(r => r.ProductionOrderId == newer).Quantity.Should().Be(1m, "decisão do PO: primeiro a BOM liberada mais recentemente");
        reservations.Single(r => r.ProductionOrderId == newer).Shortage.Should().Be(3m, "gera a falta");
        reservations.Single(r => r.ProductionOrderId == older).Quantity.Should().Be(4m);
        (await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == approverId && n.Type == StockAdjustmentService.ReservationsReducedType).Select(n => n.Title).ToListAsync()))
            .Should().ContainSingle(t => t.Contains("reduziu as reservas de 1 OS", StringComparison.Ordinal), "avisa quem tem inventory.approve");
    }

    [Fact]
    public async Task Dois_contadores_na_mesma_linha_so_um_grava()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, a) = await ActorAsync("warehouse");
        var (_, b) = await ActorAsync("warehouse");
        var location = await LocationAsync();
        var item = await ItemAsync();
        await MoveAsync(item, location, 10m);
        var count = await OkAsync(await owner.SendAsync(Command(Counts, Open(location))), HttpStatusCode.Created);
        var line = LineOf(await OkAsync(await a.GetAsync($"{Counts}/{Id(count)}/counting")), item);
        var responses = await Task.WhenAll(a.SendAsync(CountRequest(Id(count), line, 9m)), b.SendAsync(CountRequest(Id(count), line, 8m)));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "CA 16: xmin");
        var loser = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
        loser.StatusCode.Should().Be(HttpStatusCode.Conflict, "409, nunca 500");
        var problem = await JsonAsync(loser);
        Detail(problem).Should().StartWith("Esta linha foi contada por ").And.EndWith(" agora há pouco.");
    }

    [Fact]
    public async Task Tres_camadas_IDOR_e_historico()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, viewer) = await ActorAsync("view:inventory");
        var (_, nobody) = await ActorAsync(null);
        var location = await LocationAsync();
        var other = await LocationAsync();
        var item = await ItemAsync();
        await MoveAsync(item, location, 3m);
        await MoveAsync(item, other, 3m);
        var count = await OkAsync(await owner.SendAsync(Command(Counts, Open(location))), HttpStatusCode.Created);
        var otherCount = await OkAsync(await owner.SendAsync(Command(Counts, Open(other))), HttpStatusCode.Created);
        var line = LineOf(await OkAsync(await owner.GetAsync($"{Counts}/{Id(count)}/counting")), item);

        Code(await JsonAsync(await nobody.GetAsync(Counts))).Should().Be("forbidden");
        (await viewer.GetAsync(Counts)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.SendAsync(Command(Counts, Open(await LocationAsync())))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "abrir sem inventory.adjust");
        (await viewer.SendAsync(CountRequest(Id(count), line, 1m))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "contar sem inventory.create");
        (await viewer.SendAsync(Command($"{Counts}/{Id(count)}/close", new { uncountedPolicy = "Zero", version = Version(count) }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.GetAsync($"{Counts}/{Id(otherCount)}/lines/{Id(line)}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "CA 14: IDOR");
        (await owner.SendAsync(CountRequest(Id(otherCount), line, 1m))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await OkAsync(await owner.SendAsync(CountRequest(Id(count), line, 2m)));
        var history = await OkAsync(await owner.GetAsync($"{Counts}/{Id(count)}/history"));
        history.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(2, "CA 17: o inventário e a contagem da linha");

        await SetReleasedAsync(false);
        try
        {
            Code(await JsonAsync(await owner.GetAsync(Counts))).Should().Be("module_not_released");
        }
        finally
        {
            await SetReleasedAsync(true);
        }

        await owner.SendAsync(Command($"{Counts}/{Id(count)}/cancel", new { reason = "Fim do teste", version = Version(await OkAsync(await owner.GetAsync($"{Counts}/{Id(count)}"))) }));
        await owner.SendAsync(Command($"{Counts}/{Id(otherCount)}/cancel", new { reason = "Fim do teste", version = Version(otherCount) }));
    }
}
