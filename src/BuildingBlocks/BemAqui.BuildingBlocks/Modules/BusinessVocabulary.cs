using System.Text.RegularExpressions;

namespace BemAqui.BuildingBlocks.Modules;

/// <summary>
/// Vocabulário por perfil de negócio (PERF-03, ADR 0046 §6, PP-25): dicionário pequeno de termos de interface, declarado em código,
/// com teto de <see cref="MaxTerms"/> chaves. O tenant não edita termo. Vale só para rótulos de interface e ajuda: nome de
/// permissão, código de módulo e texto de documento fiscal não mudam.
/// </summary>
public static partial class BusinessVocabulary
{
    /// <summary>Teto de chaves (PP-25, decidido em 16/11/2026): o teste de unidade falha acima disso.</summary>
    public const int MaxTerms = 30;

    /// <summary>Rótulo da área do dock que agrupa Estoque e Compras: "Fábrica" na fábrica, "Retaguarda" nos demais perfis.</summary>
    public const string FactoryArea = "area.factory";

    /// <summary>
    /// Termos padrão: como o sistema fala hoje (o perfil Fábrica). Toda chave usada num perfil ou na ajuda tem de existir aqui;
    /// o teste de unidade e a validação da ajuda falham com a chave desconhecida.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [FactoryArea] = ModuleAreas.Factory,
    };

    /// <summary>Área do dock → chave do vocabulário que dá o rótulo dela.</summary>
    private static readonly (string Area, string Key)[] AreaKeys = [(ModuleAreas.Factory, FactoryArea)];

    /// <summary>Termos dos perfis que não são fábrica (Comércio, Serviços e E-commerce).</summary>
    internal static IReadOnlyDictionary<string, string> Backoffice { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [FactoryArea] = "Retaguarda",
    };

    /// <summary>Vocabulário do tenant pelos perfis ativos, só com o que difere do padrão (a Konebras recebe vazio).</summary>
    public static IReadOnlyDictionary<string, string> Resolve(IEnumerable<string> profiles) => Resolve(profiles, BusinessProfileCatalog.All);

    /// <summary>
    /// CA 6: com dois perfis que definem a mesma chave, vale a ordem fixa do catálogo (<c>factory</c>, <c>retail</c>, <c>services</c>,
    /// <c>ecommerce</c>), não a ordem em que os perfis foram aplicados. Como a Fábrica declara os termos padrão, tenant com
    /// <c>factory</c> vê sempre o padrão (CA 3). Sem perfil (rede de segurança da ADR 0046 §3), também o padrão.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Resolve(IEnumerable<string> profiles, IEnumerable<BusinessProfileDefinition> catalog)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(catalog);
        var active = profiles.ToHashSet(StringComparer.Ordinal);
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var profile in catalog.Where(p => active.Contains(p.Code)))
        {
            foreach (var (key, term) in profile.Vocabulary)
            {
                resolved.TryAdd(key, term);
            }
        }

        return resolved.Where(t => !Defaults.TryGetValue(t.Key, out var standard) || !string.Equals(standard, t.Value, StringComparison.Ordinal))
            .ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
    }

    /// <summary>Rótulos de área diferentes do padrão (área do <see cref="ModuleAreas"/> → rótulo), a partir do vocabulário resolvido.</summary>
    public static IReadOnlyDictionary<string, string> AreaLabels(IReadOnlyDictionary<string, string> vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        return AreaKeys.Where(a => vocabulary.ContainsKey(a.Key)).ToDictionary(a => a.Area, a => vocabulary[a.Key], StringComparer.Ordinal);
    }

    /// <summary>
    /// Troca as marcas <c>{{chave|Termo padrão}}</c> de um texto (ajuda) pelo termo do tenant ou, sem ele, pelo padrão escrito na
    /// marca. Padrão em minúscula deixa o termo em minúscula ("{{area.factory|fábrica}}" → "retaguarda").
    /// </summary>
    public static string Apply(string text, IReadOnlyDictionary<string, string> vocabulary)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(vocabulary);
        return !text.Contains("{{", StringComparison.Ordinal) ? text : TermPattern().Replace(text, m =>
        {
            var standard = m.Groups[2].Value;
            return vocabulary.TryGetValue(m.Groups[1].Value, out var term) ? MatchCase(term, standard) : standard;
        });
    }

    /// <summary>Chaves citadas em marcas <c>{{chave|...}}</c> que não existem no vocabulário (a ajuda com elas não sobe).</summary>
    public static IReadOnlyList<string> UnknownKeys(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TermPattern().Matches(text).Select(m => m.Groups[1].Value).Where(k => !Defaults.ContainsKey(k)).Distinct(StringComparer.Ordinal).ToList();
    }

    private static string MatchCase(string term, string standard) =>
        term.Length > 0 && standard.Length > 0 && char.IsLower(standard[0]) ? char.ToLowerInvariant(term[0]) + term[1..] : term;

    [GeneratedRegex(@"\{\{([a-z0-9_.]+)\|([^{}|]+)\}\}")]
    private static partial Regex TermPattern();
}
