using BemAqui.BuildingBlocks.Modules;
using Microsoft.AspNetCore.Builder;

namespace BemAqui.BuildingBlocks.Web;

/// <summary>
/// Marca de autoatendimento (ADR 0035 §1): endpoint de <c>/api/v1/me/*</c> do funcionário sobre os próprios dados, liberado pela camada 1
/// (<c>RequireModule</c>) e pelo filtro de vínculo do módulo, no lugar de <c>perm:</c>. O teste de autorização das rotas (D19) aceita a
/// rota só com esta marca, dentro de <c>/api/v1/me/</c> e na lista versionada.
/// </summary>
public sealed record SelfServiceMetadata(string ModuleCode);

public static class SelfServiceEndpoints
{
    public static TBuilder RequireSelfService<TBuilder>(this TBuilder builder, string moduleCode)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (ModuleCatalog.Find(moduleCode) is null)
        {
            throw new ArgumentException("Módulo fora do ModuleCatalog: " + moduleCode, nameof(moduleCode));
        }

        return builder.WithMetadata(new SelfServiceMetadata(moduleCode));
    }
}
