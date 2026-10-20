namespace BemAqui.Architecture.Tests;

/// <summary>
/// Regras de arquitetura como funções puras sobre os projetos (ADR 0006), para
/// poderem ser testadas também com projetos fictícios (prova de que falham).
/// </summary>
public static class Rules
{
    private static readonly string[] ForbiddenLibraries = ["MediatR", "AutoMapper"];

    private static readonly string[] DomainForbiddenPackages = ["Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore"];

    /// <summary>Módulo só referencia <c>*.Contracts</c> de outro módulo.</summary>
    public static IReadOnlyList<string> CrossModuleReferences(IEnumerable<ProjectInfo> projects) =>
        projects
            .Where(p => p.Module is not null)
            .SelectMany(p => p.ProjectReferences.Select(r => (Project: p, Target: Solution.Classify(r), Reference: r)))
            .Where(x => x.Target.Module is not null && x.Target.Module != x.Project.Module && x.Target.Layer != "Contracts")
            .Select(x => $"{x.Project.Name} referencia {x.Reference} (outro módulo só via *.Contracts)")
            .ToList();

    /// <summary>Contracts de um módulo não referencia camadas internas de nenhum módulo.</summary>
    public static IReadOnlyList<string> ContractsReferences(IEnumerable<ProjectInfo> projects) =>
        projects
            .Where(p => p.Layer == "Contracts")
            .SelectMany(p => p.ProjectReferences.Select(r => (Project: p, Target: Solution.Classify(r), Reference: r)))
            .Where(x => x.Target.Module is not null && x.Target.Layer != "Contracts")
            .Select(x => $"{x.Project.Name} referencia {x.Reference} (Contracts só depende de Contracts e SharedKernel)")
            .ToList();

    /// <summary>Contracts não referencia o Contracts de outro módulo; vocabulário comum fica no SharedKernel (ADR 0019).</summary>
    public static IReadOnlyList<string> ContractsCrossReferences(IEnumerable<ProjectInfo> projects) =>
        projects
            .Where(p => p.Layer == "Contracts")
            .SelectMany(p => p.ProjectReferences.Select(r => (Project: p, Target: Solution.Classify(r), Reference: r)))
            .Where(x => x.Target.Module is not null && x.Target.Module != x.Project.Module && x.Target.Layer == "Contracts")
            .Select(x => $"{x.Project.Name} referencia {x.Reference} (Contracts não depende de outro Contracts)")
            .ToList();

    /// <summary>Nenhum projeto usa MediatR ou AutoMapper (licença comercial).</summary>
    public static IReadOnlyList<string> ForbiddenPackages(IEnumerable<ProjectInfo> projects) =>
        projects
            .SelectMany(p => p.PackageReferences.Where(IsForbiddenLibrary).Select(pkg => $"{p.Name} usa {pkg} (proibido: licença comercial)"))
            .ToList();

    /// <summary>Domain não depende de EF Core, ASP.NET Core nem Npgsql.</summary>
    public static IReadOnlyList<string> DomainInfrastructure(IEnumerable<ProjectInfo> projects) =>
        projects
            .Where(p => p.Layer == "Domain")
            .SelectMany(p =>
                p.PackageReferences.Where(pkg => DomainForbiddenPackages.Any(f => pkg.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                    .Concat(p.FrameworkReferences.Where(f => f.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase)))
                    .Select(dep => $"{p.Name} depende de {dep} (Domain sem infraestrutura)"))
            .ToList();

    public static bool IsForbiddenLibrary(string name) =>
        ForbiddenLibraries.Any(f => name.Equals(f, StringComparison.OrdinalIgnoreCase) || name.StartsWith(f + ".", StringComparison.OrdinalIgnoreCase));
}
