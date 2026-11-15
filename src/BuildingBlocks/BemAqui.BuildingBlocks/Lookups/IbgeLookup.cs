using System.Collections.Frozen;
using System.Text;

namespace BemAqui.BuildingBlocks.Lookups;

/// <summary>
/// Código IBGE do município (7 dígitos) pelo nome e pela UF, para o endereço que chegou sem ele (importação, API, digitado): a NF-e
/// exige o código. Sem município com esse nome na UF, devolve null e quem chamou segue sem o código.
/// </summary>
public interface IIbgeLookup
{
    Task<string?> FindAsync(string? state, string? city, CancellationToken cancellationToken = default);
}

/// <summary>
/// Tabela oficial de municípios do IBGE embutida no assembly (<c>Lookups/ibge-municipios.csv</c>, linhas <c>UF;código;nome</c>): a
/// consulta não sai do processo. A API do IBGE e o <c>/api/ibge</c> da BrasilAPI não respondem a servidor fora do Brasil (o da
/// produção fica nos EUA), e a tabela muda raramente (município novo é raro): atualizar o arquivo quando o IBGE publicar mudança.
/// A comparação ignora acento, caixa, hífen, apóstrofo e espaços repetidos.
/// </summary>
public sealed class EmbeddedIbgeLookup : IIbgeLookup
{
    private const string Resource = "BemAqui.BuildingBlocks.Lookups.ibge-municipios.csv";

    private static readonly Lazy<FrozenDictionary<string, string>> Cities = new(Load);

    /// <summary>Quantos municípios a tabela embutida tem (o teste confere que o arquivo veio inteiro).</summary>
    public static int Count => Cities.Value.Count;

    public Task<string?> FindAsync(string? state, string? city, CancellationToken cancellationToken = default) => Task.FromResult(Find(state, city));

    public static string? Find(string? state, string? city)
    {
        var uf = (state ?? string.Empty).Trim().ToUpperInvariant();
        if (uf.Length != 2 || !uf.All(char.IsAsciiLetterUpper) || string.IsNullOrWhiteSpace(city))
        {
            return null;
        }

        return Cities.Value.GetValueOrDefault(uf + ":" + CityKey(city));
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

    private static FrozenDictionary<string, string> Load()
    {
        using var stream = typeof(EmbeddedIbgeLookup).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException("Tabela de municípios do IBGE não encontrada no assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var cities = new Dictionary<string, string>(6000, StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(';', 3);
            if (parts.Length == 3 && parts[1].Length == 7)
            {
                cities[parts[0] + ":" + CityKey(parts[2])] = parts[1];
            }
        }

        return cities.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
