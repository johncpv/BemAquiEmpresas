using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Orçado × realizado e margem (DSH-02): um cálculo só (<see cref="ProjectMarginCalculator"/>) para a aba Custos do Projeto 360, o relatório
/// "Orçado × realizado" e o <see cref="IProjectMargins"/> do painel do dono. Fontes: a razão <c>commercial.project_costs</c> e o orçado (do
/// Commercial), os títulos pelo <see cref="IProjectPayableCosts"/> (em lote) e os nomes pelo <see cref="IParties"/> só da página. Tudo com
/// <c>projects.view_cost</c> (CA 19); sem cache nesta story (CA 18).
/// </summary>
public sealed partial class ProjectMarginService(
    ICommercialDb db, IProjectPayableCosts payableCosts, IParties parties, IUserDirectory users, IPermissionChecker permissions, IProjectProductionCosts productionCosts,
    IProductionOrders productionOrders, ITenantContext tenant, ITenantScopeFactory scopes)
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
        var computed = await ComputeAsync(db.Projects.Where(p => EF.Parameter(ids).Contains(p.Id)), cancellationToken);
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
        // Muitos projetos (o relatório): os títulos do Finance numa conexão própria, em paralelo com as consultas do Commercial abaixo.
        var parallelPayables = ids.Length >= ParallelPayablesFrom && tenant.Tenant is { } current ? SeparatePayablesAsync(current, ids, ct) : null;
        var projectIds = projects.Select(p => p.Id);
        var contractIds = projects.Select(p => p.ContractId);
        var amendments = await db.ContractAmendments.AsNoTracking().Where(a => contractIds.Contains(a.ContractId) && a.Status == AmendmentStatus.Approved)
            .GroupBy(a => a.ContractId).Select(g => new { g.Key, Delta = g.Sum(a => a.ValueDelta) }).ToDictionaryAsync(g => g.Key, g => g.Delta, ct);
        // Orçado numa linha por projeto (uma coluna por natureza): 5 mil linhas em vez de 25 mil.
        var budgets = await db.ProjectBudgets.AsNoTracking().Where(b => projectIds.Contains(b.ProjectId))
            .GroupBy(b => b.ProjectId)
            .Select(g => new BudgetRow(
                g.Key, g.Sum(b => b.CostType == CostType.Material ? b.BudgetedAmount : 0), g.Sum(b => b.CostType == CostType.Labor ? b.BudgetedAmount : 0),
                g.Sum(b => b.CostType == CostType.Installation ? b.BudgetedAmount : 0), g.Sum(b => b.CostType == CostType.Freight ? b.BudgetedAmount : 0),
                g.Sum(b => b.CostType == CostType.Tax ? b.BudgetedAmount : 0), g.Sum(b => b.CostType == CostType.Other ? b.BudgetedAmount : 0)))
            .ToDictionaryAsync(b => b.ProjectId, ct);
        // Razão somada por projeto e natureza, com as somas condicionais do cálculo (LedgerTypeTotals): sem agrupar pela fonte nem ordenar texto.
        var ledger = (await db.ProjectCosts.AsNoTracking().Where(c => projectIds.Contains(c.ProjectId))
            .GroupBy(c => new { c.ProjectId, c.CostType })
            .Select(g => new LedgerTypeRow(
                g.Key.ProjectId, g.Key.CostType, g.Sum(c => c.Amount),
                g.Sum(c => c.Source == ProjectCostSource.StockIssue || c.Source == ProjectCostSource.StockReturn ? c.Amount : 0),
                g.Sum(c => c.Source == ProjectCostSource.InvoiceTax || c.Source == ProjectCostSource.InvoiceTaxReversal ? c.Amount : 0),
                g.Sum(c => c.Source == ProjectCostSource.InvoiceTax || c.Source == ProjectCostSource.InvoiceTaxReversal ? c.InvoiceAmount ?? 0 : 0)))
            .ToListAsync(ct)).ToLookup(l => l.ProjectId);
        // Marca de recomposição (CA 6): só as linhas recompostas, pelo índice parcial; vale a natureza com alguma fonte (fora o imposto) ≠ 0.
        var backfilled = (await db.ProjectCosts.AsNoTracking()
            .Where(c => c.IsBackfill && projectIds.Contains(c.ProjectId) && c.Source != ProjectCostSource.InvoiceTax && c.Source != ProjectCostSource.InvoiceTaxReversal)
            .GroupBy(c => new { c.ProjectId, c.CostType, c.Source })
            .Where(g => g.Sum(c => c.Amount) != 0)
            .Select(g => new { g.Key.ProjectId, g.Key.CostType })
            .Distinct()
            .ToListAsync(ct)).Select(b => (b.ProjectId, b.CostType)).ToHashSet();
        var payables = (parallelPayables is null ? await payableCosts.CostSummaryByProjectsAsync(ids, ct) : await parallelPayables).ToLookup(p => p.ProjectId);
        return heads.Select(h =>
        {
            var revenue = h.ContractValue + amendments.GetValueOrDefault(h.ContractId);
            var result = ProjectMarginCalculator.FromTotals(
                revenue, budgets.TryGetValue(h.Id, out var budget) ? budget.ToDictionary() : NoBudget, ProjectCosts.Totals(payables[h.Id]),
                ledger[h.Id].Select(l => l.Totals(backfilled.Contains((h.Id, l.CostType)))).ToList());
            return new ComputedProject(h, result);
        }).ToList();
    }

    /// <summary>A partir de quantos projetos os títulos vêm numa conexão própria (abaixo disso, a consulta é rápida e não paga o escopo novo).</summary>
    private const int ParallelPayablesFrom = 500;

    /// <summary>Os títulos num escopo próprio do mesmo tenant (outro <c>TenantDbContext</c>, outra conexão), para rodar junto com a razão.</summary>
    private async Task<IReadOnlyList<ProjectPayableCostTotals>> SeparatePayablesAsync(TenantInfo current, Guid[] ids, CancellationToken ct)
    {
        await using var scope = await scopes.CreateScopeAsync(current, ct);
        return await scope.ServiceProvider.GetRequiredService<IProjectPayableCosts>().CostSummaryByProjectsAsync(ids, ct);
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

    /// <summary>Um projeto calculado; os números no formato do contrato só quando pedidos (o ranking ordena pelo resultado e monta só a página).</summary>
    internal sealed record ComputedProject(ProjectHead Head, MarginResult Result)
    {
        private ProjectMarginFigures? figures;

        public ProjectMarginFigures Figures => figures ??= ProjectCosts.Figures(Result);
    }

    private static readonly IReadOnlyDictionary<CostType, decimal> NoBudget = new Dictionary<CostType, decimal>();

    private sealed record BudgetRow(Guid ProjectId, decimal Material, decimal Labor, decimal Installation, decimal Freight, decimal Tax, decimal Other)
    {
        public Dictionary<CostType, decimal> ToDictionary() => new()
        {
            [CostType.Material] = Material, [CostType.Labor] = Labor, [CostType.Installation] = Installation, [CostType.Freight] = Freight, [CostType.Tax] = Tax, [CostType.Other] = Other,
        };
    }

    /// <summary>Uma natureza da razão de um projeto, somada no banco: <c>Total</c> com o imposto da nota, que o cálculo separa.</summary>
    private sealed record LedgerTypeRow(Guid ProjectId, CostType CostType, decimal Total, decimal StockMovement, decimal InvoiceTax, decimal InvoiceAmount)
    {
        public LedgerTypeTotals Totals(bool backfill) => new(CostType, Total - InvoiceTax, StockMovement, InvoiceTax, InvoiceAmount, backfill);
    }
}
