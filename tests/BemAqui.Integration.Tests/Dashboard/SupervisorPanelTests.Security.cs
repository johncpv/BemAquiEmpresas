using System.Diagnostics;
using System.Net;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>DSH-03 CA 8 e 9: três camadas por bloco, metadados dos endpoints, "Atualizar" com rate limit e o desempenho com 300 OS, 200 faltas e 100 NC.</summary>
public partial class SupervisorPanelTests
{
    private static readonly string[] Blocks = ["", "/board", "/capacity", "/quality", "/indicators", "/shortages"];

    [Fact]
    public async Task Tres_camadas_modulo_production_grupo_e_acao_em_cada_bloco()
    {
        var (_, supervisor) = await ActorAsync("production_supervision");
        await SetReleasedAsync("production", false);
        try
        {
            foreach (var block in Blocks)
            {
                var response = await supervisor.GetAsync(Base + block);
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden, block);
                Code(await JsonAsync(response)).Should().Be("module_not_released", block);
            }
        }
        finally
        {
            await SetReleasedAsync("production", true);
        }

        await SetReleasedAsync("inventory", false);
        try
        {
            var shortages = await supervisor.GetAsync(Base + "/shortages");
            shortages.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(shortages)).Should().Be("module_not_released", "o bloco confere o módulo do dono");
            (await OkAsync(supervisor, Base)).GetProperty("blocks").EnumerateArray().Select(b => b.GetString()).Should().NotContain("shortages");
            (await supervisor.GetAsync(Base + "/board")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await SetReleasedAsync("inventory", true);
        }

        // Sem grupo, ou com o grupo sem production.view: 403 forbidden em todos os blocos.
        var (_, nobody) = await ActorAsync(null);
        var (_, stockOnly) = await ActorAsync(null, "inventory.view");
        foreach (var client in new[] { nobody, stockOnly })
        {
            foreach (var block in Blocks)
            {
                var response = await client.GetAsync(Base + block);
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden, block);
                Code(await JsonAsync(response)).Should().Be("forbidden", block);
            }
        }

        // Cada endpoint declara o RequireModule e a perm: do dono; nenhum exige o módulo dashboard (CA 1).
        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith(Base, StringComparison.Ordinal) == true).ToList();
        endpoints.Should().HaveCount(7);
        foreach (var endpoint in endpoints)
        {
            var modules = endpoint.Metadata.GetOrderedMetadata<RequiresModuleMetadata>().Select(m => m.ModuleCode).ToList();
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            modules.Should().Contain("production", endpoint.DisplayName).And.NotContain("dashboard", endpoint.DisplayName);
            policies.Should().Contain(ModuleEndpoints.PermissionPolicyPrefix + "production.view", endpoint.DisplayName);
            if (endpoint.RoutePattern.RawText!.EndsWith("/shortages", StringComparison.Ordinal))
            {
                modules.Should().Contain("inventory");
                policies.Should().Contain(ModuleEndpoints.PermissionPolicyPrefix + "inventory.view");
            }
        }
    }

    [Fact]
    public async Task Atualizar_le_a_capacidade_de_novo_com_rate_limit()
    {
        var d = await SeedAsync();
        var (_, supervisor) = await ActorAsync("production_supervision");
        var first = await OkAsync(supervisor, Base + "/indicators" + Q(d));
        var cached = await OkAsync(supervisor, Base + "/indicators" + Q(d));
        cached.GetProperty("updatedAt").GetDateTimeOffset().Should().Be(first.GetProperty("updatedAt").GetDateTimeOffset(), "dentro dos 60 s vem do cache");
        (await supervisor.PostAsync(Base + "/refresh", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var fresh = await OkAsync(supervisor, Base + "/indicators" + Q(d));
        fresh.GetProperty("updatedAt").GetDateTimeOffset().Should().BeAfter(first.GetProperty("updatedAt").GetDateTimeOffset());
        var limited = await supervisor.PostAsync(Base + "/refresh", null);
        limited.StatusCode.Should().Be((HttpStatusCode)429);
        (await limited.Content.ReadAsStringAsync()).Should().Contain("Aguarde alguns segundos para atualizar de novo.");

        // O quadro e a qualidade não passam pelo cache (tempo real e recarga de 60 s).
        var board = await OkAsync(supervisor, Base + "/board" + Q(d));
        (await OkAsync(supervisor, Base + "/board" + Q(d))).GetProperty("updatedAt").GetDateTimeOffset().Should().BeAfter(board.GetProperty("updatedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Com_300_OS_200_faltas_e_100_NC_cada_bloco_chega_em_menos_de_1_5_s()
    {
        var tag = Tag();
        var line = "Carga " + tag;
        var orderIds = new List<Guid>();
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            var calendar = await scope.ServiceProvider.GetRequiredService<CalendarService>().DefaultEntityAsync();
            var centers = Enumerable.Range(0, 3).Select(i =>
            {
                var c = new WorkCenter { Name = $"Carga {i} {tag}", Line = line, CalendarId = calendar.Id, CreatedAt = DateTimeOffset.UtcNow.AddYears(-1) };
                c.SetShifts([new Shift("07:00", "16:00")]);
                return c;
            }).ToList();
            db.WorkCenters.AddRange(centers);
            var projects = new List<Guid>();
            for (var p = 0; p < 10; p++)
            {
                projects.Add(await ProjectAsync(db, $"K{tag}{p}", Today.AddDays(p % 3 == 0 ? -2 : 20)));
            }

            var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
            var location = await db.Locations.Select(l => l.Id).FirstAsync();
            var items = Enumerable.Range(0, 20).Select(i => new BemAqui.Inventory.Domain.Item { Type = ItemType.RawMaterial, Code = $"MPK-{tag}-{i}", Name = $"Material {i} {tag}", UnitId = unit, IsActive = true, CreatedAt = DateTimeOffset.UtcNow }).ToList();
            db.Items.AddRange(items);
            var ncCount = 0;
            for (var o = 0; o < 300; o++)
            {
                var order = Order($"OS-K{tag}-{o:000}", projects[o % projects.Count], o % 10 == 0 ? ProductionOrderStatus.Quality : ProductionOrderStatus.InProgress);
                db.ProductionOrders.Add(order);
                orderIds.Add(order.Id);
                var inQuality = o % 10 == 0;
                var cut = Step(order.Id, 1, "C", "Corte", centers[0].Id, inQuality ? ProductionStepStatus.Done : ProductionStepStatus.InProgress, 2m);
                var mount = Step(order.Id, 2, "M", "Montagem", centers[1].Id, inQuality ? ProductionStepStatus.Done : ProductionStepStatus.Waiting, 3m);
                var quality = Step(order.Id, 3, "Q", "Qualidade", centers[2].Id, inQuality ? ProductionStepStatus.Ready : ProductionStepStatus.Waiting, 0.5m, quality: true);
                db.ProductionOrderSteps.AddRange(cut, mount, quality);
                db.ScheduleStepForecasts.Add(new ScheduleStepForecast { StepId = cut.Id, ProductionOrderId = order.Id, Sequence = 1, Hours = 2m, PlannedStart = Today.AddDays(o % 15), PlannedEnd = Today.AddDays(o % 15), RunId = Guid.CreateVersion7() });
                if (o < 200)
                {
                    db.Reservations.Add(new BemAqui.Inventory.Domain.Reservation
                    {
                        ItemId = items[o % items.Count].Id, LocationId = location, ProductionOrderId = order.Id, ProjectId = order.ProjectId, BomRevision = 1, RequestedQuantity = 5m, Quantity = 1m,
                        Origin = "BomReleased", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                    });
                }

                if (ncCount < 100 && o % 3 == 0)
                {
                    db.NonConformities.Add(new NonConformity
                    {
                        Number = $"NC-K{tag}-{ncCount:000}", OrderId = order.Id, OrderStepId = quality.Id, Attempt = 1, Description = "Carga", ResponsibleUserId = Guid.CreateVersion7(),
                        ReturnToStepId = cut.Id, OpenedAt = DateTimeOffset.UtcNow.AddDays(-(ncCount % 20)),
                    });
                    ncCount++;
                }
            }

            await db.SaveChangesAsync();
        }

        try
        {
            var (_, supervisor) = await ActorAsync("production_supervision");
            var query = "?line=" + Uri.EscapeDataString(line);
            foreach (var block in Blocks)
            {
                await OkAsync(supervisor, Base + block + query); // aquece (JIT e conexões)
            }

            (await OkAsync(supervisor, Base + "/board" + query)).GetProperty("openCount").GetInt32().Should().Be(300);
            (await OkAsync(supervisor, Base + "/shortages" + query)).GetProperty("total").GetInt32().Should().Be(200);
            (await OkAsync(supervisor, Base + "/quality" + query)).GetProperty("openNonConformities").GetInt32().Should().Be(100);
            await supervisor.PostAsync(Base + "/refresh", null);
            foreach (var block in Blocks)
            {
                var watch = Stopwatch.StartNew();
                await OkAsync(supervisor, Base + block + query);
                watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1.5), Base + block);
            }
        }
        finally
        {
            await WithDbAsync(async db =>
            {
                await db.Reservations.Where(r => orderIds.Contains(r.ProductionOrderId)).ExecuteDeleteAsync();
                await db.NonConformities.Where(n => orderIds.Contains(n.OrderId)).ExecuteDeleteAsync();
                await db.ScheduleStepForecasts.Where(f => orderIds.Contains(f.ProductionOrderId)).ExecuteDeleteAsync();
                await db.ProductionOrderSteps.Where(s => orderIds.Contains(s.OrderId)).ExecuteDeleteAsync();
                return await db.ProductionOrders.Where(o => orderIds.Contains(o.Id)).ExecuteDeleteAsync();
            });
        }
    }
}
