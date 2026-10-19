using System.Runtime.CompilerServices;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BemAqui.Migrations.Application;

/// <summary>
/// Sessão de uma execução, entregue aos carregadores. Conta o que foi lido, decide o resultado de cada linha pelo hash da
/// origem e pelo <c>xmin</c> do destino (CA 11 a 13) e guarda os itens da staging. Cada linha roda num savepoint: erro do
/// dado vira item com motivo e a carga segue; erro de infraestrutura sobe e desfaz a entidade (CA 15).
/// </summary>
public sealed class ImportSession
{
    private readonly IMigrationDb _db;
    private readonly ISnapshotSource _source;
    private readonly TimeProvider _clock;
    private readonly List<ImportItem> _items = [];
    private readonly Dictionary<string, int> _read = new(StringComparer.Ordinal);
    private IDbContextTransaction? _transaction;
    private string _entity = string.Empty;
    private int _processed;

    /// <summary>Linhas por lote: a cada lote o rastreador do EF é esvaziado, para a memória não crescer com a entidade.</summary>
    public const int BatchSize = 500;

    internal ImportSession(IMigrationDb db, ISnapshotSource source, ImportMapping mapping, Guid runId, TimeProvider clock)
    {
        _db = db;
        _source = source;
        _clock = clock;
        Mapping = mapping;
        RunId = runId;
    }

    public Guid RunId { get; }

    public ImportMapping Mapping { get; }

    internal IReadOnlyList<ImportItem> Items => _items;

    internal int ReadCount(string entity) => _read.GetValueOrDefault(entity);

    internal void Begin(string entity, IDbContextTransaction transaction)
    {
        _entity = entity;
        _transaction = transaction;
        _read[entity] = 0;
    }

    /// <summary>Descarta os itens da entidade em curso (falha de infraestrutura: a entidade toda foi desfeita).</summary>
    internal void Discard(string entity) => _items.RemoveAll(i => i.Entity == entity);

    /// <summary>Lê a tabela do snapshot; cada linha lida precisa ser processada ou registrada, porque a soma fecha com o total lido (CA 17).</summary>
    public async IAsyncEnumerable<SourceRow> ReadAsync(SnapshotTable table, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        await foreach (var row in _source.ReadAsync(table, cancellationToken))
        {
            _read[_entity] = _read.GetValueOrDefault(_entity) + 1;
            yield return row;
        }
    }

    /// <summary>
    /// Tabela de apoio (UFs, tipos, categorias): lida sem contar no total, porque não vira cadastro e não entra na soma do
    /// relatório (CA 17).
    /// </summary>
    public async Task<IReadOnlyList<SourceRow>> LookupAsync(SnapshotTable table, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        var rows = new List<SourceRow>();
        await foreach (var row in _source.ReadAsync(table, cancellationToken))
        {
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Se algum registro de origem já aponta para este destino (fusão por documento, MIG-01b CA 6).</summary>
    public Task<bool> TargetHasRefAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default) =>
        _db.ExternalRefs.AnyAsync(r => r.TargetType == targetType && r.TargetId == targetId, cancellationToken);

    /// <summary>Chave externa de uma linha já importada (ex.: o terceiro de um contato).</summary>
    public Task<ExternalRef?> FindRefAsync(string sourceTable, string sourceId, CancellationToken cancellationToken = default) =>
        _db.ExternalRefs.AsNoTracking().FirstOrDefaultAsync(r => r.Source == ExternalRef.Dolibarr && r.SourceTable == sourceTable && r.SourceId == sourceId, cancellationToken);

    /// <summary>Registra sem gravar no destino: linha fora do filtro, duplicada na origem etc.</summary>
    public void Skip(SourceRow row, string reason)
    {
        ArgumentNullException.ThrowIfNull(row);
        Add(row, SourceHash.Of(row), new RowResult(ImportOutcome.Skipped, Message: reason));
    }

    public async Task<ImportOutcome> ProcessAsync(SourceRow row, IRowImporter importer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(importer);
        var transaction = _transaction ?? throw new InvalidOperationException("Linha processada fora de uma entidade.");
        var hash = SourceHash.Of(row);
        await transaction.CreateSavepointAsync("row", cancellationToken);
        try
        {
            var (result, differences) = await ClassifyAsync(row, hash, importer, cancellationToken);
            await transaction.ReleaseSavepointAsync("row", cancellationToken);
            Add(row, hash, result, differences);
            return result.Outcome;
        }
        catch (Exception ex) when (DbErrors.IsDataError(ex, out var detail))
        {
            await transaction.RollbackToSavepointAsync("row", cancellationToken);
            _db.ChangeTracker.Clear();
            Add(row, hash, RowResult.Fail("O banco recusou o registro (" + detail + "). Confira os dados de origem."));
            return ImportOutcome.Error;
        }
    }

    private async Task<(RowResult Result, IReadOnlyList<FieldDifference>? Differences)> ClassifyAsync(SourceRow row, string hash, IRowImporter importer, CancellationToken ct)
    {
        var existing = await _db.ExternalRefs.FirstOrDefaultAsync(r => r.Source == ExternalRef.Dolibarr && r.SourceTable == row.Table.Name && r.SourceId == row.Id, ct);
        if (existing is null)
        {
            var created = await importer.CreateAsync(row, ct);
            if (created is { Outcome: ImportOutcome.Imported or ImportOutcome.Linked or ImportOutcome.Merged, TargetId: { } id, TargetType: { } type, Xmin: { } xmin })
            {
                _db.ExternalRefs.Add(new ExternalRef
                {
                    SourceTable = row.Table.Name, SourceId = row.Id, TargetType = type, TargetId = id,
                    SourceHash = hash, TargetXmin = xmin, ImportedAt = _clock.GetUtcNow(), LastRunId = RunId,
                });
                await _db.SaveChangesAsync(ct);
                await SyncXminAsync(type, id, xmin, ct);
            }

            return (created, null);
        }

        var target = new RowResult(ImportOutcome.Unchanged, existing.TargetType, existing.TargetId);
        if (existing.SourceHash == hash)
        {
            existing.LastRunId = RunId;
            await _db.SaveChangesAsync(ct);
            return (target, null);
        }

        var current = await importer.VersionAsync(existing.TargetId, ct);
        if (current is null)
        {
            return (target with { Outcome = ImportOutcome.Conflict, Message = "O registro foi excluído no BemAqui e mudou no Dolibarr. A mudança não foi aplicada." }, null);
        }

        if (current != existing.TargetXmin)
        {
            var differences = await importer.DiffAsync(row, existing.TargetId, ct);
            return (target with { Outcome = ImportOutcome.Conflict, Message = "Editado no BemAqui depois da importação e alterado no Dolibarr. O BemAqui não foi sobrescrito." }, differences);
        }

        var updated = await importer.UpdateAsync(row, existing.TargetId, ct);
        if (updated is { Outcome: ImportOutcome.Updated, Xmin: { } newXmin })
        {
            existing.SourceHash = hash;
            existing.TargetXmin = newXmin;
            existing.LastRunId = RunId;
            await _db.SaveChangesAsync(ct);
            await SyncXminAsync(existing.TargetType, existing.TargetId, newXmin, ct);
        }

        return (updated with { TargetType = updated.TargetType ?? existing.TargetType, TargetId = updated.TargetId ?? existing.TargetId }, null);
    }

    /// <summary>
    /// Vários registros de origem podem apontar para o mesmo destino (fusão, MIG-01b CA 6): depois de gravar, todos passam a ter
    /// o xmin novo, senão a próxima mudança de um deles pareceria edição no BemAqui (conflito falso).
    /// </summary>
    private Task<int> SyncXminAsync(string targetType, Guid targetId, long xmin, CancellationToken ct) =>
        _db.ExternalRefs.Where(r => r.TargetType == targetType && r.TargetId == targetId && r.TargetXmin != xmin)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.TargetXmin, xmin), ct);

    private void Add(SourceRow row, string hash, RowResult result, IReadOnlyList<FieldDifference>? differences = null)
    {
        if (++_processed % BatchSize == 0)
        {
            _db.ChangeTracker.Clear();
        }

        _items.Add(new ImportItem
        {
            RunId = RunId,
            Entity = _entity,
            SourceTable = row.Table.Name,
            SourceId = row.Id,
            Payload = PersonalMask.Payload(row),
            Hash = hash,
            Outcome = result.Outcome,
            TargetType = result.TargetType,
            TargetId = result.TargetId,
            Message = result.Message,
            Warning = result.Warning,
            Differences = differences is { Count: > 0 } ? JsonSerializer.Serialize(differences.Select(PersonalMask.Difference)) : null,
        });
    }
}
