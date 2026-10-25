using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Purchasing.Endpoints;

/// <summary>
/// Cotações (CMP-02 CA 1 a 15). Ver com <c>purchasing.view</c>; criar a cotação e gerar os pedidos com <c>purchasing.create</c> (com
/// <c>Idempotency-Key</c>); fornecedores, respostas, concluir e cancelar com <c>purchasing.edit</c>; "Enviar todos para aprovação" com a
/// mesma permissão do envio da CMP-03a (<c>purchasing.create</c> ou <c>purchasing.edit</c>). Fornecedor e linha são sempre lidos pela
/// cotação da URL (IDOR). Os anexos da proposta usam o dono <c>Rfq</c> (id do fornecedor na cotação) nas rotas de anexos.
/// </summary>
internal static partial class PurchasingEndpoints
{
    private static readonly string[] RfqChildren = [nameof(RfqSupplier), nameof(RfqLine), nameof(RfqQuote), nameof(RfqLineRequest)];

    private static void MapRfqs(RouteGroupBuilder rfqs)
    {
        rfqs.MapGet(string.Empty, async (string? show, RfqStatus? status, string? q, int? page, int? pageSize, RfqService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new RfqListQuery(show, status, q), Page(page, pageSize), ct)))
            .RequirePermission("purchasing.view").WithName("ListRfqs").Produces<PagedList<RfqSummary>>();
        rfqs.MapGet("/defaults", async ([FromQuery] Guid[]? requestLineIds, RfqService service, CancellationToken ct) => (await service.DefaultsAsync(requestLineIds, ct)).ToHttpResult())
            .RequirePermission("purchasing.create").WithName("GetRfqDefaults").Produces<RfqDefaults>();
        rfqs.MapGet("/{id:guid}", async (Guid id, RfqService service, CancellationToken ct) => (await service.DetailAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetRfq").Produces<RfqDetail>();
        rfqs.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(Rfq), id.ToString(), RfqChildren, "rfqId", Page(page, pageSize), ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetRfqHistory").Produces<PagedList<RelatedAuditEntryDto>>();
        rfqs.MapPost(string.Empty, async (RfqInput input, RfqService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/purchasing/rfqs/" + saved.Id, saved)))
            .RequirePermission("purchasing.create").RequireIdempotencyKey().WithName("CreateRfq").Produces<RfqDetail>(StatusCodes.Status201Created);
        rfqs.MapPost("/{id:guid}/suppliers", async (Guid id, RfqSupplierInput input, RfqService service, CancellationToken ct) => (await service.AddSupplierAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("AddRfqSupplier").Produces<RfqDetail>();
        rfqs.MapPost("/{id:guid}/suppliers/{supplierId:guid}/remove", async (Guid id, Guid supplierId, RfqVersionInput input, RfqService service, CancellationToken ct) =>
                (await service.RemoveSupplierAsync(id, supplierId, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("RemoveRfqSupplier").Produces<RfqDetail>();
        rfqs.MapPut("/{id:guid}/suppliers/{supplierId:guid}/response", async (Guid id, Guid supplierId, RfqResponseInput input, RfqService service, CancellationToken ct) =>
                (await service.RespondAsync(id, supplierId, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("SaveRfqResponse").Produces<RfqDetail>();
        rfqs.MapPost("/{id:guid}/suppliers/{supplierId:guid}/decline", async (Guid id, Guid supplierId, RfqVersionInput input, RfqService service, CancellationToken ct) =>
                (await service.DeclineAsync(id, supplierId, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("DeclineRfqSupplier").Produces<RfqDetail>();
        rfqs.MapPost("/{id:guid}/close", async (Guid id, RfqCloseInput input, RfqService service, CancellationToken ct) => (await service.CloseAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("CloseRfq").Produces<RfqDetail>();
        rfqs.MapPost("/{id:guid}/cancel", async (Guid id, RfqCancelInput input, RfqService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("CancelRfq").Produces<RfqDetail>();
        rfqs.MapPost("/{id:guid}/purchase-orders", async (Guid id, RfqVersionInput input, RfqService service, CancellationToken ct) =>
                (await service.GenerateOrdersAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.create").RequireIdempotencyKey().WithName("GenerateRfqPurchaseOrders").Produces<RfqOrdersResult>();
        rfqs.MapPost("/{id:guid}/purchase-orders/submit", async (Guid id, RfqService service, CancellationToken ct) => (await service.SubmitOrdersAsync(id, ct)).ToHttpResult())
            .RequireAnyPermissionOf("purchasing.create", "purchasing.edit").WithName("SubmitRfqPurchaseOrders").Produces<RfqSubmitResult>();
    }
}
