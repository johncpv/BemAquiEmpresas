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
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CultureInfo = System.Globalization.CultureInfo;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PLN-03 com o banco real e o plano gravado direto (como o <c>ScheduleService</c> faria), com o relógio fixo na sexta 30/10/2026: a visão
/// por centro com o dataset fixo da PLN-01a (CA 5: barras na ordem do tempo, sub-linhas quando se sobrepõem no dia, ocupação do dia nas
/// faixas da PLN-04, feriado com o nome) e o desempenho da API com 300 OS de 5 etapas (CA 13). Banco desta classe; cada teste filtra pelos
/// centros e OS que criou.
/// </summary>
public class GanttTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    /// <summary>Sexta 30/10/2026, 12:00 em São Paulo.</summary>
    private static readonly FixedClock Friday = new(new DateTimeOffset(2026, 10, 30, 15, 0, 0, TimeSpan.Zero));

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static string Unique(string name) => name + " " + Guid.NewGuid().ToString("N")[..6];

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<T> WithServiceAsync<T>(Func<GanttService, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        var sp = scope.ServiceProvider;
        var service = new GanttService(sp.GetRequiredService<IProductionDb>(), sp.GetRequiredService<PlanningQueueStore>(), sp.GetRequiredService<BemAqui.Commercial.Contracts.IProjects>(),
            sp.GetRequiredService<BemAqui.Parties.Contracts.IParties>(), sp.GetRequiredService<IWorkCenters>(), sp.GetRequiredService<IWorkCenterCapacity>(), Friday);
        return await action(service);
    }

    private async Task<HttpClient> OwnerAsync()
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "gnt" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var group = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    private sealed record Plant(Guid CalendarId, Guid Cut, Guid Assembly);

    /// <summary>Calendário seg–sex próprio com Finados (02/11/2026, 0 h); Corte 8 h/dia e Montagem 16 h/dia.</summary>
    private async Task<Plant> PlantAsync()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IProductionDb>();
        var created = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var calendar = new Calendar { Name = Unique("Cronograma"), WorkingDays = [.. CalendarService.DefaultWorkingDays], CreatedAt = created };
        db.Calendars.Add(calendar);
        db.CalendarExceptions.Add(new CalendarExceptionDay { CalendarId = calendar.Id, Date = D(11, 2), Type = CalendarExceptionType.Holiday, HoursAvailable = 0, Description = "Finados", CreatedAt = created });
        var cut = new WorkCenter { Name = Unique("Corte"), Line = Unique("Cabines"), CalendarId = calendar.Id, CreatedAt = created };
        cut.SetShifts([new Shift("07:00", "11:00"), new Shift("12:00", "16:00")]);
        var assembly = new WorkCenter { Name = Unique("Montagem"), Line = cut.Line, CalendarId = calendar.Id, CreatedAt = created };
        assembly.SetShifts([new Shift("00:00", "16:00")]);
        db.WorkCenters.AddRange(cut, assembly);
        await db.SaveChangesAsync();
        return new Plant(calendar.Id, cut.Id, assembly.Id);
    }

    private async Task<ScheduleOrderInput> OrderAsync(string label, Guid projectId, params (string Name, Guid Center, decimal Hours, ProductionStepStatus Status, decimal Logged)[] steps)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IProductionDb>();
        var order = new ProductionOrder
        {
            Number = label + "-" + Guid.NewGuid().ToString("N")[..8], Type = ProductionOrderType.Fabrication, ProjectId = projectId, ContractId = Guid.CreateVersion7(),
            ProjectItemId = Guid.CreateVersion7(), Quantity = 1m, Status = ProductionOrderStatus.Open, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ProductionOrders.Add(order);
        var inputs = new List<ScheduleStepInput>();
        for (var i = 0; i < steps.Length; i++)
        {
            var step = new ProductionOrderStep
            {
                OrderId = order.Id, Sequence = i + 1, Name = steps[i].Name, Code = ((char)('A' + i)).ToString(), WorkCenterId = steps[i].Center, StandardHours = steps[i].Hours, Status = steps[i].Status,
            };
            db.ProductionOrderSteps.Add(step);
            inputs.Add(new ScheduleStepInput(step.Id, i + 1, steps[i].Name, steps[i].Center, steps[i].Hours, steps[i].Logged, steps[i].Status));
        }

        await db.SaveChangesAsync();
        return new ScheduleOrderInput(order.Id, order.Number, projectId, 1, inputs);
    }

    private async Task SavePlanAsync(ScheduleResult result)
    {
        await WithDbAsync(async db =>
        {
            await db.ScheduleAllocations.ExecuteDeleteAsync();
            await db.ScheduleStepForecasts.ExecuteDeleteAsync();
            var run = new ScheduleRun { Trigger = ScheduleTrigger.Manual, Status = ScheduleRunStatus.Succeeded, RequestedAt = DateTimeOffset.UtcNow, StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow };
            db.ScheduleRuns.Add(run);
            db.ScheduleAllocations.AddRange(result.Allocations.Select(a => new ScheduleAllocation { StepId = a.StepId, ProductionOrderId = a.OrderId, WorkCenterId = a.WorkCenterId, Date = a.Date, Hours = a.Hours }));
            db.ScheduleStepForecasts.AddRange(result.Steps.Select(s => new ScheduleStepForecast
            {
                StepId = s.StepId, ProductionOrderId = s.OrderId, Sequence = s.Sequence, Hours = s.Hours, PlannedStart = s.Start, PlannedEnd = s.End, NoForecastReason = s.NoForecastReason, RunId = run.Id,
            }));
            return await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Por_centro_mostra_as_etapas_em_sub_linhas_a_ocupacao_do_dia_e_o_feriado()
    {
        var plant = await PlantAsync();
        var (p1, p2, p3) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var os1 = await OrderAsync("OS-1", p1, ("Corte", plant.Cut, 12m, ProductionStepStatus.Ready, 0m), ("Montagem", plant.Assembly, 20m, ProductionStepStatus.Waiting, 0m));
        var os2 = await OrderAsync("OS-2", p2, ("Corte", plant.Cut, 6m, ProductionStepStatus.InProgress, 4m), ("Montagem", plant.Assembly, 10m, ProductionStepStatus.Waiting, 0m));
        var os3 = await OrderAsync("OS-3", p3, ("Corte", plant.Cut, 4m, ProductionStepStatus.Ready, 0m), ("Montagem", plant.Assembly, 4m, ProductionStepStatus.Waiting, 0m));
        var today = D(10, 30);
        var horizon = today.AddDays(FiniteScheduler.HorizonDays - 1);
        await using (var scope = await ScopeAsync())
        {
            var capacity = scope.ServiceProvider.GetRequiredService<IWorkCenterCapacity>();
            var centers = new[]
            {
                new ScheduleCenterInput(plant.Cut, "Corte", (await capacity.GetAvailableHoursAsync(plant.Cut, today, horizon))!.Days),
                new ScheduleCenterInput(plant.Assembly, "Montagem", (await capacity.GetAvailableHoursAsync(plant.Assembly, today, horizon))!.Days),
            };
            var projects = new[] { new ScheduleProjectInput(p1, "P1", D(12, 1), D(1, 10), null), new ScheduleProjectInput(p2, "P2", D(12, 10), D(1, 10), null), new ScheduleProjectInput(p3, "P3", D(12, 20), D(1, 10), null) };
            await SavePlanAsync(FiniteScheduler.Run(new ScheduleInput(today, projects, [os1, os2, os3], centers)));
        }

        var line = (await WithDbAsync(db => db.WorkCenters.AsNoTracking().Where(c => c.Id == plant.Cut).Select(c => c.Line).SingleAsync()))!;
        var result = await WithServiceAsync(service => service.CentersAsync(new GanttQuery(Line: line)));
        result.IsSuccess.Should().BeTrue();
        var view = result.Value;
        (view.Today, view.From, view.To).Should().Be((today, D(10, 23), new DateOnly(2027, 1, 22)), "CA 6: período padrão");
        view.Centers.Select(c => c.WorkCenterId).Should().BeEquivalentTo([plant.Cut, plant.Assembly], "CA 9: filtro de linha");
        view.PlanComputedAt.Should().NotBeNull();

        var assembly = view.Centers.Single(c => c.WorkCenterId == plant.Assembly);
        assembly.Bars.Select(b => (b.OrderId, b.PlannedStart, b.PlannedEnd, b.Lane)).Should().Equal(
            (os2.OrderId, D(11, 3), D(11, 3), 0), (os1.OrderId, D(11, 4), D(11, 5), 0), (os3.OrderId, D(11, 5), D(11, 5), 1));
        assembly.Lanes.Should().Be(2, "CA 5: OS-1 e OS-3 se sobrepõem em 05/11");

        var cut = view.Centers.Single(c => c.WorkCenterId == plant.Cut);
        cut.Bars.Should().BeInAscendingOrder(b => b.PlannedStart, "na ordem do tempo");
        var running = cut.Bars.Single(b => b.OrderId == os2.OrderId);
        (running.State, running.RemainingHours, running.Progress).Should().Be((GanttStepState.InProgress, 2m, 0.6667m), "CA 2: 4 de 6 h apontadas");
        var friday = cut.Days.Single(d => d.Date == today);
        (friday.AllocatedHours, friday.CapacityHours, friday.Percent, friday.Band).Should().Be((8m, 8m, 100.0m, FactoryLoadBand.Full), "ocupação do dia nas faixas da PLN-04");
        var holiday = cut.Days.Single(d => d.Date == D(11, 2));
        (holiday.CapacityHours, holiday.ExceptionType, holiday.ExceptionName).Should().Be((0m, CalendarExceptionType.Holiday, "Finados"), "CA 5: o nome da exceção");
        cut.Days.Single(d => d.Date == D(10, 31)).ExceptionName.Should().BeNull("sábado: dia sem capacidade, sem exceção");
        cut.Days.Should().NotContain(d => d.Date == D(11, 10) && d.AllocatedHours == 0m && d.CapacityHours > 0m, "dia útil livre não vem");

        var single = await WithServiceAsync(service => service.CentersAsync(new GanttQuery(WorkCenterId: plant.Assembly)));
        single.Value.Centers.Should().ContainSingle().Which.WorkCenterId.Should().Be(plant.Assembly);
        (await WithServiceAsync(service => service.CentersAsync(new GanttQuery(D(11, 1), D(10, 1))))).Error!.Code.Should().Be("planning_gantt.period");
    }

    [Fact]
    public async Task Trezentas_OS_com_cinco_etapas_respondem_em_menos_de_1_s()
    {
        var today = BusinessTime.Today(TimeProvider.System);
        var projectIds = new List<Guid>();
        await WithDbAsync(async db =>
        {
            var created = DateTimeOffset.UtcNow.AddYears(-1);
            var calendar = new Calendar { Name = Unique("Cronograma perf"), WorkingDays = [.. Enum.GetValues<Weekday>()], CreatedAt = created };
            db.Calendars.Add(calendar);
            var centers = new List<Guid>();
            for (var i = 0; i < 20; i++)
            {
                var center = new WorkCenter { Name = Unique("Perf " + i), CalendarId = calendar.Id, CreatedAt = created };
                center.SetShifts([new Shift("08:00", "12:00"), new Shift("13:00", "17:00")]);
                db.WorkCenters.Add(center);
                centers.Add(center.Id);
            }

            var run = new ScheduleRun { Trigger = ScheduleTrigger.Manual, Status = ScheduleRunStatus.Succeeded, RequestedAt = DateTimeOffset.UtcNow, StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow };
            db.ScheduleRuns.Add(run);
            for (var o = 0; o < 300; o++)
            {
                var project = Guid.CreateVersion7();
                projectIds.Add(project);
                var order = new ProductionOrder
                {
                    Number = "OS-GNT-" + o.ToString("D3", CultureInfo.InvariantCulture) + Guid.NewGuid().ToString("N")[..4], Type = ProductionOrderType.Fabrication, ProjectId = project,
                    ContractId = Guid.CreateVersion7(), ProjectItemId = Guid.CreateVersion7(), Quantity = 1m, CreatedAt = DateTimeOffset.UtcNow,
                };
                db.ProductionOrders.Add(order);
                for (var k = 0; k < 5; k++)
                {
                    var step = new ProductionOrderStep
                    {
                        OrderId = order.Id, Sequence = k + 1, Name = "Etapa " + k, Code = ((char)('A' + k)).ToString(), WorkCenterId = centers[(o + k) % 20], StandardHours = 6m, Status = ProductionStepStatus.Waiting,
                    };
                    db.ProductionOrderSteps.Add(step);
                    var start = today.AddDays(o % 60 + k * 2);
                    db.ScheduleStepForecasts.Add(new ScheduleStepForecast { StepId = step.Id, ProductionOrderId = order.Id, Sequence = k + 1, Hours = 6m, PlannedStart = start, PlannedEnd = start.AddDays(1), RunId = run.Id });
                }
            }

            return await db.SaveChangesAsync();
        });

        var owner = await OwnerAsync();
        const string Url = "/api/v1/production/planning/gantt/projects";
        (await owner.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.OK); // aquece
        var watch = Stopwatch.StartNew();
        var response = await owner.GetAsync(Url);
        watch.Stop();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        watch.ElapsedMilliseconds.Should().BeLessThan(1000, "CA 13: 300 OS com 5 etapas (1.500 barras)");
        var mine = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projects").EnumerateArray()
            .Where(p => projectIds.Contains(p.GetProperty("projectId").GetGuid())).ToList();
        mine.Should().HaveCount(300);
        mine.Sum(p => p.GetProperty("orders").EnumerateArray().Sum(o => o.GetProperty("steps").GetArrayLength())).Should().Be(1500);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
