using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Domain;
using BemAqui.Production.Contracts;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Ajuste automático do tipo do projeto (EXE-01a CA 5 a 9): uma execução por tenant. Considera só os projetos com <c>execution_kind_source = Default</c>
/// (os anteriores à migration). Com o recurso <c>projects.service_execution</c>, os <c>Planned</c>, <c>InProgress</c>, <c>Delivered</c> ou <c>Closed</c> sem
/// fabricação registrada e sem nenhuma OS (fabricação ou instalação, inclusive cancelada) passam pela regra única do tipo; os demais ficam
/// <c>Fabrication</c>. Projeto de serviço entregue ganha o aceite herdado da entrega. Lotes de 500. Não publica evento, não toca no Finance e grava a
/// marca <c>backfill.execution_kind_done</c> no fim: as execuções seguintes terminam sem ler nada.
/// </summary>
public sealed class ExecutionKindBackfill(
    ICommercialDb db, IProductionOrders productionOrders, IInstallationOrders installations, TenantContextHolder tenant, AuditOrigin origin, TimeProvider clock)
{
    public const int BatchSize = 500;
    public const string AuditLabel = "Ajuste automático EXE-01a";

    private static readonly ProjectStatus[] Candidates = [ProjectStatus.Planned, ProjectStatus.InProgress, ProjectStatus.Delivered, ProjectStatus.Closed];

    /// <summary>Projetos gravados (0 quando a marca já existe).</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (await db.CommercialParameters.AnyAsync(p => p.Key == CommercialParametersService.ExecutionKindBackfillDone, cancellationToken))
        {
            return 0;
        }

        origin.Label = AuditLabel;
        var released = tenant.Resolved?.HasFeature(BusinessProfileCatalog.ServiceExecutionFeature) == true;
        var done = 0;
        while (true)
        {
            var batch = await db.Projects.Where(p => p.ExecutionKindSource == ExecutionKindSource.Default).OrderBy(p => p.Id).Take(BatchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            var eligible = released ? batch.Where(p => Candidates.Contains(p.Status) && p.FabricatedOn is null).Select(p => p.Id).ToList() : [];
            var rows = eligible.Count == 0 ? [] : await db.ProjectItems.AsNoTracking().Where(i => eligible.Contains(i.ProjectId))
                .Join(db.QuoteItems.AsNoTracking(), i => i.QuoteItemId, q => q.Id, (i, q) => new ItemKindRow(i.ProjectId, q.Kind)).ToListAsync(cancellationToken);
            var kinds = rows.ToLookup(x => x.ProjectId, x => x.Kind);
            foreach (var project in batch)
            {
                var kind = eligible.Contains(project.Id) && ExecutionKindRule.For(true, kinds[project.Id].ToList()) == ExecutionKind.Service
                    && !await HasOrdersAsync(project.Id, cancellationToken)
                    ? ExecutionKind.Service
                    : ExecutionKind.Fabrication;
                project.ExecutionKind = kind;
                project.ExecutionKindSource = ExecutionKindSource.Backfill;
                if (kind == ExecutionKind.Service && project.Status is ProjectStatus.Delivered or ProjectStatus.Closed && project.DeliveredOn is { } delivered
                    && !await db.ProjectAcceptances.AnyAsync(a => a.ProjectId == project.Id, cancellationToken))
                {
                    // CA 6: aceite herdado da entrega (conta como aceito na conclusão); o histórico mostra a origem pelo rótulo da auditoria.
                    db.ProjectAcceptances.Add(new ProjectAcceptance
                    {
                        ProjectId = project.Id, Version = 1, Kind = AcceptanceKind.Inherited, OccurredOn = delivered, RecordedAt = clock.GetUtcNow(),
                    });
                    project.AcceptedOn = delivered;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            done += batch.Count;
        }

        db.CommercialParameters.Add(new CommercialParameter { Key = CommercialParametersService.ExecutionKindBackfillDone, Value = 1m, UpdatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
        return done;
    }

    /// <summary>Alguma OS do projeto, de fabricação ou de instalação, inclusive cancelada (CA 5).</summary>
    private async Task<bool> HasOrdersAsync(Guid projectId, CancellationToken ct) =>
        (await productionOrders.ByProjectAsync(projectId, ct)).Count > 0 || (await installations.ByProjectAsync(projectId, ct)).Count > 0;
}

internal sealed record ItemKindRow(Guid ProjectId, QuoteItemKind Kind);

public sealed record ExecutionKindBackfillArgs;

/// <summary>Execução por tenant do ajuste automático do tipo (CA 5); argumentos só de ids (o tenant).</summary>
public sealed class ExecutionKindBackfillJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<ExecutionKindBackfillJob> logger)
    : TenantJob<ExecutionKindBackfillArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, ExecutionKindBackfillArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<ExecutionKindBackfill>().RunAsync(cancellationToken);
}

/// <summary>
/// Recorrente global que enfileira o ajuste por tenant ativo, na fila Maintenance: disparado na subida do processo (logo depois do deploy) e repetido de
/// madrugada (03:55 de São Paulo); com a marca gravada, termina sem ler nada.
/// </summary>
public sealed class ExecutionKindBackfillRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<ExecutionKindBackfillJob, ExecutionKindBackfillArgs>(directory, jobs)
{
    public const string Id = "commercial-execution-kind-backfill";
    public const string Cron = "55 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override ExecutionKindBackfillArgs ArgsFor(TenantInfo tenant) => new();
}
