using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>
/// Boleto com Pix (BNK-01b CA 14 e 21): dentro de <c>/finance</c> (módulo <c>finance</c>, respostas <c>no-store</c>); ver com
/// <c>finance.view</c>; emitir, enviar, tentar de novo e cancelar com <c>finance.edit</c> (acima dos limites, <c>finance.approve</c>
/// no serviço). O boleto é sempre lido pelo título da URL (IDOR: de outro título, 404). Configurações → Cobrança fica em
/// <c>/settings/billing</c>: módulos <c>settings</c> e <c>finance</c>, ler com <c>settings.view</c> ou <c>finance.view</c>, gravar e
/// conferir com <c>settings.edit</c>.
/// </summary>
internal static partial class FinanceEndpoints
{
    private static void MapBillingSlips(RouteGroupBuilder receivables)
    {
        receivables.MapPost("/billing-slips/batch-preview", async (BillingBatchPreviewInput input, BillingSlipService service, CancellationToken ct) => (await service.BatchPreviewAsync(input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("PreviewBillingSlipBatch").Produces<BillingBatchPreview>();
        receivables.MapPost("/billing-slips/batch", async (BillingBatchInput input, BillingSlipService service, CancellationToken ct) => (await service.IssueBatchAsync(input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("IssueBillingSlipBatch").Produces<BillingBatchResult>();

        var slips = receivables.MapGroup("/{id:guid}/billing-slips");
        slips.MapGet(string.Empty, async (Guid id, BillingSlipService service, CancellationToken ct) => (await service.ListAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("ListBillingSlips").Produces<IReadOnlyList<BillingSlipView>>();
        slips.MapGet("/draft", async (Guid id, BillingSlipService service, CancellationToken ct) => (await service.DraftAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetBillingSlipDraft").Produces<BillingSlipDraft>();
        slips.MapPost(string.Empty, async (Guid id, BillingSlipInput input, BillingSlipService service, HttpContext http, CancellationToken ct) =>
                (await service.IssueAsync(id, input, Guid.TryParse(http.Request.Headers[IdempotencyFilter.Header].ToString(), out var key) ? key : null, ct))
                .ToHttpResult(saved => Results.Created($"/api/v1/finance/receivables/{id}/billing-slips/{saved.Slip.Id}", saved)))
            .RequirePermission("finance.edit").RequireIdempotencyKey().WithName("IssueBillingSlip").Produces<BillingSlipResult>(StatusCodes.Status201Created);
        slips.MapGet("/{slipId:guid}", async (Guid id, Guid slipId, BillingSlipService service, CancellationToken ct) => (await service.GetAsync(id, slipId, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetBillingSlip").Produces<BillingSlipView>();
        slips.MapPost("/{slipId:guid}/send", async (Guid id, Guid slipId, BillingSlipSendInput input, BillingSlipService service, CancellationToken ct) => (await service.SendAsync(id, slipId, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("SendBillingSlip").Produces<BillingSlipResult>();
        slips.MapPost("/{slipId:guid}/retry", async (Guid id, Guid slipId, BillingSlipService service, CancellationToken ct) => (await service.RetryAsync(id, slipId, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("RetryBillingSlip").Produces<BillingSlipResult>();
        slips.MapPost("/{slipId:guid}/cancel", async (Guid id, Guid slipId, BillingSlipCancelInput input, BillingSlipService service, CancellationToken ct) => (await service.CancelAsync(id, slipId, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("CancelBillingSlip").Produces<BillingSlipResult>();
    }

    public static IEndpointRouteBuilder MapBillingSettingsEndpoints(this IEndpointRouteBuilder api)
    {
        var settings = api.MapGroup("/settings/billing").RequireModule("settings").WithTags("Configurações")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        var billing = settings.MapGroup(string.Empty).RequireModule("finance");
        billing.MapGet(string.Empty, async (BillingSettingsService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .RequireAnyPermissionOf("settings.view", "finance.view").WithName("GetBillingSettings").Produces<BillingSettingsView>();
        billing.MapPut(string.Empty, async (BillingSettingsInput input, BillingSettingsService service, CancellationToken ct) => (await service.UpdateAsync(input, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("UpdateBillingSettings").Produces<BillingSettingsView>();
        billing.MapPost("/confirm", async (BillingSettingConfirmInput input, BillingSettingsService service, CancellationToken ct) => (await service.ConfirmAsync(input.Key, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("ConfirmBillingSetting").Produces<BillingSettingsView>();
        return api;
    }
}
