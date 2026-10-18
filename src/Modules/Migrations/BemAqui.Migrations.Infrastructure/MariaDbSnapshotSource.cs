using System.Data.Common;
using System.Runtime.CompilerServices;
using BemAqui.Migrations.Application;
using BemAqui.Migrations.Application.Snapshot;
using MySqlConnector;

namespace BemAqui.Migrations.Infrastructure;

/// <summary>
/// Leitura do snapshot restaurado no MariaDB temporário (ADR 0017, CA 4), com <c>MySqlConnector</c> (MIT). Só colunas
/// explícitas da lista fixa; o texto passa por <see cref="DolibarrConvert.Text"/> e data zerada do MySQL vira null.
/// </summary>
public sealed class MariaDbSnapshotSource : ISnapshotSource
{
    private readonly MySqlConnection _connection;

    private MariaDbSnapshotSource(MySqlConnection connection) => _connection = connection;

    public static async Task<MariaDbSnapshotSource> OpenAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            CharacterSet = "utf8mb4",
            ConvertZeroDateTime = true,
            Pooling = false,
        };
        var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return new MariaDbSnapshotSource(connection);
    }

    public async Task<DateTimeOffset?> TakenAtAsync(CancellationToken cancellationToken = default)
    {
        await using var command = new MySqlCommand("SELECT taken_at FROM bqe_snapshot ORDER BY taken_at DESC LIMIT 1", _connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DateTime dt ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)) : null;
    }

    public async Task<string?> CompanyDocumentAsync(CancellationToken cancellationToken = default)
    {
        await using var command = new MySqlCommand("SELECT `value` FROM `llx_const` WHERE `name` = 'MAIN_INFO_SIREN' AND `entity` IN (0, 1) ORDER BY `entity` DESC LIMIT 1", _connection);
        return DolibarrConvert.Text(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async IAsyncEnumerable<SourceRow> ReadAsync(SnapshotTable table, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        await using var command = new MySqlCommand(SnapshotScripts.Select(table), _connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < table.Columns.Count; i++)
            {
                values[table.Columns[i].Name] = Read(reader, i);
            }

            var id = string.Join('-', table.Key.Select(k => Convert.ToString(values[k], System.Globalization.CultureInfo.InvariantCulture)));
            yield return new SourceRow(table, id, values);
        }
    }

    private static object? Read(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal) switch
        {
            string s => DolibarrConvert.Text(s),
            byte[] bytes => DolibarrConvert.Text(bytes),
            DateTime dt when dt == DateTime.MinValue => null,
            var other => other,
        };
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

/// <summary>Conexão lida do arquivo de secret (CA 5): o caminho vem do <c>--snapshot</c>, o conteúdo nunca aparece em log nem em erro.</summary>
internal sealed class MariaDbSnapshotSourceFactory : ISnapshotSourceFactory
{
    public async Task<ISnapshotSource> OpenAsync(string connectionSecretFile, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(connectionSecretFile))
        {
            throw new FileNotFoundException("Arquivo de secret da conexão do snapshot não encontrado: " + connectionSecretFile);
        }

        var connectionString = (await File.ReadAllTextAsync(connectionSecretFile, cancellationToken)).Trim();
        try
        {
            return await MariaDbSnapshotSource.OpenAsync(connectionString, cancellationToken);
        }
        catch (Exception ex) when (ex is MySqlException or ArgumentException)
        {
            // Sem a mensagem original: ela pode trazer parte da conexão.
            throw new InvalidOperationException("Não foi possível abrir o snapshot no MariaDB temporário (" + ex.GetType().Name + "). Confira se o container está no ar e o arquivo de secret.");
        }
    }
}
