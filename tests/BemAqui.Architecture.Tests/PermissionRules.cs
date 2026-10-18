using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Modules;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// FND-06 CA 3: todo <c>RequireModule("…")</c> e <c>RequirePermission("…")</c> do código usa módulo e ação do
/// <c>ModuleCatalog</c>, e permissão tem sempre dois níveis (<c>modulo.acao</c>), nunca <c>modulo.entidade.acao</c>.
/// </summary>
public static partial class PermissionRules
{
    [GeneratedRegex("""\.RequireModule\("([^"]*)"\)""")]
    private static partial Regex ModuleUse();

    [GeneratedRegex("""\.RequirePermission\("([^"]*)"\)""")]
    private static partial Regex PermissionUse();

    public static IReadOnlyList<string> Violations(IEnumerable<(string File, string Content)> sources) =>
        sources.SelectMany(s =>
                ModuleUse().Matches(s.Content).Select(m => m.Groups[1].Value)
                    .Where(code => ModuleCatalog.Find(code) is null)
                    .Select(code => $"{s.File}: RequireModule(\"{code}\") fora do ModuleCatalog")
                .Concat(PermissionUse().Matches(s.Content).Select(m => m.Groups[1].Value)
                    .Where(p => !ModuleCatalog.IsKnownPermission(p))
                    .Select(p => $"{s.File}: RequirePermission(\"{p}\") não é modulo.acao do ModuleCatalog")))
            .ToList();

    public static IEnumerable<(string File, string Content)> SolutionSources() =>
        Directory.GetFiles(Path.Combine(Solution.RootDirectory, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(Solution.RootDirectory, f), File.ReadAllText(f)));

    /// <summary>Consistência do catálogo (plano de testes da FND-06, unidade).</summary>
    public static IReadOnlyList<string> CatalogProblems()
    {
        var problems = new List<string>();
        problems.AddRange(ModuleCatalog.All.GroupBy(m => m.Code).Where(g => g.Count() > 1).Select(g => "código repetido: " + g.Key));
        foreach (var m in ModuleCatalog.All)
        {
            if (!ModuleCatalog.SnakeCase().IsMatch(m.Code)) problems.Add(m.Code + ": código fora do snake_case");
            if (!ModuleAreas.All.Contains(m.Area)) problems.Add(m.Code + ": área fora do dock (" + m.Area + ")");
            if (m.Standard.Any(a => !StandardActions.All.Contains(a))) problems.Add(m.Code + ": ação padrão desconhecida");
            if (m.Specific.Any(s => !ModuleCatalog.SnakeCase().IsMatch(s.Code) || StandardActions.All.Contains(s.Code) || string.IsNullOrWhiteSpace(s.Label) || string.IsNullOrWhiteSpace(s.Description)))
                problems.Add(m.Code + ": ação específica precisa de snake_case, rótulo e descrição, e não pode repetir uma padrão");
            if (m.Standard.Contains(StandardActions.Approve) != !string.IsNullOrWhiteSpace(m.ApproveMeaning))
                problems.Add(m.Code + ": approve e o significado de aprovar andam juntos");
            if (string.IsNullOrWhiteSpace(m.Name) || string.IsNullOrWhiteSpace(m.Description)) problems.Add(m.Code + ": nome ou descrição vazios");
        }

        return problems;
    }
}
