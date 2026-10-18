namespace BemAqui.Migrations.Application.Snapshot;

/// <summary>Linha lida do snapshot: tabela, id de origem (chave, partes unidas por <c>-</c>) e valores já convertidos.</summary>
public sealed record SourceRow(SnapshotTable Table, string Id, IReadOnlyDictionary<string, object?> Values)
{
    public object? this[string column] => Values.TryGetValue(column, out var value) ? value : null;

    public string? Text(string column) => this[column] as string;

    public int? WholeNumber(string column) => this[column] switch
    {
        null => null,
        int i => i,
        long l => checked((int)l),
        decimal d => (int)d,
        var other => Convert.ToInt32(other, System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// Snapshot do Dolibarr já restaurado no MariaDB temporário (ADR 0017, CA 4). Lê só as colunas da lista fixa, com
/// <c>SELECT</c> de colunas explícitas; nunca conecta no banco do Dolibarr em produção.
/// </summary>
public interface ISnapshotSource : IAsyncDisposable
{
    /// <summary>Data e hora da extração, gravadas pelo script do snapshot (CA 3).</summary>
    Task<DateTimeOffset?> TakenAtAsync(CancellationToken cancellationToken = default);

    /// <summary>Valor do <c>MAIN_INFO_SIREN</c> (entidade 1), ou null se não existe.</summary>
    Task<string?> CompanyDocumentAsync(CancellationToken cancellationToken = default);

    /// <summary>Linhas da tabela, ordenadas pela chave, com os valores convertidos por <see cref="DolibarrConvert"/>.</summary>
    IAsyncEnumerable<SourceRow> ReadAsync(SnapshotTable table, CancellationToken cancellationToken = default);
}

/// <summary>Abre a fonte a partir do arquivo de secret com a conexão do MariaDB temporário (CA 5: nunca variável de ambiente nem argumento).</summary>
public interface ISnapshotSourceFactory
{
    Task<ISnapshotSource> OpenAsync(string connectionSecretFile, CancellationToken cancellationToken = default);
}
