using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Sales.Application;
using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Sales.Endpoints;

/// <summary>Filtro do relatório de devoluções (PDV-06 CA 12): período em datas de negócio e loja.</summary>
public sealed record SaleReturnQuery(DateOnly From, DateOnly To, Guid? StoreId)
{
    public SaleReturnFilter ToFilter() => new(From, To, StoreId);
}

/// <summary>Filtro e página da lista de vendas (PDV-07 CA 2): período em datas de negócio, loja, caixa, operador, forma de pagamento e estado.</summary>
public sealed record SaleListQuery(
    DateOnly From, DateOnly To, Guid? StoreId, Guid? RegisterId, Guid? OperatorId, Guid? PaymentMethodId, SaleListState? State, int? Page, int? PageSize, string? Sort)
{
    public SaleListFilter ToFilter() => new(From, To, StoreId, RegisterId, OperatorId, PaymentMethodId, State);

    public PageRequest ToPage() => new(Page ?? 1, PageSize ?? PageRequest.DefaultPageSize, Sort);
}

/// <summary>Motivo do cancelamento e a confirmação de que os pagamentos capturados foram estornados fora (maquininha, provedor).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaleCancelInput(string Reason, bool CapturedRefundedOutside);

/// <summary>
/// Vendas (VND-01): sem tela nesta story; quem cria venda são os canais, pelos contratos. A retaguarda (lista, detalhe e cards) é a PDV-07. Módulo
/// <c>sales</c> (oculto para a fábrica: fora do perfil, 403 <c>module_not_released</c>); custo da linha só com <c>sales.view_cost</c>.
/// </summary>
internal static class SalesEndpoints
{
    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder api)
    {
        var sales = api.MapGroup("/sales").WithTags("Vendas").RequireModule(SaleService.Module)
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        // PDV-07: Loja → Vendas. Lista e totais do filtro, exportação (ADR 0045) e detalhe com a trilha; custo e margem só com sales.view_cost.
        sales.MapGet("/sales", async ([AsParameters] SaleListQuery query, SaleBackofficeService service, CancellationToken ct) =>
                (await service.ListAsync(query.ToFilter(), query.ToPage(), ct)).ToHttpResult())
            .RequirePermission("sales.view").WithName("ListSales").Produces<SaleListPage>();
        sales.MapGet("/sales/export", (string? format, [AsParameters] SaleListQuery query, HttpContext http, SaleBackofficeService service, ListExportService export,
                CancellationToken ct) => export.ExportAsync(http, format, (max, c) => service.ExportAsync(query.ToFilter(), query.Sort, max, c), ct))
            .RequirePermission("sales.view").WithName("ExportSales");
        sales.MapGet("/sales/{id:guid}", async (Guid id, SaleBackofficeService service, CancellationToken ct) =>
                await service.DetailAsync(id, ct) is { } detail ? Results.Ok(detail) : Error.NotFound(Error.Codes.NotFound, "Venda não encontrada.").ToProblem())
            .RequirePermission("sales.view").WithName("GetSaleDetail").Produces<SaleDetail>();
        // CA 6: o CPF/CNPJ inteiro só aqui, com a leitura auditada (a lista e o arquivo trazem a máscara).
        sales.MapGet("/sales/{id:guid}/customer-document", async (Guid id, SaleBackofficeService service, CancellationToken ct) =>
                await service.RevealDocumentAsync(id, ct) is { } document ? Results.Ok(document) : Error.NotFound(Error.Codes.NotFound, "Esta venda não tem CPF ou CNPJ do consumidor.").ToProblem())
            .RequirePermission("sales.view_customer_document").RequireRateLimiting(SaleBackofficeService.RevealRateLimitPolicy).WithName("GetSaleCustomerDocument").Produces<SaleCustomerDocumentView>();

        sales.MapGet("/{id:guid}", async (Guid id, SaleService service, IPermissionChecker permissions, CancellationToken ct) =>
                await service.ViewAsync(id, ct) is { } sale ? Results.Ok(await MaskAsync(sale, permissions, ct)) : Error.NotFound(Error.Codes.NotFound, "Venda não encontrada.").ToProblem())
            .RequirePermission("sales.view").WithName("GetSale").Produces<SaleView>();
        // Cancelar venda (auditado): estorno de pagamento capturado é decisão de supervisor (rascunho C §7), por isso sales.approve.
        sales.MapPost("/{id:guid}/cancel", async (Guid id, SaleCancelInput input, SaleService service, IPermissionChecker permissions, CancellationToken ct) =>
            {
                var result = await service.CancelAsync(new SaleCancellationRequest(id, input.Reason, input.CapturedRefundedOutside), ct);
                return result.IsSuccess ? Results.Ok(await MaskAsync(result.Value, permissions, ct)) : result.Error!.ToProblem();
            })
            .RequirePermission("sales.approve").WithName("CancelSale").Produces<SaleView>();

        // IMP-01 CA 12: vendas sem documento fiscal por período (data de negócio), loja e operador, com exportação em CSV.
        sales.MapGet("/reports/non-fiscal", async (DateOnly from, DateOnly to, Guid? storeId, Guid? operatorId, NonFiscalSalesReportService service, CancellationToken ct) =>
                (await service.QueryAsync(from, to, storeId, operatorId, ct)).ToHttpResult())
            .RequirePermission("sales.view").WithName("GetNonFiscalSalesReport").Produces<NonFiscalSalesReport>();
        sales.MapGet("/reports/non-fiscal/export", async (DateOnly from, DateOnly to, Guid? storeId, Guid? operatorId, NonFiscalSalesReportService service, CancellationToken ct) =>
            {
                var file = await service.ExportAsync(from, to, storeId, operatorId, ct);
                return file.IsSuccess ? Results.File(file.Value.Content, SafeCsv.ContentType, file.Value.FileName) : file.Error!.ToProblem();
            })
            .RequirePermission("sales.view").WithName("ExportNonFiscalSalesReport");

        // PDV-04 CA 4: vendas abaixo do custo (o custo é da baixa; só com sales.view_cost, o custo nunca desce para o caixa).
        sales.MapGet("/reports/below-cost", async (DateOnly from, DateOnly to, Guid? storeId, BelowCostSalesReportService service, CancellationToken ct) =>
                (await service.QueryAsync(from, to, storeId, ct)).ToHttpResult())
            .RequirePermission("sales.view_cost").WithName("GetBelowCostSalesReport").Produces<BelowCostSalesReport>();

        // PDV-06 CA 12: Loja → Devoluções, por operador, motivo e forma de reembolso, com a exportação.
        sales.MapGet("/returns", async ([AsParameters] SaleReturnQuery query, SaleReturnReportService service, CancellationToken ct) =>
                (await service.QueryAsync(query.ToFilter(), ct)).ToHttpResult())
            .RequirePermission("sales.view").WithName("GetSaleReturnsReport").Produces<SaleReturnReport>();
        sales.MapGet("/returns/export", async ([AsParameters] SaleReturnQuery query, SaleReturnReportService service, CancellationToken ct) =>
            {
                var file = await service.ExportAsync(query.ToFilter(), ct);
                return file.IsSuccess ? Results.File(file.Value.Content, SafeCsv.ContentType, file.Value.FileName) : file.Error!.ToProblem();
            })
            .RequirePermission("sales.view").WithName("ExportSaleReturns");

        sales.MapSalesOrderEndpoints();
        MapPosReturnEndpoints(api);
        return api;
    }

    /// <summary>
    /// PDV-06: devolução, troca e vale-troca no caixa, só com rede e só na sessão por PIN do caixa (o caixa vem do cookie do aparelho, nunca do corpo).
    /// Ficam em <c>/api/v1/pos/*</c> porque a sessão por PIN do caixa só alcança rotas do PDV (ADR 0048; a story citava <c>/sales/*</c>). O CPF e o código
    /// do vale vão sempre no corpo de um POST (nunca na URL nem no log de acesso); a busca tem rate limit por tenant, usuário e aparelho.
    /// </summary>
    private static void MapPosReturnEndpoints(IEndpointRouteBuilder api)
    {
        var pos = api.MapGroup("/pos").WithTags("PDV: devolução").RequireModule("pos")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        pos.MapPost("/sales/lookup", async (SaleLookupInput input, HttpContext http, SaleReturnService service, CancellationToken ct) =>
                IsPosPinSession(http) ? (await service.LookupAsync(Cookie(http), input, ct)).ToHttpResult() : PinOnly())
            .RequirePermission("pos.return").AllowPosPinSession().RequireRateLimiting(SaleReturnService.RateLimitPolicy)
            .WithName("LookupSaleForReturn").Produces<SaleLookupResult>();
        pos.MapPost("/returns", async (ReturnRequestInput input, HttpContext http, ICurrentUser user, SaleReturnService service, CancellationToken ct) =>
                IsPosPinSession(http) ? (await service.RequestAsync(Cookie(http), user.UserId!.Value, input, ct)).ToHttpResult() : PinOnly())
            .RequirePermission("pos.return").AllowPosPinSession().WithName("RequestSaleReturn").Produces<SaleReturnView>();
        pos.MapPost("/returns/{id:guid}/approve", async (Guid id, ReturnApproveInput input, HttpContext http, ICurrentUser user, SaleReturnService service, CancellationToken ct) =>
                IsPosPinSession(http) ? (await service.ApproveAsync(Cookie(http), user.UserId!.Value, id, input, ct)).ToHttpResult() : PinOnly())
            .RequirePermission("pos.return").AllowPosPinSession().WithName("ApproveSaleReturn").Produces<SaleReturnView>();
        // Revisão de segurança (achado 5): o código do vale perdido é reemitido com o PIN do supervisor; o antigo deixa de valer.
        pos.MapPost("/returns/{id:guid}/store-credit/reissue", async (Guid id, StoreCreditReissueInput input, HttpContext http, ICurrentUser user, SaleReturnService service,
                CancellationToken ct) =>
                IsPosPinSession(http) ? (await service.ReissueCodeAsync(Cookie(http), user.UserId!.Value, id, input, ct)).ToHttpResult() : PinOnly())
            .RequirePermission("pos.return").AllowPosPinSession().RequireRateLimiting(StoreCreditService.RateLimitPolicy)
            .WithName("ReissueStoreCreditCode").Produces<StoreCreditIssued>();
        pos.MapPost("/store-credits/lookup", async (StoreCreditLookupInput input, HttpContext http, StoreCreditService service, CancellationToken ct) =>
                IsPosPinSession(http) ? (await service.LookupAsync(Cookie(http), input, ct)).ToHttpResult() : PinOnly())
            .RequirePermission("pos.create").AllowPosPinSession().RequireRateLimiting(StoreCreditService.RateLimitPolicy)
            .WithName("LookupStoreCredit").Produces<StoreCreditView>();
        pos.MapPost("/store-credits/redeem", async (StoreCreditRedeemInput input, HttpContext http, ICurrentUser user, StoreCreditService service, CancellationToken ct) =>
                IsPosPinSession(http) ? (await service.RedeemAsync(Cookie(http), user.UserId!.Value, input, ct)).ToHttpResult() : PinOnly())
            .RequirePermission("pos.create").AllowPosPinSession().RequireRateLimiting(StoreCreditService.RateLimitPolicy)
            .WithName("RedeemStoreCredit").Produces<StoreCreditRedeemed>();
    }

    private static bool IsPosPinSession(HttpContext http) => http.User.FindAll(AuthMethods.Claim).Any(c => c.Value == AuthMethods.DeviceKey);

    private static string? Cookie(HttpContext http) => http.Request.Cookies[PosDeviceCookie.Name];

    private static IResult PinOnly() => Error.Forbidden(Error.Codes.Forbidden, SaleReturnService.NotRegisterMessage).ToProblem();

    private static async Task<SaleView> MaskAsync(SaleView sale, IPermissionChecker permissions, CancellationToken ct) =>
        await permissions.HasPermissionAsync(SaleService.Module, "view_cost", ct) ? sale : sale with { Lines = sale.Lines.Select(l => l with { UnitCost = null }).ToList() };
}
