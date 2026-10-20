using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>Extrato, transferências e lançamentos sem OP (FIN-04): consulta com <c>finance.view</c>, movimento com <c>finance.pay</c>.</summary>
internal static partial class FinanceEndpoints
{
    private static void MapBanking(RouteGroupBuilder finance)
    {
        finance.MapGet("/bank-accounts/balances", async (BankStatementService service, CancellationToken ct) => Results.Ok(await service.BalancesAsync(ct)))
            .RequirePermission("finance.view").WithName("GetBankBalances").Produces<AccountBalances>();
        finance.MapGet("/bank-accounts/{id:guid}/statement", async (Guid id, DateOnly? from, DateOnly? to, bool? withoutOrder, int? page, int? pageSize, BankStatementService service, CancellationToken ct) =>
                (await service.StatementAsync(id, from, to, withoutOrder == true, new PageRequest(page ?? 1, pageSize ?? 50).Normalize(), ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetBankStatement").Produces<StatementResult>();
        finance.MapPost("/transfers", async (TransferInput input, BankMovementService service, CancellationToken ct) =>
                (await service.TransferAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/transfers/" + saved.Id, saved)))
            .RequirePermission("finance.pay").RequireIdempotencyKey().WithName("CreateTransfer").Produces<BankTransferDetail>(StatusCodes.Status201Created);
        finance.MapPost("/transfers/{id:guid}/reverse", async (Guid id, ReasonInput input, BankMovementService service, CancellationToken ct) => (await service.ReverseTransferAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.pay").WithName("ReverseTransfer").Produces<BankTransferDetail>();
        finance.MapPost("/bank-entries", async (BankEntryInput input, BankMovementService service, CancellationToken ct) =>
                (await service.CreateEntryAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/bank-entries/" + saved.Id, saved)))
            .RequirePermission("finance.pay").RequireIdempotencyKey().WithName("CreateBankEntry").Produces<BankEntryDetail>(StatusCodes.Status201Created);
        finance.MapPost("/bank-entries/{id:guid}/reverse", async (Guid id, ReasonInput input, BankMovementService service, CancellationToken ct) => (await service.ReverseEntryAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.pay").WithName("ReverseBankEntry").Produces<BankEntryDetail>();
    }
}
