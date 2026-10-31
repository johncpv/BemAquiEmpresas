using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>
/// Aviso de pagamento do Inter (BNK-02 CA 1 a 4, 9, 11 e 16). Configurações → Integrações → Banco Inter → conta → "Aviso de pagamento": módulos
/// <c>settings</c> e <c>finance</c>; ver com <c>settings.view</c> ou <c>finance.view</c>; ativar e trocar com <c>settings.edit</c> e o 2FA
/// de novo; respostas <c>no-store</c> (a URL completa sai uma única vez). "Pagamentos a conferir" fica em <c>/finance/billing-slips</c>:
/// listar com <c>finance.view</c>, abrir e conferir com <c>finance.edit</c> (desconto acima do limite, <c>finance.approve</c> no serviço).
/// </summary>
internal static partial class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapPaymentNoticeEndpoints(this IEndpointRouteBuilder api)
    {
        var settings = api.MapGroup("/settings/integrations/inter/{bankAccountId:guid}/payment-notice").RequireModule("settings").RequireModule("finance").WithTags("Configurações")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        settings.MapGet(string.Empty, async (Guid bankAccountId, InterWebhookService service, CancellationToken ct) => (await service.GetAsync(bankAccountId, ct)).ToHttpResult())
            .RequireAnyPermissionOf("settings.view", "finance.view").WithName("GetPaymentNotice").Produces<PaymentNoticeView>();
        settings.MapPost(string.Empty, async (Guid bankAccountId, InterWebhookService service, CancellationToken ct) => (await service.ActivateAsync(bankAccountId, rotate: false, ct)).ToHttpResult())
            .RequirePermission("settings.edit").RequireRecentTwoFactor().WithName("ActivatePaymentNotice").Produces<PaymentNoticeActivation>();
        settings.MapPost("/rotate", async (Guid bankAccountId, InterWebhookService service, CancellationToken ct) => (await service.ActivateAsync(bankAccountId, rotate: true, ct)).ToHttpResult())
            .RequirePermission("settings.edit").RequireRecentTwoFactor().WithName("RotatePaymentNotice").Produces<PaymentNoticeActivation>();
        return api;
    }

    private static void MapPaymentReviews(RouteGroupBuilder slips)
    {
        slips.MapGet("/pending-review", async (BankPaymentService service, CancellationToken ct) => Results.Ok(await service.PendingAsync(ct)))
            .RequirePermission("finance.view").WithName("ListPaymentsToReview").Produces<PaymentReviewList>();
        slips.MapGet("/{slipId:guid}/review", async (Guid slipId, BankPaymentService service, CancellationToken ct) => (await service.DraftAsync(slipId, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("GetPaymentReview").Produces<PaymentReviewDraft>();
        slips.MapPost("/{slipId:guid}/review", async (Guid slipId, PaymentReviewInput input, BankPaymentService service, CancellationToken ct) => (await service.ReviewAsync(slipId, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ReviewPayment").Produces<PaymentReviewResult>();
    }
}
