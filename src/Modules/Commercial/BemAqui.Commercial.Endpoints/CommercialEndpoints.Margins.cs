using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Contracts;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

internal static partial class CommercialEndpoints
{
    /// <summary>
    /// Orçado × realizado (DSH-02): relatório ranqueado, consolidado e drill-down, todos no módulo <c>projects</c> (grupo com
    /// <c>RequireModule</c>) e com <c>projects.view_cost</c> (CA 19 e 20); ordenação por allowlist (422) e natureza validada (422).
    /// </summary>
    private static void MapMargins(RouteGroupBuilder projects)
    {
        projects.MapGet("/margins/ranking", async (
                    string? situation, Guid? partyId, Guid? managerId, DateOnly? signedFrom, DateOnly? signedTo, bool? belowBudget, DateOnly? deliveredFrom, DateOnly? deliveredTo, string? sort, int? page, int? pageSize,
                    ProjectMarginService service, CancellationToken ct) =>
                (await service.RankingAsync(Filter(situation, partyId, managerId, signedFrom, signedTo, belowBudget, deliveredFrom, deliveredTo), sort,
                    new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)).ToHttpResult())
            .RequirePermission("projects.view_cost").WithName("ListProjectMarginRanking").Produces<ProjectMarginRanking>();
        projects.MapGet("/margins/portfolio", async (
                    string? situation, Guid? partyId, Guid? managerId, DateOnly? signedFrom, DateOnly? signedTo, bool? belowBudget, DateOnly? deliveredFrom, DateOnly? deliveredTo, ProjectMarginService service, CancellationToken ct) =>
                (await service.PortfolioAsync(Filter(situation, partyId, managerId, signedFrom, signedTo, belowBudget, deliveredFrom, deliveredTo), ct)).ToHttpResult())
            .RequirePermission("projects.view_cost").WithName("GetProjectMarginPortfolio").Produces<ProjectMarginPortfolio>();
        projects.MapGet("/margins/entries", async (
                    string costType, string? column, string? situation, Guid? partyId, Guid? managerId, DateOnly? signedFrom, DateOnly? signedTo, bool? belowBudget, DateOnly? deliveredFrom, DateOnly? deliveredTo,
                    ProjectMarginService service, CancellationToken ct) =>
                ParseCostType(costType) is { } type
                    ? (await service.PortfolioEntriesAsync(Filter(situation, partyId, managerId, signedFrom, signedTo, belowBudget, deliveredFrom, deliveredTo), type, column, ct)).ToHttpResult()
                    : InvalidCostType())
            .RequirePermission("projects.view_cost").WithName("ListProjectMarginEntries").Produces<ProjectCostEntries>();
        projects.MapGet("/{id:guid}/costs/labor/workers", async (Guid id, ProjectMarginService service, CancellationToken ct) => (await service.LaborWorkersAsync(id, ct)).ToHttpResult())
            .RequirePermission("projects.view_cost").WithName("ListProjectLaborWorkers").Produces<ProjectLaborWorkers>();
        projects.MapGet("/{id:guid}/costs/{costType}/entries", async (Guid id, string costType, string? column, ProjectMarginService service, CancellationToken ct) =>
                ParseCostType(costType) is { } type ? (await service.EntriesAsync(id, type, column, ct)).ToHttpResult() : InvalidCostType())
            .RequirePermission("projects.view_cost").WithName("ListProjectCostEntries").Produces<ProjectCostEntries>();
    }

    private static ProjectMarginFilter Filter(
        string? situation, Guid? partyId, Guid? managerId, DateOnly? signedFrom, DateOnly? signedTo, bool? belowBudget, DateOnly? deliveredFrom = null, DateOnly? deliveredTo = null) =>
        new(string.IsNullOrWhiteSpace(situation) ? null : situation, partyId, managerId, signedFrom, signedTo, belowBudget ?? false, deliveredFrom, deliveredTo);

    private static CostType? ParseCostType(string value) =>
        Enum.TryParse<CostType>(value, ignoreCase: true, out var type) && Enum.IsDefined(type) && !int.TryParse(value, out _) ? type : null;

    private static IResult InvalidCostType() =>
        Result.Failure(Error.Validation("validation", "Natureza de custo inválida.", new Dictionary<string, string[]> { ["costType"] = ["Natureza de custo inválida."] })).ToHttpResult();
}
