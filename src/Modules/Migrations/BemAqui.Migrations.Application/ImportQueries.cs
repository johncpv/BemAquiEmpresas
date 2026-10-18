using System.Text.Json;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Migrations.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Migrations.Application;

public sealed record ImportRunRow(Guid Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Mode, string Status, string Operator, string Entities, bool Purged);

public sealed record ImportRunDetail(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string Mode,
    string Status,
    string Operator,
    DateTimeOffset? SnapshotTakenAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? PurgedAt,
    string? Failure,
    IReadOnlyList<EntitySummary> Entities);

/// <summary>Pendência (erro, conflito ou aviso) com o destino, para o link ao registro no BemAqui (CA 19).</summary>
public sealed record ImportPendingItem(Guid Id, string Entity, string SourceTable, string SourceId, string Kind, string? TargetType, Guid? TargetId, string? Message, string? Warning, IReadOnlyList<FieldDifference>? Differences);

/// <summary>Tipo de pendência do filtro: <c>error</c>, <c>conflict</c> ou <c>warning</c>.</summary>
public sealed record ImportPendingQuery(string? Entity, string? Kind);

/// <summary>
/// Leitura da tela Configurações → Importação do Dolibarr (D4, CA 19): só execuções, totais e pendências. O conteúdo de
/// origem (<c>payload</c>) da staging nunca sai por aqui (CA 22).
/// </summary>
public sealed class ImportQueries(IMigrationDb db)
{
    public Task<PagedList<ImportRunRow>> ListRunsAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        db.ImportRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new ImportRunRow(r.Id, r.StartedAt, r.FinishedAt, r.Mode.ToString(), r.Status.ToString(), r.Operator, r.Entities, r.PurgedAt != null))
            .ToPagedListAsync(page, cancellationToken);

    public async Task<Result<ImportRunDetail>> GetRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await db.ImportRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null)
        {
            return Error.NotFound("migration.run_not_found", "Execução não encontrada.");
        }

        var entities = run.Summary is null ? [] : JsonSerializer.Deserialize<List<EntitySummary>>(run.Summary, ImportRunner.Json) ?? [];
        return new ImportRunDetail(run.Id, run.StartedAt, run.FinishedAt, run.Mode.ToString(), run.Status.ToString(), run.Operator,
            run.SnapshotTakenAt, run.AcceptedAt, run.PurgedAt, run.Failure, entities);
    }

    public async Task<Result<PagedList<ImportPendingItem>>> PendingAsync(Guid id, ImportPendingQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await db.ImportRuns.AnyAsync(r => r.Id == id, cancellationToken))
        {
            return Error.NotFound("migration.run_not_found", "Execução não encontrada.");
        }

        var items = db.ImportItems.AsNoTracking().Where(i => i.RunId == id
            && (i.Outcome == ImportOutcome.Error || i.Outcome == ImportOutcome.Conflict || i.Warning != null));
        if (!string.IsNullOrWhiteSpace(query.Entity))
        {
            items = items.Where(i => i.Entity == query.Entity);
        }

        items = query.Kind switch
        {
            "error" => items.Where(i => i.Outcome == ImportOutcome.Error),
            "conflict" => items.Where(i => i.Outcome == ImportOutcome.Conflict),
            "warning" => items.Where(i => i.Warning != null),
            _ => items,
        };

        var result = await items.OrderBy(i => i.Entity).ThenBy(i => i.SourceTable).ThenBy(i => i.SourceId)
            .Select(i => new { i.Id, i.Entity, i.SourceTable, i.SourceId, i.Outcome, i.TargetType, i.TargetId, i.Message, i.Warning, i.Differences })
            .ToPagedListAsync(page, cancellationToken);
        var mapped = result.Items.Select(i => new ImportPendingItem(i.Id, i.Entity, i.SourceTable, i.SourceId,
            i.Outcome == ImportOutcome.Error ? "error" : i.Outcome == ImportOutcome.Conflict ? "conflict" : "warning",
            i.TargetType, i.TargetId, i.Message, i.Warning,
            i.Differences is null ? null : JsonSerializer.Deserialize<List<FieldDifference>>(i.Differences, ImportRunner.Json))).ToList();
        return new PagedList<ImportPendingItem>(mapped, result.Page, result.PageSize, result.Total);
    }
}
