using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Messaging;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PLN-01a com o banco real: gravação atômica nas tabelas do plano sem tocar a OS (CA 9 e 10), concorrência (CA 12), job de hora em
/// hora com execução dupla, dois tenants e módulo não liberado (CA 13), "Recalcular agora" e três camadas (CA 14 e 18), leitura e
/// simulação sem gravar (CA 15) e desempenho com 50 e 300 OS (CA 17). Banco desta classe; o plano é do tenant inteiro, então cada teste
/// usa centros e calendário próprios (todos os dias úteis, sem exceção: as datas não dependem do dia da semana) e filtra pelos ids que criou.
/// </summary>
public class PlanningTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/production/schedule";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static string Unique(string name) => name + " " + Guid.NewGuid().ToString("N")[..6];

    private async Task<AsyncServiceScope> ScopeAsync(Tenant? tenant = null)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync((tenant ?? tenancy.TenantA).Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action, Tenant? tenant = null)
    {
        await using var scope = await ScopeAsync(tenant);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Usuário num grupo do seed (seed_key), num grupo com as ações dadas ("production:view,edit") ou sem grupo.</summary>
    private async Task<HttpClient> ActorAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "pln" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (seedKey.Contains(':', StringComparison.Ordinal))
            {
                var group = new Group { Name = "Grupo " + Guid.NewGuid().ToString("N")[..8], Description = "Teste" };
                db.Groups.Add(group);
                var (module, actions) = (seedKey.Split(':')[0], seedKey.Split(':')[1].Split(','));
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                foreach (var action in actions)
                {
                    db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = action });
                }

                groupId = group.Id;
            }
            else
            {
                groupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync();
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    /// <summary>Calendário próprio com os 7 dias úteis e um centro com <paramref name="hours"/> h por dia, criado há um ano.</summary>
    private async Task<Guid> CenterAsync(string name, decimal hours = 8m, Tenant? tenant = null)
    {
        await using var scope = await ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<IProductionDb>();
        var created = DateTimeOffset.UtcNow.AddYears(-1);
        var calendar = new Calendar { Name = Unique("Planejamento"), WorkingDays = [.. Enum.GetValues<Weekday>()], CreatedAt = created };
        db.Calendars.Add(calendar);
        var center = new WorkCenter { Name = Unique(name), CalendarId = calendar.Id, CreatedAt = created };
        center.SetShifts([new Shift("08:00", "12:00"), new Shift("13:00", "17:00")]);
        if (hours != 8m)
        {
            center.SetShifts([new Shift("00:00", TimeOnly.FromTimeSpan(TimeSpan.FromHours((double)hours)).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture))]);
        }

        db.WorkCenters.Add(center);
        await db.SaveChangesAsync();
        return center.Id;
    }

    /// <summary>OS de fabricação gravada direto (a geração tem os próprios testes), com as etapas (nome, centro, horas, situação).</summary>
    private async Task<(Guid Order, Guid[] Steps)> OrderAsync(Tenant? tenant, params (string Name, Guid? Center, decimal Hours, ProductionStepStatus Status)[] steps)
    {
        await using var scope = await ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<IProductionDb>();
        var order = new ProductionOrder
        {
            Number = "OS-PLN-" + Guid.NewGuid().ToString("N")[..8], Type = ProductionOrderType.Fabrication, ProjectId = Guid.CreateVersion7(), ContractId = Guid.CreateVersion7(),
            ProjectItemId = Guid.CreateVersion7(), Quantity = 1m, Status = steps.Any(s => s.Status != ProductionStepStatus.Waiting && s.Status != ProductionStepStatus.Ready) ? ProductionOrderStatus.InProgress : ProductionOrderStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ProductionOrders.Add(order);
        var ids = new Guid[steps.Length];
        for (var i = 0; i < steps.Length; i++)
        {
            var step = new ProductionOrderStep
            {
                OrderId = order.Id, Sequence = i + 1, Name = steps[i].Name, Code = ((char)('A' + i)).ToString(), WorkCenterId = steps[i].Center, StandardHours = steps[i].Hours, Status = steps[i].Status,
            };
            db.ProductionOrderSteps.Add(step);
            ids[i] = step.Id;
        }

        await db.SaveChangesAsync();
        return (order.Id, ids);
    }

    private Task<(Guid Order, Guid[] Steps)> OrderAsync(params (string Name, Guid? Center, decimal Hours, ProductionStepStatus Status)[] steps) => OrderAsync(null, steps);

    private async Task<ScheduleRecalculation> RecalculateAsync(ScheduleTrigger trigger = ScheduleTrigger.Manual, Tenant? tenant = null)
    {
        await using var scope = await ScopeAsync(tenant);
        return await scope.ServiceProvider.GetRequiredService<ScheduleService>().RecalculateAsync(trigger, null);
    }

    /// <summary>
    /// As OS de carga dos testes de desempenho saem do plano no fim (canceladas): o tenant é da classe inteira e, com mais de
    /// <see cref="PlanningService.InRequestLimit"/> OS abertas, o "Recalcular agora" vai para a fila (CA 14) nos outros testes.
    /// </summary>
    private Task<int> CancelLoadAsync(string numberPrefix) => WithDbAsync(db => db.ProductionOrders.Where(o => o.Number.StartsWith(numberPrefix))
        .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, ProductionOrderStatus.Cancelled)));

    private async Task SetProductionReleasedAsync(bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "production").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    [Fact]
    public async Task Execucao_grava_o_plano_numa_transacao_sem_tocar_a_OS_nem_a_instalacao()
    {
        var cut = await CenterAsync("Corte");
        var assembly = await CenterAsync("Montagem", 16m);
        var (order, steps) = await OrderAsync(("Corte", cut, 12m, ProductionStepStatus.Ready), ("Montagem", assembly, 20m, ProductionStepStatus.Waiting));
        var installation = await WithDbAsync(async db =>
        {
            var os = new ProductionOrder
            {
                Number = "OS-INST-" + Guid.NewGuid().ToString("N")[..6], Type = ProductionOrderType.Installation, ProjectId = Guid.CreateVersion7(), ContractId = Guid.CreateVersion7(),
                InstallerId = Guid.CreateVersion7(), ContractValue = 1000m, PlannedStart = Today.AddDays(30), PlannedEnd = Today.AddDays(35), CreatedAt = DateTimeOffset.UtcNow,
            };
            db.ProductionOrders.Add(os);
            await db.SaveChangesAsync();
            return os.Id;
        });
        var keys = steps.Select(s => s.ToString()).Append(order.ToString()).ToList();
        var before = await WithDbAsync(async db => new
        {
            Order = await db.ProductionOrders.AsNoTracking().Where(o => o.Id == order).Select(o => o.Version).SingleAsync(),
            Audit = await db.AuditEntries.CountAsync(a => keys.Contains(a.EntityId)),
        });

        var first = await RecalculateAsync();
        first.Ran.Should().BeTrue();
        first.Run!.Status.Should().Be(ScheduleRunStatus.Succeeded, first.Run.Error);
        var second = await RecalculateAsync(ScheduleTrigger.Hourly);
        second.Run!.ChangedOrdersCount.Should().Be(0, "nada mudou entre as execuções");

        await WithDbAsync(async db =>
        {
            var forecasts = await db.ScheduleStepForecasts.AsNoTracking().Where(f => f.ProductionOrderId == order).OrderBy(f => f.Sequence).ToListAsync();
            forecasts.Should().HaveCount(2, "execução dupla não duplica linha");
            forecasts[0].Should().Match<ScheduleStepForecast>(f => f.PlannedStart == Today && f.PlannedEnd == Today.AddDays(1) && f.Hours == 12m && f.RunId == second.Run.Id);
            forecasts[1].Should().Match<ScheduleStepForecast>(f => f.PlannedStart == Today.AddDays(2) && f.PlannedEnd == Today.AddDays(3));
            var allocations = await db.ScheduleAllocations.AsNoTracking().Where(a => a.ProductionOrderId == order).OrderBy(a => a.Date).ThenBy(a => a.WorkCenterId == assembly).ToListAsync();
            allocations.Select(a => (a.WorkCenterId, a.Date, a.Hours)).Should().Equal(
                (cut, Today, 8m), (cut, Today.AddDays(1), 4m), (assembly, Today.AddDays(2), 16m), (assembly, Today.AddDays(3), 4m));

            (await db.ProductionOrders.AsNoTracking().Where(o => o.Id == order).Select(o => o.Version).SingleAsync()).Should().Be(before.Order, "CA 10: a versão da OS não muda");
            (await db.AuditEntries.CountAsync(a => keys.Contains(a.EntityId))).Should().Be(before.Audit, "o histórico da OS não recebe datas do plano");
            (await db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == order).AllAsync(s => s.PlannedStart == null && s.PlannedEnd == null)).Should().BeTrue();
            var inst = await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == installation);
            (inst.PlannedStart, inst.PlannedEnd).Should().Be((Today.AddDays(30), Today.AddDays(35)), "CA 9: as datas da instalação nunca mudam");
            (await db.ScheduleStepForecasts.AsNoTracking().AnyAsync(f => f.ProductionOrderId == installation)).Should().BeFalse("a OS de instalação não entra no cálculo");
            (await db.AuditEntries.CountAsync(a => a.Entity.StartsWith("Schedule"))).Should().Be(0, "as tabelas do plano ficam fora da auditoria");
            return 0;
        });

        // Leitura pelo contrato e pela API: "Previsto" das etapas e "Fim previsto" da OS.
        var owner = await ActorAsync("owner");
        var read = await owner.GetAsync($"{Base}/orders?ids={order}&ids={installation}");
        read.StatusCode.Should().Be(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var body = await JsonAsync(read);
        body.GetArrayLength().Should().Be(1);
        body[0].GetProperty("plannedEnd").GetString().Should().Be(Today.AddDays(3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        body[0].GetProperty("steps").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Etapa_sem_centro_e_OS_sem_roteiro_aparecem_nos_avisos()
    {
        var center = await CenterAsync("Pintura");
        var (blocked, _) = await OrderAsync(("Solda", null, 4m, ProductionStepStatus.Ready), ("Pintura", center, 4m, ProductionStepStatus.Waiting));
        var (noRouting, _) = await OrderAsync();
        await RecalculateAsync();

        var owner = await ActorAsync("owner");
        var summary = await JsonAsync(await owner.GetAsync(Base));
        summary.GetProperty("lastRun").GetProperty("status").GetString().Should().Be("Succeeded");
        var warnings = summary.GetProperty("warnings").EnumerateArray().ToList();
        warnings.Should().Contain(w => w.GetProperty("orderId").GetGuid() == blocked && w.GetProperty("reason").GetString() == "Etapa Solda sem centro de trabalho"
            && w.GetProperty("action").GetString() == "SetWorkCenter");
        warnings.Should().Contain(w => w.GetProperty("orderId").GetGuid() == noRouting && w.GetProperty("action").GetString() == "ChooseRouting");
        (await WithDbAsync(db => db.ScheduleStepForecasts.CountAsync(f => f.ProductionOrderId == noRouting))).Should().Be(0, "CA 9: OS sem etapas não entra no cálculo");
    }

    [Fact]
    public async Task Dez_pedidos_simultaneos_rodam_no_maximo_duas_vezes()
    {
        var center = await CenterAsync("Dobra");
        for (var i = 0; i < 20; i++)
        {
            await OrderAsync(("Dobra", center, 3m, ProductionStepStatus.Ready), ("Dobra 2", center, 2m, ProductionStepStatus.Waiting));
        }

        // Um cálculo "em andamento" (o lock do tenant preso por outra conexão): os 10 pedidos marcam pendente, uma marca só.
        var since = DateTimeOffset.UtcNow;
        await using (var holder = await ScopeAsync())
        {
            var db = holder.ServiceProvider.GetRequiredService<TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({ScheduleService.LockKey}, 0))) AS l").ToListAsync();
            var queued = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => RecalculateAsync(ScheduleTrigger.Hourly))));
            queued.Should().OnlyContain(r => r.Queued && !r.Ran, "ninguém calcula em paralelo com quem tem o lock");
            (await WithDbAsync(d => d.ScheduleRuns.CountAsync(r => r.Status == ScheduleRunStatus.Pending))).Should().Be(1, "a marca é uma só");
            await transaction.CommitAsync();
        }

        // Quem calcula depois assume a marca: uma execução para os 10 pedidos, não 10.
        (await RecalculateAsync(ScheduleTrigger.Hourly)).Run!.Status.Should().Be(ScheduleRunStatus.Succeeded);
        var runs = await WithDbAsync(db => db.ScheduleRuns.AsNoTracking().Where(r => r.RequestedAt >= since).ToListAsync());
        runs.Should().ContainSingle(r => r.Status == ScheduleRunStatus.Succeeded);
        runs.Should().NotContain(r => r.Status == ScheduleRunStatus.Pending);

        // Pedidos ao mesmo tempo sem lock preso: no máximo uma execução e a pendente, nunca em paralelo.
        var start = DateTimeOffset.UtcNow;
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => RecalculateAsync(ScheduleTrigger.Hourly))));
        // Se um recálculo disparado pelos próprios pedidos já tinha o lock, todos ficam na fila e nenhum roda; os que rodam terminam bem.
        results.Where(r => r.Ran).All(r => r.Run!.Status == ScheduleRunStatus.Succeeded).Should().BeTrue();
        var concurrent = await WithDbAsync(db => db.ScheduleRuns.AsNoTracking().Where(r => r.RequestedAt >= start).ToListAsync());
        concurrent.Count(r => r.Status == ScheduleRunStatus.Pending).Should().BeLessThanOrEqualTo(1);
        var spans = concurrent.Where(r => r.Status == ScheduleRunStatus.Succeeded).OrderBy(r => r.StartedAt).ToList();
        spans.Zip(spans.Skip(1)).All(p => p.First.FinishedAt <= p.Second.StartedAt).Should().BeTrue("as execuções nunca se sobrepõem");
    }

    [Fact]
    public async Task Job_de_hora_em_hora_e_idempotente_isola_tenants_e_confere_o_modulo()
    {
        var center = await CenterAsync("Corte A");
        var (orderA, stepsA) = await OrderAsync(("Corte", center, 5m, ProductionStepStatus.Ready));
        var tenantB = tenancy.TenantB;
        var centerB = await CenterAsync("Corte B", tenant: tenantB);
        var (orderB, _) = await OrderAsync(tenantB, ("Corte", centerB, 5m, ProductionStepStatus.Ready));
        var job = ActivatorUtilities.CreateInstance<PlanningRecalculateJob>(tenancy.Factory.Services);

        await job.ExecuteAsync(tenancy.TenantA.Id, new PlanningRecalculateArgs(), CancellationToken.None);
        await job.ExecuteAsync(tenancy.TenantA.Id, new PlanningRecalculateArgs(), CancellationToken.None);
        await job.ExecuteAsync(tenantB.Id, new PlanningRecalculateArgs(), CancellationToken.None);

        (await WithDbAsync(db => db.ScheduleStepForecasts.CountAsync(f => stepsA.Contains(f.StepId)))).Should().Be(1, "execução dupla não duplica");
        (await WithDbAsync(db => db.ScheduleAllocations.CountAsync(a => a.ProductionOrderId == orderA))).Should().Be(1);
        (await WithDbAsync(db => db.ScheduleStepForecasts.AnyAsync(f => f.ProductionOrderId == orderB))).Should().BeFalse("o tenant A não vê a OS do B");
        (await WithDbAsync(db => db.ScheduleStepForecasts.AnyAsync(f => f.ProductionOrderId == orderB), tenantB)).Should().BeTrue();
        (await WithDbAsync(db => db.ScheduleStepForecasts.AnyAsync(f => f.ProductionOrderId == orderA), tenantB)).Should().BeFalse("nem o B a do A");

        // O recorrente enfileira só o tenantId, e só para quem tem a Produção liberada; o job confere de novo no início.
        var capturing = new CapturingJobClient();
        var recurring = new PlanningRecalculateRecurringJob(tenancy.Factory.Services.GetRequiredService<ITenantDirectory>(), capturing,
            tenancy.Factory.Services.GetRequiredService<ITenantResolver>());
        await recurring.ExecuteAsync(CancellationToken.None);
        capturing.Created.Should().Contain(c => (Guid)c.Job.Args[0] == tenancy.TenantA.Id && c.Job.Type == typeof(PlanningRecalculateJob));
        capturing.Created.Should().OnlyContain(c => c.Job.Args[1] is PlanningRecalculateArgs);

        await SetProductionReleasedAsync(false);
        try
        {
            var runs = await WithDbAsync(db => db.ScheduleRuns.CountAsync());
            var skipped = new CapturingJobClient();
            await new PlanningRecalculateRecurringJob(tenancy.Factory.Services.GetRequiredService<ITenantDirectory>(), skipped,
                tenancy.Factory.Services.GetRequiredService<ITenantResolver>()).ExecuteAsync(CancellationToken.None);
            skipped.Created.Should().NotContain(c => (Guid)c.Job.Args[0] == tenancy.TenantA.Id, "módulo não liberado não enfileira");
            await job.ExecuteAsync(tenancy.TenantA.Id, new PlanningRecalculateArgs(), CancellationToken.None);
            (await WithDbAsync(db => db.ScheduleRuns.CountAsync())).Should().Be(runs, "o job termina sem calcular");
        }
        finally
        {
            await SetProductionReleasedAsync(true);
        }
    }

    [Fact]
    public async Task Recalcular_agora_e_as_tres_camadas()
    {
        var center = await CenterAsync("Montagem");
        await OrderAsync(("Montagem", center, 6m, ProductionStepStatus.Ready));
        var editor = await ActorAsync("production:view,edit");
        var viewer = await ActorAsync("production:view");
        var nobody = await ActorAsync(null);

        var response = await editor.PostAsync(Base + "/recalculate", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var run = (await JsonAsync(response)).GetProperty("run");
        run.GetProperty("trigger").GetString().Should().Be("Manual");
        run.GetProperty("ordersCount").GetInt32().Should().BeGreaterThan(0);
        run.GetProperty("finishedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        (await viewer.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK);
        var denied = await viewer.PostAsync(Base + "/recalculate", null);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, "CA 14: sem production.edit");
        Code(await JsonAsync(denied)).Should().Be("forbidden");
        var none = await nobody.GetAsync(Base);
        none.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(none)).Should().Be("forbidden");

        await SetProductionReleasedAsync(false);
        try
        {
            foreach (var blocked in new[] { await editor.GetAsync(Base), await editor.PostAsync(Base + "/recalculate", null), await editor.GetAsync(Base + "/orders") })
            {
                blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                Code(await JsonAsync(blocked)).Should().Be("module_not_released");
            }
        }
        finally
        {
            await SetProductionReleasedAsync(true);
        }

        var summary = await (await editor.GetAsync(Base)).Content.ReadAsStringAsync();
        summary.Should().NotContainAny(["cost", "Cost", "amount"], "CA 18: nenhuma resposta tem custo");
    }

    [Fact]
    public async Task Leitura_e_simulacao_nao_gravam()
    {
        var center = await CenterAsync("Usinagem");
        var (first, _) = await OrderAsync(("Usinagem", center, 8m, ProductionStepStatus.Ready));
        var (second, _) = await OrderAsync(("Usinagem", center, 8m, ProductionStepStatus.Ready));
        await RecalculateAsync();
        await using var scope = await ScopeAsync();
        var schedule = scope.ServiceProvider.GetRequiredService<IProductionSchedule>();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var counts = (await db.ScheduleRuns.CountAsync(), await db.ScheduleStepForecasts.CountAsync(), await db.ScheduleAllocations.CountAsync());

        var stored = await schedule.ForOrdersAsync([first, second]);
        stored.Should().HaveCount(2);
        (await schedule.AllocationsAsync(Today, Today.AddDays(30), [center])).Should().HaveCount(2).And.OnlyContain(a => a.WorkCenterId == center);
        (await schedule.LastRunAsync())!.Status.Should().Be(ScheduleRunStatus.Succeeded);

        var projectOf = await db.ProductionOrders.AsNoTracking().Where(o => o.Id == first || o.Id == second).ToDictionaryAsync(o => o.Id, o => o.ProjectId);
        var virtualProject = new SimulatedProject(Guid.CreateVersion7(), "ORC-SIM", null, 1, [[new SimulatedStep("Usinagem", center, 8m)]]);
        var simulation = await schedule.SimulateAsync([projectOf[second], projectOf[first]], virtualProject);
        var ends = simulation.Projects.ToDictionary(p => p.ProjectId, p => p.FabricationEnd);
        ends[virtualProject.ProjectId].Should().Be(Today, "o projeto virtual em primeiro na fila ocupa o primeiro dia");
        ends[projectOf[first]].Should().BeAfter(Today, "o centro de hoje ficou com o projeto virtual");
        ends[projectOf[second]].Should().BeAfter(Today);
        (await db.ScheduleRuns.CountAsync(), await db.ScheduleStepForecasts.CountAsync(), await db.ScheduleAllocations.CountAsync()).Should().Be(counts, "CA 15: a simulação não grava");
        (await schedule.FabricationEndByProjectsAsync([projectOf[first]])).Single().FabricationEnd.Should().NotBeNull();
    }

    [Fact]
    public async Task Recalculo_de_50_OS_em_menos_de_2_s_e_de_300_em_menos_de_5_s()
    {
        var centers = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            centers.Add(await CenterAsync("Centro " + i));
        }

        async Task SeedAsync(int count, int offset) => await WithDbAsync(async db =>
        {
            for (var i = offset; i < offset + count; i++)
            {
                var order = new ProductionOrder
                {
                    Number = "OS-PERF-" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + Guid.NewGuid().ToString("N")[..4], Type = ProductionOrderType.Fabrication,
                    ProjectId = Guid.CreateVersion7(), ContractId = Guid.CreateVersion7(), ProjectItemId = Guid.CreateVersion7(), Quantity = 1m, CreatedAt = DateTimeOffset.UtcNow,
                };
                db.ProductionOrders.Add(order);
                for (var k = 0; k < 5; k++)
                {
                    db.ProductionOrderSteps.Add(new ProductionOrderStep
                    {
                        OrderId = order.Id, Sequence = k + 1, Name = "Etapa " + k, Code = ((char)('A' + k)).ToString(), WorkCenterId = centers[(i + k) % 6], StandardHours = 4m + k,
                        Status = k == 0 ? ProductionStepStatus.Ready : ProductionStepStatus.Waiting,
                    });
                }
            }

            return await db.SaveChangesAsync();
        });

        try
        {
            await SeedAsync(50, 0);
            await RecalculateAsync(); // aquece
            var watch = Stopwatch.StartNew();
            var fifty = await RecalculateAsync();
            watch.Stop();
            fifty.Run!.Status.Should().Be(ScheduleRunStatus.Succeeded);
            watch.ElapsedMilliseconds.Should().BeLessThan(2000, "CA 17: 50 OS com 5 etapas e 6 centros");

            await SeedAsync(250, 50);
            watch.Restart();
            var many = await RecalculateAsync();
            watch.Stop();
            many.Run!.OrdersCount.Should().BeGreaterThanOrEqualTo(300);
            watch.ElapsedMilliseconds.Should().BeLessThan(5000, "CA 17: 300 OS");
        }
        finally
        {
            await CancelLoadAsync("OS-PERF-");
        }
    }

    [Fact]
    public async Task Fila_de_50_projetos_e_300_OS_abre_em_menos_de_1_s_e_a_previa_em_menos_de_2_s()
    {
        // PLN-02 CA 10: 50 projetos com 6 OS de 5 etapas cada, em 6 centros.
        var centers = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            centers.Add(await CenterAsync("Fila " + i));
        }

        try
        {
            var projects = Enumerable.Range(0, 50).Select(_ => Guid.CreateVersion7()).ToList();
            await WithDbAsync(async db =>
            {
                for (var p = 0; p < projects.Count; p++)
                {
                    for (var o = 0; o < 6; o++)
                    {
                        var order = new ProductionOrder
                        {
                            Number = "OS-FILA-" + p.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + o + Guid.NewGuid().ToString("N")[..4], Type = ProductionOrderType.Fabrication,
                            ProjectId = projects[p], ContractId = Guid.CreateVersion7(), ProjectItemId = Guid.CreateVersion7(), Quantity = 1m, CreatedAt = DateTimeOffset.UtcNow,
                        };
                        db.ProductionOrders.Add(order);
                        for (var k = 0; k < 5; k++)
                        {
                            db.ProductionOrderSteps.Add(new ProductionOrderStep
                            {
                                OrderId = order.Id, Sequence = k + 1, Name = "Etapa " + k, Code = ((char)('A' + k)).ToString(), WorkCenterId = centers[(p + o + k) % 6], StandardHours = 2m + k,
                                Status = k == 0 ? ProductionStepStatus.Ready : ProductionStepStatus.Waiting,
                            });
                        }
                    }
                }

                return await db.SaveChangesAsync();
            });
            (await RecalculateAsync()).Run!.Status.Should().Be(ScheduleRunStatus.Succeeded, "o recálculo também monta a fila");

            var owner = await ActorAsync("owner");
            const string queue = "/api/v1/production/planning/queue";
            (await owner.GetAsync(queue)).StatusCode.Should().Be(HttpStatusCode.OK); // aquece
            var watch = Stopwatch.StartNew();
            var opened = await owner.GetAsync(queue);
            watch.Stop();
            opened.StatusCode.Should().Be(HttpStatusCode.OK);
            watch.ElapsedMilliseconds.Should().BeLessThan(1000, "CA 10: a fila abre em menos de 1 s");
            var order = (await JsonAsync(opened)).GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("projectId").GetGuid()).ToList();
            order.Should().Contain(projects);

            var reversed = Enumerable.Reverse(order).ToList();
            watch.Restart();
            var preview = await owner.PostAsJsonAsync(queue + "/preview", new { order = reversed });
            watch.Stop();
            preview.StatusCode.Should().Be(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
            watch.ElapsedMilliseconds.Should().BeLessThan(2000, "CA 10: a prévia chega em menos de 2 s");
            (await JsonAsync(preview)).GetProperty("rows").GetArrayLength().Should().Be(order.Count);
        }
        finally
        {
            await CancelLoadAsync("OS-FILA-");
        }
    }
}
