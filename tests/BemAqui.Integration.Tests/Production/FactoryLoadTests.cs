using System.Diagnostics;
using CultureInfo = System.Globalization.CultureInfo;
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
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PLN-04 com o banco real: o dataset fixo da PLN-01a CA 7 com os números do CA 2 (hoje fixo na sexta 30/10/2026, pelo relógio do serviço),
/// o quadro "Depois de" (CA 3), a exceção nova no calendário depois do plano (CA 4), o drill-down igual à célula (CA 5), o
/// <see cref="IFactoryLoad"/> igual à tela (CA 10), o desempenho com 20 centros, 26 semanas e 300 OS (CA 11) e as três camadas com a
/// allowlist (CA 12). Banco desta classe; o plano é do tenant inteiro, então cada teste grava o próprio plano (as tabelas do plano são
/// escritas aqui direto, como o <c>ScheduleService</c> faria) com centros e calendário próprios, e filtra pelos centros que criou.
/// </summary>
public class FactoryLoadTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/production/planning/load";

    /// <summary>Sexta 30/10/2026, 12:00 em São Paulo: o "hoje" do cenário fixo.</summary>
    private static readonly FixedClock Friday = new(new DateTimeOffset(2026, 10, 30, 15, 0, 0, TimeSpan.Zero));

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

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

    /// <summary>O serviço com o relógio dado (o cenário fixo é de 30/10/2026), no escopo do tenant A.</summary>
    private async Task<T> WithServiceAsync<T>(TimeProvider clock, Func<FactoryLoadService, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        var service = new FactoryLoadService(scope.ServiceProvider.GetRequiredService<IProductionDb>(), scope.ServiceProvider.GetRequiredService<IWorkCenters>(),
            scope.ServiceProvider.GetRequiredService<IWorkCenterCapacity>(), scope.ServiceProvider.GetRequiredService<BemAqui.Commercial.Contracts.IProjects>(), clock);
        return await action(service);
    }

    /// <summary>Usuário num grupo do seed (seed_key), num grupo com as ações dadas ("production:view") ou sem grupo.</summary>
    private async Task<HttpClient> ActorAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "ocp" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
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

    private sealed record Plant(Guid CalendarId, Guid Cut, Guid Assembly);

    /// <summary>Calendário seg–sex próprio com o feriado de 02/11/2026 (0 h); Corte 8 h/dia e Montagem 16 h/dia, criados em 01/01/2026.</summary>
    private async Task<Plant> PlantAsync()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IProductionDb>();
        var created = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var calendar = new Calendar { Name = Unique("Ocupação"), WorkingDays = [.. CalendarService.DefaultWorkingDays], CreatedAt = created };
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

    /// <summary>OS de fabricação gravada direto, com as etapas (nome, centro, horas, situação).</summary>
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

    /// <summary>Grava o resultado do motor nas tabelas do plano (substituindo o plano anterior) e uma execução bem-sucedida.</summary>
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

    private sealed record Fixed(Plant Plant, ScheduleOrderInput Os1, ScheduleOrderInput Os2, ScheduleOrderInput Os3, ScheduleResult Plan);

    /// <summary>
    /// O cenário da PLN-01a CA 7 no banco: os centros e o calendário gravados, as três OS, a capacidade lida pelo <see cref="IWorkCenterCapacity"/>
    /// a partir de 30/10 e o motor rodado com essa entrada; o plano é gravado como o <c>ScheduleService</c> faria.
    /// </summary>
    private async Task<Fixed> FixedPlanAsync()
    {
        var plant = await PlantAsync();
        var (p1, p2, p3) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var os1 = await OrderAsync("OS-1", p1, ("Corte", plant.Cut, 12m, ProductionStepStatus.Ready, 0m), ("Montagem", plant.Assembly, 20m, ProductionStepStatus.Waiting, 0m));
        var os2 = await OrderAsync("OS-2", p2, ("Corte", plant.Cut, 6m, ProductionStepStatus.InProgress, 4m), ("Montagem", plant.Assembly, 10m, ProductionStepStatus.Waiting, 0m));
        var os3 = await OrderAsync("OS-3", p3, ("Corte", plant.Cut, 4m, ProductionStepStatus.Ready, 0m), ("Montagem", plant.Assembly, 4m, ProductionStepStatus.Waiting, 0m));
        var today = D(10, 30);
        var to = today.AddDays(FiniteScheduler.HorizonDays - 1);
        await using var scope = await ScopeAsync();
        var capacity = scope.ServiceProvider.GetRequiredService<IWorkCenterCapacity>();
        var centers = new[]
        {
            new ScheduleCenterInput(plant.Cut, "Corte", (await capacity.GetAvailableHoursAsync(plant.Cut, today, to))!.Days),
            new ScheduleCenterInput(plant.Assembly, "Montagem", (await capacity.GetAvailableHoursAsync(plant.Assembly, today, to))!.Days),
        };
        var projects = new[] { new ScheduleProjectInput(p1, "P1", D(12, 1), D(1, 10), null), new ScheduleProjectInput(p2, "P2", D(12, 10), D(1, 10), null), new ScheduleProjectInput(p3, "P3", D(12, 20), D(1, 10), null) };
        var plan = FiniteScheduler.Run(new ScheduleInput(today, projects, [os1, os2, os3], centers));
        await SavePlanAsync(plan);
        return new Fixed(plant, os1, os2, os3, plan);
    }

    private static FactoryLoadCell CellOf(FactoryLoadWeekly load, Guid center, int week) => load.Centers.Single(c => c.WorkCenterId == center).Cells[week];

    [Fact]
    public async Task Dataset_fixo_da_PLN_01a_da_os_numeros_do_CA_2()
    {
        var s = await FixedPlanAsync();
        var view = await WithServiceAsync(Friday, async service => await service.ViewAsync(new FactoryLoadQuery(null, [s.Plant.Cut, s.Plant.Assembly], null)));
        view.IsSuccess.Should().BeTrue();
        var load = view.Value.Load;
        load.Today.Should().Be(D(10, 30));
        load.Weeks.Should().HaveCount(8, "CA 1: 8 semanas a partir da atual");
        (load.Weeks[0].Start, load.Weeks[0].End, load.Weeks[0].From, load.Weeks[1].IsoWeek).Should().Be((D(10, 26), D(11, 1), D(10, 30), 45));
        load.PlanComputedAt.Should().NotBeNull("a tela mostra \"Plano de {hh:mm}\"");

        var cut0 = CellOf(load, s.Plant.Cut, 0);
        (cut0.CommittedHours, cut0.CapacityHours, cut0.Percent).Should().Be((8m, 8m, 100.0m), "Corte 8 de 8 h na semana de 26/10 a 01/11");
        var assembly0 = CellOf(load, s.Plant.Assembly, 0);
        (assembly0.CommittedHours, assembly0.CapacityHours, assembly0.Percent).Should().Be((0m, 16m, 0.0m), "Montagem 0 de 16 h");
        var cut1 = CellOf(load, s.Plant.Cut, 1);
        (cut1.CommittedHours, cut1.CapacityHours, cut1.Percent).Should().Be((10m, 32m, 31.3m), "Corte 10 de 32 h de 02/11 a 08/11 (feriado em 02/11)");
        var assembly1 = CellOf(load, s.Plant.Assembly, 1);
        (assembly1.CommittedHours, assembly1.CapacityHours, assembly1.Percent).Should().Be((34m, 64m, 53.1m), "Montagem 34 de 64 h");
        (cut0.Band, assembly0.Band, cut1.Band, assembly1.Band).Should().Be((FactoryLoadBand.Full, FactoryLoadBand.Idle, FactoryLoadBand.Idle, FactoryLoadBand.Idle));
        (load.Factory.Cells[1].CommittedHours, load.Factory.Cells[1].CapacityHours, load.Factory.Cells[1].Percent).Should().Be((44m, 96m, 45.8m), "Fábrica = Σ ÷ Σ");
        load.Centers.Select(c => c.Line).Should().OnlyContain(l => l != null);
        view.Value.Lines.Should().Contain(load.Centers[0].Line!);

        // CA 9: filtro pela linha dá os mesmos dois centros.
        var byLine = await WithServiceAsync(Friday, async service => await service.ViewAsync(new FactoryLoadQuery(4, null, load.Centers[0].Line)));
        byLine.Value.Load.Centers.Select(c => c.WorkCenterId).Should().BeEquivalentTo(new Guid?[] { s.Plant.Cut, s.Plant.Assembly });
        byLine.Value.Load.Weeks.Should().HaveCount(4);
    }

    [Fact]
    public async Task Depois_de_mostra_a_carga_alem_das_semanas_visiveis()
    {
        var plant = await PlantAsync();
        var order = await OrderAsync("OS-LONGA", Guid.CreateVersion7(), ("Corte", plant.Cut, 600m, ProductionStepStatus.Ready, 0m));
        await using (var scope = await ScopeAsync())
        {
            var to = D(10, 30).AddDays(FiniteScheduler.HorizonDays - 1);
            var days = (await scope.ServiceProvider.GetRequiredService<IWorkCenterCapacity>().GetAvailableHoursAsync(plant.Cut, D(10, 30), to))!.Days;
            await SavePlanAsync(FiniteScheduler.Run(new ScheduleInput(D(10, 30), [new ScheduleProjectInput(order.ProjectId, "P", null, null, null)], [order],
                [new ScheduleCenterInput(plant.Cut, "Corte", days), new ScheduleCenterInput(plant.Assembly, "Montagem", [])])));
        }

        var load = await WithServiceAsync(Friday, service => service.WeeklyAsync(8, [plant.Cut, plant.Assembly]));
        var cut = load.Centers.Single(c => c.WorkCenterId == plant.Cut);
        var visible = cut.Cells.Sum(c => c.CommittedHours);
        visible.Should().Be(8m + 4 * 8m + 6 * 5 * 8m, "sexta 30/10, a semana do feriado e mais 6 semanas cheias");
        cut.Cells.Skip(1).Should().OnlyContain(c => c.Band == FactoryLoadBand.Full, "o plano finito enche o centro até 100%");
        cut.Cells.Should().OnlyContain(c => c.Percent <= 100m, "pelo plano a ocupação não passa de 100%");
        cut.HoursAfter.Should().Be(600m - visible, "CA 3: o que ficou para depois de 20/12");
        var last = await WithDbAsync(db => db.ScheduleAllocations.Where(a => a.ProductionOrderId == order.OrderId).MaxAsync(a => a.Date));
        cut.BusyUntil.Should().Be(last, "\"Corte ocupado até {data}\"");
        load.Centers.Single(c => c.WorkCenterId == plant.Assembly).BusyUntil.Should().BeNull();
        (load.Factory.HoursAfter, load.Factory.BusyUntil).Should().Be((cut.HoursAfter, last));
    }

    [Fact]
    public async Task Excecao_nova_no_calendario_depois_do_plano_da_acima_da_capacidade()
    {
        var s = await FixedPlanAsync();
        await WithDbAsync(async db =>
        {
            db.CalendarExceptions.Add(new CalendarExceptionDay
            {
                CalendarId = s.Plant.CalendarId, Date = D(10, 30), Type = CalendarExceptionType.Stoppage, HoursAvailable = 4, Description = "Parada nova", CreatedAt = DateTimeOffset.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

        var load = await WithServiceAsync(Friday, service => service.WeeklyAsync(2, [s.Plant.Cut, s.Plant.Assembly]));
        var cut = CellOf(load, s.Plant.Cut, 0);
        (cut.CommittedHours, cut.CapacityHours, cut.Percent, cut.Band).Should().Be((8m, 4m, 200.0m, FactoryLoadBand.Over), "CA 4: a capacidade caiu depois do plano");
        CellOf(load, s.Plant.Assembly, 0).CapacityHours.Should().Be(4m, "o calendário é o mesmo: a Montagem também fica com 4 h no dia");
        (load.Factory.Cells[0].CommittedHours, load.Factory.Cells[0].CapacityHours, load.Factory.Cells[0].Band).Should().Be((8m, 8m, FactoryLoadBand.Full), "8 de 8 h na fábrica");
        CellOf(load, s.Plant.Cut, 1).Band.Should().NotBe(FactoryLoadBand.Over, "só a semana da parada");
    }

    [Fact]
    public async Task Drill_down_lista_as_etapas_e_o_total_e_igual_ao_da_celula()
    {
        var s = await FixedPlanAsync();
        var ids = new[] { s.Plant.Cut, s.Plant.Assembly };
        var load = await WithServiceAsync(Friday, service => service.WeeklyAsync(8, ids));

        var assembly = (await WithServiceAsync(Friday, service => service.CellAsync(s.Plant.Assembly, D(11, 2), new FactoryLoadQuery(null, null, null)))).Value;
        assembly.TotalHours.Should().Be(CellOf(load, s.Plant.Assembly, 1).CommittedHours).And.Be(34m);
        assembly.Steps.Select(x => (x.OrderId, x.StepName, x.Hours, x.PlannedStart, x.PlannedEnd)).Should().BeEquivalentTo(new[]
        {
            (s.Os2.OrderId, "Montagem", 10m, (DateOnly?)D(11, 3), (DateOnly?)D(11, 3)),
            (s.Os1.OrderId, "Montagem", 20m, (DateOnly?)D(11, 4), (DateOnly?)D(11, 5)),
            (s.Os3.OrderId, "Montagem", 4m, (DateOnly?)D(11, 5), (DateOnly?)D(11, 5)),
        });
        assembly.Steps.Should().OnlyContain(x => x.OrderNumber.StartsWith("OS-", StringComparison.Ordinal) && x.WorkCenterId == s.Plant.Assembly);

        var cut = (await WithServiceAsync(Friday, service => service.CellAsync(s.Plant.Cut, D(10, 26), new FactoryLoadQuery(null, null, null)))).Value;
        (cut.From, cut.To, cut.TotalHours).Should().Be((D(10, 30), D(11, 1), 8m));
        cut.Steps.Select(x => (x.OrderId, x.Hours)).Should().BeEquivalentTo(new[] { (s.Os2.OrderId, 2m), (s.Os1.OrderId, 6m) }, "OS-1 Corte: só as 6 h de 30/10 na semana");

        var factory = (await WithServiceAsync(Friday, service => service.CellAsync(null, D(11, 2), new FactoryLoadQuery(null, ids, null)))).Value;
        (factory.Name, factory.TotalHours).Should().Be(("Fábrica", load.Factory.Cells[1].CommittedHours));
        factory.Steps.Sum(x => x.Hours).Should().Be(44m);

        var monday = await WithServiceAsync(Friday, service => service.CellAsync(s.Plant.Cut, D(11, 3), new FactoryLoadQuery(null, null, null)));
        monday.Error!.Code.Should().Be("planning_load.week", "a semana começa na segunda");
        var past = await WithServiceAsync(Friday, service => service.CellAsync(s.Plant.Cut, D(10, 19), new FactoryLoadQuery(null, null, null)));
        past.Error!.Code.Should().Be("planning_load.week");
        var unknown = await WithServiceAsync(Friday, service => service.CellAsync(Guid.CreateVersion7(), D(11, 2), new FactoryLoadQuery(null, null, null)));
        unknown.Error!.Code.Should().Be("work_center_not_found");
    }

    [Fact]
    public async Task IFactoryLoad_da_os_mesmos_numeros_da_tela()
    {
        var s = await FixedPlanAsync();
        var ids = new[] { s.Plant.Cut, s.Plant.Assembly };
        await using var scope = await ScopeAsync();
        var contract = await scope.ServiceProvider.GetRequiredService<IFactoryLoad>().WeeklyAsync(5, ids);
        contract.Weeks.Should().HaveCount(5);
        contract.Centers.Should().HaveCount(2);

        var owner = await ActorAsync("owner");
        var response = await owner.GetAsync($"{Base}?weeks=5&workCenterIds={s.Plant.Cut}&workCenterIds={s.Plant.Assembly}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var screen = (await JsonAsync(response)).GetProperty("load");
        screen.GetProperty("weeks").GetArrayLength().Should().Be(5);
        foreach (var center in contract.Centers.Append(contract.Factory))
        {
            var row = center.WorkCenterId is { } id
                ? screen.GetProperty("centers").EnumerateArray().Single(r => r.GetProperty("workCenterId").GetGuid() == id)
                : screen.GetProperty("factory");
            row.GetProperty("hoursAfter").GetDecimal().Should().Be(center.HoursAfter);
            var cells = row.GetProperty("cells").EnumerateArray().ToList();
            cells.Select(c => (c.GetProperty("weekStart").GetString()!, c.GetProperty("committedHours").GetDecimal(), c.GetProperty("capacityHours").GetDecimal(), c.GetProperty("band").GetString()!))
                .Should().Equal(center.Cells.Select(c => (Iso(c.WeekStart), c.CommittedHours, c.CapacityHours, c.Band.ToString())), "CA 10: os mesmos da tela");
        }
    }

    [Fact]
    public async Task Vinte_centros_26_semanas_e_300_OS_respondem_em_menos_de_1_s()
    {
        var today = BusinessTime.Today(TimeProvider.System);
        var centers = new List<Guid>();
        await WithDbAsync(async db =>
        {
            var created = DateTimeOffset.UtcNow.AddYears(-1);
            var calendar = new Calendar { Name = Unique("Ocupação perf"), WorkingDays = [.. Enum.GetValues<Weekday>()], CreatedAt = created };
            db.Calendars.Add(calendar);
            for (var i = 0; i < 20; i++)
            {
                var center = new WorkCenter { Name = Unique("Perf " + i), CalendarId = calendar.Id, CreatedAt = created };
                center.SetShifts([new Shift("08:00", "12:00"), new Shift("13:00", "17:00")]);
                db.WorkCenters.Add(center);
                centers.Add(center.Id);
            }

            // 300 OS com 5 etapas, cada etapa alocada em 6 dias espalhados pelas 26 semanas (9.000 linhas no plano; até hoje + 175, que
            // cabe sempre na 26ª semana).
            var run = new ScheduleRun { Trigger = ScheduleTrigger.Manual, Status = ScheduleRunStatus.Succeeded, RequestedAt = DateTimeOffset.UtcNow, StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow };
            db.ScheduleRuns.Add(run);
            for (var o = 0; o < 300; o++)
            {
                var order = new ProductionOrder
                {
                    Number = "OS-OCP-" + o.ToString("D3", CultureInfo.InvariantCulture) + Guid.NewGuid().ToString("N")[..4], Type = ProductionOrderType.Fabrication, ProjectId = Guid.CreateVersion7(),
                    ContractId = Guid.CreateVersion7(), ProjectItemId = Guid.CreateVersion7(), Quantity = 1m, CreatedAt = DateTimeOffset.UtcNow,
                };
                db.ProductionOrders.Add(order);
                for (var k = 0; k < 5; k++)
                {
                    var center = centers[(o + k) % 20];
                    var step = new ProductionOrderStep { OrderId = order.Id, Sequence = k + 1, Name = "Etapa " + k, Code = ((char)('A' + k)).ToString(), WorkCenterId = center, StandardHours = 6m, Status = ProductionStepStatus.Waiting };
                    db.ProductionOrderSteps.Add(step);
                    for (var d = 0; d < 6; d++)
                    {
                        db.ScheduleAllocations.Add(new ScheduleAllocation { StepId = step.Id, ProductionOrderId = order.Id, WorkCenterId = center, Date = today.AddDays((o + k * 7 + d * 29) % 176), Hours = 0.5m });
                    }
                }
            }

            return await db.SaveChangesAsync();
        });

        var owner = await ActorAsync("owner");
        var query = $"{Base}?weeks=26&" + string.Join('&', centers.Select(c => "workCenterIds=" + c));
        (await owner.GetAsync(query)).StatusCode.Should().Be(HttpStatusCode.OK); // aquece
        var watch = Stopwatch.StartNew();
        var response = await owner.GetAsync(query);
        watch.Stop();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        watch.ElapsedMilliseconds.Should().BeLessThan(1000, "CA 11: 20 centros, 26 semanas e 300 OS");
        var load = (await JsonAsync(response)).GetProperty("load");
        load.GetProperty("centers").GetArrayLength().Should().Be(20);
        load.GetProperty("factory").GetProperty("cells").EnumerateArray().Sum(c => c.GetProperty("committedHours").GetDecimal()).Should().Be(300 * 5 * 6 * 0.5m);
    }

    [Fact]
    public async Task Tres_camadas_allowlist_e_sem_custo()
    {
        var plant = await PlantAsync();
        var viewer = await ActorAsync("production:view");
        var nobody = await ActorAsync(null);
        var monday = FactoryLoadCalculator.WeekStart(BusinessTime.Today(TimeProvider.System));

        var ok = await viewer.GetAsync($"{Base}?workCenterIds={plant.Cut}");
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var body = await ok.Content.ReadAsStringAsync();
        body.Should().NotContainAny(["cost", "Cost", "amount", "price"], "CA 12: sem custo na resposta");
        (await viewer.GetAsync($"{Base}/cell?workCenterId={plant.Cut}&weekStart={Iso(monday)}")).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var invalid in new[] { $"{Base}?weeks=0", $"{Base}?weeks=27", $"{Base}?line=" + new string('x', 121),
                     $"{Base}?" + string.Join('&', Enumerable.Range(0, 51).Select(_ => "workCenterIds=" + Guid.CreateVersion7())),
                     $"{Base}/cell?weekStart={Iso(monday.AddDays(1))}", $"{Base}/cell?weekStart={Iso(monday.AddDays(-7))}", $"{Base}/cell?weekStart={Iso(monday.AddDays(7 * 26))}" })
        {
            var response = await viewer.GetAsync(invalid);
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, invalid);
            Code(await JsonAsync(response)).Should().StartWith("planning_load.");
        }

        var none = await nobody.GetAsync(Base);
        none.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(none)).Should().Be("forbidden");
        var other = await ActorAsync("inventory:view");
        var denied = await other.GetAsync($"{Base}/cell?weekStart={Iso(monday)}");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(denied)).Should().Be("forbidden");

        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "production").ExecuteUpdateAsync(x => x.SetProperty(m => m.Enabled, false));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        try
        {
            foreach (var blocked in new[] { await viewer.GetAsync(Base), await viewer.GetAsync($"{Base}/cell?weekStart={Iso(monday)}") })
            {
                blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                Code(await JsonAsync(blocked)).Should().Be("module_not_released");
            }
        }
        finally
        {
            await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                    .Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "production").ExecuteUpdateAsync(x => x.SetProperty(m => m.Enabled, true));
            }

            tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
