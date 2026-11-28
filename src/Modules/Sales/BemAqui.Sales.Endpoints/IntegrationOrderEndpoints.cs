using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Fiscal.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Sales.Application;
using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Sales.Endpoints;

/// <summary>Linha pedida pelo aplicativo: produto, quantidade e desconto opcional. <c>unitPrice</c>, se vier, é ignorado (sempre o da tabela, VND-02a CA 5).</summary>
public sealed record IntegrationOrderLineInput(Guid ProductId, decimal Quantity, decimal Discount = 0, decimal? UnitPrice = null);

/// <summary>
/// Pedido do aplicativo (INT-03 CA 12): cliente, linhas, desconto do pedido, observação e o id externo (obrigatório). Origem, aplicativo e autor vêm do
/// token, nunca do corpo: campo desconhecido é recusado.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntegrationOrderInput(Guid CustomerId, IReadOnlyList<IntegrationOrderLineInput> Lines, string? ExternalId, decimal OrderDiscount = 0, string? Note = null);

/// <summary>Confirmar (CA 13): <c>version</c> opcional (sem ela, vale a versão atual); com o preço mudado, 409 <c>price_changed</c> até aceitar.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntegrationOrderConfirmInput(uint? Version = null, bool AcceptNewPrices = false);

/// <summary>Gerar a cobrança (CA 14): vencimento opcional (padrão e teto do aplicativo: até 3 dias úteis) e se o boleto sai por e-mail.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntegrationOrderChargeInput(DateOnly? DueDate = null, bool SendEmail = true, uint? Version = null);

/// <summary>Cancelar (CA 16): o motivo (3 a 200 caracteres).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntegrationOrderCancelInput(string Reason, uint? Version = null);

public sealed record IntegrationOrderLine(Guid ProductId, string Description, string Unit, decimal Quantity, decimal UnitPrice, decimal Discount, decimal Total);

/// <summary>Cobrança do pedido: o id do título (o mesmo de <c>GET /charges/{id}</c>), a situação dele e a do boleto.</summary>
public sealed record IntegrationOrderCharge(Guid Id, string Status, string? SlipStatus);

/// <summary>Nota do pedido (CA 18): tipo, situação, série, número, chave, data e o link do DANFE; INT-04 CA 2 soma o destinatário e o pedido (aditivo).</summary>
public sealed record IntegrationOrderInvoice(
    Guid Id, InvoiceType Type, string Status, int? Series, long? Number, string? AccessKey, DateOnly? IssuedOn, string DanfeUrl, Guid? PartyId = null, Guid? SalesOrderId = null);

/// <summary>Data de cada estado (a última passagem por ele); null se ainda não passou.</summary>
public sealed record IntegrationOrderDates(DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt, DateTimeOffset? ChargedAt, DateTimeOffset? PaidAt, DateTimeOffset? CancelledAt);

/// <summary>
/// Pedido visto pelo aplicativo (CA 15): número, estado, origem, linhas, total, cobrança (id e estado), nota (id e estado) e as datas de cada estado.
/// Sem custo e sem dado de outro cliente (nem o nome do cliente: só o id).
/// </summary>
public sealed record IntegrationOrder(
    Guid Id, string Number, SalesOrderStatus Status, SalesOrderOrigin Origin, string? ExternalId, Guid CustomerId, IReadOnlyList<IntegrationOrderLine> Lines, decimal Subtotal,
    decimal DiscountTotal, decimal Total, bool DiscountNeedsApproval, string? Message, IntegrationOrderCharge? Charge, IntegrationOrderInvoice? Invoice, IntegrationOrderDates Dates,
    uint Version);

/// <summary>
/// Pedidos pela API de integração (INT-03 CA 12 a 16 e 18), finos sobre o <see cref="SalesOrderService"/> (VND-02a): a origem, o aplicativo
/// (<c>source_app_id</c>) e quem autorizou vêm do token; ações só nos pedidos do próprio aplicativo (o resto, 404; INT-04 CA 6). Leitura de qualquer pedido do tenant
/// com <c>sales.view</c>, como a pessoa que autorizou.
/// </summary>
internal static class IntegrationOrderEndpoints
{
    public const string DraftMessage = "Pedido do Conecta: confirme com o cliente.";
    public const string CancelBackofficeCode = "order_cancel_requires_backoffice";

    public static void MapIntegrationOrders(this IEndpointRouteBuilder api)
    {
        var root = api.MapIntegrationGroup();
        var orders = root.MapGroup("/orders");
        orders.MapPost(string.Empty, CreateAsync).RequirePermission("sales.create").RequireIdempotencyKey().WithName("IntegrationCreateOrder")
            .Produces<IntegrationOrder>(StatusCodes.Status201Created).Produces<IntegrationOrder>();
        orders.MapGet("/{id:guid}", async (Guid id, SalesOrderService service, IOutgoingInvoices invoices, CancellationToken ct) =>
                await service.DetailAsync(id, ct) is { } detail ? Results.Ok(await ViewAsync(detail, invoices, ct)) : IntegrationApi.NotFound())
            .RequirePermission("sales.view").WithName("IntegrationGetOrder").Produces<IntegrationOrder>();
        orders.MapPost("/{id:guid}/confirm", ConfirmAsync).RequirePermission("sales.create").WithName("IntegrationConfirmOrder").Produces<IntegrationOrder>();
        orders.MapPost("/{id:guid}/charge", ChargeAsync).RequirePermission("sales.edit").RequireIdempotencyKey().WithName("IntegrationChargeOrder")
            .Produces<IntegrationOrder>(StatusCodes.Status202Accepted);
        orders.MapPost("/{id:guid}/cancel", CancelAsync).RequirePermission("sales.edit").WithName("IntegrationCancelOrder").Produces<IntegrationOrder>();
        orders.MapGet("/{id:guid}/invoice", async (Guid id, SalesOrderService service, IOutgoingInvoices invoices, IAuditWriter audit, CancellationToken ct) =>
            {
                if (await service.DetailAsync(id, ct) is null || await invoices.ForSalesOrderAsync(id, ct) is not { } invoice)
                {
                    return IntegrationApi.NotFound();
                }

                await audit.RecordReadAsync("OutgoingInvoice", invoice.Id.ToString(), "order-invoice", ct);
                return Results.Ok(Invoice(invoice, id));
            })
            .RequirePermission("fiscal.view").WithName("IntegrationGetOrderInvoice").Produces<IntegrationOrderInvoice>();
        root.MapGet("/customers/{id:guid}/orders", async (Guid id, IParties parties, ISalesOrderLookup lookup, SalesOrderService service, IOutgoingInvoices invoices,
                CancellationToken ct) =>
            {
                if (await parties.FindAsync(id, ct) is not { IsDeleted: false } party || !party.Roles.Contains(PartyRoleKind.Customer))
                {
                    return IntegrationApi.NotFound();
                }

                var views = new List<IntegrationOrder>();
                foreach (var summary in await lookup.ByCustomerAsync(id, 20, ct))
                {
                    if (await service.DetailAsync(summary.Id, ct) is { } detail)
                    {
                        views.Add(await ViewAsync(detail, invoices, ct));
                    }
                }

                return Results.Ok(views);
            })
            .RequirePermission("sales.view").WithName("IntegrationListCustomerOrders").Produces<IReadOnlyList<IntegrationOrder>>();
    }

    private static async Task<IResult> CreateAsync(IntegrationOrderInput input, ConnectedAppContext app, SalesOrderService service, IOutgoingInvoices invoices, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.ExternalId))
        {
            const string message = "Informe o id externo do pedido (externalId).";
            return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["externalId"] = [message] }).ToProblem();
        }

        var lines = (input.Lines ?? []).Select(l => new SalesOrderLineInput(l.ProductId, l.Quantity, l.Discount)).ToList();
        var created = await service.CreateAsync(new SalesOrderInput(input.CustomerId, lines, input.OrderDiscount, null, null, input.Note, input.ExternalId), Actor(app), ct);
        if (created.IsFailure)
        {
            return created.Error!.ToProblem();
        }

        var view = await ViewAsync(created.Value, invoices, ct);
        return created.Value.Replayed ? Results.Ok(view) : Results.Created($"/api/v1/integration/orders/{view.Id}", view);
    }

    private static async Task<IResult> ConfirmAsync(Guid id, IntegrationOrderConfirmInput? input, ConnectedAppContext app, SalesOrderService service, IOutgoingInvoices invoices,
        CancellationToken ct)
    {
        if (await VersionAsync(id, input?.Version, service, ct) is not { } version)
        {
            return IntegrationApi.NotFound();
        }

        return await ResultAsync(await service.ConfirmAsync(id, new SalesOrderConfirmInput(version, input?.AcceptNewPrices ?? false), Actor(app), ct), invoices, ct);
    }

    private static async Task<IResult> ChargeAsync(Guid id, IntegrationOrderChargeInput? input, HttpContext http, ConnectedAppContext app, SalesOrderService service,
        IOutgoingInvoices invoices, CancellationToken ct)
    {
        var actor = Actor(app);
        var draft = await service.ChargeDraftAsync(id, actor, ct);
        if (draft.IsFailure)
        {
            return draft.Error!.Type == ErrorType.NotFound ? IntegrationApi.NotFound() : draft.Error.ToProblem();
        }

        var key = Guid.TryParse(http.Request.Headers[IdempotencyFilter.Header].ToString(), out var parsed) ? parsed : (Guid?)null;
        var charged = await service.ChargeAsync(id, new SalesOrderChargeInput(input?.DueDate ?? draft.Value.DueDate, input?.SendEmail ?? true, input?.Version ?? draft.Value.Version),
            actor, key, ct);
        if (charged.IsFailure)
        {
            return charged.Error!.ToProblem();
        }

        // CA 14: 202 com a cobrança Requested; linha digitável, Pix e PDF chegam por GET /charges/{id} e pelo aviso billing_slip.issued.
        return Results.Accepted($"/api/v1/integration/orders/{id}", await ViewAsync(charged.Value, invoices, ct));
    }

    private static async Task<IResult> CancelAsync(Guid id, IntegrationOrderCancelInput input, ConnectedAppContext app, SalesOrderService service, IOutgoingInvoices invoices,
        CancellationToken ct)
    {
        if (await service.DetailAsync(id, ct) is not { } detail)
        {
            return IntegrationApi.NotFound();
        }

        var cancelled = await service.CancelAsync(id, new SalesOrderCancelInput(input.Reason ?? string.Empty, input.Version ?? detail.Version), Actor(app), ct);
        if (cancelled.IsFailure && cancelled.Error!.Type == ErrorType.Forbidden && detail.Status == SalesOrderStatus.Paid)
        {
            // CA 16: pago em diante, o cancelamento (com estorno da venda) é só no ERP.
            return Error.Conflict(CancelBackofficeCode, "Este pedido já foi pago. O cancelamento é feito no ERP.").ToProblem();
        }

        return await ResultAsync(cancelled, invoices, ct);
    }

    private static async Task<IResult> ResultAsync(Result<SalesOrderDetail> result, IOutgoingInvoices invoices, CancellationToken ct) =>
        result.IsFailure
            ? result.Error!.Type == ErrorType.NotFound ? IntegrationApi.NotFound() : result.Error.ToProblem()
            : Results.Ok(await ViewAsync(result.Value, invoices, ct));

    private static async Task<uint?> VersionAsync(Guid id, uint? version, SalesOrderService service, CancellationToken ct) =>
        version ?? (await service.DetailAsync(id, ct))?.Version;

    /// <summary>
    /// Autor do pedido pelo token (VND-02a CA 2, ADR 0053 §13): o Conecta tem a origem própria; outro aplicativo, <c>Api</c>. O <b>aplicativo</b> é o
    /// <c>source_app_id</c> (INT-04 CA 6: reconectar não deixa pedido órfão), a conexão fica só na trilha e quem autorizou é o autor.
    /// </summary>
    internal static SalesOrderActor Actor(ConnectedAppContext app)
    {
        var grant = app.Current ?? throw new InvalidOperationException("Rota de aplicativo sem a concessão no request.");
        var origin = string.Equals(grant.ClientId, ConnectedAppEndpoints.ModuleCode, StringComparison.Ordinal) ? SalesOrderOrigin.Conecta : SalesOrderOrigin.Api;
        return new SalesOrderActor(origin, grant.GrantorId, grant.AppId, grant.AppName, grant.ConnectionId);
    }

    private static async Task<IntegrationOrder> ViewAsync(SalesOrderDetail d, IOutgoingInvoices invoices, CancellationToken ct)
    {
        DateTimeOffset? Last(SalesOrderStatus status) => d.Timeline.Where(t => t.ToStatus == status && t.FromStatus != status).Select(t => (DateTimeOffset?)t.At).LastOrDefault();
        var invoice = await invoices.ForSalesOrderAsync(d.Id, ct);
        var fromApp = d.Origin != SalesOrderOrigin.Backoffice;
        return new IntegrationOrder(d.Id, d.Number, d.Status, d.Origin, d.ExternalId, d.CustomerId,
            d.Lines.Select(l => new IntegrationOrderLine(l.ProductId, l.Description, l.UnitCode, l.Quantity, l.UnitPrice, l.Discount, l.Total)).ToList(), d.Subtotal, d.DiscountTotal,
            d.Total, d.Problems.Contains(SalesOrderProblem.DiscountNeedsApproval), fromApp && d.Status == SalesOrderStatus.Draft
                ? d.Origin == SalesOrderOrigin.Conecta ? DraftMessage : "Pedido do aplicativo: confirme com o cliente."
                : null,
            d.Charge is { } c ? new IntegrationOrderCharge(c.ReceivableId, c.Status.ToString(), c.Slip?.Status.ToString()) : null,
            invoice is null ? null : Invoice(invoice, d.Id),
            new IntegrationOrderDates(d.CreatedAt, Last(SalesOrderStatus.Open), Last(SalesOrderStatus.AwaitingPayment), Last(SalesOrderStatus.Paid), Last(SalesOrderStatus.Cancelled)),
            d.Version);
    }

    private static IntegrationOrderInvoice Invoice(OutgoingInvoiceSummary i, Guid orderId) =>
        new(i.Id, i.Type, i.Status, i.Series, i.Number, i.AccessKey, i.IssuedOn, $"/api/v1/integration/invoices/{i.Id}/danfe", i.PartyId, orderId);
}
