using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Catalog.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Catalog.Endpoints;

/// <summary>Catálogo de produtos (CAT-01): produto vendável, categoria, grade, códigos de barras e tabela de preço (schema <c>catalog_products</c>).</summary>
public sealed class CatalogModule : IModule
{
    public string Code => "catalog";

    public Assembly? PersistenceAssembly => typeof(CatalogInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddCatalogInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapCatalogEndpoints();
}
