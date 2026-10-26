using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>Ordens de pagamento (FIN-03a): criar com <c>finance.create</c>, decidir com <c>finance.approve</c>, pagar e estornar com <c>finance.pay</c>.</summary>
internal static partial class FinanceEndpoints
{
    private static void MapPaymentOrders(RouteGroupBuilder orders)
    {
        orders.MapGet(string.Empty, async (string? situation, bool? awaitingMe, bool? selfApproved, int? page, int? pageSize, PaymentOrderService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new PaymentOrderListQuery(situation, awaitingMe == true, selfApproved == true), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("finance.view").WithName("ListPaymentOrders").Produces<PaymentOrderPage>();
        orders.MapGet("/{id:guid}", async (Guid id, PaymentOrderService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetPaymentOrder").Produces<PaymentOrderDetail>();
        orders.MapPost(string.Empty, async (PaymentOrderInput input, PaymentOrderService service, CancellationToken ct) =>
                (await service.SubmitAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/payment-orders/" + saved.Summary.Id, saved)))
            .RequirePermission("finance.create").RequireIdempotencyKey().WithName("SubmitPaymentOrder").Produces<PaymentOrderDetail>(StatusCodes.Status201Created);
        orders.MapPost("/{id:guid}/approve", async (Guid id, DecisionInput input, PaymentOrderService service, CancellationToken ct) => (await service.ApproveAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.approve").WithName("ApprovePaymentOrder").Produces<PaymentOrderDetail>();
        orders.MapPost("/{id:guid}/reject", async (Guid id, DecisionInput input, PaymentOrderService service, CancellationToken ct) => (await service.RejectAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.approve").WithName("RejectPaymentOrder").Produces<PaymentOrderDetail>();
        orders.MapPost("/{id:guid}/cancel", async (Guid id, ReasonInput input, PaymentOrderService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("CancelPaymentOrder").Produces<PaymentOrderDetail>();
        orders.MapPost("/{id:guid}/resubmit", async (Guid id, ResubmitInput input, PaymentOrderService service, CancellationToken ct) => (await service.ResubmitAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.create").WithName("ResubmitPaymentOrder").Produces<PaymentOrderDetail>();
        orders.MapPost("/{id:guid}/pay", async (Guid id, PayInput input, PaymentOrderService service, CancellationToken ct) => (await service.PayAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.pay").RequireRecentTwoFactor().RequireIdempotencyKey().WithName("PayPaymentOrder").Produces<PaymentResult>();
        // Pagar e estornar pedem o 2FA de novo (D6, P-19; BNK-01a CA 2): sem a prova recente, 403 two_factor_required e nada é gravado.
        orders.MapPost("/{id:guid}/reverse", async (Guid id, ReasonInput input, PaymentOrderService service, CancellationToken ct) => (await service.ReverseAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.pay").RequireRecentTwoFactor().WithName("ReversePaymentOrder").Produces<PaymentOrderDetail>();
    }
}
