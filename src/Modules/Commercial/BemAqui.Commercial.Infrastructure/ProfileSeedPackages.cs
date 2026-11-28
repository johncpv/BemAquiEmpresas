using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Commercial.Infrastructure;

/// <summary>
/// Pacote do perfil Serviços nos Projetos (EXE-02 CA 11, PERF-04): os quatro modelos de etapas da proposta §6, pela <c>seed_key</c>. Não recria o
/// que o tenant excluiu, não muda o que ele editou e só dá o padrão à "Implantação de software" se o tenant ainda não tem padrão. A Fábrica não
/// tem pacote (CA 12).
/// </summary>
public sealed class ProjectsServicesSeedPackage : IProfileSeedPackage
{
    public string Key => BusinessProfileSeeds.ProjectsServices;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        await tenantScope.GetRequiredService<StageTemplateService>().SeedSuggestedAsync(cancellationToken);
    }
}
