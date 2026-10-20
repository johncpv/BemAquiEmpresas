using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>Implantação financeira (FIN-00): data de corte e conclusão com <c>finance.approve</c> (conferido no serviço).</summary>
internal static partial class FinanceEndpoints
{
    private static void MapOpening(RouteGroupBuilder opening)
    {
        opening.MapGet(string.Empty, async (OpeningService service, CancellationToken ct) => Results.Ok(await service.StatusAsync(ct)))
            .RequirePermission("finance.view").WithName("GetOpening").Produces<OpeningStatus>();
        opening.MapPut("/cutover", async (CutoverInput input, OpeningService service, CancellationToken ct) => (await service.SetCutoverAsync(input, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("SetOpeningCutover").Produces<OpeningStatus>();
        opening.MapPut("/balances", async (OpeningBalancesInput input, OpeningService service, CancellationToken ct) => (await service.SaveBalancesAsync(input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("SaveOpeningBalances").Produces<OpeningStatus>();
        opening.MapPost("/receivables", async (OpeningReceivablesInput input, OpeningService service, CancellationToken ct) => (await service.CreateReceivablesAsync(input, ct)).ToHttpResult())
            .RequirePermission("finance.create").RequireIdempotencyKey().WithName("CreateOpeningReceivables").Produces<OpeningBatchResult>();
        opening.MapPost("/payables", async (OpeningPayablesInput input, OpeningService service, CancellationToken ct) => (await service.CreatePayablesAsync(input, ct)).ToHttpResult())
            .RequirePermission("finance.create").RequireIdempotencyKey().WithName("CreateOpeningPayables").Produces<OpeningBatchResult>();
        opening.MapPost("/complete", async (CompleteOpeningInput input, OpeningService service, CancellationToken ct) => (await service.CompleteAsync(input, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("CompleteOpening").Produces<OpeningStatus>();
    }
}
