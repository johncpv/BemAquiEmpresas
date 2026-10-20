using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>Fluxo de caixa base (FIN-05): só leitura, com <c>finance.view</c>.</summary>
internal static partial class FinanceEndpoints
{
    private static void MapCashFlow(RouteGroupBuilder cashFlow)
    {
        cashFlow.MapGet(string.Empty, async (string? view, int? days, Guid? bankAccountId, DateOnly? from, CashFlowService service, CancellationToken ct) =>
                (await service.BuildAsync(new CashFlowQuery(view, days, bankAccountId, from), ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetCashFlow").Produces<CashFlowResult>();
        cashFlow.MapGet("/day", async (DateOnly date, Guid? bankAccountId, string? sort, CashFlowService service, CancellationToken ct) =>
                (await service.DayAsync(date, bankAccountId, sort, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetCashFlowDay").Produces<CashFlowDay>();
    }
}
