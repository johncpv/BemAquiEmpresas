using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

internal static partial class FinanceEndpoints
{
    /// <summary>Formas de pagamento e taxas da adquirente (PAG-01): só as ações que o <c>finance</c> já tem (view, create, edit, delete).</summary>
    private static void MapPaymentMethods(RouteGroupBuilder methods)
    {
        methods.MapGet(string.Empty, async (bool? includeInactive, int? page, int? pageSize, PaymentMethodService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(includeInactive == true, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("finance.view").WithName("ListPaymentMethods").Produces<PagedList<PaymentMethodDetail>>();
        methods.MapGet("/acquirers", async (string? q, PaymentMethodService service, CancellationToken ct) => Results.Ok(await service.AcquirersAsync(q, ct)))
            .RequirePermission("finance.view").WithName("SearchPaymentMethodAcquirers").Produces<IReadOnlyList<AcquirerChoice>>();
        methods.MapGet("/{id:guid}", async (Guid id, PaymentMethodService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetPaymentMethod").Produces<PaymentMethodDetail>();
        methods.MapPost(string.Empty, async (PaymentMethodRequest input, PaymentMethodService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/payment-methods/" + saved.Id, saved)))
            .RequirePermission("finance.create").WithName("CreatePaymentMethod").Produces<PaymentMethodDetail>(StatusCodes.Status201Created);
        methods.MapPost("/suggested", async (PaymentMethodService service, CancellationToken ct) => (await service.ApplySuggestedAsync(ct)).ToHttpResult())
            .RequirePermission("finance.create").WithName("ApplySuggestedPaymentMethods").Produces<IReadOnlyList<PaymentMethodDetail>>();
        methods.MapPut("/{id:guid}", async (Guid id, PaymentMethodRequest input, PaymentMethodService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("UpdatePaymentMethod").Produces<PaymentMethodDetail>();
        methods.MapPost("/{id:guid}/confirm", async (Guid id, PaymentMethodService service, CancellationToken ct) => (await service.ConfirmAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ConfirmPaymentMethod").Produces<PaymentMethodDetail>();
        methods.MapPost("/{id:guid}/inactivate", async (Guid id, PaymentMethodService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("InactivatePaymentMethod").Produces<PaymentMethodDetail>();
        methods.MapPost("/{id:guid}/reactivate", async (Guid id, PaymentMethodService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ReactivatePaymentMethod").Produces<PaymentMethodDetail>();
        methods.MapDelete("/{id:guid}", async (Guid id, PaymentMethodService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.delete").WithName("DeletePaymentMethod");
        methods.MapGet("/{id:guid}/fees", async (Guid id, PaymentMethodService service, CancellationToken ct) => (await service.FeesAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("ListAcquirerFees").Produces<IReadOnlyList<AcquirerFeeView>>();
        methods.MapPost("/{id:guid}/fees", async (Guid id, AcquirerFeeInput input, PaymentMethodService service, CancellationToken ct) => (await service.AddFeeAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("AddAcquirerFee").Produces<AcquirerFeeView>();
    }
}
