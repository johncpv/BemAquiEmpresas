using System.Text.RegularExpressions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// FND-03a CA 13 / CLAUDE.md: migration destrutiva (remove ou renomeia tabela, coluna ou schema) só com ADR
/// referenciada no próprio arquivo (expand/contract em duas versões).
/// </summary>
public static partial class MigrationRules
{
    [GeneratedRegex(@"migrationBuilder\.(DropTable|DropColumn|RenameColumn|RenameTable|DropSchema)\(")]
    private static partial Regex Destructive();

    [GeneratedRegex(@"ADR\s*\d{4}")]
    private static partial Regex AdrReference();

    public static IReadOnlyList<string> DestructiveWithoutAdr(IEnumerable<(string File, string Content)> migrations) =>
        migrations
            .Select(m => (m.File, Up: UpBody(m.Content), m.Content))
            .Where(m => Destructive().IsMatch(m.Up) && !AdrReference().IsMatch(m.Content))
            .Select(m => $"{m.File}: migration destrutiva sem ADR referenciada (expand/contract)")
            .ToList();

    public static IEnumerable<(string File, string Content)> SolutionMigrations() =>
        Directory.GetFiles(Path.Combine(Solution.RootDirectory, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal) && !f.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(Solution.RootDirectory, f), File.ReadAllText(f)));

    private static string UpBody(string content)
    {
        var up = content.IndexOf("void Up(", StringComparison.Ordinal);
        if (up < 0)
        {
            return string.Empty;
        }

        var down = content.IndexOf("void Down(", up, StringComparison.Ordinal);
        return down < 0 ? content[up..] : content[up..down];
    }
}
