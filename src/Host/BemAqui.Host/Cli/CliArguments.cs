namespace BemAqui.Host.Cli;

/// <summary>Leitura simples de <c>--opcao valor</c> e <c>--flag</c> (sem dependência externa).</summary>
public sealed class CliArguments
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public CliArguments(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Count; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                Positional.Add(args[i]);
                continue;
            }

            var name = args[i][2..];
            var hasValue = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            _options[name] = hasValue ? args[++i] : null;
        }
    }

    public List<string> Positional { get; } = [];

    public string? Get(string name) => _options.GetValueOrDefault(name);

    public bool Has(string name) => _options.ContainsKey(name);
}
