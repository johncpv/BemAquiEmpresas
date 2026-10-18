using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BemAqui.Migrations.Application.Snapshot;

namespace BemAqui.Migrations.Infrastructure;

public sealed partial class DolibarrApiSnapshotSource
{
    private static JsonElement? Value(object? value) => value is null ? null : JsonSerializer.SerializeToElement(value);

    private static JsonElement? Field(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) ? value : null;

    /// <summary>Linhas da tabela como (coluna → valor do JSON). Tabelas sem endpoint próprio saem de outros objetos da API.</summary>
    private async IAsyncEnumerable<IReadOnlyDictionary<string, JsonElement?>> RawAsync(SnapshotTable table, [EnumeratorCancellation] CancellationToken ct)
    {
        if (DolibarrApiMap.Tables.TryGetValue(table.Name, out var api))
        {
            foreach (var item in await ListAsync(api.Endpoint, ct))
            {
                var row = api.Fields.ToDictionary(f => f.Key, f => Field(item, f.Value), StringComparer.Ordinal);
                foreach (var (column, constant) in api.Constants ?? new Dictionary<string, object?>())
                {
                    row[column] = Value(constant);
                }

                yield return row;
            }

            yield break;
        }

        switch (table.Name)
        {
            case "llx_c_typent":
                // Sem dicionário na API: os tipos usados saem dos próprios terceiros.
                foreach (var type in (await ListAsync("/thirdparties", ct)).Select(t => (Id: Field(t, "typent_id"), Code: Field(t, "typent_code")))
                    .Where(t => t.Id is { ValueKind: not JsonValueKind.Null }).DistinctBy(t => t.Id!.Value.ToString()))
                {
                    yield return new Dictionary<string, JsonElement?> { ["id"] = type.Id, ["code"] = type.Code };
                }

                break;
            case "llx_categorie":
                foreach (var (kind, code) in DolibarrApiMap.CategoryTypes)
                {
                    foreach (var c in await ListAsync("/categories?type=" + kind, ct))
                    {
                        yield return new Dictionary<string, JsonElement?> { ["rowid"] = Field(c, "id"), ["label"] = Field(c, "label"), ["type"] = Value(code), ["fk_parent"] = Field(c, "fk_parent"), ["entity"] = Field(c, "entity") };
                    }
                }

                break;
            case "llx_categorie_fournisseur":
            case "llx_categorie_product":
                var (apiType, owner) = table.Name == "llx_categorie_product" ? ("product", "fk_product") : ("supplier", "fk_soc");
                foreach (var c in await ListAsync("/categories?type=" + apiType, ct))
                {
                    var id = Field(c, "id")!.Value.ToString();
                    foreach (var o in await ListAsync("/categories/" + id + "/objects?type=" + apiType, ct))
                    {
                        yield return new Dictionary<string, JsonElement?> { ["fk_categorie"] = Field(c, "id"), [owner] = Field(o, "id") };
                    }
                }

                break;
            case "llx_product_extrafields":
                foreach (var p in await ListAsync("/products", ct))
                {
                    var options = Field(p, "array_options");
                    if (options is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("options_elev_garantia", out var warranty))
                    {
                        yield return new Dictionary<string, JsonElement?> { ["fk_object"] = Field(p, "id"), ["elev_garantia"] = warranty };
                    }
                }

                break;
            case "llx_product_fournisseur_price":
                foreach (var p in await ListAsync("/products", ct))
                {
                    var productId = Field(p, "id")!.Value.ToString();
                    foreach (var price in await ListAsync("/products/" + productId + "/purchase_prices", ct))
                    {
                        yield return new Dictionary<string, JsonElement?>
                        {
                            ["rowid"] = Field(price, "product_fourn_price_id") ?? Field(price, "id"), ["fk_product"] = Field(p, "id"),
                            ["fk_soc"] = Field(price, "fourn_id"), ["ref_fourn"] = Field(price, "ref_supplier") ?? Field(price, "fourn_ref"),
                            ["quantity"] = Field(price, "fourn_qty"), ["fk_unit"] = Field(price, "fk_unit"), ["entity"] = Value(1),
                        };
                    }
                }

                break;
            default:
                // llx_const: a chave da API não lê as constantes; o CNPJ vem do arquivo de secret.
                break;
        }
    }

    /// <summary>GET paginado (<c>limit</c> e <c>page</c>), com cache por endpoint. Lista vazia pode vir como 404 no Dolibarr.</summary>
    private async Task<IReadOnlyList<JsonElement>> ListAsync(string endpoint, CancellationToken ct)
    {
        if (_cache.TryGetValue(endpoint, out var cached))
        {
            return cached;
        }

        var all = new List<JsonElement>();
        for (var page = 0; ; page++)
        {
            var separator = endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var url = endpoint + separator + "limit=" + PageSize + "&page=" + page.ToString(CultureInfo.InvariantCulture);
            using var response = await _http.GetAsync(new Uri(url.TrimStart('/'), UriKind.Relative), ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                break;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("A API do Dolibarr recusou " + endpoint.Split('?')[0] + " (" + (int)response.StatusCode + "). Confira a permissão da chave.", null, response.StatusCode);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var items = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().Select(e => e.Clone()).ToList() : [];
            all.AddRange(items);
            if (items.Count < PageSize)
            {
                break;
            }
        }

        _cache[endpoint] = all;
        return all;
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
