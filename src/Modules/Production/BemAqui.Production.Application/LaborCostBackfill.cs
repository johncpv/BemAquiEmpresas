using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Production.Contracts;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Production.Application;

/// <summary>
/// Recomposição dos apontamentos fechados antes da PRD-04 (D3, ADR 0025 §3, CA 12): cada apontamento fechado sem custo (versão 0)
/// recebe o custo-hora atual do cargo (o único disponível, valor do dia da recomposição) como snapshot, o custo e a versão 1 com a
/// origem "Implantação" (<c>Backfill</c>), e publica o <see cref="LaborEntryClosed"/> que o Commercial grava com <c>is_backfill</c>.
/// Idempotente por tenant: a marca é a própria versão do custo (rodar de novo não acha nada); o fechamento ao vivo já nasce com a
/// versão 1; corrida com uma correção cai na versão (xmin) do apontamento e o Hangfire repete. Sem custo-hora segue o CA 5.
/// </summary>
public sealed class LaborCostBackfill(IProductionDb db, LaborCosting costing, IEventPublisher events, TenantContextHolder tenant)
{
    public const int BatchSize = 200;

    /// <summary>Quantos apontamentos foram recompostos (0 com o módulo não liberado ou nada pendente).</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.Resolved?.IsReleased("production") != true)
        {
            return 0;
        }

        var done = 0;
        while (true)
        {
            var batch = await db.LaborEntries.Where(e => e.EndedAt != null && e.CostVersion == 0).OrderBy(e => e.EndedAt).ThenBy(e => e.Id).Take(BatchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return done;
            }

            var orderIds = batch.Select(e => e.OrderId).Distinct().ToList();
            var projects = await db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.ProjectId, cancellationToken);
            var ids = batch.Select(e => e.Id).ToList();
            var pauses = (await db.StepPauses.AsNoTracking().Where(p => ids.Contains(p.LaborEntryId)).Select(p => new { p.LaborEntryId, p.StartedAt, p.EndedAt }).ToListAsync(cancellationToken))
                .ToLookup(p => p.LaborEntryId, p => new PauseSpan(p.StartedAt, p.EndedAt));
            foreach (var entry in batch)
            {
                var ended = entry.EndedAt!.Value;
                var minutes = LaborRules.NetMinutes(entry.StartedAt, ended, pauses[entry.Id], ended);
                var on = DateOnly.FromDateTime(BusinessTime.ToBusiness(ended).DateTime);
                events.Publish(await costing.CloseAsync(entry, projects[entry.OrderId], minutes, LaborCostOrigin.Backfill, on, cancellationToken));
            }

            await db.SaveChangesAsync(cancellationToken);
            done += batch.Count;
        }
    }
}

public sealed record LaborCostBackfillArgs;

/// <summary>Execução por tenant da recomposição (CA 12); argumentos só de ids (o tenant).</summary>
public sealed class LaborCostBackfillJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<LaborCostBackfillJob> logger)
    : TenantJob<LaborCostBackfillArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, LaborCostBackfillArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<LaborCostBackfill>().RunAsync(cancellationToken);
}

/// <summary>
/// Recorrente global que enfileira a recomposição por tenant ativo: disparado na subida do processo (logo depois do deploy) e
/// repetido de madrugada (03:30 de São Paulo); depois da primeira execução não acha nada e termina.
/// </summary>
public sealed class LaborCostBackfillRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<LaborCostBackfillJob, LaborCostBackfillArgs>(directory, jobs)
{
    public const string Id = "production-labor-cost-backfill";
    public const string Cron = "30 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override LaborCostBackfillArgs ArgsFor(TenantInfo tenant) => new();
}