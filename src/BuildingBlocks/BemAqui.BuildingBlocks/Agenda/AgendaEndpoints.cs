using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.BuildingBlocks.Agenda;

public static class AgendaEndpoints
{
    /// <summary>Agregador e opções (tempo limite por fonte). As fontes são registradas por cada módulo.</summary>
    public static IServiceCollection AddBemAquiAgenda(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AgendaOptions>(configuration.GetSection(AgendaOptions.Section));
        services.TryAddScoped<IAgendaService, AgendaService>();
        return services;
    }

    /// <summary>
    /// <c>GET /api/v1/me/agenda</c> (CA 1, 13, 15): rota do usuário, autenticada pela política padrão, sem <c>RequireModule</c> (cada
    /// fonte confere as próprias camadas) e fora da lista da sessão de PIN (ADR 0026). Nunca cacheada: <c>Cache-Control: no-store</c>.
    /// <paramref name="categories"/> opcional, separadas por vírgula (o "Tentar de novo" de uma categoria).
    /// </summary>
    public static IEndpointRouteBuilder MapAgendaEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/me/agenda", async (string? categories, IAgendaService agenda, HttpContext http, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                var filter = string.IsNullOrWhiteSpace(categories)
                    ? null
                    : categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
                return Results.Ok(await agenda.ForUserAsync(filter, ct));
            })
            .WithName("GetMyAgenda").WithTags("Minha pauta").Produces<AgendaView>();
        return api;
    }
}
