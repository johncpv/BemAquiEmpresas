using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Commercial.Application;

public sealed partial class ProjectMarginService
{
    /// <summary>
    /// Realizado por período (DSH-01b CA 6): a regra do realizado do DSH-02 (ADR 0025 §1) pela data de cada lançamento. Razão por
    /// <c>occurred_on</c> (baixa, devolução, horas e a correção; o imposto da nota vai para Impostos) e títulos confirmados pela emissão. Duas
    /// consultas agregadas no banco, para qualquer quantidade de projetos.
    /// </summary>
    public async Task<Result<IReadOnlyList<ProjectRealizedByPeriod>>> RealizedByPeriodAsync(
        IReadOnlyCollection<Guid> projectIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (!await CanCostAsync(cancellationToken))
        {
            return NoCost();
        }

        if (projectIds.Count == 0)
        {
            return Result<IReadOnlyList<ProjectRealizedByPeriod>>.Success([]);
        }

        var ids = projectIds.Distinct().ToArray();
        // Muitos projetos (o resultado por projeto): os títulos do Finance numa conexão própria, em paralelo com a razão (como no ComputeAsync).
        var parallelPayables = ids.Length >= ParallelPayablesFrom && tenant.Tenant is { } current ? SeparateConfirmedAsync(current, ids, from, to, cancellationToken) : null;
        var ledger = await db.ProjectCosts.AsNoTracking()
            .Where(c => EF.Parameter(ids).Contains(c.ProjectId) && c.OccurredOn >= from && c.OccurredOn <= to)
            .GroupBy(c => new
            {
                c.ProjectId,
                CostType = c.Source == ProjectCostSource.InvoiceTax || c.Source == ProjectCostSource.InvoiceTaxReversal ? CostType.Tax : c.CostType,
                c.OccurredOn.Year,
                c.OccurredOn.Month,
            })
            .Select(g => new { g.Key.ProjectId, g.Key.CostType, g.Key.Year, g.Key.Month, Amount = g.Sum(c => c.Amount) })
            .ToListAsync(cancellationToken);
        var payables = parallelPayables is null ? await payableCosts.ConfirmedByMonthAsync(ids, from, to, cancellationToken) : await parallelPayables;
        var rows = ledger.Select(l => (l.ProjectId, l.CostType, l.Year, l.Month, l.Amount))
            .Concat(payables.Select(p => (p.ProjectId, p.CostType, p.Year, p.Month, p.Amount)))
            .GroupBy(r => (r.ProjectId, r.CostType, r.Year, r.Month))
            .Select(g => new ProjectRealizedByPeriod(g.Key.ProjectId, g.Key.Year, g.Key.Month, g.Key.CostType, g.Sum(r => r.Amount)))
            .Where(r => r.Amount != 0)
            .OrderBy(r => r.ProjectId).ThenBy(r => r.Year).ThenBy(r => r.Month).ThenBy(r => r.CostType)
            .ToList();
        return Result<IReadOnlyList<ProjectRealizedByPeriod>>.Success(rows);
    }

    private async Task<IReadOnlyList<ProjectPayableCostByMonth>> SeparateConfirmedAsync(BemAqui.BuildingBlocks.Tenancy.TenantInfo current, Guid[] ids, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var scope = await scopes.CreateScopeAsync(current, ct);
        return await scope.ServiceProvider.GetRequiredService<IProjectPayableCosts>().ConfirmedByMonthAsync(ids, from, to, ct);
    }

    /// <summary>
    /// Lançamentos do realizado no período (DSH-01b CA 8), os mesmos do drill-down do DSH-02 recortados pela data: baixas e devoluções por OS,
    /// horas agregadas por OS e centro (sem nome nem custo-hora), imposto da nota e títulos confirmados pela emissão. Projeto inexistente: 404.
    /// </summary>
    public async Task<Result<IReadOnlyList<ProjectRealizedEntry>>> RealizedEntriesAsync(
        Guid projectId, CostType? costType, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (!await CanCostAsync(cancellationToken))
        {
            return NoCost();
        }

        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return ProjectService.NotFound();
        }

        var links = await LinksAsync(cancellationToken);
        var ledger = await db.ProjectCosts.AsNoTracking().Where(c => c.ProjectId == projectId && c.OccurredOn >= from && c.OccurredOn <= to)
            .Select(c => new LedgerRow(c.CostType, c.Source, c.SourceId, c.ProductionOrderId, c.OccurredOn, c.Amount)).ToListAsync(cancellationToken);
        CostType[] types = costType is { } only ? [only] : Enum.GetValues<CostType>();
        var result = new List<ProjectRealizedEntry>();
        foreach (var type in types)
        {
            var entries = new List<ProjectCostEntry>();
            if (type == CostType.Tax)
            {
                entries.AddRange(ledger.Where(l => ProjectMarginCalculator.IsInvoiceTax(l.Source)).GroupBy(l => (l.Source, l.SourceId))
                    .Select(g => new ProjectCostEntry(
                        g.Key.Source == ProjectCostSource.InvoiceTax ? ProjectCostEntryKind.InvoiceTax : ProjectCostEntryKind.InvoiceTaxReversal, "Nota fiscal", g.Min(l => l.OccurredOn),
                        g.Sum(l => l.Amount), null, null)));
            }
            else
            {
                var mine = ledger.Where(l => l.CostType == type && !ProjectMarginCalculator.IsInvoiceTax(l.Source)).ToList();
                var orderNumbers = await OrderNumbersAsync(mine.Where(l => l.OrderId is not null).Select(l => l.OrderId!.Value).Distinct().ToList(), cancellationToken);
                entries.AddRange(mine.Where(l => ProjectMarginCalculator.IsStockMovement(l.Source)).GroupBy(l => (l.Source, l.SourceId, l.OrderId))
                    .Select(g =>
                    {
                        var order = g.Key.OrderId is { } o ? orderNumbers.GetValueOrDefault(o, "OS") : "OS";
                        var issue = g.Key.Source == ProjectCostSource.StockIssue;
                        return new ProjectCostEntry(
                            issue ? ProjectCostEntryKind.StockIssue : ProjectCostEntryKind.StockReturn, issue ? $"Baixa para a {order}" : $"Devolução da {order}", g.Max(l => l.OccurredOn),
                            g.Sum(l => l.Amount), null, links.Inventory ? $"/app/estoque/movimentos/{g.Key.SourceId}" : null);
                    }));
                var labor = mine.Where(l => l.Source is ProjectCostSource.Labor or ProjectCostSource.LaborCorrection).ToList();
                if (labor.Count > 0)
                {
                    entries.AddRange(await LaborEntriesAsync(labor, links, cancellationToken));
                }

                var payables = (await payableCosts.CostEntriesAsync(projectId, type, cancellationToken))
                    .Where(p => p.Bucket == PayableCostBucket.Confirmed && p.IssueDate >= from && p.IssueDate <= to).ToList();
                entries.AddRange(await PayableEntriesAsync(payables, links, cancellationToken));
            }

            result.AddRange(Ordered(entries).Where(e => e.Amount != 0)
                .Select(e => new ProjectRealizedEntry(e.Kind.ToString(), type, e.Document, e.Date, e.Amount, e.Hours, e.Link)));
        }

        return Result<IReadOnlyList<ProjectRealizedEntry>>.Success(result);
    }
}
