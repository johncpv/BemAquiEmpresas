using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

internal static partial class FinanceEndpoints
{
    /// <summary>Alçadas (FIN-03b): finance.view vê; só o Proprietário altera (conferido no serviço).</summary>
    private static void MapApprovalRules(RouteGroupBuilder rules)
    {
        rules.MapGet(string.Empty, async (ApprovalRuleService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("finance.view").WithName("ListApprovalRules").Produces<IReadOnlyList<ApprovalRuleView>>();
        rules.MapGet("/approvers", async (ApprovalRuleService service, CancellationToken ct) => Results.Ok(await service.ApproversAsync(ct)))
            .RequirePermission("finance.view").WithName("ListApprovalApprovers").Produces<IReadOnlyList<ApproverOption>>();
        rules.MapGet("/simulate", async (decimal amount, Guid? accountId, Guid? partyId, ApprovalSimulator service, CancellationToken ct) => (await service.SimulateAsync(amount, accountId, partyId, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("SimulateApproval").Produces<ApprovalSimulation>();
        rules.MapPost(string.Empty, async (ApprovalRuleInput input, ApprovalRuleService service, CancellationToken ct) => (await service.CreateAsync(input, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("CreateApprovalRule").Produces<ApprovalRuleView>();
        rules.MapPut("/{id:guid}", async (Guid id, ApprovalRuleInput input, ApprovalRuleService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("UpdateApprovalRule").Produces<ApprovalRuleView>();
        rules.MapDelete("/{id:guid}", async (Guid id, ApprovalRuleService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("DeleteApprovalRule");
    }
}
