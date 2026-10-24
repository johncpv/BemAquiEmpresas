using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Purchasing.Endpoints;

/// <summary>
/// Pedidos de compra (CMP-03a CA 1 a 23). Ver com <c>purchasing.view</c>; criar com <c>purchasing.create</c> (com
/// <c>Idempotency-Key</c>; a CMP-02 usa o mesmo POST com <c>rfqId</c>); editar e cancelar com <c>purchasing.edit</c>; enviar com
/// <c>purchasing.create</c> ou <c>purchasing.edit</c>; aprovar e reprovar com <c>purchasing.approve</c> (e dentro da alçada, no serviço).
/// A linha é sempre lida pelo pedido da URL (IDOR). Os anexos usam o dono <c>PurchaseOrder</c> nas rotas de anexos.
/// </summary>
internal static partial class PurchasingEndpoints
{
    private static readonly string[] OrderChildren = [nameof(PurchaseOrderLine)];

    private static void MapOrders(RouteGroupBuilder orders)
    {
        orders.MapGet(string.Empty, async (string? show, PurchaseOrderStatus? status, Guid? partyId, Guid? projectId, string? q, int? page, int? pageSize, PurchaseOrderService service,
                CancellationToken ct) => Results.Ok(await service.ListAsync(new PurchaseOrderListQuery(show, status, partyId, projectId, q), Page(page, pageSize), ct)))
            .RequirePermission("purchasing.view").WithName("ListPurchaseOrders").Produces<PagedList<PurchaseOrderSummary>>();
        orders.MapGet("/defaults", async ([FromQuery] Guid[]? requestLineIds, Guid? itemId, PurchaseOrderService service, CancellationToken ct) =>
                (await service.DefaultsAsync(requestLineIds, itemId, ct)).ToHttpResult())
            .RequirePermission("purchasing.create").WithName("GetPurchaseOrderDefaults").Produces<PurchaseOrderDefaults>();
        orders.MapGet("/{id:guid}", async (Guid id, PurchaseOrderService service, CancellationToken ct) => (await service.DetailAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetPurchaseOrder").Produces<PurchaseOrderDetail>();
        orders.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(PurchaseOrder), id.ToString(), OrderChildren, "orderId", Page(page, pageSize), ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetPurchaseOrderHistory").Produces<PagedList<RelatedAuditEntryDto>>();
        orders.MapGet("/{id:guid}/decisions", async (Guid id, PurchaseOrderService service, CancellationToken ct) => (await service.DecisionsAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetPurchaseOrderDecisions").Produces<PurchaseOrderDecisions>();
        orders.MapGet("/{id:guid}/payables", async (Guid id, PurchaseOrderService service, CancellationToken ct) => (await service.PayablesAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("ListPurchaseOrderPayables").Produces<IReadOnlyList<PurchaseOrderPayableView>>();
        orders.MapPost(string.Empty, async (PurchaseOrderInput input, PurchaseOrderService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/purchasing/purchase-orders/" + saved.Id, saved)))
            .RequirePermission("purchasing.create").RequireIdempotencyKey().WithName("CreatePurchaseOrder").Produces<PurchaseOrderDetail>(StatusCodes.Status201Created);
        orders.MapPut("/{id:guid}", async (Guid id, PurchaseOrderUpdateInput input, PurchaseOrderService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("UpdatePurchaseOrder").Produces<PurchaseOrderDetail>();
        orders.MapPut("/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, PurchaseOrderLineUpdate input, PurchaseOrderService service, CancellationToken ct) =>
                (await service.UpdateLineAsync(id, lineId, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("UpdatePurchaseOrderLine").Produces<PurchaseOrderDetail>();
        orders.MapPost("/{id:guid}/submit", async (Guid id, PurchaseOrderVersionInput input, PurchaseOrderService service, CancellationToken ct) => (await service.SubmitAsync(id, input, ct)).ToHttpResult())
            .RequireAnyPermissionOf("purchasing.create", "purchasing.edit").WithName("SubmitPurchaseOrder").Produces<PurchaseOrderDetail>();
        orders.MapPost("/{id:guid}/approve", async (Guid id, PurchaseOrderDecisionInput input, PurchaseOrderService service, CancellationToken ct) => (await service.ApproveAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.approve").WithName("ApprovePurchaseOrder").Produces<PurchaseOrderDetail>();
        orders.MapPost("/{id:guid}/reject", async (Guid id, PurchaseOrderDecisionInput input, PurchaseOrderService service, CancellationToken ct) => (await service.RejectAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.approve").WithName("RejectPurchaseOrder").Produces<PurchaseOrderDetail>();
        orders.MapPost("/{id:guid}/cancel", async (Guid id, PurchaseOrderCancelInput input, PurchaseOrderService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("CancelPurchaseOrder").Produces<PurchaseOrderDetail>();
    }

    /// <summary>Fornecedores e condições para o formulário do pedido, sem exigir as permissões de Terceiros nem do Financeiro.</summary>
    private static void MapOrderOptions(RouteGroupBuilder purchasing)
    {
        purchasing.MapGet("/suppliers", async (string? q, PurchaseOrderService service, CancellationToken ct) => Results.Ok(await service.SupplierOptionsAsync(q, ct)))
            .RequireAnyPermissionOf("purchasing.view", "purchasing.create").WithName("ListPurchaseSupplierOptions").Produces<IReadOnlyList<PurchaseSupplierOption>>();
        purchasing.MapGet("/payment-terms", async (PurchaseOrderService service, CancellationToken ct) => Results.Ok(await service.PaymentTermOptionsAsync(ct)))
            .RequireAnyPermissionOf("purchasing.view", "purchasing.create").WithName("ListPurchasePaymentTermOptions").Produces<IReadOnlyList<PurchasePaymentTermOption>>();
    }
}
