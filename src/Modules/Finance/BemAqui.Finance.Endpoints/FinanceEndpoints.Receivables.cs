using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>Contas a receber (FIN-01): sem rota DELETE (título nunca é apagado, CA 21); criação com Idempotency-Key (CA 5).</summary>
internal static partial class FinanceEndpoints
{
    private static void MapReceivables(RouteGroupBuilder receivables)
    {
        receivables.MapGet(string.Empty, async (string? q, string? situation, Guid? partyId, DateOnly? dueFrom, DateOnly? dueTo, Guid? contractId, bool? withBillingSlip, int? page, int? pageSize, string? sort, ReceivableService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new ReceivableListQuery(q, situation, partyId, dueFrom, dueTo, contractId, withBillingSlip == true), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort).Normalize(), ct)))
            .RequirePermission("finance.view").WithName("ListReceivables").Produces<ReceivablePage>();
        receivables.MapGet("/discounts-and-cancellations", async (DateOnly? from, DateOnly? to, ReceivableReports reports, TimeProvider clock, CancellationToken ct) =>
            {
                var today = BusinessTime.Today(clock);
                var start = from ?? new DateOnly(today.Year, today.Month, 1);
                return Results.Ok(await reports.DiscountsAndCancellationsAsync(start, to ?? start.AddMonths(1).AddDays(-1), ct));
            })
            .RequirePermission("finance.view").WithName("GetDiscountsAndCancellations").Produces<DiscountsAndCancellations>();
        receivables.MapPost("/split-preview", async (SplitPreviewInput input, ReceivableService service, CancellationToken ct) => (await service.SplitPreviewAsync(input, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("PreviewReceivableSplit").Produces<IReadOnlyList<InstallmentDue>>();
        receivables.MapGet("/{id:guid}", async (Guid id, ReceivableService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetReceivable").Produces<ReceivableDetail>();
        receivables.MapGet("/{id:guid}/settlements", async (Guid id, ReceivableService service, CancellationToken ct) => (await service.SettlementsAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("ListReceivableSettlements").Produces<IReadOnlyList<SettlementView>>();
        receivables.MapPost(string.Empty, async (ReceivableInput input, ReceivableService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/receivables/" + saved.Items[0].Id, saved)))
            .RequirePermission("finance.create").RequireIdempotencyKey().WithName("CreateReceivable").Produces<ReceivablesCreated>(StatusCodes.Status201Created);
        receivables.MapPost("/installments", async (ReceivableInstallmentsInput input, ReceivableService service, CancellationToken ct) =>
                (await service.CreateInstallmentsAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/receivables/" + saved.Items[0].Id, saved)))
            .RequirePermission("finance.create").RequireIdempotencyKey().WithName("CreateReceivableInstallments").Produces<ReceivablesCreated>(StatusCodes.Status201Created);
        receivables.MapPut("/{id:guid}", async (Guid id, ReceivableUpdateInput input, ReceivableService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("UpdateReceivable").Produces<ReceivableDetail>();
        receivables.MapPost("/{id:guid}/due-date", async (Guid id, DueDateInput input, ReceivableService service, CancellationToken ct) => (await service.ChangeDueDateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ChangeReceivableDueDate").Produces<ReceivableDetail>();
        receivables.MapPost("/{id:guid}/settlements", async (Guid id, SettlementInput input, ReceivableService service, CancellationToken ct) => (await service.SettleAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("SettleReceivable").Produces<ReceivableDetail>();
        receivables.MapPost("/{id:guid}/write-off", async (Guid id, WriteOffInput input, ReceivableService service, CancellationToken ct) => (await service.WriteOffAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("WriteOffReceivable").Produces<ReceivableDetail>();
        receivables.MapPost("/{id:guid}/settlements/{settlementId:guid}/reverse", async (Guid id, Guid settlementId, ReasonInput input, ReceivableService service, CancellationToken ct) =>
                (await service.ReverseAsync(id, settlementId, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ReverseReceivableSettlement").Produces<ReceivableDetail>();
        // Baixa feita pelo banco (BNK-02 CA 14, D6): estorno com finance.approve e o 2FA de novo; pela rota comum, 403.
        receivables.MapPost("/{id:guid}/settlements/{settlementId:guid}/reverse-bank", async (Guid id, Guid settlementId, ReasonInput input, ReceivableService service, CancellationToken ct) =>
                (await service.ReverseAsync(id, settlementId, input, bankReversal: true, ct)).ToHttpResult())
            .RequirePermission("finance.approve").RequireRecentTwoFactor().WithName("ReverseBankSettlement").Produces<ReceivableDetail>();
        receivables.MapPost("/{id:guid}/cancel", async (Guid id, ReasonInput input, ReceivableService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("CancelReceivable").Produces<ReceivableDetail>();
    }

    private static void MapCustomers(RouteGroupBuilder customers) =>
        customers.MapGet(string.Empty, async (string? q, ReceivableService service, CancellationToken ct) => Results.Ok(await service.CustomersAsync(q, ct)))
            .RequirePermission("finance.view").WithName("ListFinanceCustomers").Produces<IReadOnlyList<CustomerOption>>();

    private static void MapFinanceParameters(RouteGroupBuilder parameters)
    {
        parameters.MapGet(string.Empty, async (IFinanceParameters service, CancellationToken ct) => Results.Ok(await service.LimitsAsync(ct)))
            .RequirePermission("finance.view").WithName("GetFinanceLimits").Produces<FinanceLimits>();
        // Edição em Configurações → Parâmetros: o serviço confere settings.edit.
        parameters.MapPut(string.Empty, async (FinanceLimitsInput input, FinanceParametersService service, CancellationToken ct) => (await service.UpdateAsync(input, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("UpdateFinanceLimits").Produces<FinanceLimits>();
    }
}
