using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>EST-02 CA 13 a 15, 17 e 18 pela API: disponível na saída avulsa e na transferência, aba Material, Faltas, três camadas, IDOR e histórico.</summary>
public partial class ReservationTests
{
    private static HttpRequestMessage Command(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }

    private static object Manual(string kind, Guid item, Guid location, decimal quantity, Guid? to = null) =>
        new { kind, itemId = item, locationId = location, toLocationId = to, quantity, unitId = (Guid?)null, unitCost = (decimal?)null, lotId = (Guid?)null, lotNumber = (string?)null, reason = "Teste", projectId = (Guid?)null };

    [Fact]
    public async Task Saida_avulsa_e_transferencia_respeitam_o_disponivel()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, alm, other) = (await ItemAsync(), await DefaultLocationAsync(), await LocationAsync());
        await ReceiveAsync(item, alm, 10m);
        var order = Order();
        Release(order, 1, (item, 4m));
        await ReconcileAsync(order);

        var issue = await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", item, alm, 7m)));
        issue.StatusCode.Should().Be(HttpStatusCode.Conflict, await issue.Content.ReadAsStringAsync());
        var problem = await JsonAsync(issue);
        Code(problem).Should().Be("insufficient_stock");
        problem.GetProperty("detail").GetString().Should().Be("Há 6,0000 UN disponível; 4,0000 UN está reservado para OS.", "CA 13");
        var transfer = await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Transfer", item, alm, 7m, other)));
        transfer.StatusCode.Should().Be(HttpStatusCode.Conflict, "a transferência também respeita o disponível");
        (await warehouse.SendAsync(Command($"{Base}/manual-postings", Manual("Issue", item, alm, 6m)))).StatusCode.Should().Be(HttpStatusCode.Created, "o disponível sai");
        (await PositionAsync(item, alm)).Should().Be((4m, 4m), "o reservado continua intacto");

        var balances = await JsonAsync(await warehouse.GetAsync($"{Base}/balances?q={Uri.EscapeDataString((await WithDbAsync(db => db.Items.Where(i => i.Id == item).Select(i => i.Code).SingleAsync())))}"));
        var row = balances.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("locationId").GetGuid() == alm);
        (row.GetProperty("onHand").GetDecimal(), row.GetProperty("reserved").GetDecimal(), row.GetProperty("available").GetDecimal()).Should().Be((4m, 4m, 0m), "Saldos lê a posição");
    }

    [Fact]
    public async Task Aba_Material_e_lista_Faltas_mostram_reservado_e_falta()
    {
        var (_, production) = await ActorAsync("view:production");
        var (_, warehouse) = await ActorAsync("warehouse");
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 6m);
        var order = Order();
        Release(order, 1, (item, 10m));

        var before = await JsonAsync(await production.GetAsync($"{Base}/production-orders/{order}/reservations"));
        before.GetProperty("reconciling").GetBoolean().Should().BeTrue("BOM liberada e reservas ainda não feitas: \"Reservando material…\"");
        await ReconcileAsync(order);

        var material = await JsonAsync(await production.GetAsync($"{Base}/production-orders/{order}/reservations"));
        material.GetProperty("reconciling").GetBoolean().Should().BeFalse();
        material.GetProperty("totalShortage").GetDecimal().Should().Be(4m, "CA 14: total no cabeçalho da OS");
        var line = material.GetProperty("lines").EnumerateArray().Single();
        (line.GetProperty("required").GetDecimal(), line.GetProperty("reserved").GetDecimal(), line.GetProperty("shortage").GetDecimal()).Should().Be((10m, 6m, 4m));
        line.GetProperty("locations").EnumerateArray().Single().GetProperty("locationId").GetGuid().Should().Be(alm);

        var shortages = await JsonAsync(await warehouse.GetAsync($"{Base}/shortages?itemId={item}"));
        var shortage = shortages.GetProperty("items").EnumerateArray().Single();
        shortage.GetProperty("shortQty").GetDecimal().Should().Be(4m, "CA 15: a falta somada das OS abertas");
        shortage.GetProperty("nearestPromisedDate").GetString().Should().Be("2026-12-01");
        shortage.GetProperty("orders").EnumerateArray().Single().GetProperty("orderNumber").GetString().Should().Be(Orders[order].Number);
        (await production.GetAsync($"{Base}/shortages")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a lista Faltas exige inventory.view");
    }

    [Fact]
    public async Task Tres_camadas_e_IDOR_na_reserva_da_OS()
    {
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 3m);
        var (order, other) = (Order(), Order());
        Release(order, 1, (item, 2m));
        Release(other, 1, (item, 1m));
        await ReconcileAsync(order);
        await ReconcileAsync(other);
        var mine = (await ReservationsAsync(order)).Single().Id;
        var foreign = (await ReservationsAsync(other)).Single().Id;

        var (_, nobody) = await ActorAsync(null);
        var noGroup = await nobody.GetAsync($"{Base}/production-orders/{order}/reservations");
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(noGroup)).Should().Be("forbidden");

        var (_, viewer) = await ActorAsync("view:inventory");
        (await viewer.GetAsync($"{Base}/production-orders/{order}/reservations/{mine}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var idor = await viewer.GetAsync($"{Base}/production-orders/{order}/reservations/{foreign}");
        idor.StatusCode.Should().Be(HttpStatusCode.NotFound, "CA 17: reserva de outra OS responde como inexistente");
        Code(await JsonAsync(idor)).Should().Be("reservation_not_found");
        (await viewer.GetAsync($"{Base}/production-orders/{Guid.NewGuid()}/reservations")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await SetReleasedAsync(false);
        try
        {
            var notReleased = await viewer.GetAsync($"{Base}/production-orders/{order}/reservations");
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetReleasedAsync(true);
        }

        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && (raw.Contains("/reservations", StringComparison.Ordinal) || raw.EndsWith("/shortages", StringComparison.Ordinal))).ToList();
        endpoints.Should().HaveCount(5);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()!.ModuleCode.Should().Be("inventory", endpoint.DisplayName);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy != null && a.Policy.StartsWith(ModuleEndpoints.PermissionPolicyPrefix, StringComparison.Ordinal), endpoint.DisplayName);
        }
    }

    [Fact]
    public async Task Historico_mostra_reservado_liberado_e_completado_com_a_origem_e_a_auditoria()
    {
        var (_, viewer) = await ActorAsync("view:inventory");
        var (item, alm) = (await ItemAsync(), await DefaultLocationAsync());
        await ReceiveAsync(item, alm, 2m);
        var order = Order();
        Release(order, 1, (item, 5m));
        await ReconcileAsync(order, "Evento BomReleased · mensagem 1");
        await ReceiveAsync(item, alm, 3m);
        SetStatus(order, BemAqui.Production.Contracts.ProductionOrderState.Cancelled);
        await ReconcileAsync(order, "Evento ProductionOrderCancelled · mensagem 2");

        var history = await JsonAsync(await viewer.GetAsync($"{Base}/production-orders/{order}/reservations/history"));
        var rows = history.GetProperty("items").EnumerateArray().Select(r => (Kind: r.GetProperty("kind").GetString(), Delta: r.GetProperty("quantityDelta").GetDecimal(), Origin: r.GetProperty("origin").GetString()!)).ToList();
        rows.Should().Contain(("Reserved", 2m, "Evento BomReleased · mensagem 1"), "CA 18");
        rows.Should().Contain(r => r.Kind == "Completed" && r.Delta == 3m && r.Origin.StartsWith("Entrada MOV-", StringComparison.Ordinal));
        rows.Should().Contain(("Released", -5m, "Evento ProductionOrderCancelled · mensagem 2"));
        var byItem = await JsonAsync(await viewer.GetAsync($"{Base}/items/{item}/reservations/history"));
        byItem.GetProperty("total").GetInt32().Should().Be(rows.Count, "o histórico do item tem as mesmas mudanças");

        var reservation = (await ReservationsAsync(order)).Single().Id.ToString();
        var audit = await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(Reservation) && a.EntityId == reservation).ToListAsync());
        audit.Should().Contain(a => a.Action == AuditActions.Created).And.Contain(a => a.Action == AuditActions.Updated, "IAuditable na reserva");
    }

    private async Task SetReleasedAsync(bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "inventory").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }
}
