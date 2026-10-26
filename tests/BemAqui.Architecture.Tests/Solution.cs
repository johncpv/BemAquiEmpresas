using System.Reflection;
using System.Xml.Linq;

namespace BemAqui.Architecture.Tests;

/// <summary>Projeto da solução lido do .csproj: caminho relativo, módulo e camada.</summary>
public sealed record ProjectInfo(string Name, string? Module, string Layer, IReadOnlyList<string> ProjectReferences, IReadOnlyList<string> PackageReferences, IReadOnlyList<string> FrameworkReferences);

/// <summary>Leitura da solução (arquivos .csproj e assemblies compilados) para as regras de arquitetura.</summary>
public static class Solution
{
    public static readonly string[] ModuleLayers = ["Domain", "Contracts", "Application", "Infrastructure", "Endpoints"];

    /// <summary>Módulos de composição (ADR 0028 §1): só compõem números dos donos pelos <c>*.Contracts</c>, sem tabela.</summary>
    public static readonly IReadOnlyList<string> CompositionModules = ["Dashboard"];

    public static readonly string[] CompositionLayers = ["Application", "Endpoints"];

    public static string RootDirectory { get; } = FindRoot();

    public static IReadOnlyList<string> ModuleNames { get; } =
        Directory.GetDirectories(Path.Combine(RootDirectory, "src", "Modules")).Select(Path.GetFileName).OfType<string>().Order().ToList();

    public static IReadOnlyList<ProjectInfo> Projects => LazyProjects.Value;

    /// <summary>Assemblies <c>BemAqui.*</c> de produção copiados para a saída dos testes (via referência ao Host).</summary>
    public static IReadOnlyList<Assembly> Assemblies => LazyAssemblies.Value;

    private static readonly Lazy<IReadOnlyList<ProjectInfo>> LazyProjects = new(LoadProjects);

    private static readonly Lazy<IReadOnlyList<Assembly>> LazyAssemblies = new(() =>
        Directory.GetFiles(AppContext.BaseDirectory, "BemAqui.*.dll")
            .Where(f => !Path.GetFileName(f).Contains(".Tests", StringComparison.Ordinal))
            .Select(f => Assembly.LoadFrom(f))
            .ToList());

    public static ProjectInfo Parse(string csprojPath)
    {
        var name = Path.GetFileNameWithoutExtension(csprojPath);
        var doc = XDocument.Load(csprojPath);
        var refs = doc.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
            .ToList();
        var packages = doc.Descendants("PackageReference").Select(e => (string?)e.Attribute("Include") ?? string.Empty).ToList();
        var frameworks = doc.Descendants("FrameworkReference").Select(e => (string?)e.Attribute("Include") ?? string.Empty).ToList();
        var (module, layer) = Classify(name);
        return new ProjectInfo(name, module, layer, refs, packages, frameworks);
    }

    /// <summary><c>BemAqui.Finance.Domain</c> → (Finance, Domain). Fora de módulo → (null, nome).</summary>
    public static (string? Module, string Layer) Classify(string projectName)
    {
        var parts = projectName.Split('.');
        if (parts.Length == 3 && parts[0] == "BemAqui" && ModuleLayers.Contains(parts[2]))
        {
            return (parts[1], parts[2]);
        }

        return (null, projectName);
    }

    private static List<ProjectInfo> LoadProjects() =>
        Directory.GetFiles(Path.Combine(RootDirectory, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Parse)
            .ToList();

    private static string FindRoot()
    {
        var root = typeof(Solution).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryRoot")?.Value;

        return root is not null && File.Exists(Path.Combine(root, "global.json"))
            ? root
            : throw new InvalidOperationException($"Raiz do repositório (global.json) não encontrada em '{root}'.");
    }
}
