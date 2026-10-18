using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Migrations.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Migrations.Endpoints;

/// <summary>
/// Importação do Dolibarr (MIG-01a, ADR 0017). Fora do <c>ModuleCatalog</c>: a execução é só pela CLI e a tela de
/// leitura fica em Configurações, com a permissão <c>settings.view</c>.
/// </summary>
public sealed class MigrationModule : IModule
{
    public string Code => "migration";

    public Assembly? PersistenceAssembly => typeof(MigrationInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddMigrationInfrastructure(configuration);

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapImportEndpoints();
}
