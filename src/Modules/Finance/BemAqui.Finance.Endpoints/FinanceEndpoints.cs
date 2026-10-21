using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>
/// Contas e caixas e condições de pagamento (CAD-05b). Módulo <c>finance</c> com as três camadas de acesso (CA 18); respostas
/// com <c>no-store</c>, porque trazem dados bancários.
/// </summary>
internal static partial class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder api)
    {
        var finance = api.MapGroup("/finance").WithTags("Financeiro").RequireModule("finance")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        finance.MapGet("/banks", () => Results.Ok(Banks.All)).RequirePermission("finance.view").WithName("ListBanks").Produces<IReadOnlyList<BankInfo>>();
        MapBankAccounts(finance.MapGroup("/bank-accounts"));
        MapPaymentTerms(finance.MapGroup("/payment-terms"));
        MapChartOfAccounts(finance.MapGroup("/chart-of-accounts"));
        MapCostCenters(finance.MapGroup("/cost-centers"));
        MapReceivables(finance.MapGroup("/receivables"));
        MapFinanceParameters(finance.MapGroup("/parameters"));
        MapCustomers(finance.MapGroup("/customers"));
        MapPayables(finance.MapGroup("/payables"));
        MapSuppliers(finance.MapGroup("/suppliers"));
        MapProjects(finance.MapGroup("/projects"));
        MapDeadLetters(finance.MapGroup("/dead-letters"));
        MapRecurringExpenses(finance.MapGroup("/recurring-expenses"));
        MapOpening(finance.MapGroup("/opening"));
        MapCashFlow(finance.MapGroup("/cash-flow"));
        MapPaymentOrders(finance.MapGroup("/payment-orders"));
        MapBanking(finance);
        finance.MapGet("/aging", async (string? sort, AgingService service, CancellationToken ct) => (await service.ReportAsync(sort, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetAging").Produces<AgingReport>();
        finance.MapGet("/aging/projects", async (AgingService service, CancellationToken ct) => Results.Ok(await service.ProjectsAsync(ct)))
            .RequirePermission("finance.view").WithName("GetAgingByProject").Produces<AgingProjectReport>();
        // Por terceiro (FIN-06b CA 5 a 7): pontualidade é dado sensível do cliente, sem cache.
        finance.MapGet("/parties/{id:guid}/summary", async (Guid id, AgingService service, HttpContext http, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return Results.Ok(await service.PartySummaryAsync(id, ct));
            })
            .RequirePermission("finance.view").WithName("GetPartyFinanceSummary").Produces<PartyFinanceSummary>();
        return api;
    }

    private static void MapBankAccounts(RouteGroupBuilder accounts)
    {
        accounts.MapGet(string.Empty, async (string? q, bool? includeInactive, int? page, int? pageSize, BankAccountService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new BankAccountListQuery(q, includeInactive == true), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("finance.view").WithName("ListBankAccounts").Produces<PagedList<BankAccountDetail>>();
        accounts.MapGet("/{id:guid}", async (Guid id, BankAccountService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetBankAccount").Produces<BankAccountDetail>();
        accounts.MapPost(string.Empty, async (BankAccountInput input, BankAccountService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/bank-accounts/" + saved.Id, saved)))
            .RequirePermission("finance.create").WithName("CreateBankAccount").Produces<BankAccountDetail>(StatusCodes.Status201Created);
        // Trocar banco, agência ou conta exige também finance.approve, conferido no serviço (CA 9).
        accounts.MapPut("/{id:guid}", async (Guid id, BankAccountInput input, BankAccountService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("UpdateBankAccount").Produces<BankAccountDetail>();
        accounts.MapPost("/{id:guid}/inactivate", async (Guid id, BankAccountService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("InactivateBankAccount").Produces<BankAccountDetail>();
        accounts.MapPost("/{id:guid}/reactivate", async (Guid id, BankAccountService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ReactivateBankAccount").Produces<BankAccountDetail>();
        accounts.MapDelete("/{id:guid}", async (Guid id, BankAccountService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.delete").WithName("DeleteBankAccount");
    }
}
