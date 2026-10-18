using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BemAqui.Migrations.Application;
using BemAqui.Migrations.Application.Snapshot;

namespace BemAqui.Migrations.Infrastructure;

/// <summary>
/// Origem pela API REST do Dolibarr (ADR 0017, adendo 2): só GET, com a chave em arquivo de secret (CA 5). Cada tabela da
/// lista fixa é montada a partir do JSON, só com as colunas mapeadas e com os tipos do DDL fixo; o resto é descartado.
/// O CNPJ da empresa vem do arquivo de secret, porque a chave não lê <c>/setup/company</c> (CA 6).
/// </summary>
public sealed partial class DolibarrApiSnapshotSource : ISnapshotSource
{
    public const int PageSize = 500;

    private readonly HttpClient _http;
    private readonly string _companyDocument;
    private readonly DateTimeOffset _openedAt;
    private readonly Dictionary<string, List<JsonElement>> _cache = new(StringComparer.Ordinal);

    /// <param name="http">Cliente com <c>BaseAddress</c> terminando em <c>api/index.php/</c>.</param>
    public DolibarrApiSnapshotSource(HttpClient http, string apiKey, string companyDocument, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(clock);
        http.DefaultRequestHeaders.Remove("DOLAPIKEY");
        http.DefaultRequestHeaders.Add("DOLAPIKEY", apiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http = http;
        _companyDocument = companyDocument;
        _openedAt = clock.GetUtcNow();
    }

    /// <summary>Sem transação consistente na API: vale a hora em que a leitura começou.</summary>
    public Task<DateTimeOffset?> TakenAtAsync(CancellationToken cancellationToken = default) => Task.FromResult<DateTimeOffset?>(_openedAt);

    public Task<string?> CompanyDocumentAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(_companyDocument);

    public async IAsyncEnumerable<SourceRow> ReadAsync(SnapshotTable table, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        var rows = new List<Dictionary<string, object?>>();
        await foreach (var raw in RawAsync(table, cancellationToken))
        {
            rows.Add(Project(table, raw));
        }

        // Na ordem da chave, como o SELECT do snapshot (ids numéricos alinhados para "9" vir antes de "10").
        foreach (var values in rows.OrderBy(v => string.Join('-', Parts(table, v).Select(p => p.PadLeft(20, '0'))), StringComparer.Ordinal))
        {
            yield return new SourceRow(table, Key(table, values), values);
        }
    }

    private static string Key(SnapshotTable table, IReadOnlyDictionary<string, object?> values) => string.Join('-', Parts(table, values));

    private static IEnumerable<string> Parts(SnapshotTable table, IReadOnlyDictionary<string, object?> values) =>
        table.Key.Select(k => System.Convert.ToString(values.GetValueOrDefault(k), CultureInfo.InvariantCulture) ?? string.Empty);

    /// <summary>Converte cada coluna para o tipo do DDL fixo; o JSON do Dolibarr traz quase tudo como texto.</summary>
    internal static Dictionary<string, object?> Project(SnapshotTable table, IReadOnlyDictionary<string, JsonElement?> raw)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var column in table.Columns)
        {
            values[column.Name] = Convert(column, raw.GetValueOrDefault(column.Name));
        }

        return values;
    }

    private static object? Convert(SnapshotColumn column, JsonElement? element)
    {
        if (element is not { } e || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var text = e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return column.Type switch
        {
            "INT" or "TINYINT" or "SMALLINT" => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null,
            "FLOAT" or "DOUBLE" => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
            "DATETIME" => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) && unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : null,
            _ => DolibarrConvert.Text(text),
        };
    }
}
