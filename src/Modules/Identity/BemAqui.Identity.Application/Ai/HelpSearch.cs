using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Help;

namespace BemAqui.Identity.Application.Ai;

/// <summary>Uma seção de ajuda encontrada, com a ajuda de origem.</summary>
public sealed record HelpHit(HelpDocument Document, HelpSection Section);

/// <summary>
/// Busca lexical em português nas seções de ajuda (FND-15 CA 1, mesmo método do NSHome, ADR 0014): termo no texto vale 1,
/// no título da ajuda ou da seção vale mais 2, e a ajuda da tela atual ganha mais 2. Sem acento, sem palavras vazias.
/// </summary>
public static partial class HelpSearch
{
    public const int MaxSections = 6;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "o", "as", "os", "de", "da", "do", "das", "dos", "e", "em", "no", "na", "nos", "nas", "um", "uma", "uns", "umas", "para", "por",
        "com", "que", "como", "qual", "quais", "onde", "quando", "eu", "se", "ou", "ao", "aos", "faz", "fazer", "isso", "essa", "esse", "esta",
        "este", "tela", "posso", "faco", "fica", "existe", "pra", "pro", "meu", "minha", "sistema", "bemaqui", "consigo", "preciso", "quero", "tem", "ter", "sao", "nao",
    };

    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static IReadOnlyList<string> Terms(string text) =>
        NonTerm().Split(Normalize(text)).Where(t => t.Length > 2 && !Stopwords.Contains(t)).Distinct().ToList();

    /// <summary>As seções mais próximas entre as ajudas recebidas (já filtradas pela permissão, R-AJD-6).</summary>
    public static IReadOnlyList<HelpHit> Find(IEnumerable<HelpDocument> documents, string question, string? currentKey)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var terms = Terms(question);
        if (terms.Count == 0)
        {
            return [];
        }

        return documents
            .SelectMany(d => d.Sections.Select((s, i) => (Hit: new HelpHit(d, s), Order: i)))
            .Select(x =>
            {
                var title = Normalize(x.Hit.Document.Title + " " + x.Hit.Section.Title);
                var text = title + " " + Normalize(x.Hit.Section.Body);
                var score = terms.Sum(t => (text.Contains(t, StringComparison.Ordinal) ? 1 : 0) + (title.Contains(t, StringComparison.Ordinal) ? 2 : 0));
                if (score > 0 && x.Hit.Document.Key == currentKey)
                {
                    score += 2;
                }

                return (x.Hit, x.Order, Score: score);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Hit.Document.Key == currentKey).ThenBy(x => x.Hit.Document.Key, StringComparer.Ordinal).ThenBy(x => x.Order)
            .Take(MaxSections)
            .Select(x => x.Hit)
            .ToList();
    }

    [GeneratedRegex(@"[^a-z0-9\-]+")]
    private static partial Regex NonTerm();
}
