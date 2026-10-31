using BemAqui.BuildingBlocks.Web;
using BemAqui.Fiscal.Application.TaxRules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Fiscal.Endpoints;

/// <summary>
/// Fiscal → Regras fiscais (FIS-02b CA 2 a 5, 10 e 12): o módulo <c>fiscal</c> liberado; ver e simular com <c>fiscal.view</c> (simular com a
/// política <c>fiscal-simulate</c>); cadastrar e editar com <c>fiscal.edit</c>; ativar, inativar e marcar como conferida com
/// <c>fiscal.approve</c>. A versão do caminho tem de ser da regra do caminho (IDOR, CA 12). Respostas sem cache.
/// </summary>
internal static class FiscalTaxRuleEndpoints
{
    public static IEndpointRouteBuilder MapFiscalTaxRuleEndpoints(this IEndpointRouteBuilder api)
    {
        var rules = api.MapGroup("/fiscal/tax-rules").WithTags("Fiscal").RequireModule("fiscal")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        rules.MapGet(string.Empty, async (string? filter, TaxRuleService service, CancellationToken ct) => Results.Ok(await service.ListAsync(filter, ct)))
            .RequirePermission("fiscal.view").WithName("ListTaxRules").Produces<TaxRuleList>();
        rules.MapPost(string.Empty, async (TaxRuleValues values, TaxRuleService service, CancellationToken ct) => (await service.CreateAsync(values, ct)).ToHttpResult())
            .RequirePermission("fiscal.edit").WithName("CreateTaxRule").Produces<TaxRuleView>();
        rules.MapPost("/simulate", async (SimulateTaxRequest request, TaxSimulationService service, CancellationToken ct) => (await service.SimulateAsync(request, ct)).ToHttpResult())
            .RequirePermission("fiscal.view").RequireRateLimiting(TaxSimulationService.RateLimitPolicy).WithName("SimulateTaxes").Produces<TaxSimulationView>();
        rules.MapGet("/{id:guid}", async (Guid id, TaxRuleService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("fiscal.view").WithName("GetTaxRule").Produces<TaxRuleView>();
        rules.MapPut("/{id:guid}", async (Guid id, TaxRuleValues values, TaxRuleService service, CancellationToken ct) => (await service.UpdateAsync(id, values, ct)).ToHttpResult())
            .RequirePermission("fiscal.edit").WithName("UpdateTaxRule").Produces<TaxRuleView>();
        rules.MapGet("/{id:guid}/versions/{versionId:guid}", async (Guid id, Guid versionId, TaxRuleService service, CancellationToken ct) => (await service.GetVersionAsync(id, versionId, ct)).ToHttpResult())
            .RequirePermission("fiscal.view").WithName("GetTaxRuleVersion").Produces<TaxRuleView>();
        rules.MapPost("/{id:guid}/versions/{versionId:guid}/activate", async (Guid id, Guid versionId, TaxRuleService service, CancellationToken ct) => (await service.ActivateAsync(id, versionId, ct)).ToHttpResult())
            .RequirePermission("fiscal.approve").WithName("ActivateTaxRule").Produces<TaxRuleView>();
        rules.MapPost("/{id:guid}/versions/{versionId:guid}/deactivate", async (Guid id, Guid versionId, DeactivateTaxRuleRequest request, TaxRuleService service, CancellationToken ct) =>
                (await service.DeactivateAsync(id, versionId, request, ct)).ToHttpResult())
            .RequirePermission("fiscal.approve").WithName("DeactivateTaxRule").Produces<TaxRuleView>();
        rules.MapPost("/{id:guid}/versions/{versionId:guid}/confirm", async (Guid id, Guid versionId, TaxRuleService service, CancellationToken ct) => (await service.ConfirmAsync(id, versionId, ct)).ToHttpResult())
            .RequirePermission("fiscal.approve").WithName("ConfirmTaxRule").Produces<TaxRuleView>();
        return api;
    }
}
