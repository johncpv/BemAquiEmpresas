using System.Reflection;
using System.Text.RegularExpressions;

namespace BemAqui.BuildingBlocks.Help;

public sealed record HelpSection(string Title, string Body);

/// <summary>Uma ajuda de tela (<c>Ajuda/&lt;chave&gt;.md</c>, padrão NSHome, PRD §5.15a).</summary>
public sealed record HelpDocument(string Key, string Title, string Route, string? Tab, bool IsDefault, string? Module, IReadOnlyList<HelpSection> Sections);

/// <summary>
/// Catálogo das ajudas (FND-14): os <c>.md</c> de <c>Ajuda/</c>, na raiz do repositório, são embutidos no assembly do Host
/// e validados na subida e no CI. Cabeçalho entre <c>---</c>: titulo, rota, aba, padrao, modulo. Seções obrigatórias:
/// "Para que serve", "Como usar" e "Perguntas frequentes".
/// </summary>
public sealed partial class HelpCatalog
{
    public static readonly string[] RequiredSections = ["Para que serve", "Como usar", "Perguntas frequentes"];
    private static readonly string[] RequiredHeaders = ["titulo", "rota", "padrao"];

    public HelpCatalog(IEnumerable<HelpDocument> documents) => Documents = documents.OrderBy(d => d.Key, StringComparer.Ordinal).ToList();

    public IReadOnlyList<HelpDocument> Documents { get; }

    public HelpDocument? Find(string key) => Documents.FirstOrDefault(d => d.Key == key);

    /// <summary>Lê os recursos <c>Ajuda.&lt;chave&gt;.md</c> do assembly. Ajuda inválida derruba a subida (nunca vai para produção quebrada).</summary>
    public static HelpCatalog FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var documents = new List<HelpDocument>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Ajuda.", StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            documents.Add(Parse(name["Ajuda.".Length..^".md".Length], reader.ReadToEnd()));
        }

        return new HelpCatalog(documents);
    }

    /// <summary>Interpreta e valida uma ajuda (FND-14 CA 1). Lança com a lista do que falta.</summary>
    public static HelpDocument Parse(string key, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var problems = new List<string>();
        if (!KeyPattern().IsMatch(key))
        {
            problems.Add("chave fora do kebab-case");
        }

        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var body = text;
        var match = HeaderPattern().Match(text);
        if (match.Success)
        {
            foreach (var line in match.Groups[1].Value.Split('\n'))
            {
                var i = line.IndexOf(':', StringComparison.Ordinal);
                if (i > 0)
                {
                    header[line[..i].Trim()] = line[(i + 1)..].Trim();
                }
            }

            body = text[match.Length..];
        }

        problems.AddRange(RequiredHeaders.Where(h => string.IsNullOrWhiteSpace(header.GetValueOrDefault(h))).Select(h => "cabeçalho sem '" + h + "'"));
        if (!header.ContainsKey("aba") || !header.ContainsKey("modulo"))
        {
            problems.Add("cabeçalho precisa declarar 'aba' e 'modulo' (podem ficar vazios)");
        }

        var sections = new List<HelpSection>();
        string? current = null;
        var lines = new List<string>();
        void Flush()
        {
            if (current is not null)
            {
                sections.Add(new HelpSection(current, string.Join('\n', lines).Trim()));
            }
        }

        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                current = line[3..].Trim();
                lines.Clear();
            }
            else
            {
                lines.Add(line);
            }
        }

        Flush();
        problems.AddRange(RequiredSections.Where(r => !sections.Any(s => s.Title == r && s.Body.Length > 0)).Select(r => "seção '" + r + "' faltando ou vazia"));
        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"Ajuda '{key}' inválida: {string.Join("; ", problems)}.");
        }

        var padrao = header["padrao"];
        return new HelpDocument(
            key,
            header["titulo"],
            header["rota"],
            string.IsNullOrWhiteSpace(header["aba"]) ? null : header["aba"],
            padrao.Equals("true", StringComparison.OrdinalIgnoreCase) || padrao.Equals("sim", StringComparison.OrdinalIgnoreCase),
            string.IsNullOrWhiteSpace(header["modulo"]) ? null : header["modulo"],
            sections);
    }

    [GeneratedRegex("^---\n(.*?)\n---\n", RegexOptions.Singleline)]
    private static partial Regex HeaderPattern();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KeyPattern();
}
