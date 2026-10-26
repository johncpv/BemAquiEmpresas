using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PLN-01b com o banco real: os gatilhos do Production marcam o pedido de recálculo depois do commit e o job refaz as datas (CA 1 e 4); o
/// aditivo e o cancelamento do contrato também pedem (CA 2); 20 pedidos viram uma execução e o resultado é o mesmo do cálculo do zero
/// (CA 3); a previsão do projeto chega ao Commercial pelo evento, fora de ordem não sobrescreve e o projeto fica intacto (CA 5 a 8); as
/// quatro telas usam a mesma função de risco (CA 9); o aviso de risco ao entrar e ao piorar, com dedup (CA 10); o parâmetro com auditoria e
/// recálculo (CA 12); dois tenants (CA 13); módulo não liberado e três camadas (CA 14). Cada teste usa o próprio projeto e filtra pelos ids
/// que criou; o plano é do tenant inteiro, então as datas são conferidas contra o próprio plano, não contra o calendário do dia.
/// </summary>
public partial class FabricationTests
{
    private const string Projects = "/api/v1/projects";
    private const string Parameters = "/api/v1/production/parameters";

    private Task<int> ClearPendingAsync() =>
        WithDbAsync(db => db.Database.ExecuteSqlAsync($"DELETE FROM production.schedule_runs WHERE status = 'Pending'"));

    private Task<bool> PendingAsync() => WithDbAsync(db => db.ScheduleRuns.AsNoTracking().AnyAsync(r => r.Status == ScheduleRunStatus.Pending));

    /// <summary>O que o <see cref="PlanningRecalculateJob"/> faz com o gatilho <c>Event</c>.</summary>
    private async Task<ScheduleRecalculation> RunPendingAsync()
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<ScheduleService>().RunPendingAsync();
    }

    private async Task<ScheduleRecalculation> RecalcNowAsync()
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<ScheduleService>().RecalculateAsync(ScheduleTrigger.Manual, null);
    }

    private Task<ScheduleProjectForecast?> PlanForecastAsync(Guid project) =>
        WithDbAsync(db => db.ScheduleProjectForecasts.AsNoTracking().FirstOrDefaultAsync(f => f.ProjectId == project));

    private Task<BemAqui.Commercial.Domain.ProjectForecast?> CommercialForecastAsync(Guid project) =>
        WithDbAsync(db => db.ProjectForecasts.AsNoTracking().FirstOrDefaultAsync(f => f.ProjectId == project));

    /// <summary>Fim previsto de cada OS do projeto no plano gravado (a data esperada sai do próprio plano).</summary>
    private async Task<DateOnly?> PlannedEndAsync(IReadOnlyCollection<Guid> orders)
    {
        await using var scope = await ScopeAsync();
        var forecasts = await scope.ServiceProvider.GetRequiredService<IProductionSchedule>().ForOrdersAsync(orders);
        return forecasts.Max(f => f.PlannedEnd);
    }

    private Task<int> SetForecastsAsync(Guid project, DateOnly? forecast) => WithDbAsync(async db =>
    {
        await db.Database.ExecuteSqlAsync($"UPDATE production.schedule_project_forecasts SET forecast_date = {forecast} WHERE project_id = {project}");
        return await db.Database.ExecuteSqlAsync($"UPDATE commercial.project_forecasts SET forecast_date = {forecast} WHERE project_id = {project}");
    });

    private async Task ConsumeForecastAsync(ProjectForecastChanged changed)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<ICommercialDb>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await ActivatorUtilities.CreateInstance<ProjectForecastConsumer>(scope.ServiceProvider).HandleAsync(changed, CancellationToken.None);
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task Gatilhos_do_Production_marcam_o_pedido_e_o_job_refaz_a_previsao_em_ate_10_s()
    {
        var (owner, _, project, orders, _) = await KanbanDatasetAsync(1);
        var (_, supervisor) = await ActorAsync("production:view,edit,approve;shopfloor:view,create");
        await RecalcNowAsync();
        await DrainAsync();
        var before = (await PlanForecastAsync(project))!;
        before.ForecastDate.Should().Be(await PlannedEndAsync(orders), "CA 5 (c): sem instalação, a entrega é o fim da fabricação");
        (await CommercialForecastAsync(project))!.ForecastDate.Should().Be(before.ForecastDate, "CA 7: a previsão chegou ao Commercial pelo evento");

        var center = (await StepAsync(await StepIdAsync(orders[0], 1))).WorkCenterId;

        // Editar as etapas (mais horas na última) empurra a data; o Projeto 360 mostra a nova. Antes do apontamento: com a OS iniciada
        // as etapas não mudam (PRD-01 CA 8).
        var order = await GetOrderAsync(owner, orders[0]);
        var steps = CurrentSteps(order);
        await ClearPendingAsync();
        var last = order.GetProperty("steps").GetArrayLength() - 1;
        var edited = steps.Select((s, i) => i == last ? (object)new
        {
            id = order.GetProperty("steps")[i].GetProperty("id").GetGuid(), name = "Qualidade", code = "Q", workCenterId = center, standardHours = 40m, isQualityStep = true,
            requiresApproval = false, checklist = Array.Empty<string>(),
        } : s).ToList();
        (await owner.PutAsJsonAsync($"{Base}/{orders[0]}/steps", StepsBody(order, edited))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PendingAsync()).Should().BeTrue("CA 1: editar etapas");
        await RunPendingAsync();
        await DrainAsync();
        var after = (await PlanForecastAsync(project))!;
        after.ForecastDate.Should().BeAfter(before.ForecastDate!.Value, "40 h a mais na última etapa");
        after.ForecastDate.Should().Be(await PlannedEndAsync(orders));
        var detail = await JsonAsync(await owner.GetAsync($"{Projects}/{project}"));
        detail.GetProperty("forecast").GetProperty("forecastDate").GetString().Should().Be(Iso(after.ForecastDate!.Value), "CA 8: Entrega prevista de project_forecasts");

        // Apontamento finalizado acima do padrão (2 h): o comando marca o pedido depois do commit; o job refaz as datas.
        var (_, employee, _, _) = await OperatorAsync(center);
        await ClearPendingAsync();
        await RecordAsync(supervisor, orders[0], employee, await StepIdAsync(orders[0], 1), 180);
        var committed = Stopwatch.StartNew();
        (await PendingAsync()).Should().BeTrue("CA 1: apontamento fechado pede o recálculo, gatilho Event");
        (await RunPendingAsync()).Run!.Status.Should().Be(ScheduleRunStatus.Succeeded);
        await DrainAsync();
        committed.Stop();
        committed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "CA 4: as datas novas em até 10 s depois do commit");
        (await WithDbAsync(db => db.ScheduleRuns.AsNoTracking().OrderByDescending(r => r.FinishedAt).FirstAsync())).Trigger.Should().Be(ScheduleTrigger.Event);

        // Calendário, centro e parâmetro também pedem.
        var calendar = await WithDbAsync(db => db.Calendars.AsNoTracking().Where(c => c.IsDefault).Select(c => c.Id).SingleAsync());
        await ClearPendingAsync();
        (await owner.PostAsJsonAsync($"/api/v1/production/calendars/{calendar}/exceptions", new { date = Iso(Today.AddYears(1).AddDays(3)), type = "Stoppage", hoursAvailable = 4m, description = "Parada " + Guid.NewGuid().ToString("N")[..4] }))
            .IsSuccessStatusCode.Should().BeTrue();
        (await PendingAsync()).Should().BeTrue("CA 1: exceção do calendário");
        await ClearPendingAsync();
        (await owner.PostAsync($"/api/v1/production/work-centers/{center}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PendingAsync()).Should().BeTrue("CA 1: centro inativado");
        await RunPendingAsync();
        (await PlanForecastAsync(project))!.Should().Match<ScheduleProjectForecast>(f => f.ForecastDate == null && f.Reason == ProjectForecastReasons.NoCapacity && f.ReasonOrderId == orders[0],
            "CA 6: OS sem previsão deixa a data vazia com o motivo");
        await DrainAsync();
        var empty = await JsonAsync(await owner.GetAsync($"{Projects}/{project}"));
        empty.GetProperty("forecast").GetProperty("reason").GetString().Should().Be("NoCapacity");
        (await owner.PostAsync($"/api/v1/production/work-centers/{center}/reactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await RunPendingAsync();

        // Concluir etapa pelo supervisor e cancelar a OS: o projeto fica sem OS de fabricação.
        await ClearPendingAsync();
        order = await GetOrderAsync(supervisor, orders[0]);
        var current = order.GetProperty("steps").EnumerateArray().First(s => s.GetProperty("status").GetString() != "Done").GetProperty("id").GetGuid();
        (await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{current}/complete", new { reason = "Feito fora do sistema.", version = Version(order) })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PendingAsync()).Should().BeTrue("CA 1: concluir etapa");
        await ClearPendingAsync();
        order = await GetOrderAsync(owner, orders[0]);
        (await owner.PostAsJsonAsync($"{Base}/{orders[0]}/cancel", new { reason = "Cliente desistiu do item.", version = Version(order) })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PendingAsync()).Should().BeTrue("CA 1: cancelar a OS");
        await RunPendingAsync();
        (await PlanForecastAsync(project))!.Reason.Should().Be(ProjectForecastReasons.NoFabricationOrder);
    }

    [Fact]
    public async Task Aditivo_e_cancelamento_do_contrato_pedem_o_recalculo()
    {
        await using var scope = await ScopeAsync();
        var consumer = ActivatorUtilities.CreateInstance<PlanningContractConsumers>(scope.ServiceProvider);

        await ClearPendingAsync();
        await consumer.HandleAsync(new ContractAmended(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(), Today, 0m, [], []), CancellationToken.None);
        (await PendingAsync()).Should().BeTrue("CA 2: aditivo muda a data prometida");

        await ClearPendingAsync();
        await consumer.HandleAsync(new ContractCancelled(Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);
        (await PendingAsync()).Should().BeTrue("CA 2: contrato cancelado");
    }

    [Fact]
    public async Task Vinte_pedidos_viram_uma_execucao_e_o_resultado_e_o_do_calculo_do_zero()
    {
        var (_, _, project, orders, _) = await KanbanDatasetAsync(2);
        await RecalcNowAsync();
        await ClearPendingAsync();
        var since = DateTimeOffset.UtcNow;

        // Um cálculo "em andamento" (o lock preso por outra conexão): os 20 pedidos se juntam numa marca só.
        await using (var holder = await ScopeAsync())
        {
            var db = holder.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({ScheduleService.LockKey}, 0))) AS l").ToListAsync();
            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
            {
                await using var scope = await ScopeAsync();
                await scope.ServiceProvider.GetRequiredService<IScheduleTrigger>().RequestAsync("teste");
            })));
            (await WithDbAsync(d => d.ScheduleRuns.CountAsync(r => r.Status == ScheduleRunStatus.Pending))).Should().Be(1, "CA 3: no máximo uma pendente");
            (await RunPendingAsync()).Ran.Should().BeFalse("ninguém calcula em paralelo com quem tem o lock");
            await transaction.CommitAsync();
        }

        (await RunPendingAsync()).Run!.Status.Should().Be(ScheduleRunStatus.Succeeded);
        (await RunPendingAsync()).Ran.Should().BeFalse("a marca já foi assumida: os 20 pedidos rodaram uma vez");
        var runs = await WithDbAsync(db => db.ScheduleRuns.AsNoTracking().Where(r => r.RequestedAt >= since).ToListAsync());
        runs.Should().ContainSingle(r => r.Status == ScheduleRunStatus.Succeeded).Which.Trigger.Should().Be(ScheduleTrigger.Event);

        var grouped = await PlannedEndAsync(orders);
        var forecast = (await PlanForecastAsync(project))!.ForecastDate;
        var fresh = await RecalcNowAsync();
        fresh.Run!.ChangedOrdersCount.Should().Be(0, "CA 3: o resultado agrupado é igual ao de uma execução do zero");
        (await PlannedEndAsync(orders)).Should().Be(grouped);
        (await PlanForecastAsync(project))!.ForecastDate.Should().Be(forecast);
    }

    [Fact]
    public async Task Previsao_fora_de_ordem_nao_sobrescreve_e_o_projeto_fica_intacto()
    {
        var (_, _, project, _, _) = await KanbanDatasetAsync(1);
        var before = await WithDbAsync(async db => new
        {
            Version = await db.Projects.AsNoTracking().Where(p => p.Id == project).Select(p => p.Version).SingleAsync(),
            Audit = await db.AuditEntries.CountAsync(a => a.EntityId == project.ToString()),
        });
        var newer = DateTimeOffset.UtcNow;

        await ConsumeForecastAsync(new ProjectForecastChanged(project, Today.AddDays(40), Today.AddDays(35), null, newer));
        await ConsumeForecastAsync(new ProjectForecastChanged(project, Today.AddDays(20), Today.AddDays(15), null, newer.AddMinutes(-5)));
        await ConsumeForecastAsync(new ProjectForecastChanged(project, Today.AddDays(20), Today.AddDays(15), null, newer));

        var row = (await CommercialForecastAsync(project))!;
        row.ForecastDate.Should().Be(Today.AddDays(40), "CA 7: o evento mais antigo (ou do mesmo instante) não sobrescreve");
        row.ComputedAt.Should().BeCloseTo(newer, TimeSpan.FromMilliseconds(1));
        var after = await WithDbAsync(async db => new
        {
            Version = await db.Projects.AsNoTracking().Where(p => p.Id == project).Select(p => p.Version).SingleAsync(),
            Audit = await db.AuditEntries.CountAsync(a => a.EntityId == project.ToString()),
        });
        after.Should().Be(before, "CA 7: a linha de commercial.projects não é tocada (versão e auditoria)");
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == nameof(BemAqui.Commercial.Domain.ProjectForecast)))).Should().Be(0, "fora da auditoria");

        await ConsumeForecastAsync(new ProjectForecastChanged(project, null, null, ProjectForecastReasons.NoRouting, newer.AddMinutes(1), Guid.CreateVersion7()));
        (await CommercialForecastAsync(project))!.Should().Match<BemAqui.Commercial.Domain.ProjectForecast>(f => f.ForecastDate == null && f.Reason == ProjectForecastReasons.NoRouting);
    }

    [Fact]
    public async Task Aviso_de_risco_ao_entrar_e_ao_piorar_uma_vez_por_data()
    {
        var (_, _, project, _, _) = await KanbanDatasetAsync(1);
        var (approver, _) = await ActorAsync("production:view,approve");
        var (viewer, _) = await ActorAsync("production:view");
        var manager = await WithDbAsync(db => db.Projects.AsNoTracking().Where(p => p.Id == project).Select(p => p.ManagerUserId).SingleAsync());
        await SetPromisedAsync(project, Today.AddDays(20));
        var at = DateTimeOffset.UtcNow;
        Task<List<BemAqui.BuildingBlocks.Notifications.Notification>> NoticesAsync() =>
            WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.Type == ProjectForecastConsumer.NotificationType && n.Link == ProjectForecastConsumer.Link(project)).ToListAsync());

        await ConsumeForecastAsync(new ProjectForecastChanged(project, Today.AddDays(18), Today.AddDays(13), null, at));
        (await NoticesAsync()).Should().BeEmpty("antes do prometido não é risco");

        await ConsumeForecastAsync(new ProjectForecastChanged(project, Today.AddDays(27), Today.AddDays(22), null, at.AddSeconds(1)));
        var entered = await NoticesAsync();
        entered.Should().Contain(n => n.UserId == approver, "CA 10: production.approve");
        if (manager is { } m)
        {
            entered.Should().Contain(n => n.UserId == m, "o responsável do projeto");
        }

        entered.Should().NotContain(n => n.UserId == viewer, "só production.view não recebe");
        entered.Single(n => n.UserId == approver).Title.Should().EndWith($"vai atrasar 7 dias: previsão {Today.AddDays(27):dd/MM/yyyy}, prometido ao cliente {Today.AddDays(20):dd/MM/yyyy}.");

        // Mesma data de novo (recálculo seguinte) não repete; piorou, avisa de novo; melhorou ou saiu do risco, não.
        await ConsumeForecastAsync(new ProjectForecastChanged(project, Today.AddDays(27), Today.AddDays(23), null, at.AddSeconds(2)));
        (await NoticesAsync()).Count(n => n.UserId == approver).Should().Be(1);
        await ConsumeForecastAsync(new ProjectForecastChanged(project, Today.AddDays(30), Today.AddDays(25), null, at.AddSeconds(3)));
        (await NoticesAsync()).Count(n => n.UserId == approver).Should().Be(2, "piorou");
        await ConsumeForecastAsync(new ProjectForecastChanged(project, Today.AddDays(25), Today.AddDays(20), null, at.AddSeconds(4)));
        await ConsumeForecastAsync(new ProjectForecastChanged(project, Today.AddDays(15), Today.AddDays(10), null, at.AddSeconds(5)));
        (await NoticesAsync()).Count(n => n.UserId == approver).Should().Be(2, "melhorar ou sair do risco não avisa");
        await ConsumeForecastAsync(new ProjectForecastChanged(project, Today.AddDays(30), Today.AddDays(25), null, at.AddSeconds(6)));
        (await NoticesAsync()).Count(n => n.UserId == approver).Should().Be(2, "a mesma data prevista já foi avisada (dedup_key)");
    }

    [Fact]
    public async Task Quatro_telas_usam_a_mesma_funcao_de_risco()
    {
        var (owner, routing, project, orders, _) = await KanbanDatasetAsync(1);
        await RecalcNowAsync();
        await DrainAsync();
        await SetPromisedAsync(project, Today.AddDays(10));

        async Task<string[]> RisksAsync()
        {
            var card = Card(await BoardAsync(owner, $"routingTemplateId={routing}"), orders[0]).GetProperty("risk").GetString()!;
            var list = (await JsonAsync(await owner.GetAsync($"{Base}?projectId={project}"))).GetProperty("items").EnumerateArray()
                .Single(o => o.GetProperty("id").GetGuid() == orders[0]).GetProperty("risk").GetString()!;
            var detail = (await JsonAsync(await owner.GetAsync($"{Projects}/{project}"))).GetProperty("forecast").GetProperty("risk").GetString()!;
            var number = await WithDbAsync(db => db.Projects.AsNoTracking().Where(p => p.Id == project).Select(p => p.Number).SingleAsync());
            var row = (await JsonAsync(await owner.GetAsync($"{Projects}?q={number}"))).GetProperty("items").EnumerateArray()
                .Single(p => p.GetProperty("id").GetGuid() == project).GetProperty("risk").GetString()!;
            return [card, list, detail, row];
        }

        await SetForecastsAsync(project, Today.AddDays(11));
        (await RisksAsync()).Should().AllBe("AtRisk", "CA 9: previsão um dia depois do prometido, sem tolerância");
        await SetForecastsAsync(project, Today.AddDays(10));
        (await RisksAsync()).Should().AllBe("None", "no próprio dia prometido não é risco; a janela de 7 dias da Fase 3 saiu");
        await SetPromisedAsync(project, Today.AddDays(-1));
        (await RisksAsync()).Should().AllBe("Late");
        var slack = (await JsonAsync(await owner.GetAsync($"{Projects}/{project}"))).GetProperty("forecast").GetProperty("slackDays").GetInt32();
        slack.Should().Be(-11, "folga = prometido − previsto, em dias corridos");
    }

    [Fact]
    public async Task Parametro_prazo_de_instalacao_com_auditoria_recalculo_e_tres_camadas()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, viewer) = await ActorAsync("production:view");
        var (_, nobody) = await ActorAsync(null);
        var current = await JsonAsync(await owner.GetAsync(Parameters));
        current.GetProperty("installationDays").GetInt32().Should().BeInRange(0, 120);
        var hours = current.GetProperty("openLaborAlertHours").GetInt32();
        var show = current.GetProperty("showLaborEntryCost").GetBoolean();
        var days = current.GetProperty("installationDays").GetInt32() == 7 ? 8 : 7;

        await ClearPendingAsync();
        var saved = await owner.PutAsJsonAsync(Parameters, new { showLaborEntryCost = show, openLaborAlertHours = hours, installationDays = days, installationDaysConfirmed = true });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var body = await JsonAsync(saved);
        body.GetProperty("installationDays").GetInt32().Should().Be(days);
        body.GetProperty("installationDaysConfirmed").GetBoolean().Should().BeTrue();
        (await PendingAsync()).Should().BeTrue("CA 12: o prazo novo pede um recálculo");
        var id = body.GetProperty("installationDaysId").GetGuid();
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.EntityId == id.ToString()))).Should().BeGreaterThan(0, "CA 12: auditoria com antes e depois");

        var invalid = await owner.PutAsJsonAsync(Parameters, new { showLaborEntryCost = show, openLaborAlertHours = hours, installationDays = 121 });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(invalid)).GetProperty("errors").GetProperty("installationDays")[0].GetString().Should().Be("Informe de 0 a 120 dias úteis.");
        var denied = await viewer.PutAsJsonAsync(Parameters, new { showLaborEntryCost = show, openLaborAlertHours = hours, installationDays = 3 });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, "CA 12: sem settings.edit");
        (await viewer.GetAsync(Parameters)).StatusCode.Should().Be(HttpStatusCode.OK, "ver com production.view (só leitura)");
        var none = await nobody.GetAsync(Parameters);
        none.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(none)).Should().Be("forbidden");

        await SetProductionReleasedAsync(false);
        try
        {
            var blocked = await owner.GetAsync(Parameters);
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(blocked)).Should().Be("module_not_released", "CA 14");
            var schedule = await owner.GetAsync("/api/v1/production/schedule");
            Code(await JsonAsync(schedule)).Should().Be("module_not_released");
        }
        finally
        {
            await SetProductionReleasedAsync(true);
        }
    }

    [Fact]
    public async Task Modulo_nao_liberado_nao_pede_nem_calcula_e_um_tenant_nao_ve_o_outro()
    {
        var (_, _, project, _, _) = await KanbanDatasetAsync(1);
        await SetProductionReleasedAsync(false);
        try
        {
            await ClearPendingAsync();
            await using (var scope = await ScopeAsync())
            {
                await scope.ServiceProvider.GetRequiredService<IScheduleTrigger>().RequestAsync("teste");
            }

            (await PendingAsync()).Should().BeFalse("CA 14: módulo não liberado não pede recálculo");
            await WithDbAsync(db => db.Database.ExecuteSqlAsync($"INSERT INTO production.schedule_runs (id, trigger, status, requested_at, orders_count, steps_count, changed_orders_count, without_forecast_count, duration_ms) VALUES ({Guid.CreateVersion7()}, 'Event', 'Pending', {DateTimeOffset.UtcNow}, 0, 0, 0, 0, 0)"));
            var job = ActivatorUtilities.CreateInstance<PlanningRecalculateJob>(tenancy.Factory.Services);
            await job.ExecuteAsync(tenancy.TenantA.Id, new PlanningRecalculateArgs(ScheduleTrigger.Event), CancellationToken.None);
            (await PendingAsync()).Should().BeTrue("CA 14: o job confere o módulo no início e termina sem calcular");
        }
        finally
        {
            await SetProductionReleasedAsync(true);
        }

        // CA 13: o job do pedido por evento calcula só o tenant do tenantId; o ProjectForecastChanged de A não chega a B.
        var jobA = ActivatorUtilities.CreateInstance<PlanningRecalculateJob>(tenancy.Factory.Services);
        await jobA.ExecuteAsync(tenancy.TenantA.Id, new PlanningRecalculateArgs(ScheduleTrigger.Event), CancellationToken.None);
        (await PlanForecastAsync(project)).Should().NotBeNull();
        await jobA.ExecuteAsync(tenancy.TenantB.Id, new PlanningRecalculateArgs(ScheduleTrigger.Hourly), CancellationToken.None);
        var infoB = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantB.Id))!;
        await using var scopeB = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(infoB);
        var dbB = scopeB.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        (await dbB.ScheduleProjectForecasts.AsNoTracking().AnyAsync(f => f.ProjectId == project)).Should().BeFalse("o tenant B não vê o projeto do A");
        (await dbB.OutboxMessages.AsNoTracking().Where(m => m.Type == typeof(ProjectForecastChanged).FullName).Select(m => m.Payload).ToListAsync())
            .Should().NotContain(p => p.Contains(project.ToString(), StringComparison.Ordinal));
        var payload = await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type == typeof(ProjectForecastChanged).FullName).Select(m => m.Payload).ToListAsync());
        var mine = payload.Where(p => p.Contains(project.ToString(), StringComparison.Ordinal)).ToList();
        mine.Should().NotBeEmpty();
        using var json = JsonDocument.Parse(mine[^1]);
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().BeSubsetOf(["ProjectId", "ForecastDate", "FabricationEnd", "Reason", "ComputedAt", "ReasonOrderId"],
            "CA 14: só ids, datas e o motivo como código; o payload do outbox é PascalCase");
    }
}
