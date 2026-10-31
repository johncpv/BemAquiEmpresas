using BemAqui.BuildingBlocks.Web;
using BemAqui.Fiscal.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Fiscal.Endpoints;

/// <summary>
/// Configurações → Integrações → Emissor fiscal (FIS-01 CA 7 a 10 e 18): o módulo <c>fiscal</c> liberado (403 <c>module_not_released</c>);
/// ver com <c>settings.view</c>; salvar (com 2FA de novo) e testar com <c>settings.edit</c>, que o ADR 0008 reserva ao Proprietário; o
/// teste tem a política de rate limit <c>fiscal-integration</c> (429 com <c>Retry-After</c>). Respostas sem cache: nada de segredo volta,
/// mas a situação da integração não fica guardada no navegador.
/// </summary>
internal static class FiscalEndpoints
{
    public static IEndpointRouteBuilder MapFiscalEndpoints(this IEndpointRouteBuilder api)
    {
        var integration = api.MapGroup("/fiscal/integration").WithTags("Fiscal").RequireModule("fiscal");

        integration.MapGet(string.Empty, async (FiscalIntegrationService service, HttpContext http, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return Results.Ok(await service.GetAsync(ct));
            })
            .RequirePermission("settings.view").WithName("GetFiscalIntegration").Produces<FiscalIntegrationView>();

        integration.MapPut(string.Empty, async (SaveFiscalIntegrationRequest request, FiscalIntegrationService service, HttpContext http, CancellationToken ct) =>
                (await service.SaveAsync(request, ct)).ToHttpResult(view =>
                {
                    http.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(view);
                }))
            .RequirePermission("settings.edit").WithName("SaveFiscalIntegration").Produces<FiscalIntegrationView>();

        integration.MapPost("/test", async (FiscalIntegrationService service, HttpContext http, CancellationToken ct) =>
                (await service.TestAsync(ct)).ToHttpResult(test =>
                {
                    http.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(test);
                }))
            .RequirePermission("settings.edit").RequireRateLimiting(FiscalIntegrationService.TestRateLimitPolicy)
            .WithName("TestFiscalIntegration").Produces<FiscalConnectionTest>();

        api.MapFiscalSettingsEndpoints();
        api.MapFiscalTaxRuleEndpoints();
        return api;
    }
}
