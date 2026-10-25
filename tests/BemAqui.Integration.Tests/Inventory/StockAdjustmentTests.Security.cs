using System.Net;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>EST-01b CA 15 a 18: "Aguardando você", três camadas e IDOR, auditoria e o bloqueio do local com o dublê do ILocationCountLock.</summary>
public partial class StockAdjustmentTests
{
    [Fact]
    public async Task Aguardando_voce_mostra_os_pendentes_de_outras_pessoas_para_quem_aprova()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (_, owner) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ReceiveAsync(item, location, 10m, 1m);
        var theirs = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 9m)))), HttpStatusCode.Created);
        var other = await ItemAsync();
        await ReceiveAsync(other, location, 3m, 1m);
        var mine = await OkAsync(await owner.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(other, 2m)))), HttpStatusCode.Created);

        var awaiting = await OkAsync(await owner.GetAsync($"{Adjustments}?awaitingMe=true&locationId={location}"));
        var rows = awaiting.GetProperty("items").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("id").GetGuid()).Should().Contain(Id(theirs)).And.NotContain(Id(mine), "CA 15: os que o usuário pode aprovar, não sendo o autor");
        var row = rows.Single(r => r.GetProperty("id").GetGuid() == Id(theirs));
        (row.GetProperty("mode").GetString(), row.GetProperty("status").GetString(), row.GetProperty("itemCount").GetInt32()).Should().Be(("Counted", "Pending", 1));
        row.GetProperty("differenceValue").GetDecimal().Should().Be(1m);
        row.GetProperty("awaitingMe").GetBoolean().Should().BeTrue();
        var forWarehouse = await OkAsync(await warehouse.GetAsync($"{Adjustments}?awaitingMe=true&locationId={location}"));
        forWarehouse.GetProperty("items").GetArrayLength().Should().Be(0, "quem não aprova não tem nada aguardando");
        var all = await OkAsync(await warehouse.GetAsync($"{Adjustments}?locationId={location}"));
        all.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Tres_camadas_permissoes_por_acao_e_IDOR_na_linha()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (_, viewer) = await ActorAsync("view:inventory");
        var (_, nobody) = await ActorAsync(null);
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ReceiveAsync(item, location, 10m, 1m);
        var a = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 9m)))), HttpStatusCode.Created);
        var other = await ItemAsync();
        await ReceiveAsync(other, location, 2m, 1m);
        var b = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(other, 1m)))), HttpStatusCode.Created);

        var noGroup = await nobody.GetAsync(Adjustments);
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(noGroup)).Should().Be("forbidden", "CA 16: sem grupo");
        (await viewer.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 8m))))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "lançar exige inventory.adjust");
        (await warehouse.SendAsync(Command($"{Adjustments}/{Id(a)}/approve", Decision(a)))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "aprovar exige inventory.approve");
        (await warehouse.SendAsync(Command($"{Adjustments}/{Id(a)}/reject", Decision(a, "Não")))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "reprovar exige inventory.approve");
        (await viewer.SendAsync(Command($"{Adjustments}/{Id(a)}/cancel", new { reason = "x", version = a.GetProperty("version").GetUInt32() }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var detail = await OkAsync(await viewer.GetAsync($"{Adjustments}/{Id(a)}"));
        (detail.GetProperty("canDecide").GetBoolean(), detail.GetProperty("canCancel").GetBoolean()).Should().Be((false, false), "o botão some");
        detail.GetProperty("decideBlockedReason").GetString().Should().Be("Quem decide o ajuste é quem aprova no Estoque.");

        var line = a.GetProperty("lines")[0].GetProperty("id").GetGuid();
        (await viewer.GetAsync($"{Adjustments}/{Id(a)}/lines/{line}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var idor = await viewer.GetAsync($"{Adjustments}/{Id(b)}/lines/{line}");
        idor.StatusCode.Should().Be(HttpStatusCode.NotFound, "CA 16: linha de outro ajuste responde como inexistente");
        Code(await JsonAsync(idor)).Should().Be("stock_adjustment_line_not_found");
        (await viewer.GetAsync($"{Adjustments}/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await SetReleasedAsync(false);
        try
        {
            var notReleased = await viewer.GetAsync(Adjustments);
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetReleasedAsync(true);
        }

        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && raw.Contains("/inventory/adjustments", StringComparison.Ordinal)).ToList();
        endpoints.Should().HaveCount(10, "lista, detalhe, linha, histórico, lançar, editar (409), aprovar, reprovar, cancelar e estornar");
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()!.ModuleCode.Should().Be("inventory", endpoint.DisplayName);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(x => x.Policy != null && x.Policy.StartsWith(ModuleEndpoints.PermissionPolicyPrefix, StringComparison.Ordinal), endpoint.DisplayName);
        }
    }

    [Fact]
    public async Task Historico_de_alteracoes_tem_o_ajuste_e_as_linhas_com_antes_e_depois()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (_, owner) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ReceiveAsync(item, location, 10m, 1m);
        var created = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 7m)))), HttpStatusCode.Created);
        await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(created)}/approve", Decision(created, "Ok"))));
        var id = Id(created).ToString();
        var audit = await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(x => x.Entity == nameof(StockAdjustment) && x.EntityId == id).ToListAsync());
        audit.Should().Contain(x => x.Action == AuditActions.Created).And.Contain(x => x.Action == AuditActions.Updated, "CA 17: IAuditable no ajuste");
        audit.Where(x => x.Action == AuditActions.Updated).Should().Contain(x => x.Changes.Contains("Approved"), "antes/depois da situação");
        var history = await OkAsync(await owner.GetAsync($"{Adjustments}/{id}/history"));
        history.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("entity").GetString()).Should().Contain(nameof(StockAdjustmentLine), "as linhas entram no histórico");
    }

    [Fact]
    public async Task Com_inventario_em_contagem_lancar_aprovar_e_estornar_sao_recusados_e_nada_e_gravado()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (_, owner) = await ActorAsync("owner");
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await ReceiveAsync(item, location, 10m, 1m);
        var pending = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 9m)))), HttpStatusCode.Created);
        var approvedBefore = await OkAsync(await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Delta", Line(item, 2m)))), HttpStatusCode.Created);
        var approved = await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(approvedBefore)}/approve", Decision(approvedBefore))));
        var manual = await OkAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings",
            new { kind = "Receipt", itemId = item, locationId = location, toLocationId = (Guid?)null, quantity = 1m, unitId = (Guid?)null, unitCost = 1m, lotId = (Guid?)null, lotNumber = (string?)null, reason = "Teste", projectId = (Guid?)null })),
            HttpStatusCode.Created);
        var manualMovement = manual.GetProperty("lines")[0].GetProperty("movementId").GetGuid();
        var count = Guid.CreateVersion7();
        Counting[location] = new LocationCountBlock(location, "Corredor B", count, "INV-2026-0007");
        try
        {
            const string message = "Há um inventário em contagem em Corredor B: INV-2026-0007. Ajustes e estornos manuais ficam bloqueados até fechar ou cancelar.";
            var create = await warehouse.SendAsync(Command(Adjustments, Adjustment(location, "Counted", Line(item, 5m))));
            create.StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 18: lançar");
            var problem = await JsonAsync(create);
            (Code(problem), Detail(problem)).Should().Be(("location_counting", message));
            problem.GetProperty("link").GetString().Should().Be("/app/estoque/inventarios/" + count);
            Code(await JsonAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(pending)}/approve", Decision(pending))))).Should().Be("location_counting", "aprovar");
            Code(await JsonAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(approved)}/reverse", new { reason = "Teste", version = approved.GetProperty("version").GetUInt32() })))).Should().Be("location_counting", "estornar");
            Code(await JsonAsync(await warehouse.SendAsync(Command($"{Base}/movements/{manualMovement}/reverse", new { reason = "Teste" })))).Should().Be("location_counting", "estorno de movimento manual (EST-01a CA 20)");
        }
        finally
        {
            Counting.TryRemove(location, out _);
        }

        (await WithDbAsync(db => db.StockAdjustments.CountAsync(a => a.LocationId == location))).Should().Be(2, "nada gravado");
        (await OkAsync(await owner.GetAsync($"{Adjustments}/{Id(pending)}"))).GetProperty("status").GetString().Should().Be("Pending");
        (await OkAsync(await owner.GetAsync($"{Adjustments}/{Id(approved)}"))).GetProperty("reversedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        (await WithDbAsync(db => db.StockMovements.CountAsync(m => m.ReversesMovementId == manualMovement))).Should().Be(0);
        (await StateAsync(item, location)).OnHand.Should().Be(13m, "10 + 2 do ajuste aprovado + 1 da entrada manual");

        // Fechada ou cancelada a contagem, o local libera.
        await OkAsync(await owner.SendAsync(Command($"{Adjustments}/{Id(pending)}/approve", Decision(pending))));
        (await StateAsync(item, location)).OnHand.Should().Be(9m);
    }
}
