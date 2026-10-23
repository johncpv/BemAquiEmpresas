using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>EST-01a CA 20 a 24, 28, 30 e 32: estorno com os limites da D7, imutabilidade, Saldos, três camadas e auditoria.</summary>
public partial class StockTests
{
    private static async Task<(Guid PostingId, Guid MovementId)> ManualReceiptAsync(HttpClient client, Guid item, Guid location, decimal quantity, decimal cost)
    {
        var posted = await OkAsync(await client.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, location, quantity, cost))), HttpStatusCode.Created);
        return (posted.GetProperty("id").GetGuid(), posted.GetProperty("lines")[0].GetProperty("movementId").GetGuid());
    }

    private static HttpRequestMessage Reverse(Guid movement, string reason = "Lançado errado") => Command($"{Base}/movements/{movement}/reverse", new { reason });

    [Fact]
    public async Task Autor_estorna_o_proprio_movimento_dentro_do_limite_e_o_original_nao_muda()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        var (_, movement) = await ManualReceiptAsync(warehouse, item, location, 10m, 20m);
        var detail = await OkAsync(await warehouse.GetAsync($"{Base}/movements/{movement}"));
        detail.GetProperty("reverseRule").GetString().Should().Be("Author");

        var reversed = await OkAsync(await warehouse.SendAsync(Reverse(movement)));
        reversed.GetProperty("movement").GetProperty("reversedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String, "\"Estornado em\" lido do movimento inverso");
        reversed.GetProperty("reversalReason").GetString().Should().Be("Lançado errado");
        var inverse = await WithDbAsync(db => db.StockMovements.AsNoTracking().SingleAsync(m => m.ReversesMovementId == movement));
        inverse.Quantity.Should().Be(-10m);
        inverse.Type.Should().Be(StockMovementType.Receipt, "mesmo tipo, sinal oposto");
        (await StateAsync(item, location)).Should().Be((0m, 20m), "físico zerado: fica o último médio válido (CA 21)");

        var twice = await warehouse.SendAsync(Reverse(movement));
        twice.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Detail(await JsonAsync(twice)).Should().Be("Este movimento já foi estornado.");
        var ofReversal = await warehouse.SendAsync(Reverse(inverse.Id));
        ofReversal.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(ofReversal)).Should().Be("movement_is_reversal");
    }

    [Fact]
    public async Task Acima_do_limite_ou_depois_de_24_horas_o_estorno_e_de_outra_pessoa_com_aprovar()
    {
        var (authorId, warehouse) = await ActorAsync("warehouse");
        var (_, owner) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        var pending = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, location, 30m, 50m))), HttpStatusCode.Created);
        var approved = await OkAsync(await owner.SendAsync(Command($"{Base}/manual-postings/{pending.GetProperty("id").GetGuid()}/approve", new { comment = (string?)null, version = pending.GetProperty("version").GetUInt32() })));
        var movement = approved.GetProperty("lines")[0].GetProperty("movementId").GetGuid();

        var denied = await warehouse.SendAsync(Reverse(movement));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Detail(await JsonAsync(denied)).Should().Be("Este estorno precisa de outra pessoa com permissão de aprovar no Estoque.");
        (await OkAsync(await owner.SendAsync(Reverse(movement)))).GetProperty("movement").GetProperty("reversedByMovementId").GetGuid().Should().NotBeEmpty();

        // Movimento manual de 25 h atrás, abaixo do limite: o autor não estorna mais (o trigger não deixa mudar o posted_at; nasce assim).
        var (oldItem, oldLocation) = (await ItemAsync(), await LocationAsync());
        var old = await OldManualReceiptAsync(authorId, oldItem, oldLocation, 2m, 10m, DateTimeOffset.UtcNow.AddHours(-25));
        (await warehouse.SendAsync(Reverse(old))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.SendAsync(Reverse(old))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Estorno_de_entrada_consumida_em_parte_protege_o_medio_e_o_que_deixaria_negativo_da_409()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ManualReceiptAsync(warehouse, item, location, 10m, 10m);
        var (_, second) = await ManualReceiptAsync(warehouse, item, location, 10m, 12m);
        (await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", item, location, 5m)))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await StateAsync(item, location)).Should().Be((15m, 11m));
        await OkAsync(await warehouse.SendAsync(Reverse(second)));
        (await StateAsync(item, location)).Should().Be((5m, 9m), "(15 × 11 − 10 × 12) ÷ 5, sai pelo custo da entrada (CA 21)");

        var (other, otherLocation) = (await ItemAsync(), await LocationAsync());
        var (_, receipt) = await ManualReceiptAsync(warehouse, other, otherLocation, 5m, 10m);
        (await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", other, otherLocation, 3m)))).StatusCode.Should().Be(HttpStatusCode.Created);
        var negative = await warehouse.SendAsync(Reverse(receipt));
        negative.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(negative)).Should().Be("insufficient_stock", "CA 22");
    }

    [Fact]
    public async Task Transferencia_estorna_as_duas_pernas_e_documento_de_outra_origem_estorna_pela_origem()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, a, b) = (await ItemAsync(), await LocationAsync(), await LocationAsync());
        await ManualReceiptAsync(warehouse, item, a, 5m, 4m);
        var transfer = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Transfer", item, a, 2m, to: b))), HttpStatusCode.Created);
        await OkAsync(await warehouse.SendAsync(Reverse(transfer.GetProperty("lines")[0].GetProperty("movementId").GetGuid())));
        (await StateAsync(item, a)).OnHand.Should().Be(5m);
        (await StateAsync(item, b)).OnHand.Should().Be(0m);

        var fromDocument = await PostAsync(new StockPosting(StockMovementType.Receipt, item, a, null, 1m, 4m, StockSources.GoodsReceipt, Guid.NewGuid(), SourceNumber: "REC-2026-0007"));
        var refused = await warehouse.SendAsync(Reverse(fromDocument.Value.MovementId));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(refused);
        Detail(problem).Should().Be("Estorne pelo documento de origem (Recebimento REC-2026-0007).", "CA 23");
        problem.GetProperty("sourceNumber").GetString().Should().Be("REC-2026-0007");
    }

    [Fact]
    public async Task Movimento_nao_muda_nem_e_apagado_pela_API()
    {
        var (_, owner) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        var movement = (await ReceiveAsync(item, location, 1m, 1m)).Value.MovementId;
        var put = await owner.PutAsJsonAsync($"{Base}/movements/{movement}", new { quantity = 2m });
        put.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        Detail(await JsonAsync(put)).Should().Be("Movimento de estoque não muda nem é apagado; estorne-o.");
        (await owner.DeleteAsync($"{Base}/movements/{movement}")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Saldos_mostram_fisico_reservado_disponivel_situacao_e_custo_medio_com_filtros()
    {
        var (_, viewer) = await ActorAsync("view:inventory");
        var (item, empty, location) = (await ItemAsync(minStock: 10m), await ItemAsync(), await LocationAsync());
        (await ReceiveAsync(item, location, 4m, 2.5m)).IsSuccess.Should().BeTrue();
        (await ReceiveAsync(empty, location, 1m, 1m)).IsSuccess.Should().BeTrue();
        (await IssueAsync(empty, location, 1m)).IsSuccess.Should().BeTrue();
        var page = await OkAsync(await viewer.GetAsync($"{Base}/balances?locationId={location}&pageSize=100"));
        var rows = page.GetProperty("items").EnumerateArray().ToList();
        var low = rows.Single(r => r.GetProperty("itemId").GetGuid() == item);
        low.GetProperty("onHand").GetDecimal().Should().Be(4m);
        low.GetProperty("reserved").GetDecimal().Should().Be(0m, "0 até a EST-02");
        low.GetProperty("available").GetDecimal().Should().Be(4m);
        low.GetProperty("averageCost").GetDecimal().Should().Be(2.5m, "custo médio com inventory.view (ponto 11)");
        low.GetProperty("situation").GetString().Should().Be("Low");
        rows.Single(r => r.GetProperty("itemId").GetGuid() == empty).GetProperty("situation").GetString().Should().Be("Out");
        var outOnly = await OkAsync(await viewer.GetAsync($"{Base}/balances?locationId={location}&situation=Out"));
        outOnly.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("itemId").GetGuid()).Should().Equal(empty);

        (await viewer.SendAsync(Command($"{Base}/manual-postings", Manual("Receipt", item, location, 1m, 1m)))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem inventory.create");
        (await viewer.SendAsync(Reverse(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "estornar exige inventory.edit ou inventory.approve");
        (await viewer.PostAsJsonAsync($"{Base}/opening/complete", new { version = 0u })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Tres_camadas_e_Idempotency_Key_em_todo_endpoint_do_estoque()
    {
        var (_, nobody) = await ActorAsync(null);
        var noGroup = await nobody.GetAsync($"{Base}/balances");
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(noGroup)).Should().Be("forbidden");
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        var noKey = await warehouse.PostAsJsonAsync($"{Base}/manual-postings", Manual("Receipt", item, location, 1m, 1m));
        noKey.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(noKey)).Should().Be("idempotency_key_required");

        await SetReleasedAsync(false);
        try
        {
            var notReleased = await warehouse.GetAsync($"{Base}/movements");
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetReleasedAsync(true);
        }

        var stock = new[] { "/balances", "/opening", "/movements", "/manual-postings", "/items/{itemId:guid}/stock", "/items/{itemId:guid}/lots", "/items/{itemId:guid}/movements" };
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && stock.Any(s => raw.StartsWith("/api/v1/inventory" + s, StringComparison.Ordinal))).ToList();
        endpoints.Should().HaveCountGreaterThan(15);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()!.ModuleCode.Should().Be("inventory", endpoint.DisplayName);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy != null && a.Policy.StartsWith(ModuleEndpoints.PermissionPolicyPrefix + "inventory.", StringComparison.Ordinal), endpoint.DisplayName);
        }
    }

    [Fact]
    public async Task Auditoria_tem_o_lancamento_e_deixa_o_custo_medio_fora_do_diff()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        var (posting, _) = await ManualReceiptAsync(warehouse, item, location, 3m, 7m);
        var entries = await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => (a.Entity == nameof(Item) && a.EntityId == item.ToString()) || (a.Entity == nameof(ManualPosting) && a.EntityId == posting.ToString())).ToListAsync());
        entries.Should().Contain(e => e.Entity == nameof(ManualPosting) && e.Action == AuditActions.Created, "quem, quando e o quê no lançamento (CA 32)");
        entries.Where(e => e.Entity == nameof(Item)).Should().NotContain(e => e.Changes.Contains("averageCost", StringComparison.Ordinal) && e.Action == AuditActions.Updated, "custo médio fora do diff");
        var history = await OkAsync(await warehouse.GetAsync($"{Base}/manual-postings/{posting}/history"));
        history.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
    }

    private async Task SetReleasedAsync(bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "inventory").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>Lançamento manual já gravado há 25 h (insert direto: o trigger só recusa UPDATE e DELETE), com posição e lote coerentes.</summary>
    private async Task<Guid> OldManualReceiptAsync(Guid author, Guid item, Guid location, decimal quantity, decimal cost, DateTimeOffset postedAt)
    {
        var (posting, line, movement) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var number = "MOV-1999-" + Random.Shared.Next(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var total = Math.Round(quantity * cost, 2);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"""
            INSERT INTO inventory.manual_postings (id, number, kind, status, estimated_value, reason, requested_by, requested_at, self_approved, posted_at)
            VALUES ({posting}, {number}, 'Receipt', 'Posted', {total}, 'Antigo', {author}, {postedAt}, false, {postedAt});
            INSERT INTO inventory.stock_movements (id, number, type, item_id, location_id, quantity, unit_cost, total_cost, balance_after, average_cost_after, source_type, source_id, source_number, leg, origin, at, posted_at, user_id)
            VALUES ({movement}, {number}, 'Receipt', {item}, {location}, {quantity}, {cost}, {total}, {quantity}, {cost}, 'ManualPosting', {posting}, {number}, 1, 'Manual', {postedAt}, {postedAt}, {author});
            INSERT INTO inventory.manual_posting_lines (id, posting_id, sequence, item_id, location_id, quantity, entered_quantity, unit_cost, movement_id, reversal_self_approved)
            VALUES ({line}, {posting}, 1, {item}, {location}, {quantity}, {quantity}, {cost}, {movement}, false);
            INSERT INTO inventory.stock_positions (id, item_id, location_id, quantity_on_hand, quantity_reserved) VALUES ({Guid.NewGuid()}, {item}, {location}, {quantity}, 0);
            INSERT INTO inventory.stock_balances (id, item_id, location_id, lot_id, quantity_on_hand) VALUES ({Guid.NewGuid()}, {item}, {location}, NULL, {quantity});
            UPDATE inventory.items SET average_cost = {cost} WHERE id = {item};
            """));
        return movement;
    }
}
