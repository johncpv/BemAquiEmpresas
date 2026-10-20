using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

internal static partial class CommercialEndpoints
{
    /// <summary>Aprovar e gerar contrato (COM-04a): no módulo <c>quotes</c>; o <c>projects</c> bloqueado não impede (CA 19).</summary>
    private static void MapApproval(RouteGroupBuilder quotes)
    {
        quotes.MapGet("/{id:guid}/approval", async (Guid id, QuoteApprovalService service, CancellationToken ct) => (await service.PreviewAsync(id, ct)).ToHttpResult())
            .RequirePermission("quotes.approve").WithName("GetQuoteApproval").Produces<ApprovalPreview>();
        quotes.MapGet("/{id:guid}/approval/costs", async (Guid id, QuoteApprovalService service, CancellationToken ct) => (await service.CostAlertsAsync(id, ct)).ToHttpResult())
            .RequirePermission("quotes.approve").WithName("GetQuoteApprovalCosts").Produces<ApprovalCostView>();
        quotes.MapPost("/{id:guid}/approve", async (Guid id, ApproveInput input, QuoteApprovalService service, CancellationToken ct) =>
                (await service.ApproveAsync(id, input, ct)).ToHttpResult(saved => Results.Created("/api/v1/contracts/" + saved.ContractId, saved)))
            .RequirePermission("quotes.approve").RequireIdempotencyKey().WithName("ApproveQuote").Produces<ApprovalResult>(StatusCodes.Status201Created);
        quotes.MapGet("/{id:guid}/contract", async (Guid id, ContractService service, CancellationToken ct) => Results.Ok(new QuoteContractRef(await service.ByQuoteAsync(id, ct))))
            .RequirePermission("quotes.view").WithName("GetQuoteContract").Produces<QuoteContractRef>();
    }

    /// <summary>Contratos e projetos (COM-04a CA 16, 17): módulo <c>projects</c>, só leitura.</summary>
    private static void MapContracts(IEndpointRouteBuilder api)
    {
        var contracts = api.MapGroup("/contracts").WithTags("Contratos e projetos").RequireModule("projects");
        contracts.MapGet(string.Empty, async (string? q, string? situation, int? page, int? pageSize, ContractService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(q, situation, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("projects.view").WithName("ListContracts").Produces<ContractPage>();
        contracts.MapGet("/{id:guid}", async (Guid id, ContractService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("projects.view").WithName("GetContract").Produces<ContractDetail>();

        var projects = api.MapGroup("/projects").WithTags("Contratos e projetos").RequireModule("projects");
        projects.MapGet("/{id:guid}", async (Guid id, ProjectService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("projects.view").WithName("GetProject").Produces<ProjectDetail>();
        projects.MapGet("/{id:guid}/budget", async (Guid id, ProjectService service, CancellationToken ct) => (await service.BudgetAsync(id, ct)).ToHttpResult())
            .RequirePermission("projects.view_cost").WithName("GetProjectBudget").Produces<IReadOnlyList<ProjectBudgetLine>>();
    }
}

public sealed record QuoteContractRef(Guid? ContractId);
