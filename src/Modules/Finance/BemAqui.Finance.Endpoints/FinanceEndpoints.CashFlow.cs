using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>Fluxo de caixa base (FIN-05) e ajustado (FIN-07): só leitura, com <c>finance.view</c>.</summary>
internal static partial class FinanceEndpoints
{
    private static void MapCashFlow(RouteGroupBuilder cashFlow)
    {
        cashFlow.MapGet(string.Empty, async (string? view, int? days, Guid? bankAccountId, DateOnly? from, string? scenario, CashFlowService service, CancellationToken ct) =>
                (await service.BuildAsync(new CashFlowQuery(view, days, bankAccountId, from, scenario), ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetCashFlow").Produces<CashFlowResult>();
        cashFlow.MapGet("/day", async (DateOnly date, Guid? bankAccountId, string? sort, string? scenario, CashFlowService service, CancellationToken ct) =>
                (await service.DayAsync(date, bankAccountId, sort, scenario, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetCashFlowDay").Produces<CashFlowDay>();
        // Resumo para o painel do dono (FIN-07 CA 14): 30, 60 ou 90 dias; os mesmos números da tela.
        cashFlow.MapGet("/summary", async (int? days, ICashForecast forecast, CancellationToken ct) =>
            {
                Result<CashForecastSummary> result = days is 30 or 60 or 90 or null
                    ? await forecast.SummaryAsync(days ?? 90, ct)
                    : ReceivableService.Invalid("days", "Escolha 30, 60 ou 90 dias.");
                return result.ToHttpResult();
            })
            .RequirePermission("finance.view").WithName("GetCashForecastSummary").Produces<CashForecastSummary>();
    }
}
