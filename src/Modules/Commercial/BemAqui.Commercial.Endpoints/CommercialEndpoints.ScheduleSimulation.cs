using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application;
using BemAqui.Production.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

internal static partial class CommercialEndpoints
{
    /// <summary>
    /// Simulação de prazo do orçamento (PLN-05), no grupo do módulo <c>quotes</c>: <c>quotes.view</c> e o rate limit da política nomeada
    /// <c>planning-simulate</c> (CA 10). O módulo <c>production</c> liberado é conferido no serviço (CA 11). Só datas e dias (CA 9).
    /// </summary>
    private static void MapScheduleSimulation(RouteGroupBuilder quotes) =>
        quotes.MapPost("/{id:guid}/schedule-simulation", async (Guid id, QuoteScheduleSimulationInput input, QuoteScheduleSimulationService service, CancellationToken ct) =>
                (await service.SimulateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("quotes.view").RequireRateLimiting(PlanningRateLimits.SimulatePolicy).WithName("SimulateQuoteSchedule").Produces<QuoteScheduleSimulationView>();
}
