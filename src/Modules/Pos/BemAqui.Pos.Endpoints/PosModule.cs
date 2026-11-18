using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Pos.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Pos.Endpoints;

/// <summary>PDV (PDV-01): dono do schema <c>pos</c> e das rotas <c>/api/v1/pos/…</c> (02, seção 4.1; ADR 0048).</summary>
public sealed class PosModule : IModule
{
    public string Code => "pos";

    public Assembly? PersistenceAssembly => typeof(PosInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddPosInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPosEndpoints();
}
