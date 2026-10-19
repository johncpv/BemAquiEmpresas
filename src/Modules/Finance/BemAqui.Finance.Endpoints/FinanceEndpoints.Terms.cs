using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

internal static partial class FinanceEndpoints
{
    private static void MapPaymentTerms(RouteGroupBuilder terms)
    {
        terms.MapGet(string.Empty, async (bool? includeInactive, int? page, int? pageSize, PaymentTermService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(includeInactive == true, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("finance.view").WithName("ListPaymentTerms").Produces<PagedList<PaymentTermDetail>>();
        terms.MapGet("/{id:guid}", async (Guid id, PaymentTermService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetPaymentTerm").Produces<PaymentTermDetail>();
        terms.MapPost("/preview", (SplitPreviewRequest request) => PaymentTermService.Preview(request).ToHttpResult())
            .RequirePermission("finance.view").WithName("PreviewPaymentTerm").Produces<IReadOnlyList<InstallmentDue>>();
        terms.MapPost(string.Empty, async (PaymentTermInput input, PaymentTermService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/payment-terms/" + saved.Id, saved)))
            .RequirePermission("finance.create").WithName("CreatePaymentTerm").Produces<PaymentTermDetail>(StatusCodes.Status201Created);
        terms.MapPut("/{id:guid}", async (Guid id, PaymentTermInput input, PaymentTermService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("UpdatePaymentTerm").Produces<PaymentTermDetail>();
        terms.MapPost("/{id:guid}/inactivate", async (Guid id, PaymentTermService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("InactivatePaymentTerm").Produces<PaymentTermDetail>();
        terms.MapPost("/{id:guid}/reactivate", async (Guid id, PaymentTermService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ReactivatePaymentTerm").Produces<PaymentTermDetail>();
        terms.MapDelete("/{id:guid}", async (Guid id, PaymentTermService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.delete").WithName("DeletePaymentTerm");
    }
}
