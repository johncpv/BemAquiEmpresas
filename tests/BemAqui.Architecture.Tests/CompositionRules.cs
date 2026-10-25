using System.Text.RegularExpressions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// Regras do módulo de composição (ADR 0028 §1, DSH-01a CA 1), como funções puras para também provar que falham: só
/// Application e Endpoints, referências só ao BuildingBlocks, ao próprio módulo e aos <c>*.Contracts</c>, e nenhum
/// <c>DbSet</c>, Dapper, <c>TenantDbContext</c>, migration, configuração EF ou schema no código.
/// </summary>
public static partial class CompositionRules
{
    private static readonly string[] ForbiddenPackages = ["Microsoft.EntityFrameworkCore", "Npgsql", "Dapper"];

    private static readonly string[] AllowedReferences = ["BemAqui.BuildingBlocks", "BemAqui.SharedKernel"];

    [GeneratedRegex(@"\bDbSet<|\bDbContext\b|TenantDbContext|\bDapper\b|\bMigration\b|IEntityTypeConfiguration|\.ToTable\(|HasDefaultSchema|\bNpgsql|\bQueryAsync\b|\bExecuteSql")]
    private static partial Regex PersistenceUsage();

    public static IReadOnlyList<string> ProjectViolations(IEnumerable<ProjectInfo> projects, string module) =>
        projects.Where(p => p.Module == module).SelectMany(p =>
        {
            var problems = new List<string>();
            if (!Solution.CompositionLayers.Contains(p.Layer))
            {
                problems.Add($"{p.Name}: módulo de composição só tem Application e Endpoints");
            }

            problems.AddRange(p.PackageReferences.Where(pkg => ForbiddenPackages.Any(f => pkg.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                .Select(pkg => $"{p.Name} usa {pkg} (composição não acessa banco)"));
            problems.AddRange(p.ProjectReferences.Where(r => !AllowedReferences.Contains(r, StringComparer.Ordinal))
                .Where(r => Solution.Classify(r) is var (target, layer) && !(target == module || layer == "Contracts"))
                .Select(r => $"{p.Name} referencia {r} (composição só referencia *.Contracts e o BuildingBlocks)"));
            return problems;
        }).ToList();

    public static IReadOnlyList<string> SourceViolations(IEnumerable<(string File, string Text)> sources) =>
        sources.SelectMany(s => PersistenceUsage().Matches(s.Text).Select(m => $"{s.File} usa {m.Value} (composição não tem tabela nem consulta própria)")).ToList();

    public static IEnumerable<(string File, string Text)> ModuleSources(string module)
    {
        var root = Path.Combine(Solution.RootDirectory, "src", "Modules", module);
        var sep = Path.DirectorySeparatorChar;
        return Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".csproj", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(root, f).Replace(sep, '/'), File.ReadAllText(f)));
    }
}
