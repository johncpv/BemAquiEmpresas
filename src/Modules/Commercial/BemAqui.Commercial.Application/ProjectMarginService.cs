using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Orçado × realizado e margem (DSH-02): um cálculo só (<see cref="ProjectMarginCalculator"/>) para a aba Custos do Projeto 360, o relatório
/// "Orçado × realizado" e o <see cref="IProjectMargins"/> do painel do dono. Fontes: a razão <c>commercial.project_costs</c> e o orçado (do
/// Commercial), os títulos pelo <see cref="IProjectPayableCosts"/> (em lote) e os nomes pelo <see cref="IParties"/> só da página. Tudo com
/// <c>projects.view_cost</c> (CA 19); sem cache nesta story (CA 18).
/// </summary>
public sealed partial class ProjectMarginService(
    ICommercialDb db, IProjectPayableCosts payableCosts, IParties parties, IUserDirectory users, IPermissionChecker permissions, IProjectProductionCosts productionCosts,
    IProductionOrders productionOrders)
    : IProjectMargins
{
    /// <summary>Ordenações aceitas (allowlist, CA 20); fora delas, 422.</summary>
    public static readonly IReadOnlySet<string> Sorts = new HashSet<string>(StringComparer.Ordinal) { "deviation", "-deviation", "number", "revenue", "-revenue", "projectedMargin", "-projectedMargin" };

    /// <summary>Aba Custos (COM-05, PRD-04, DSH-02 CA 1 a 6 e 12): os números do projeto e a nota das NC, sem somar em coluna nenhuma.</summary>
    public async Task<Result<ProjectCostView>> CostsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await CanCostAsync(cancellationToken))
        {
            return NoCost();
        }

        var computed = await ComputeAsync(db.Projects.Where(p => p.Id == id), cancellationToken);
        if (computed.Count == 0)
        {
            return ProjectService.NotFound();
        }

        var f = computed[0].Figures;
        var nc = await productionCosts.NonConformitiesAsync(id, cancellationToken);
        return new ProjectCostView(
            f.Lines, f.Total, f.Revenue, f.BudgetedMarginPct, f.ProjectedMarginPct, f.BudgetedMargin, f.RealizedMargin, f.RealizedMarginPct, f.ProjectedMargin, f.MarginDeviationPp, f.TaxBasis,
            f.TaxGuides, nc.Count, nc.EstimatedCost);
    }

    /// <summary>Lista ranqueada (CA 9 e 11): calcula todos os filtrados (agregação no banco) e resolve nomes só da página.</summary>
    public async Task<Result<ProjectMarginRanking>> RankingAsync(ProjectMarginFilter filter, string? sort, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);
        if (!await CanCostAsync(cancellationToken))
        {
            return NoCost();
        }

        sort ??= "deviation";
        if (!Sorts.Contains(sort))
        {
            return QuoteService.Invalid("sort", "Ordene pelo desvio, número, receita ou margem projetada.");
        }

        var filtered = await FilteredAsync(filter, cancellationToken);
        if (filtered.IsFailure)
        {
            return filtered.Error!;
        }

        var rows = Sort(filtered.Value, sort).ToList();
        var pageRows = rows.Skip(page.Skip).Take(page.PageSize).ToList();
        return new ProjectMarginRanking(await RowsAsync(pageRows, cancellationToken), page.Page, page.PageSize, rows.Count);
    }

    /// <summary>Consolidado dos projetos filtrados (CA 10): soma por natureza e margens ponderadas pela receita.</summary>
    public async Task<Result<ProjectMarginPortfolio>> PortfolioAsync(ProjectMarginFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (!await CanCostAsync(cancellationToken))
        {
            return NoCost();
        }

        var filtered = await FilteredAsync(filter, cancellationToken);
        if (filtered.IsFailure)
        {
            return filtered.Error!;
        }

        var figures = ProjectCosts.Figures(ProjectMarginCalculator.Consolidate(filtered.Value.Select(c => c.Result)));
        return new ProjectMarginPortfolio(filtered.Value.Count, figures, ProjectCosts.Worst(figures));
    }

    public async Task<Result<ProjectMarginRow>> ForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!await CanCostAsync(cancellationToken))
        {
            return NoCost();
        }

        var computed = await ComputeAsync(db.Projects.Where(p => p.Id == projectId), cancellationToken);
        if (computed.Count == 0)
        {
            return ProjectService.NotFound();
        }

        return (await RowsAsync(computed, cancellationToken))[0];
    }

    public async Task<Result<IReadOnlyList<ProjectMarginRow>>> ForProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (!await CanCostAsync(cancellationToken))
        {
            return NoCost();
        }

        var ids = projectIds.Distinct().Take(5_000).ToArray();
        var computed = await ComputeAsync(db.Projects.Where(p => ids.Contains(p.Id)), cancellationToken);
        return Result<IReadOnlyList<ProjectMarginRow>>.Success(await RowsAsync(computed, cancellationToken));
    }

    /// <summary>
    /// Projetos do filtro com os números (CA 9 e 11): situação (padrão "em andamento"), cliente, responsável, assinatura e "Só com margem
    /// abaixo da orçada" (aplicado depois do cálculo). Situação fora da lista: 422.
    /// </summary>
    private async Task<Result<List<ComputedProject>>> FilteredAsync(ProjectMarginFilter filter, CancellationToken ct)
    {
        var projects = db.Projects.AsQueryable();
        switch (filter.Situation ?? "andamento")
        {
            case "andamento":
                projects = projects.Where(p => p.Status == ProjectStatus.Planned || p.Status == ProjectStatus.InProgress || p.Status == ProjectStatus.Fabricated || p.Status == ProjectStatus.Installing);
                break;
            case "entregue":
                projects = projects.Where(p => p.Status == ProjectStatus.Delivered);
                break;
            case "encerrado":
                projects = projects.Where(p => p.Status == ProjectStatus.Closed);
                break;
            case "cancelado":
                projects = projects.Where(p => p.Status == ProjectStatus.Cancelled);
                break;
            case "todos":
                break;
            default:
                return QuoteService.Invalid("situation", "Escolha em andamento, entregues, encerrados, cancelados ou todos.");
        }

        if (filter.PartyId is { } party)
        {
            projects = projects.Where(p => p.PartyId == party);
        }

        if (filter.ManagerId is { } manager)
        {
            projects = projects.Where(p => p.ManagerUserId == manager);
        }

        if (filter.SignedFrom is { } from)
        {
            projects = projects.Where(p => db.Contracts.Any(c => c.Id == p.ContractId && c.SignedAt >= from));
        }

        if (filter.SignedTo is { } to)
        {
            projects = projects.Where(p => db.Contracts.Any(c => c.Id == p.ContractId && c.SignedAt <= to));
        }

        var computed = await ComputeAsync(projects, ct);
        return filter.BelowBudgetOnly ? computed.Where(c => c.Result.MarginDeviationPp < 0).ToList() : computed;
    }

    /// <summary>
    /// O cálculo de um conjunto de projetos (CA 1 e 18): uma consulta por fonte, agregada no banco (projetos com o contrato, aditivos aprovados,
    /// orçado, razão e títulos pelo Finance), e o <see cref="ProjectMarginCalculator"/> em memória.
    /// </summary>
    internal async Task<List<ComputedProject>> ComputeAsync(IQueryable<Project> projects, CancellationToken ct)
    {
        var heads = await projects.AsNoTracking()
            .Join(db.Contracts, p => p.ContractId, c => c.Id, (p, c) => new ProjectHead(p.Id, p.Number, p.Name, p.PartyId, p.Status, p.ManagerUserId, p.ContractId, c.SignedAt, c.TotalValue))
            .ToListAsync(ct);
        if (heads.Count == 0)
        {
            return [];
        }

        var ids = heads.Select(h => h.Id).ToArray();
        var contractIds = heads.Select(h => h.ContractId).Distinct().ToArray();
        var amendments = await db.ContractAmendments.AsNoTracking().Where(a => contractIds.Contains(a.ContractId) && a.Status == AmendmentStatus.Approved)
            .GroupBy(a => a.ContractId).Select(g => new { g.Key, Delta = g.Sum(a => a.ValueDelta) }).ToDictionaryAsync(g => g.Key, g => g.Delta, ct);
        var budgets = (await db.ProjectBudgets.AsNoTracking().Where(b => ids.Contains(b.ProjectId))
            .GroupBy(b => new { b.ProjectId, b.CostType }).Select(g => new { g.Key.ProjectId, g.Key.CostType, Amount = g.Sum(b => b.BudgetedAmount) }).ToListAsync(ct))
            .ToLookup(b => b.ProjectId);
        var ledger = (await db.ProjectCosts.AsNoTracking().Where(c => ids.Contains(c.ProjectId))
            .GroupBy(c => new { c.ProjectId, c.CostType, c.Source, c.IsBackfill })
            .Select(g => new { g.Key.ProjectId, g.Key.CostType, g.Key.Source, g.Key.IsBackfill, Amount = g.Sum(c => c.Amount), Invoice = g.Sum(c => c.InvoiceAmount ?? 0) })
            .ToListAsync(ct)).ToLookup(l => l.ProjectId);
        var payables = (await payableCosts.CostSummaryByProjectsAsync(ids, ct)).ToLookup(p => p.ProjectId);
        return heads.Select(h =>
        {
            var revenue = h.ContractValue + amendments.GetValueOrDefault(h.ContractId);
            var result = ProjectMarginCalculator.Compute(
                revenue, budgets[h.Id].ToDictionary(b => b.CostType, b => b.Amount), ProjectCosts.Totals(payables[h.Id]),
                ledger[h.Id].Select(l => new LedgerCostTotals(l.CostType, l.Source, l.IsBackfill, l.Amount, l.Invoice)).ToList());
            return new ComputedProject(h, result, ProjectCosts.Figures(result));
        }).ToList();
    }

    private static IEnumerable<ComputedProject> Sort(IEnumerable<ComputedProject> rows, string sort) => sort switch
    {
        "-deviation" => rows.OrderBy(r => r.Result.MarginDeviationPp is null).ThenByDescending(r => r.Result.MarginDeviationPp).ThenBy(r => r.Head.Number, StringComparer.Ordinal),
        "number" => rows.OrderBy(r => r.Head.Number, StringComparer.Ordinal),
        "revenue" => rows.OrderBy(r => r.Result.Revenue).ThenBy(r => r.Head.Number, StringComparer.Ordinal),
        "-revenue" => rows.OrderByDescending(r => r.Result.Revenue).ThenBy(r => r.Head.Number, StringComparer.Ordinal),
        "projectedMargin" => rows.OrderBy(r => r.Result.ProjectedMarginPct is null).ThenBy(r => r.Result.ProjectedMarginPct).ThenBy(r => r.Head.Number, StringComparer.Ordinal),
        "-projectedMargin" => rows.OrderBy(r => r.Result.ProjectedMarginPct is null).ThenByDescending(r => r.Result.ProjectedMarginPct).ThenBy(r => r.Head.Number, StringComparer.Ordinal),
        // Ranking do PRD §5.14: piores primeiro (desvio da margem mais negativo); sem receita no fim.
        _ => rows.OrderBy(r => r.Result.MarginDeviationPp is null).ThenBy(r => r.Result.MarginDeviationPp).ThenBy(r => r.Head.Number, StringComparer.Ordinal),
    };

    /// <summary>Cabeçalho com os nomes da página (cliente pelo <see cref="IParties"/> em lote, responsável pelo diretório).</summary>
    private async Task<IReadOnlyList<ProjectMarginRow>> RowsAsync(IReadOnlyList<ComputedProject> rows, CancellationToken ct)
    {
        var names = (await parties.FindManyAsync(rows.Select(r => r.Head.PartyId).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var managers = new Dictionary<Guid, string?>();
        foreach (var userId in rows.Where(r => r.Head.ManagerUserId is not null).Select(r => r.Head.ManagerUserId!.Value).Distinct())
        {
            managers[userId] = await users.DisplayNameAsync(userId, ct);
        }

        return rows.Select(r => new ProjectMarginRow(
            new ProjectMarginHeader(
                r.Head.Id, r.Head.Number, r.Head.Name, r.Head.PartyId, names.GetValueOrDefault(r.Head.PartyId, string.Empty), r.Head.Status.ToString(), r.Head.ManagerUserId,
                r.Head.ManagerUserId is { } m ? managers.GetValueOrDefault(m) : null, r.Head.ContractId, r.Head.SignedAt),
            r.Figures, ProjectCosts.Worst(r.Figures))).ToList();
    }

    private async Task<bool> CanCostAsync(CancellationToken ct) => await permissions.HasPermissionAsync("projects", "view_cost", ct);

    private static Error NoCost() => Error.Forbidden(Error.Codes.Forbidden, "Custo e margem do projeto são restritos. Fale com o administrador.");

    internal sealed record ProjectHead(Guid Id, string Number, string Name, Guid PartyId, ProjectStatus Status, Guid? ManagerUserId, Guid ContractId, DateOnly SignedAt, decimal ContractValue);

    internal sealed record ComputedProject(ProjectHead Head, MarginResult Result, ProjectMarginFigures Figures);
}
