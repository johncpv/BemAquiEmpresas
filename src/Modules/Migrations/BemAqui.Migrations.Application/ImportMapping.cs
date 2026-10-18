using System.Text.Json;
using BemAqui.SharedKernel;

namespace BemAqui.Migrations.Application;

/// <summary>
/// Arquivo de de-para (<c>--mapping</c>, ADR 0017): tabelas nomeadas de "valor no Dolibarr" para "valor no BemAqui"
/// (unidades, tipos de item, papéis especiais, condições de pagamento), para o PO ajustar sem mudar código.
/// Formato: <c>{ "maps": { "units": { "1": "UN" } } }</c>. Propriedade desconhecida ou valor vazio recusa o arquivo.
/// </summary>
public sealed class ImportMapping
{
    public static readonly ImportMapping Empty = new(new Dictionary<string, IReadOnlyDictionary<string, string>>());

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _maps;

    private ImportMapping(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> maps) => _maps = maps;

    public IReadOnlyCollection<string> Names => _maps.Keys.ToList();

    /// <summary>Valor do de-para, ou null quando o mapa ou a chave não existem.</summary>
    public string? Get(string map, string sourceValue) =>
        _maps.TryGetValue(map, out var values) && values.TryGetValue(sourceValue, out var target) ? target : null;

    public IReadOnlyDictionary<string, string> Map(string map) =>
        _maps.TryGetValue(map, out var values) ? values : new Dictionary<string, string>();

    public static Result<ImportMapping> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Invalid("o arquivo precisa ser um objeto JSON.");
            }

            var maps = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name != "maps" || property.Value.ValueKind != JsonValueKind.Object)
                {
                    return Invalid("propriedade desconhecida \"" + property.Name + "\". Use só \"maps\".");
                }

                foreach (var map in property.Value.EnumerateObject())
                {
                    if (map.Value.ValueKind != JsonValueKind.Object)
                    {
                        return Invalid("o mapa \"" + map.Name + "\" precisa ser um objeto de texto para texto.");
                    }

                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var entry in map.Value.EnumerateObject())
                    {
                        if (entry.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.Value.GetString()))
                        {
                            return Invalid("o valor de \"" + map.Name + "." + entry.Name + "\" precisa ser um texto não vazio.");
                        }

                        values[entry.Name] = entry.Value.GetString()!;
                    }

                    maps[map.Name] = values;
                }
            }

            return new ImportMapping(maps);
        }
        catch (JsonException ex)
        {
            return Invalid("JSON inválido (linha " + (ex.LineNumber + 1) + ").");
        }
    }

    private static Result<ImportMapping> Invalid(string reason) => Error.Validation("migration.mapping_invalid", "Arquivo de de-para inválido: " + reason);
}
