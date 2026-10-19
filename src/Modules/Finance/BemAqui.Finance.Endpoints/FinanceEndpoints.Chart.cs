using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

internal static partial class FinanceEndpoints
{
    /// <summary>Plano de contas (CAD-05a CA 4 a 15).</summary>
    private static void MapChartOfAccounts(RouteGroupBuilder accounts)
    {
        accounts.MapGet(string.Empty, async (AccountService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("finance.view").WithName("ListAccounts").Produces<IReadOnlyList<AccountDetail>>();
        accounts.MapGet("/suggestion", async (Guid? parentId, AccountService service, CancellationToken ct) => (await service.SuggestAsync(parentId, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("SuggestAccount").Produces<AccountSuggestion>();
        accounts.MapPost(string.Empty, async (AccountInput input, AccountService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/chart-of-accounts/" + saved.Id, saved)))
            .RequirePermission("finance.create").WithName("CreateAccount").Produces<AccountDetail>(StatusCodes.Status201Created);
        accounts.MapPut("/{id:guid}", async (Guid id, AccountInput input, AccountService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("UpdateAccount").Produces<AccountDetail>();
        accounts.MapPost("/{id:guid}/inactivate", async (Guid id, AccountService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("InactivateAccount").Produces<AccountDetail>();
        accounts.MapPost("/{id:guid}/reactivate", async (Guid id, AccountService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ReactivateAccount").Produces<AccountDetail>();
        accounts.MapDelete("/{id:guid}", async (Guid id, AccountService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.delete").WithName("DeleteAccount");
    }

    /// <summary>Centros de custo (CAD-05a CA 16).</summary>
    private static void MapCostCenters(RouteGroupBuilder centers)
    {
        centers.MapGet(string.Empty, async (CostCenterService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("finance.view").WithName("ListCostCenters").Produces<IReadOnlyList<CostCenterDetail>>();
        centers.MapGet("/suggestion", async (Guid? parentId, CostCenterService service, CancellationToken ct) =>
                (await service.SuggestCodeAsync(parentId, ct)).ToHttpResult(code => Results.Ok(new CostCenterSuggestion(code))))
            .RequirePermission("finance.view").WithName("SuggestCostCenter").Produces<CostCenterSuggestion>();
        centers.MapPost(string.Empty, async (CostCenterInput input, CostCenterService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/finance/cost-centers/" + saved.Id, saved)))
            .RequirePermission("finance.create").WithName("CreateCostCenter").Produces<CostCenterDetail>(StatusCodes.Status201Created);
        centers.MapPut("/{id:guid}", async (Guid id, CostCenterInput input, CostCenterService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("UpdateCostCenter").Produces<CostCenterDetail>();
        centers.MapPost("/{id:guid}/inactivate", async (Guid id, CostCenterService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("InactivateCostCenter").Produces<CostCenterDetail>();
        centers.MapPost("/{id:guid}/reactivate", async (Guid id, CostCenterService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("finance.edit").WithName("ReactivateCostCenter").Produces<CostCenterDetail>();
        centers.MapDelete("/{id:guid}", async (Guid id, CostCenterService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.delete").WithName("DeleteCostCenter");
    }
}
