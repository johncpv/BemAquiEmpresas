using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Lookups;

/// <summary>
/// Código IBGE do município (7 dígitos) pelo nome e pela UF, para o endereço que chegou sem ele (importação, API, digitado): a NF-e
/// exige o código. Sem resposta do provedor ou sem município com esse nome na UF, devolve null e quem chamou segue sem o código.
/// </summary>
public interface IIbgeLookup
{
    Task<string?> FindAsync(string? state, string? city, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lista de municípios da UF pela BrasilAPI (<c>/api/ibge/municipios/v1/{uf}</c>), em cache em memória pelos dias da consulta pública
/// (dado público, sem dado pessoal). A comparação ignora acento, caixa, hífen, apóstrofo e espaços repetidos.
/// </summary>
public sealed class BrasilApiIbgeLookup(PublicDataHttp http, IMemoryCache cache, IOptions<LookupOptions> options) : IIbgeLookup
{
    public const string Integration = BrasilApiCepLookup.Integration;

    public async Task<string?> FindAsync(string? state, string? city, CancellationToken cancellationToken = default)
    {
        var uf = (state ?? string.Empty).Trim().ToUpperInvariant();
        if (uf.Length != 2 || !uf.All(char.IsAsciiLetterUpper) || string.IsNullOrWhiteSpace(city))
        {
            return null;
        }

        if (!cache.TryGetValue("ibge:" + uf, out Dictionary<string, string>? cities) || cities is null)
        {
            var (status, json) = await http.GetAsync("api/ibge/municipios/v1/" + uf, Integration, cancellationToken);
            using (json)
            {
                if (status != LookupStatus.Found || json?.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                cities = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var item in json.RootElement.EnumerateArray())
                {
                    if (PublicJson.Text(item, "nome") is { } name && PublicJson.Text(item, "codigo_ibge") is { Length: 7 } code && code.All(char.IsAsciiDigit))
                    {
                        cities[CityKey(name)] = code;
                    }
                }

                cache.Set("ibge:" + uf, cities, TimeSpan.FromDays(options.Value.CacheDays));
            }
        }

        return cities.GetValueOrDefault(CityKey(city));
    }

    /// <summary>
    /// Chave de comparação do nome do município: maiúsculas sem acento, só letras e dígitos, separados por um espaço
    /// ("São José dos Campos" = "SAO JOSE  DOS CAMPOS"; "Embu-Guaçu" = "Embu Guacu"; "Pingo-d'Água" = "PINGO D AGUA").
    /// </summary>
    public static string CityKey(string name)
    {
        var folded = PublicJson.Fold(name);
        var key = new StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            if (char.IsLetterOrDigit(c))
            {
                key.Append(c);
            }
            else if (key.Length > 0 && key[^1] != ' ')
            {
                key.Append(' ');
            }
        }

        return key.ToString().TrimEnd();
    }
}
