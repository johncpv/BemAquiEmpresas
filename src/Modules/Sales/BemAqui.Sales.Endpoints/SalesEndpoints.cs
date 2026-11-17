using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Sales.Application;
using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Sales.Endpoints;

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
        return api;
    }

    private static async Task<SaleView> MaskAsync(SaleView sale, IPermissionChecker permissions, CancellationToken ct) =>
        await permissions.HasPermissionAsync(SaleService.Module, "view_cost", ct) ? sale : sale with { Lines = sale.Lines.Select(l => l with { UnitCost = null }).ToList() };
}
