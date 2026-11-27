using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Sales.Application;
using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Sales.Endpoints;

/// <summary>Filtros da lista de pedidos na URL.</summary>
public sealed record SalesOrderListParams(
    SalesOrderNextStep? Step, SalesOrderOrigin? Origin, Guid? App, Guid? CustomerId, DateOnly? From, DateOnly? To, int? StaleDays, bool? IncludeCancelled, string? Q)
{
    public SalesOrderListQuery ToQuery() => new(Step, Origin, App, CustomerId, From, To, StaleDays, IncludeCancelled ?? false, Q);
}

/// <summary>
/// Pedido de venda (VND-02a): <c>/api/v1/sales/orders</c> no grupo do módulo <c>sales</c> (403 <c>module_not_released</c> quando não liberado). Rotas de
/// pessoa: a origem é sempre <c>Backoffice</c> e o autor é o usuário da sessão (CA 2); a rota de aplicativo é da INT-03. Pedido de outro tenant ou
/// inexistente: 404 igual (IDOR).
/// </summary>
internal static class SalesOrderEndpoints
{
    public static void MapSalesOrderEndpoints(this RouteGroupBuilder sales)
    {
        var orders = sales.MapGroup("/orders");
        orders.MapGet(string.Empty, async ([AsParameters] SalesOrderListParams query, SalesOrderService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(query.ToQuery(), ct)))
            .RequirePermission("sales.view").WithName("ListSalesOrders").Produces<SalesOrderListPage>();
        orders.MapGet("/summary", async (SalesOrderService service, CancellationToken ct) => Results.Ok(await service.SummaryAsync(ct)))
            .RequirePermission("sales.view").WithName("GetSalesOrderSummary").Produces<SalesOrderSummaryView>();
        orders.MapGet("/product-options", async (string? q, Guid? locationId, SalesOrderService service, CancellationToken ct) =>
                Results.Ok(await service.ProductOptionsAsync(q, locationId, ct)))
            .RequireAnyPermissionOf("sales.create", "sales.edit").WithName("ListSalesOrderProductOptions").Produces<IReadOnlyList<SalesOrderProductOption>>();
        orders.MapGet("/customer-options", async (string? q, SalesOrderService service, CancellationToken ct) => Results.Ok(await service.CustomerOptionsAsync(q, ct)))
            .RequireAnyPermissionOf("sales.create", "sales.edit").WithName("ListSalesOrderCustomerOptions").Produces<IReadOnlyList<SalesOrderCustomerOption>>();
        orders.MapGet("/parameters", async (SalesOrderParametersService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .RequirePermission("sales.view").WithName("GetSalesOrderParameters").Produces<SalesOrderParametersView>();
        // Salvar exige settings.edit (no serviço), como as outras seções de Configurações → Parâmetros.
        orders.MapPut("/parameters", async (SalesOrderParametersInput input, SalesOrderParametersService service, CancellationToken ct) =>
                (await service.UpdateAsync(input, ct)).ToHttpResult())
            .RequirePermission("sales.view").WithName("UpdateSalesOrderParameters").Produces<SalesOrderParametersView>();

        orders.MapPost(string.Empty, async (SalesOrderInput input, ICurrentUser user, SalesOrderService service, CancellationToken ct) =>
                (await service.CreateAsync(input, SalesOrderActor.Person(user.UserId), ct)).ToHttpResult(o => Results.Created($"/api/v1/sales/orders/{o.Id}", o)))
            .RequirePermission("sales.create").RequireIdempotencyKey().WithName("CreateSalesOrder").Produces<SalesOrderDetail>(StatusCodes.Status201Created);
        orders.MapGet("/{id:guid}", async (Guid id, SalesOrderService service, CancellationToken ct) =>
                await service.DetailAsync(id, ct) is { } detail ? Results.Ok(detail) : Error.NotFound(Error.Codes.NotFound, "Pedido não encontrado.").ToProblem())
            .RequirePermission("sales.view").WithName("GetSalesOrder").Produces<SalesOrderDetail>();
        orders.MapPut("/{id:guid}", async (Guid id, SalesOrderEditInput input, ICurrentUser user, SalesOrderService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, input, SalesOrderActor.Person(user.UserId), ct)).ToHttpResult())
            .RequirePermission("sales.edit").WithName("UpdateSalesOrder").Produces<SalesOrderDetail>();
        orders.MapPost("/{id:guid}/confirm", async (Guid id, SalesOrderConfirmInput input, ICurrentUser user, SalesOrderService service, CancellationToken ct) =>
                (await service.ConfirmAsync(id, input, SalesOrderActor.Person(user.UserId), ct)).ToHttpResult())
            .RequirePermission("sales.create").WithName("ConfirmSalesOrder").Produces<SalesOrderDetail>();
        orders.MapGet("/{id:guid}/charge", async (Guid id, ICurrentUser user, SalesOrderService service, CancellationToken ct) =>
                (await service.ChargeDraftAsync(id, SalesOrderActor.Person(user.UserId), ct)).ToHttpResult())
            .RequirePermission("sales.edit").WithName("GetSalesOrderChargeDraft").Produces<SalesOrderChargeDraft>();
        orders.MapPost("/{id:guid}/charge", async (Guid id, SalesOrderChargeInput input, HttpContext http, ICurrentUser user, SalesOrderService service, CancellationToken ct) =>
                (await service.ChargeAsync(id, input, SalesOrderActor.Person(user.UserId), Key(http), ct)).ToHttpResult())
            .RequirePermission("sales.edit").RequireIdempotencyKey().WithName("ChargeSalesOrder").Produces<SalesOrderDetail>();
        orders.MapPost("/{id:guid}/payments", async (Guid id, SalesOrderPaymentInput input, ICurrentUser user, SalesOrderService service, CancellationToken ct) =>
                (await service.RegisterPaymentAsync(id, input, SalesOrderActor.Person(user.UserId), ct)).ToHttpResult())
            .RequirePermission("sales.approve").RequireIdempotencyKey().WithName("RegisterSalesOrderPayment").Produces<SalesOrderDetail>();
        // Antes do pagamento, sales.edit; o pedido pago só com sales.approve (conferido no serviço).
        orders.MapPost("/{id:guid}/cancel", async (Guid id, SalesOrderCancelInput input, ICurrentUser user, SalesOrderService service, CancellationToken ct) =>
                (await service.CancelAsync(id, input, SalesOrderActor.Person(user.UserId), ct)).ToHttpResult())
            .RequirePermission("sales.edit").WithName("CancelSalesOrder").Produces<SalesOrderDetail>();
        orders.MapPost("/{id:guid}/reopen", async (Guid id, SalesOrderVersionInput input, ICurrentUser user, SalesOrderService service, CancellationToken ct) =>
                (await service.ReopenAsync(id, input, SalesOrderActor.Person(user.UserId), ct)).ToHttpResult())
            .RequirePermission("sales.approve").WithName("ReopenSalesOrder").Produces<SalesOrderDetail>();
        orders.MapPost("/{id:guid}/refund", async (Guid id, SalesOrderVersionInput input, ICurrentUser user, SalesOrderService service, CancellationToken ct) =>
                (await service.RefundAsync(id, input, SalesOrderActor.Person(user.UserId), ct)).ToHttpResult())
            .RequirePermission("sales.approve").WithName("RefundSalesOrder").Produces<SalesOrderDetail>();
    }

    private static Guid? Key(HttpContext http) => Guid.TryParse(http.Request.Headers[IdempotencyFilter.Header].ToString(), out var key) ? key : null;
}
