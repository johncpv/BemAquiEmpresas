using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.HR.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Assets;

/// <summary>
/// PAT-03b: título do prestador previsto, atualizado, confirmado na conclusão (uma vez só, com concorrência) e cancelado pela origem; conta e
/// centro pelos parâmetros "A confirmar com a contadora"; custo por natureza e relatório só com assets.edit; parada do centro pelo evento, com a
/// capacidade descontada, o pedido de recálculo, a reentrega, o reagendamento e dois tenants; Financeiro não liberado.
/// </summary>
public partial class AssetTests
{
    private const string Costs = "/api/v1/assets/maintenance-costs";

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = await ScopeAsync();
        return await work(scope.ServiceProvider);
    }

    private Task<Payable?> PayableOfAsync(Guid orderId) => WithScopeAsync(sp => sp.GetRequiredService<IFinanceDb>().Payables.AsNoTracking()
        .Where(p => p.Origin == DocumentOrigin.Maintenance && p.OriginId == orderId).OrderBy(p => p.Status == PayableStatus.Cancelled).FirstOrDefaultAsync());

    private Task<List<Downtime>> DowntimesOfAsync(Guid orderId) => WithScopeAsync(sp => sp.GetRequiredService<IProductionDb>().Downtimes.AsNoTracking()
        .Where(d => d.SourceId == orderId).ToListAsync());

    private async Task DrainAssetsOutboxAsync()
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        for (var round = 0; round < 40; round++)
        {
            await using (var scope = await ScopeAsync())
            {
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(info);
            }

            var pending = await WithScopeAsync(sp => sp.GetRequiredService<BemAquiDbContext>().OutboxMessages
                .CountAsync(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= DateTimeOffset.UtcNow.AddSeconds(1)));
            if (pending == 0)
            {
                return;
            }

            await Task.Delay(200);
        }
    }

    private async Task SetReleasedAsync(string module, bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == module)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>Funcionário com cargo de custo-hora conhecido (o <c>IHourlyCost</c> lê do cargo).</summary>
    private async Task<Guid> MechanicAsync(decimal hourlyCost)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.HR.Application.IHrDb>();
        var position = new JobPosition { Name = "Mecânico " + Guid.NewGuid().ToString("N")[..6], MonthlyHours = 220m, HourlyCost = hourlyCost, CreatedAt = DateTimeOffset.UtcNow };
        db.JobPositions.Add(position);
        var employee = new Employee
        {
            Name = Unique("Mecânico"), PositionId = position.Id, AdmissionDate = new DateOnly(2024, 1, 2), BirthDate = new DateOnly(1990, 1, 1), BaseSalary = 3000m, CreatedAt = DateTimeOffset.UtcNow,
            Cpf = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture), Status = EmployeeStatus.Active,
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    /// <summary>
    /// CA 1, 2, 3, 5, 13 e 14: o valor combinado gera o título Planned estimado (conta 2.6 e centro Produção pelo bem com centro), a mudança
    /// atualiza e avisa o financeiro, a conclusão sem confirmação é recusada e, com ela, duas conclusões juntas confirmam o título uma vez só.
    /// </summary>
    [Fact]
    public async Task Valor_combinado_gera_o_titulo_previsto_e_a_conclusao_confirma_uma_vez_so()
    {
        var supervisor = await SupervisorAsync();
        var supplier = await SupplierAsync("Assistência Técnica");
        var center = await CenterAsync("Usinagem");
        var asset = (await CreateAsync(supervisor, Input(Unique("Torno"), center: center))).GetProperty("id").GetGuid();

        var opened = await OrderAsync(supervisor, new { assetId = asset, type = "Corrective", problem = "Folga no eixo", scheduledOn = Day(3), supplierPartyId = supplier, contractValue = 1500m });
        var orderId = opened.GetProperty("id").GetGuid();
        opened.GetProperty("contractValue").GetDecimal().Should().Be(1500m);
        opened.GetProperty("payable").GetProperty("status").GetString().Should().Be("Planned", "CA 1: o título nasce previsto");
        opened.GetProperty("payable").GetProperty("isEstimate").GetBoolean().Should().BeTrue("a Tag \"Estimado\" no fluxo de caixa");
        opened.GetProperty("payable").GetProperty("dueDate").GetString().Should().Be(Day(3), "sem condição padrão: a data agendada");
        opened.GetProperty("requiresPayableConfirmation").GetBoolean().Should().BeTrue();

        var payable = (await PayableOfAsync(orderId))!;
        payable.ProjectId.Should().BeNull("CA 5: nenhum custo de manutenção vai para projeto");
        var (account, costCenter) = await WithScopeAsync(async sp => (
            (await sp.GetRequiredService<IChartOfAccounts>().FindAsync([payable.AccountId]))[0].Code,
            await sp.GetRequiredService<IFinanceDb>().CostCenters.Where(c => c.Id == payable.CostCenterId).Select(c => c.Code).FirstOrDefaultAsync()));
        account.Should().Be("2.6", "CA 5: bem com centro de trabalho → Outros custos");
        costCenter.Should().Be("3", "o centro de custo Produção");

        // Sem assets.edit, o valor, o custo e o título não existem no JSON (CA 7).
        var viewer = await ActorAsync("assets:view");
        var seen = await viewer.GetFromJsonAsync<JsonElement>($"{Orders}/{orderId}");
        seen.TryGetProperty("contractValue", out _).Should().BeFalse();
        seen.TryGetProperty("cost", out _).Should().BeFalse();
        seen.TryGetProperty("payable", out _).Should().BeFalse();
        var creator = await ActorAsync("assets:view,create");
        (await PostOrderAsync(creator, new { assetId = asset, type = "Corrective", problem = "Outro", supplierPartyId = supplier, contractValue = 10m }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "valor combinado só com assets.edit");

        // CA 2: o valor muda com a OS aberta; o título previsto acompanha e o financeiro é avisado.
        var since = DateTimeOffset.UtcNow.AddSeconds(-1);
        var changed = await OkAsync(await supervisor.PutAsJsonAsync($"{Orders}/{orderId}/schedule", new
        {
            scheduledOn = Day(3), supplierPartyId = supplier, contractValue = 1800m, version = opened.GetProperty("version").GetUInt32(),
        }));
        changed.GetProperty("payable").GetProperty("amount").GetDecimal().Should().Be(1800m);
        changed.GetProperty("payable").GetProperty("status").GetString().Should().Be("Planned");
        var number = opened.GetProperty("number").GetString()!;
        (await WithScopeAsync(sp => sp.GetRequiredService<BemAquiDbContext>().Notifications.AsNoTracking()
            .CountAsync(n => n.CreatedAt >= since && EF.Functions.Like(n.Title, "Valor da manutenção " + number + " mudou de%")))).Should().BePositive();

        // CA 3: concluir sem a confirmação do diálogo é recusado com o impacto financeiro.
        var started = await StartAsync(supervisor, changed);
        var refused = await supervisor.PostAsJsonAsync($"{Orders}/{orderId}/complete", new { workDone = "Troca do rolamento", version = started.GetProperty("version").GetUInt32() });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be(BemAqui.Assets.Application.MaintenanceService.PayableConfirmationCode);
        problem.GetProperty("detail").GetString().Should().StartWith("Confirma o título a pagar de R$").And.Contain("1.800,00 ao prestador Assistência Técnica")
            .And.EndWith("que segue para ordem de pagamento.");
        (await PayableOfAsync(orderId))!.Status.Should().Be(PayableStatus.Planned, "sem confirmação, nada muda");

        // CA 13: duas conclusões juntas com a confirmação: só uma grava e o título é confirmado uma vez.
        var version = started.GetProperty("version").GetUInt32();
        var results = await Task.WhenAll(
            supervisor.PostAsJsonAsync($"{Orders}/{orderId}/complete", new { workDone = "Troca do rolamento", version, confirmPayable = true }),
            (await SupervisorAsync()).PostAsJsonAsync($"{Orders}/{orderId}/complete", new { workDone = "Concluí junto", version, confirmPayable = true }));
        results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        var done = await JsonAsync(results.Single(r => r.StatusCode == HttpStatusCode.OK));
        done.GetProperty("payable").GetProperty("status").GetString().Should().Be("Open", "CA 3: segue para ordem de pagamento");
        done.GetProperty("payable").GetProperty("isEstimate").GetBoolean().Should().BeFalse();
        done.GetProperty("payable").GetProperty("dueDate").GetString().Should().Be(Day(0), "vencimento real: a conclusão (sem condição padrão)");
        done.GetProperty("cost").GetDecimal().Should().Be(1800m, "o prestador entra no custo");
        var all = await WithScopeAsync(sp => sp.GetRequiredService<IFinanceDb>().Payables.AsNoTracking().Where(p => p.Origin == DocumentOrigin.Maintenance && p.OriginId == orderId).ToListAsync());
        all.Should().ContainSingle(p => p.Status != PayableStatus.Cancelled).Which.Amount.Should().Be(1800m);
        var lines = await OkAsync(await supervisor.GetAsync($"{Orders}/{orderId}/costs"));
        lines.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("costType").GetString() == "Service").Should().ContainSingle();

        // CA 14: o histórico da OS tem o valor antes e depois, com o usuário.
        var audit = await WithScopeAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.AsNoTracking()
            .Where(a => a.Entity == "MaintenanceOrder" && a.EntityId == orderId.ToString()).ToListAsync());
        audit.Should().Contain(a => a.Action == "Updated" && a.Changes.Contains("contractValue", StringComparison.Ordinal));
        audit.Should().AllSatisfy(a => a.UserId.Should().NotBeNull());
    }

    /// <summary>CA 4: cancelar a OS com o título ainda previsto cancela o título pela origem ("Origem cancelada").</summary>
    [Fact]
    public async Task Cancelar_a_OS_cancela_o_titulo_previsto_pela_origem()
    {
        var supervisor = await SupervisorAsync();
        var supplier = await SupplierAsync("Refrigeração");
        var asset = (await CreateAsync(supervisor, Input(Unique("Ar-condicionado"), type: "Furniture"))).GetProperty("id").GetGuid();
        var order = await OrderAsync(supervisor, new { assetId = asset, type = "Preventive", problem = "Limpeza", supplierPartyId = supplier, contractValue = 350m });
        var orderId = order.GetProperty("id").GetGuid();
        var payable = (await PayableOfAsync(orderId))!;
        var account = await WithScopeAsync(async sp => (await sp.GetRequiredService<IChartOfAccounts>().FindAsync([payable.AccountId]))[0].Code);
        account.Should().Be("3.3", "CA 5: bem sem centro de trabalho → Administrativas");
        order.GetProperty("downtimeFrom").ValueKind.Should().Be(JsonValueKind.Null, "bem sem centro: sem parada");

        await OkAsync(await supervisor.PostAsJsonAsync($"{Orders}/{orderId}/cancel", new { reason = "Prestador desmarcou", version = order.GetProperty("version").GetUInt32() }));
        var cancelled = (await PayableOfAsync(orderId))!;
        cancelled.Status.Should().Be(PayableStatus.Cancelled);
        cancelled.CancelReason.Should().StartWith("Origem cancelada");
    }

    /// <summary>
    /// CA 6 e 7: o custo por natureza (mão de obra pelo custo-hora do cargo, peças), o total na OS e na ficha do bem; o relatório por mês, bem e
    /// centro bate com o drill-down; sem assets.edit é 403; sem hr.view_sensitive, nenhum custo-hora com nome.
    /// </summary>
    [Fact]
    public async Task Custo_por_natureza_soma_na_OS_e_o_relatorio_bate_com_o_drill_down()
    {
        var supervisor = await SupervisorAsync();
        var owner = await ActorAsync("owner");
        var center = await CenterAsync("Prensas");
        var asset = (await CreateAsync(supervisor, Input(Unique("Prensa"), center: center))).GetProperty("id").GetGuid();
        var order = await OrderAsync(supervisor, new { assetId = asset, type = "Corrective", problem = "Vazamento" });
        var orderId = order.GetProperty("id").GetGuid();
        var mechanic = await MechanicAsync(50m);

        var early = await supervisor.PostAsJsonAsync($"{Orders}/{orderId}/costs", new { costType = "Parts", description = "Vedação", amount = 80m });
        early.StatusCode.Should().Be(HttpStatusCode.Conflict, "o custo é lançado na OS concluída");
        var started = await StartAsync(supervisor, order);
        await OkAsync(await supervisor.PostAsJsonAsync($"{Orders}/{orderId}/complete", new { workDone = "Troca da vedação", version = started.GetProperty("version").GetUInt32() }));

        await OkAsync(await supervisor.PostAsJsonAsync($"{Orders}/{orderId}/costs", new { costType = "Labor", employeeId = mechanic, hours = 3m }));
        var withParts = await OkAsync(await supervisor.PostAsJsonAsync($"{Orders}/{orderId}/costs", new { costType = "Parts", description = "Kit de vedação", amount = 120.5m }));
        withParts.GetProperty("total").GetDecimal().Should().Be(270.5m, "3 h × R$ 50 + R$ 120,50");
        withParts.GetProperty("laborDetail").GetBoolean().Should().BeFalse("o supervisor não tem hr.view_sensitive");
        var labor = withParts.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("costType").GetString() == "Labor");
        labor.TryGetProperty("hourlyCost", out _).Should().BeFalse("nenhum custo-hora sem hr.view_sensitive");
        labor.TryGetProperty("employeeName", out _).Should().BeFalse();
        labor.TryGetProperty("amount", out _).Should().BeFalse("só o total da mão de obra interna");
        withParts.GetProperty("totals").EnumerateArray().Single(t => t.GetProperty("costType").GetString() == "Labor").GetProperty("amount").GetDecimal().Should().Be(150m);
        var full = await OkAsync(await owner.GetAsync($"{Orders}/{orderId}/costs"));
        full.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("costType").GetString() == "Labor").GetProperty("hourlyCost").GetDecimal().Should().Be(50m, "com hr.view_sensitive");
        (await supervisor.GetFromJsonAsync<JsonElement>($"{Orders}/{orderId}")).GetProperty("cost").GetDecimal().Should().Be(270.5m);
        (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{asset}/maintenance-cost")).GetProperty("total").GetDecimal().Should().Be(270.5m, "a ficha do bem");

        var month = Day(0)[..7];
        var report = await OkAsync(await supervisor.GetAsync($"{Costs}?from={month}&to={month}&workCenterId={center}"));
        var cell = report.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("assetId").GetGuid() == asset).GetProperty("cost");
        cell.GetProperty("corrective").GetDecimal().Should().Be(270.5m);
        cell.GetProperty("preventive").GetDecimal().Should().Be(0m);
        var drill = await OkAsync(await supervisor.GetAsync($"{Costs}/orders?from={month}&to={month}&month={month}&assetId={asset}"));
        drill.GetProperty("total").GetDecimal().Should().Be(cell.GetProperty("total").GetDecimal(), "CA 7: o total do drill-down é o da célula");
        drill.GetProperty("orders").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).Should().Equal(orderId);

        var viewer = await ActorAsync("assets:view");
        (await viewer.GetAsync($"{Costs}?from={month}&to={month}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "relatório de custo com assets.edit");
        (await viewer.GetAsync($"{Orders}/{orderId}/costs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.GetFromJsonAsync<JsonElement>($"{Orders}/{orderId}")).TryGetProperty("cost", out _).Should().BeFalse();
    }

    /// <summary>
    /// CA 8, 10 e 11: a OS agendada de um bem com centro publica a parada; o consumidor do Production grava a parada e o IWorkCenterCapacity
    /// desconta as horas por dia; o pedido de recálculo fica pendente; a reentrega não duplica; o reagendamento substitui o período; o
    /// cancelamento antes de começar retira a parada; o outro tenant não vê nada.
    /// </summary>
    [Fact]
    public async Task Parada_da_manutencao_reduz_a_capacidade_e_o_reagendamento_substitui_o_periodo()
    {
        var supervisor = await SupervisorAsync();
        var center = await CenterAsync("Solda");
        var asset = (await CreateAsync(supervisor, Input(Unique("Solda MIG"), center: center))).GetProperty("id").GetGuid();
        var defaults = await OkAsync(await supervisor.GetAsync($"{Orders}/defaults?assetId={asset}"));
        defaults.GetProperty("centerHoursPerDay").GetDecimal().Should().Be(9m);
        defaults.GetProperty("hoursPerDay").GetDecimal().Should().Be(9m, "uma máquina no centro: as horas do centro");

        var tooMany = await PostOrderAsync(supervisor, new { assetId = asset, type = "Preventive", problem = "Revisão", scheduledOn = Day(10), hoursPerDay = 10m });
        tooMany.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "horas paradas até as horas do centro");
        var (day, full) = await WorkingDayAsync(center, 10);
        // Antes da OS: o despachante do outbox pode entregar a parada (e pedir o recálculo) logo depois da gravação.
        await WithScopeAsync(sp => sp.GetRequiredService<BemAquiDbContext>().Database.ExecuteSqlAsync($"DELETE FROM production.schedule_runs WHERE status = 'Pending'"));
        var runsBefore = await WithScopeAsync(sp => sp.GetRequiredService<IProductionDb>().ScheduleRunsView.CountAsync());
        var order = await OrderAsync(supervisor, new { assetId = asset, type = "Preventive", problem = "Revisão", scheduledOn = Iso(day), hoursPerDay = 4m, expectedHours = 4m });
        var orderId = order.GetProperty("id").GetGuid();
        var from = DateOnly.Parse(order.GetProperty("downtimeFrom").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        from.Should().Be(day);
        order.GetProperty("downtimeTo").GetString().Should().Be(Iso(day), "4 h no total, 4 h por dia: um dia útil");

        await DrainAssetsOutboxAsync();
        var stored = (await DowntimesOfAsync(orderId)).Should().ContainSingle().Subject;
        stored.WorkCenterId.Should().Be(center);
        stored.HoursPerDay.Should().Be(4m);
        stored.StartsOn.Should().Be(from);
        var capacity = await WithScopeAsync(sp => sp.GetRequiredService<IWorkCenterCapacity>().GetAvailableHoursAsync(center, from, from));
        capacity!.Days.Single().Hours.Should().Be(full - 4m, "CA 8: as horas do centro − 4 h paradas");
        // O job de recálculo pode já ter pegado o pedido (sai de Pending): vale qualquer pedido feito depois da parada.
        (await WithScopeAsync(sp => sp.GetRequiredService<IProductionDb>().ScheduleRunsView.CountAsync()))
            .Should().BeGreaterThan(runsBefore, "CA 10: a parada pede o recálculo agrupado do plano");
        var listed = await OkAsync(await (await ActorAsync("owner")).GetAsync($"/api/v1/production/work-centers/{center}/downtimes"));
        listed.EnumerateArray().Single(d => d.GetProperty("orderId").GetGuid() == orderId).GetProperty("orderNumber").GetString().Should().Be(order.GetProperty("number").GetString());

        // CA 11: a reentrega (o consumidor de novo, fora da inbox) não duplica; o outro tenant não acha a OS.
        await WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<MaintenanceDowntimeConsumers>().HandleAsync(new MaintenanceScheduled(orderId, asset, center, from, from, 4m, false, 1), CancellationToken.None);
            return await sp.GetRequiredService<BemAquiDbContext>().SaveChangesAsync();
        });
        (await DowntimesOfAsync(orderId)).Should().ContainSingle();
        var infoB = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantB.Id))!;
        await using (var scopeB = await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(infoB))
        {
            (await scopeB.ServiceProvider.GetRequiredService<DowntimeService>().ReconcileAsync(orderId)).Should().BeFalse("no tenant B a OS não existe");
            await scopeB.ServiceProvider.GetRequiredService<BemAquiDbContext>().SaveChangesAsync();
            (await scopeB.ServiceProvider.GetRequiredService<IProductionDb>().Downtimes.AnyAsync(d => d.SourceId == orderId)).Should().BeFalse();
        }

        // Reagendar: o período novo substitui o anterior (uma linha só), com as horas novas.
        var moved = await OkAsync(await supervisor.PutAsJsonAsync($"{Orders}/{orderId}/schedule", new
        {
            scheduledOn = Day(20), hoursPerDay = 3m, expectedHours = 6m, version = order.GetProperty("version").GetUInt32(),
        }));
        await DrainAssetsOutboxAsync();
        var replaced = (await DowntimesOfAsync(orderId)).Should().ContainSingle().Subject;
        replaced.StartsOn.Should().Be(DateOnly.Parse(moved.GetProperty("downtimeFrom").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        replaced.PlannedEndOn.Should().Be(DateOnly.Parse(moved.GetProperty("downtimeTo").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        replaced.HoursPerDay.Should().Be(3m);
        replaced.StartsOn.Should().BeAfter(from);
        (await WithScopeAsync(sp => sp.GetRequiredService<IWorkCenterCapacity>().GetAvailableHoursAsync(center, from, from)))!.Days.Single().Hours
            .Should().Be(full, "o período antigo voltou à capacidade cheia");

        // Cancelar antes de começar: a parada sai.
        await OkAsync(await supervisor.PostAsJsonAsync($"{Orders}/{orderId}/cancel", new { reason = "Remarcada para o ano que vem", version = moved.GetProperty("version").GetUInt32() }));
        await DrainAssetsOutboxAsync();
        (await DowntimesOfAsync(orderId)).Should().BeEmpty("CA 8: cancelada antes de começar, a parada é removida");
    }

    /// <summary>CA 9: a corretiva com máquina parada começa a parada na abertura e a conclusão a encerra.</summary>
    [Fact]
    public async Task Maquina_parada_para_o_centro_na_abertura_e_a_conclusao_encerra()
    {
        var supervisor = await SupervisorAsync();
        var center = await CenterAsync("Corte");
        var asset = (await CreateAsync(supervisor, Input(Unique("Guilhotina"), center: center))).GetProperty("id").GetGuid();
        var order = await OrderAsync(supervisor, new { assetId = asset, type = "Corrective", problem = "Motor queimado", machineStopped = true, scheduledOn = Day(5), hoursPerDay = 9m });
        order.GetProperty("downtimeFrom").GetString().Should().Be(Day(0), "máquina parada: a parada começa na abertura");
        var orderId = order.GetProperty("id").GetGuid();
        await DrainAssetsOutboxAsync();
        (await DowntimesOfAsync(orderId)).Should().ContainSingle().Which.OpenEnded.Should().BeTrue();

        var started = await StartAsync(supervisor, order);
        await OkAsync(await supervisor.PostAsJsonAsync($"{Orders}/{orderId}/complete", new { workDone = "Motor trocado", version = started.GetProperty("version").GetUInt32() }));
        await DrainAssetsOutboxAsync();
        var ended = (await DowntimesOfAsync(orderId)).Should().ContainSingle().Subject;
        ended.EndedOn.Should().Be(DateOnly.Parse(Day(0), System.Globalization.CultureInfo.InvariantCulture), "encerrada na conclusão");
        var tomorrow = DateOnly.Parse(Day(1), System.Globalization.CultureInfo.InvariantCulture);
        var after = await WithScopeAsync(sp => sp.GetRequiredService<IWorkCenterCapacity>().GetAvailableHoursAsync(center, tomorrow, tomorrow));
        after!.Days.Single().Hours.Should().Be(await BaselineAsync(tomorrow), "depois da conclusão, a capacidade volta");
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Horas de um centro novo, sem parada, no dia (mesmo calendário e turno dos centros do teste).</summary>
    private async Task<decimal> BaselineAsync(DateOnly date)
    {
        var reference = await CenterAsync("Referência");
        return (await WithScopeAsync(sp => sp.GetRequiredService<IWorkCenterCapacity>().GetAvailableHoursAsync(reference, date, date)))!.Days.Single().Hours;
    }

    /// <summary>O primeiro dia útil (com capacidade no centro) a partir de hoje + <paramref name="offset"/>, e as horas dele.</summary>
    private async Task<(DateOnly Day, decimal Hours)> WorkingDayAsync(Guid center, int offset)
    {
        var start = DateOnly.Parse(Day(offset), System.Globalization.CultureInfo.InvariantCulture);
        var days = (await WithScopeAsync(sp => sp.GetRequiredService<IWorkCenterCapacity>().GetAvailableHoursAsync(center, start, start.AddDays(20))))!.Days;
        var first = days.First(d => d.Hours > 4m);
        return (first.Date, first.Hours);
    }

    /// <summary>CA 5 e 12: contas e centros da manutenção nascem "A confirmar com a contadora"; gravar e conferir com settings.edit; trocar volta a "A confirmar".</summary>
    [Fact]
    public async Task Contas_da_manutencao_nascem_a_confirmar_e_so_settings_edit_grava_e_confere()
    {
        var owner = await ActorAsync("owner");
        var view = await OkAsync(await owner.GetAsync("/api/v1/assets/parameters"));
        var items = view.GetProperty("maintenance").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("code").GetString()).Should().BeEquivalentTo(["2.6", "3", "3.3", "1"]);
        var supervisor = await SupervisorAsync();
        (await supervisor.PutAsJsonAsync("/api/v1/assets/parameters/maintenance", new { changes = new[] { new { key = "assets.maintenance_account_other", code = "3.9" } } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "parâmetros com settings.edit");

        var confirmed = await OkAsync(await owner.PostAsJsonAsync("/api/v1/assets/parameters/confirm", new { key = "assets.maintenance_account_machine" }));
        confirmed.GetProperty("maintenance").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "assets.maintenance_account_machine").GetProperty("confirmed").GetBoolean().Should().BeTrue();
        var invalid = await owner.PutAsJsonAsync("/api/v1/assets/parameters/maintenance", new { changes = new[] { new { key = "assets.maintenance_account_machine", code = "9.99" } } });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var changed = await OkAsync(await owner.PutAsJsonAsync("/api/v1/assets/parameters/maintenance", new { changes = new[] { new { key = "assets.maintenance_account_machine", code = "3.9" } } }));
        var machine = changed.GetProperty("maintenance").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "assets.maintenance_account_machine");
        machine.GetProperty("confirmed").GetBoolean().Should().BeFalse("trocou o valor: volta para \"A confirmar\"");
        await OkAsync(await owner.PutAsJsonAsync("/api/v1/assets/parameters/maintenance", new { changes = new[] { new { key = "assets.maintenance_account_machine", code = "2.6" } } }));
    }

    /// <summary>CA 12: com o Financeiro não liberado, o valor combinado é 403 module_not_released; a OS sem valor abre normalmente.</summary>
    [Fact]
    public async Task Valor_combinado_com_o_financeiro_nao_liberado_e_recusado()
    {
        var supervisor = await SupervisorAsync();
        var supplier = await SupplierAsync("Elétrica");
        var asset = (await CreateAsync(supervisor, Input(Unique("Quadro elétrico"), type: "Tool"))).GetProperty("id").GetGuid();
        await SetReleasedAsync("finance", false);
        try
        {
            var refused = await PostOrderAsync(supervisor, new { assetId = asset, type = "Corrective", problem = "Disjuntor", supplierPartyId = supplier, contractValue = 200m });
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(refused)).Should().Be(Error.Codes.ModuleNotReleased);
            await OrderAsync(supervisor, new { assetId = asset, type = "Corrective", problem = "Disjuntor", supplierPartyId = supplier });
        }
        finally
        {
            await SetReleasedAsync("finance", true);
        }
    }
}
