using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PRD-04, custo de mão de obra (Testcontainers): snapshot e evento no fechamento (CA 1), pausas fora (CA 2), custo-hora alterado
/// depois (CA 3), correção (CA 4), sem custo-hora, aviso e recálculo (CA 5, 6), razão no Commercial idempotente e fora de ordem
/// (CA 7, 8), Custos do projeto (CA 9), DTO sem custo e três camadas (CA 10, 14 a 16), recomposição (CA 12, 13) e histórico (CA 17).
/// </summary>
public partial class FabricationTests
{
    private const string CostGroup = "production:view,edit,approve;shopfloor:view,create;projects:view,view_cost";

    /// <summary>Ajusta o custo-hora do cargo do funcionário à mão (nulo = 0, "sem custo-hora").</summary>
    private async Task<Guid> SetRateAsync(Guid employeeId, decimal? rate) => await WithDbAsync(async db =>
    {
        var positionId = await db.Employees.Where(e => e.Id == employeeId).Select(e => e.PositionId).SingleAsync();
        var position = await db.JobPositions.SingleAsync(p => p.Id == positionId);
        position.HourlyCost = rate ?? 0m;
        position.HourlyCostManual = true;
        await db.SaveChangesAsync();
        return positionId;
    });

    /// <summary>"Apontar por funcionário" de <paramref name="minutes"/> min (fim há 5 min), com as pausas dadas em (início relativo, duração).</summary>
    private static async Task<JsonElement> RecordAsync(HttpClient supervisor, Guid order, Guid employee, Guid step, int minutes, params (int From, int Minutes, string Reason)[] pauses)
    {
        var end = DateTimeOffset.UtcNow.AddMinutes(-5);
        var start = end.AddMinutes(-minutes - pauses.Sum(p => p.Minutes));
        var body = new
        {
            employeeId = employee, stepId = step, startedAt = start, endedAt = end, notes = (string?)null,
            pauses = pauses.Select(p => new { reason = p.Reason, notes = (string?)null, startedAt = start.AddMinutes(p.From), endedAt = start.AddMinutes(p.From + p.Minutes) }).ToArray(),
        };
        return await OkAsync(await supervisor.SendAsync(Post($"{Base}/{order}/labor-entries", body)));
    }

    private async Task<T> PayloadAsync<T>(Guid entryId, int version) => await WithDbAsync(async db =>
    {
        var payloads = await db.OutboxMessages.AsNoTracking().Where(m => m.Type == typeof(T).FullName && m.Payload.Contains(entryId.ToString())).Select(m => m.Payload).ToListAsync();
        return payloads.Select(p => JsonSerializer.Deserialize<T>(p)!).Single(e => (int)typeof(T).GetProperty("Version")!.GetValue(e)! == version);
    });

    private async Task<List<ProjectCost>> LedgerAsync(Guid entryId) =>
        await WithDbAsync(db => db.ProjectCosts.AsNoTracking().Where(c => c.SourceId == entryId).OrderBy(c => c.Seq).ToListAsync());

    [Fact]
    public async Task Fechamento_grava_snapshot_custo_versao_e_o_evento_com_os_valores()
    {
        var (_, mine, _, project, orders) = await ShopfloorDatasetAsync();
        var (_, employee, _, _) = await OperatorAsync(mine);
        await SetRateAsync(employee, 48.000000m);
        var (_, supervisor) = await ActorAsync(CostGroup);
        var step = await StepIdAsync(orders[0], 1);

        await RecordAsync(supervisor, orders[0], employee, step, 150);

        var entry = (await EntriesAsync(step)).Single();
        (entry.HourlyCostSnapshot, entry.Cost, entry.CostVersion, entry.CostMissing).Should().Be((48.000000m, 120.00m, 1, false));
        var closed = await PayloadAsync<LaborEntryClosed>(entry.Id, 1);
        (closed.PreviousAmount, closed.NewAmount, closed.Origin, closed.ProjectId).Should().Be((0m, 120.00m, LaborCostOrigin.Closing, project));
        closed.OccurredOn.Should().Be(Today);
        var history = await WithDbAsync(db => db.LaborCostHistory.AsNoTracking().Where(h => h.LaborEntryId == entry.Id).ToListAsync());
        history.Should().ContainSingle().Which.Origin.Should().Be(LaborCostOrigin.Closing);

        await DrainAsync();
        var ledger = await LedgerAsync(entry.Id);
        ledger.Should().ContainSingle();
        (ledger[0].Source, ledger[0].Seq, ledger[0].Amount, ledger[0].CostType, ledger[0].ProjectId, ledger[0].IsBackfill)
            .Should().Be((ProjectCostSource.Labor, 1, 120.00m, BemAqui.SharedKernel.CostType.Labor, project, false));
    }

    [Fact]
    public async Task Nenhuma_pausa_entra_no_custo_e_o_custo_hora_novo_so_vale_para_os_proximos()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, employee, _, _) = await OperatorAsync(mine);
        await SetRateAsync(employee, 48m);
        var (_, supervisor) = await ActorAsync(CostGroup);
        var step = await StepIdAsync(orders[0], 1);

        await RecordAsync(supervisor, orders[0], employee, step, 150, (10, 15, "Material"), (40, 10, "Machine"), (70, 30, "Break"), (120, 5, "Other"));
        await SetRateAsync(employee, 60m);
        await RecordAsync(supervisor, orders[0], employee, step, 60);

        var entries = await EntriesAsync(step);
        entries[0].Cost.Should().Be(120.00m, "as pausas de qualquer motivo ficam fora (CA 2)");
        entries[0].HourlyCostSnapshot.Should().Be(48m, "o snapshot não muda com o custo-hora novo (CA 3)");
        entries[1].HourlyCostSnapshot.Should().Be(60m);
        entries[1].Cost.Should().Be(60.00m);
    }

    [Fact]
    public async Task Correcao_refaz_o_custo_com_o_mesmo_snapshot_e_a_razao_grava_a_diferenca()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, employee, _, _) = await OperatorAsync(mine);
        await SetRateAsync(employee, 48m);
        var (_, supervisor) = await ActorAsync(CostGroup);
        var step = await StepIdAsync(orders[0], 1);
        await RecordAsync(supervisor, orders[0], employee, step, 150);
        var entry = (await EntriesAsync(step)).Single();
        await SetRateAsync(employee, 99m);

        var fix = new { startedAt = entry.StartedAt, endedAt = entry.StartedAt.AddHours(3), pauses = Array.Empty<object>(), reason = "Faltou meia hora.", version = entry.Version };
        await OkAsync(await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/labor-entries/{entry.Id}/correct", fix));

        var after = (await EntriesAsync(step)).Single();
        (after.HourlyCostSnapshot, after.Cost, after.CostVersion).Should().Be((48m, 144.00m, 2));
        var corrected = await PayloadAsync<LaborEntryCorrected>(entry.Id, 2);
        (corrected.PreviousAmount, corrected.NewAmount, corrected.Origin, corrected.Minutes).Should().Be((120m, 144m, LaborCostOrigin.Correction, 180));

        await DrainAsync();
        var ledger = await LedgerAsync(entry.Id);
        ledger.Select(l => (l.Source, l.Amount)).Should().Equal((ProjectCostSource.Labor, 120m), (ProjectCostSource.LaborCorrection, 24m));
        ledger.Sum(l => l.Amount).Should().Be(after.Cost, "a soma das linhas é o custo atual do apontamento");
    }

    [Fact]
    public async Task Sem_custo_hora_grava_zero_avisa_o_RH_e_o_recalculo_publica_a_diferenca()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, employee, _, _) = await OperatorAsync(mine);
        var positionId = await SetRateAsync(employee, null);
        var (hrId, _) = await ActorAsync("hr:view,edit");
        var (_, supervisor) = await ActorAsync(CostGroup);
        var (_, viewer) = await ActorAsync("production:view;projects:view,view_cost");
        var step = await StepIdAsync(orders[0], 1);
        await RecordAsync(supervisor, orders[0], employee, step, 150);

        var entry = (await EntriesAsync(step)).Single();
        (entry.Cost, entry.CostMissing, entry.HourlyCostSnapshot, entry.CostPositionId).Should().Be((0m, true, null, positionId));
        var notice = await WithDbAsync(db => db.Notifications.AsNoTracking().SingleAsync(n => n.UserId == hrId && n.Type == LaborCosting.NoCostType));
        notice.Title.Should().EndWith("sem custo-hora: 1 apontamento sem custo.").And.StartWith("Cargo ");
        notice.Link.Should().Be(LaborCosting.PositionLink + positionId);

        (await viewer.PostAsJsonAsync("/api/v1/production/labor-costs/recalculate", new { positionId })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "recalcular exige production.approve (CA 16)");
        var refused = await supervisor.PostAsJsonAsync("/api/v1/production/labor-costs/recalculate", new { positionId });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "o cargo ainda não tem custo-hora");

        await SetRateAsync(employee, 40m);
        var impact = await OkAsync(await supervisor.GetAsync($"/api/v1/production/labor-costs/missing?positionId={positionId}"));
        (impact.GetProperty("count").GetInt32(), impact.GetProperty("recalculableCount").GetInt32(), impact.GetProperty("newTotal").GetDecimal()).Should().Be((1, 1, 100.00m));
        var done = await OkAsync(await supervisor.PostAsJsonAsync("/api/v1/production/labor-costs/recalculate", new { positionId }));
        done.GetProperty("recalculated").GetInt32().Should().Be(1);

        var after = (await EntriesAsync(step)).Single();
        (after.Cost, after.CostMissing, after.HourlyCostSnapshot, after.CostVersion).Should().Be((100.00m, false, 40m, 2));
        var recalculated = await PayloadAsync<LaborEntryCorrected>(entry.Id, 2);
        (recalculated.Origin, recalculated.PreviousAmount, recalculated.NewAmount).Should().Be((LaborCostOrigin.Recalculation, 0m, 100m));
        (await OkAsync(await supervisor.GetAsync($"/api/v1/production/labor-costs/missing?positionId={positionId}"))).GetProperty("count").GetInt32().Should().Be(0, "o snapshot nunca é recalculado");
    }

    [Fact]
    public async Task Consumidor_e_idempotente_e_aceita_eventos_fora_de_ordem()
    {
        var (_, _, _, project, orders) = await ShopfloorDatasetAsync();
        var entryId = Guid.CreateVersion7();
        LaborEntryClosed Closed(decimal amount) => new(entryId, orders[0], project, Guid.CreateVersion7(), Guid.CreateVersion7(), 150, 1, 0m, amount, Today);
        LaborEntryCorrected Corrected(int version, decimal before, decimal amount, LaborCostOrigin origin) => new(entryId, orders[0], project, 180, version, before, amount, Today, origin);

        async Task Handle(Func<LaborCostConsumer, Task> action)
        {
            await using var scope = await ScopeAsync();
            await action(scope.ServiceProvider.GetRequiredService<LaborCostConsumer>());
        }

        await Handle(c => c.HandleAsync(Corrected(3, 144m, 150m, LaborCostOrigin.Recalculation), CancellationToken.None));
        await Handle(c => c.HandleAsync(Closed(120m), CancellationToken.None));
        await Handle(c => c.HandleAsync(Corrected(2, 120m, 144m, LaborCostOrigin.Correction), CancellationToken.None));
        await Handle(c => c.HandleAsync(Corrected(3, 144m, 150m, LaborCostOrigin.Recalculation), CancellationToken.None));
        await Handle(c => c.HandleAsync(Closed(120m), CancellationToken.None));
        await Handle(c => c.HandleAsync(new LaborEntryCorrected(entryId), CancellationToken.None));

        var ledger = await LedgerAsync(entryId);
        ledger.Should().ContainSingle("a versão 3 chegou primeiro e já levou o apontamento ao valor final");
        ledger.Sum(l => l.Amount).Should().Be(150m);
    }

    [Fact]
    public async Task Custos_da_OS_e_do_projeto_so_com_view_cost_e_DTO_sem_custo_para_os_outros()
    {
        var (_, mine, _, project, orders) = await ShopfloorDatasetAsync();
        var (_, employee, _, _) = await OperatorAsync(mine);
        await SetRateAsync(employee, 48m);
        var (_, supervisor) = await ActorAsync(CostGroup);
        var (_, hrViewer) = await ActorAsync("production:view;hr:view");
        var step = await StepIdAsync(orders[0], 1);
        await RecordAsync(supervisor, orders[0], employee, step, 150);
        await DrainAsync();

        var costs = await OkAsync(await supervisor.GetAsync($"{Base}/{orders[0]}/labor-costs"));
        costs.GetProperty("laborCost").GetDecimal().Should().Be(120m);
        costs.GetProperty("steps").EnumerateArray().Single().GetProperty("cost").GetDecimal().Should().Be(120m);
        var entryCost = costs.GetProperty("entries").EnumerateArray().Single();
        entryCost.GetProperty("hourlyCostSnapshot").GetDecimal().Should().Be(48m);
        entryCost.EnumerateObject().Select(p => p.Name).Should().NotContain(n => n.Contains("salary", StringComparison.OrdinalIgnoreCase) || n.Contains("employee", StringComparison.OrdinalIgnoreCase));

        (await hrViewer.GetAsync($"{Base}/{orders[0]}/labor-costs")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "hr.view sem projects.view_cost não vê custo (CA 15)");
        var plain = await OkAsync(await hrViewer.GetAsync($"{Base}/{orders[0]}/labor-entries"));
        plain.GetRawText().Should().NotContainAny("cost", "Cost", "hourly");
        var entryId = (await EntriesAsync(step)).Single().Id;
        var history = await OkAsync(await supervisor.GetAsync($"{Base}/{orders[0]}/labor-entries/{entryId}/cost-history"));
        var version = history.EnumerateArray().Single();
        (version.GetProperty("origin").GetString(), version.GetProperty("amount").GetDecimal(), version.GetProperty("costVersion").GetInt32()).Should().Be(("Closing", 120m, 1));
        version.GetProperty("recordedByName").ValueKind.Should().NotBe(JsonValueKind.Undefined, "CA 17: quem fez");
        (await OkAsync(await hrViewer.GetAsync($"{Base}/{orders[0]}/history"))).GetRawText().Should().Contain("\"changed\":true", "o histórico da OS só diz que o custo mudou, sem o valor");

        var projectCosts = await OkAsync(await supervisor.GetAsync($"/api/v1/projects/{project}/costs"));
        projectCosts.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("costType").GetString() == "Labor").GetProperty("realized").GetDecimal().Should().Be(120m);

        await SetModuleReleasedAsync("projects", false);
        try
        {
            var blocked = await supervisor.GetAsync($"{Base}/{orders[0]}/labor-costs");
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(blocked)).Should().Be("module_not_released");
        }
        finally
        {
            await SetModuleReleasedAsync("projects", true);
        }
    }

    [Fact]
    public async Task Recomposicao_roda_duas_vezes_e_com_o_consumidor_ao_vivo_e_grava_uma_vez()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, employee, _, _) = await OperatorAsync(mine);
        await SetRateAsync(employee, 30m);
        var (_, supervisor) = await ActorAsync(CostGroup);
        var step = await StepIdAsync(orders[0], 1);
        await RecordAsync(supervisor, orders[0], employee, step, 120);
        var entry = (await EntriesAsync(step)).Single();
        await DrainAsync();
        // Simula o apontamento fechado antes da PRD-04: sem custo, sem versão e sem linha na razão.
        await WithDbAsync(db => db.LaborEntries.Where(e => e.Id == entry.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.CostVersion, 0).SetProperty(e => e.Cost, 0m).SetProperty(e => e.HourlyCostSnapshot, (decimal?)null)));
        await WithDbAsync(db => db.LaborCostHistory.Where(h => h.LaborEntryId == entry.Id).ExecuteDeleteAsync());
        await WithDbAsync(db => db.ProjectCosts.Where(c => c.SourceId == entry.Id).ExecuteDeleteAsync());

        async Task<int> RunAsync()
        {
            await using var scope = await ScopeAsync();
            return await scope.ServiceProvider.GetRequiredService<LaborCostBackfill>().RunAsync();
        }

        (await RunAsync()).Should().BeGreaterThanOrEqualTo(1);
        (await RunAsync()).Should().Be(0, "a marca é a versão do custo: a segunda execução não acha nada");
        await DrainAsync();
        await DrainAsync();

        var after = (await EntriesAsync(step)).Single();
        (after.Cost, after.CostVersion, after.CostBackfilled, after.HourlyCostSnapshot).Should().Be((60.00m, 1, true, 30m));
        var ledger = await LedgerAsync(entry.Id);
        ledger.Should().ContainSingle().Which.IsBackfill.Should().BeTrue();
        (await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type == typeof(LaborEntryClosed).FullName && m.Payload.Contains(entry.Id.ToString())).Select(m => m.Payload).ToListAsync()))
            .Select(p => JsonSerializer.Deserialize<LaborEntryClosed>(p)!.Origin).Should().ContainSingle(o => o == LaborCostOrigin.Backfill);
        var costs = await OkAsync(await supervisor.GetAsync($"{Base}/{orders[0]}/labor-costs"));
        costs.GetProperty("includesBackfill").GetBoolean().Should().BeTrue("marca Calculado na implantação (CA 13)");
        (await WithDbAsync(db => db.LaborCostHistory.AsNoTracking().SingleAsync(h => h.LaborEntryId == entry.Id))).Origin.Should().Be(LaborCostOrigin.Backfill);
    }
}