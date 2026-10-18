using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.BuildingBlocks.Modules;

/// <summary>
/// Ponto de entrada de um módulo no Host: registra serviços e mapeia endpoints.
/// <see cref="Code"/> é o código do módulo no catálogo (<c>finance</c>, <c>inventory</c>...).
/// </summary>
public interface IModule
{
    string Code { get; }

    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>Assembly com as configurações EF do módulo (aplicadas no TenantDbContext; ADR 0006).</summary>
    System.Reflection.Assembly? PersistenceAssembly => null;
}
