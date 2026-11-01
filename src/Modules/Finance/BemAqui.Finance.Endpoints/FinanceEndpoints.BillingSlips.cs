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

        // Régua de cobrança (FIN-09 CA 6, 8 e 9): lembretes e pausa do título (IDOR: só pelo id da URL), ver com finance.view, pausar e
        // retomar com finance.edit.
        receivables.MapGet("/{id:guid}/collection", async (Guid id, CollectionService service, CancellationToken ct) => (await service.ForReceivableAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetReceivableCollection").Produces<ReceivableCollectionView>();
        receivables.MapPost("/{id:guid}/collection-pause", async (Guid id, CollectionPauseInput input, CollectionService service, CancellationToken ct) => (await service.PauseReceivableAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("PauseReceivableCollection").Produces<ReceivableCollectionView>();
        receivables.MapDelete("/{id:guid}/collection-pause", async (Guid id, CollectionService service, CancellationToken ct) => (await service.ResumeReceivableAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ResumeReceivableCollection").Produces<ReceivableCollectionView>();
    }

    /// <summary>Lembretes e pausa da cobrança do cliente (FIN-09 CA 6 e 8), em <c>/finance/parties/{id}</c>.</summary>
    private static void MapPartyCollection(RouteGroupBuilder parties)
    {
        parties.MapGet("/{id:guid}/collection", async (Guid id, CollectionService service, CancellationToken ct) => Results.Ok(await service.ForPartyAsync(id, ct)))
            .RequirePermission("finance.view").WithName("GetPartyCollection").Produces<PartyCollectionView>();
        parties.MapPost("/{id:guid}/collection-pause", async (Guid id, CollectionPauseInput input, CollectionService service, CancellationToken ct) => (await service.PausePartyAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("PausePartyCollection").Produces<PartyCollectionView>();
        parties.MapDelete("/{id:guid}/collection-pause", async (Guid id, CollectionService service, CancellationToken ct) => (await service.ResumePartyAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ResumePartyCollection").Produces<PartyCollectionView>();
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
        // Régua de cobrança (FIN-09 CA 1 e 2): ler com settings.view ou finance.view; salvar e ver a prévia com settings.edit.
        billing.MapGet("/collection-rule", async (CollectionRuleService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .RequireAnyPermissionOf("settings.view", "finance.view").WithName("GetCollectionRule").Produces<CollectionRuleView>();
        billing.MapPut("/collection-rule", async (CollectionRuleInput input, CollectionRuleService service, CancellationToken ct) => (await service.SaveAsync(input, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("SaveCollectionRule").Produces<CollectionRuleView>();
        billing.MapPost("/collection-rule/preview", async (CollectionPreviewInput input, CollectionRuleService service, CancellationToken ct) => (await service.PreviewAsync(input, ct)).ToHttpResult())
            .RequireAnyPermissionOf("settings.view", "finance.view").WithName("PreviewCollectionReminder").Produces<CollectionPreview>();
        return api;
    }
}
