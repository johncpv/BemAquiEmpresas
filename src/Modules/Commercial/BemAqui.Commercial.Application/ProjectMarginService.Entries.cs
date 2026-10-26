using System.Globalization;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class ProjectMarginService
{
    /// <summary>Coluna do drill-down pelo nome da query (<c>budgeted</c>, <c>realized</c>, <c>committed</c>); fora disso, null (422).</summary>
    public static ProjectCostColumn? ParseColumn(string? column) => column?.ToLowerInvariant() switch
    {
        "budgeted" => ProjectCostColumn.Budgeted,
        "realized" => ProjectCostColumn.Realized,
        "committed" => ProjectCostColumn.Committed,
        _ => null,
    };

    /// <summary>
    /// Drill-down de uma célula do projeto (CA 13 e 15): os lançamentos que somam o valor da célula, com o link só para quem tem a permissão do
    /// módulo dono do documento. Mão de obra agregada por OS e centro de trabalho, sem nome nem custo-hora (CA 14). Projeto inexistente: 404.
    /// </summary>
    public async Task<Result<ProjectCostEntries>> EntriesAsync(Guid id, CostType costType, string? column, CancellationToken cancellationToken = default)
    {
        if (!await CanCostAsync(cancellationToken))
        {
            return NoCost();
        }

        if (ParseColumn(column) is not { } col)
        {
            return QuoteService.Invalid("column", "Escolha orçado, realizado ou comprometido.");
        }

        var computed = await ComputeAsync(db.Projects.Where(p => p.Id == id), cancellationToken);
        if (computed.Count == 0)
        {
            return ProjectService.NotFound();
        }

        var project = computed[0];
        var line = project.Figures.Lines.Single(l => l.CostType == costType);
        var links = await LinksAsync(cancellationToken);
        return col switch
        {
            ProjectCostColumn.Budgeted => await BudgetEntriesAsync(project.Head, costType, line.Budgeted, links, cancellationToken),
            ProjectCostColumn.Realized => await RealizedEntriesAsync(id, costType, line.Realized, links, cancellationToken),
            _ => await CommittedEntriesAsync(id, costType, line.Committed, links, cancellationToken),
        };
    }

    /// <summary>Drill-down de uma célula do consolidado (CA 13): uma linha por projeto filtrado com valor na célula, com link para a aba Custos.</summary>
    public async Task<Result<ProjectCostEntries>> PortfolioEntriesAsync(ProjectMarginFilter filter, CostType costType, string? column, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (!await CanCostAsync(cancellationToken))
        {
            return NoCost();
        }

        if (ParseColumn(column) is not { } col)
        {
            return QuoteService.Invalid("column", "Escolha orçado, realizado ou comprometido.");
        }

        var filtered = await FilteredAsync(filter, cancellationToken);
        if (filtered.IsFailure)
        {
            return filtered.Error!;
        }

        var entries = filtered.Value
            .Select(p => (p.Head, Line: p.Figures.Lines.Single(l => l.CostType == costType)))
            .Select(p => (p.Head, Amount: col switch { ProjectCostColumn.Budgeted => p.Line.Budgeted, ProjectCostColumn.Realized => p.Line.Realized, _ => p.Line.Committed }))
            .Where(p => p.Amount != 0)
            .OrderByDescending(p => p.Amount).ThenBy(p => p.Head.Number, StringComparer.Ordinal)
            .Select(p => new ProjectCostEntry(ProjectCostEntryKind.Project, $"{p.Head.Number} · {p.Head.Name}", p.Head.SignedAt, p.Amount, null, $"/app/projetos/{p.Head.Id}?aba=custos"))
            .ToList();
        return new ProjectCostEntries(costType, col, entries.Sum(e => e.Amount), entries, null, null);
    }

    /// <summary>Horas por funcionário (CA 14): só com <c>hr.view_sensitive</c>, além do <c>projects.view_cost</c> da rota.</summary>
    public async Task<Result<ProjectLaborWorkers>> LaborWorkersAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await CanCostAsync(cancellationToken))
        {
            return NoCost();
        }

        if (!await permissions.HasPermissionAsync("hr", "view_sensitive", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Nome e custo-hora de quem trabalhou são dados protegidos. Fale com o administrador.");
        }

        if (!await db.Projects.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return ProjectService.NotFound();
        }

        var labor = await LaborLedgerAsync(id, cancellationToken);
        var ids = labor.Select(l => l.SourceId).Distinct().ToList();
        var info = (await productionCosts.LaborEntriesAsync(ids, cancellationToken)).ToDictionary(e => e.LaborEntryId);
        var workers = (await productionCosts.LaborWorkersAsync(ids, cancellationToken)).ToDictionary(e => e.LaborEntryId);
        var canProduction = await permissions.HasPermissionAsync("production", "view", cancellationToken);
        var entries = labor.GroupBy(l => l.SourceId).Select(g =>
        {
            var entry = info.GetValueOrDefault(g.Key);
            var worker = workers.GetValueOrDefault(g.Key);
            return new ProjectLaborWorkerEntry(
                worker?.WorkerName ?? string.Empty, entry?.OrderNumber ?? string.Empty, entry?.WorkCenterName, g.Max(l => l.OccurredOn), Hours(entry?.Minutes ?? 0), worker?.HourlyCostSnapshot,
                g.Sum(l => l.Amount), canProduction && entry is not null ? $"/app/producao/os/{entry.OrderId}" : null);
        }).OrderBy(e => e.Date).ThenBy(e => e.OrderNumber, StringComparer.Ordinal).ToList();
        return new ProjectLaborWorkers(entries.Sum(e => e.Amount), entries);
    }

    private async Task<ProjectCostEntries> BudgetEntriesAsync(ProjectHead head, CostType costType, decimal budgeted, EntryLinks links, CancellationToken ct)
    {
        var contractNumber = await db.Contracts.AsNoTracking().Where(c => c.Id == head.ContractId).Select(c => c.Number).FirstAsync(ct);
        var amendments = (await db.ContractAmendments.AsNoTracking().Where(a => a.ContractId == head.ContractId && a.Status == AmendmentStatus.Approved).OrderBy(a => a.Number)
                .Select(a => new { a.Id, a.Number, a.BudgetDeltas, a.ApprovedAt }).ToListAsync(ct))
            .Select(a => (a.Id, a.Number, a.ApprovedAt, Delta: AmendmentService.Deltas(a.BudgetDeltas).GetValueOrDefault(costType)))
            .Where(a => a.Delta != 0)
            .ToList();
        var entries = new List<ProjectCostEntry>
        {
            new(ProjectCostEntryKind.Contract, $"Orçamento do contrato {contractNumber}", head.SignedAt, budgeted - amendments.Sum(a => a.Delta), null,
                links.Projects ? $"/app/contratos/{head.ContractId}" : null),
        };
        entries.AddRange(amendments.Select(a => new ProjectCostEntry(
            ProjectCostEntryKind.Amendment, string.Create(CultureInfo.InvariantCulture, $"Aditivo {a.Number} do contrato {contractNumber}"),
            a.ApprovedAt is { } at ? DateOnly.FromDateTime(at.UtcDateTime) : null, a.Delta, null, links.Projects ? $"/app/contratos/{head.ContractId}/aditivos/{a.Id}" : null)));
        return new ProjectCostEntries(costType, ProjectCostColumn.Budgeted, budgeted, entries, null, null);
    }

    private async Task<ProjectCostEntries> RealizedEntriesAsync(Guid id, CostType costType, decimal realized, EntryLinks links, CancellationToken ct)
    {
        var entries = new List<ProjectCostEntry>();
        var ledger = await db.ProjectCosts.AsNoTracking().Where(c => c.ProjectId == id)
            .Select(c => new LedgerRow(c.CostType, c.Source, c.SourceId, c.ProductionOrderId, c.OccurredOn, c.Amount)).ToListAsync(ct);
        if (costType == CostType.Tax)
        {
            // Imposto realizado só pela nota (CA 2): a guia de imposto nunca entra (CA 3). A tela da nota chega com a FIS-03a.
            entries.AddRange(ledger.Where(l => ProjectMarginCalculator.IsInvoiceTax(l.Source)).GroupBy(l => (l.Source, l.SourceId))
                .Select(g => new ProjectCostEntry(
                    g.Key.Source == ProjectCostSource.InvoiceTax ? ProjectCostEntryKind.InvoiceTax : ProjectCostEntryKind.InvoiceTaxReversal, "Nota fiscal", g.Min(l => l.OccurredOn), g.Sum(l => l.Amount),
                    null, null)));
            return new ProjectCostEntries(costType, ProjectCostColumn.Realized, realized, Ordered(entries), null, null);
        }

        var mine = ledger.Where(l => l.CostType == costType && !ProjectMarginCalculator.IsInvoiceTax(l.Source)).ToList();
        var orderNumbers = await OrderNumbersAsync(mine.Where(l => l.OrderId is not null).Select(l => l.OrderId!.Value).Distinct().ToList(), ct);
        entries.AddRange(mine.Where(l => ProjectMarginCalculator.IsStockMovement(l.Source)).GroupBy(l => (l.Source, l.SourceId, l.OrderId))
            .Select(g =>
            {
                var order = g.Key.OrderId is { } o ? orderNumbers.GetValueOrDefault(o, "OS") : "OS";
                var issue = g.Key.Source == ProjectCostSource.StockIssue;
                return new ProjectCostEntry(
                    issue ? ProjectCostEntryKind.StockIssue : ProjectCostEntryKind.StockReturn, issue ? $"Baixa para a {order}" : $"Devolução da {order}", g.Max(l => l.OccurredOn), g.Sum(l => l.Amount),
                    null, links.Inventory ? $"/app/estoque/movimentos/{g.Key.SourceId}" : null);
            }));
        var labor = mine.Where(l => l.Source is ProjectCostSource.Labor or ProjectCostSource.LaborCorrection).ToList();
        if (labor.Count > 0)
        {
            entries.AddRange(await LaborEntriesAsync(labor, links, ct));
        }

        var payables = await payableCosts.CostEntriesAsync(id, costType, ct);
        entries.AddRange(await PayableEntriesAsync(payables.Where(p => p.Bucket == PayableCostBucket.Confirmed).ToList(), links, ct));
        return new ProjectCostEntries(costType, ProjectCostColumn.Realized, realized, Ordered(entries), null, null);
    }

    private async Task<ProjectCostEntries> CommittedEntriesAsync(Guid id, CostType costType, decimal committed, EntryLinks links, CancellationToken ct)
    {
        if (costType == CostType.Tax)
        {
            return new ProjectCostEntries(costType, ProjectCostColumn.Committed, committed, [], null, null);
        }

        var payables = await payableCosts.CostEntriesAsync(id, costType, ct);
        var entries = await PayableEntriesAsync(payables.Where(p => p.Bucket is PayableCostBucket.Planned or PayableCostBucket.StockPurchase).ToList(), links, ct);
        var purchased = payables.Where(p => p.Bucket == PayableCostBucket.StockPurchase).Sum(p => p.Amount);
        decimal? purchasedTotal = null, issuedTotal = null;
        if (purchased != 0)
        {
            // Comprometido de material por saldo (CA 4): a dedução das baixas líquidas fecha o total com a célula; a sobra de baixa não vira crédito.
            var issued = await db.ProjectCosts.AsNoTracking()
                .Where(c => c.ProjectId == id && c.CostType == costType && (c.Source == ProjectCostSource.StockIssue || c.Source == ProjectCostSource.StockReturn))
                .SumAsync(c => c.Amount, ct);
            entries.Add(new ProjectCostEntry(ProjectCostEntryKind.StockIssuedOffset, "Baixas líquidas para as OS do projeto", null, -Math.Min(purchased, issued), null, null));
            (purchasedTotal, issuedTotal) = (purchased, issued);
        }

        return new ProjectCostEntries(costType, ProjectCostColumn.Committed, committed, entries, purchasedTotal, issuedTotal);
    }

    /// <summary>Horas agregadas por OS, centro de trabalho e fonte (apontamento ou correção), sem nome nem custo-hora (CA 14).</summary>
    private async Task<IEnumerable<ProjectCostEntry>> LaborEntriesAsync(List<LedgerRow> labor, EntryLinks links, CancellationToken ct)
    {
        var info = (await productionCosts.LaborEntriesAsync(labor.Select(l => l.SourceId).Distinct().ToList(), ct)).ToDictionary(e => e.LaborEntryId);
        return labor.GroupBy(l => (l.Source, Order: info.GetValueOrDefault(l.SourceId)?.OrderId ?? l.OrderId, Center: info.GetValueOrDefault(l.SourceId)?.WorkCenterName))
            .Select(g =>
            {
                var number = g.Select(l => info.GetValueOrDefault(l.SourceId)?.OrderNumber).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "OS";
                var minutes = g.Key.Source == ProjectCostSource.Labor ? g.Select(l => l.SourceId).Distinct().Sum(e => info.GetValueOrDefault(e)?.Minutes ?? 0) : 0;
                return new ProjectCostEntry(
                    g.Key.Source == ProjectCostSource.Labor ? ProjectCostEntryKind.Labor : ProjectCostEntryKind.LaborCorrection, $"{number} · {g.Key.Center ?? "Sem centro de trabalho"}",
                    g.Max(l => l.OccurredOn), g.Sum(l => l.Amount), g.Key.Source == ProjectCostSource.Labor ? Hours(minutes) : null,
                    links.Production && g.Key.Order is { } o ? $"/app/producao/os/{o}" : null);
            });
    }

    private async Task<List<ProjectCostEntry>> PayableEntriesAsync(IReadOnlyList<ProjectPayableCostEntry> payables, EntryLinks links, CancellationToken ct)
    {
        var names = (await parties.FindManyAsync(payables.Select(p => p.PartyId).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        return payables.Select(p =>
        {
            var purchase = p.Bucket == PayableCostBucket.StockPurchase;
            var link = purchase
                ? links.Purchasing && p.OriginId is { } order ? $"/app/compras/pedidos/{order}" : links.Finance ? $"/app/financeiro/pagar/{p.Id}" : null
                : links.Finance ? $"/app/financeiro/pagar/{p.Id}" : null;
            var party = names.GetValueOrDefault(p.PartyId);
            return new ProjectCostEntry(
                purchase ? ProjectCostEntryKind.PurchasePayable : ProjectCostEntryKind.Payable, string.IsNullOrEmpty(party) ? p.Number : $"{p.Number} · {party}", p.IssueDate, p.Amount, null, link);
        }).ToList();
    }

    private async Task<List<LedgerRow>> LaborLedgerAsync(Guid id, CancellationToken ct) =>
        await db.ProjectCosts.AsNoTracking().Where(c => c.ProjectId == id && (c.Source == ProjectCostSource.Labor || c.Source == ProjectCostSource.LaborCorrection))
            .Select(c => new LedgerRow(c.CostType, c.Source, c.SourceId, c.ProductionOrderId, c.OccurredOn, c.Amount)).ToListAsync(ct);

    private async Task<Dictionary<Guid, string>> OrderNumbersAsync(List<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : (await productionOrders.FindManyAsync(ids, ct)).ToDictionary(o => o.Id, o => o.Number);

    private async Task<EntryLinks> LinksAsync(CancellationToken ct) => new(
        await permissions.HasPermissionAsync("projects", "view", ct), await permissions.HasPermissionAsync("inventory", "view", ct),
        await permissions.HasPermissionAsync("production", "view", ct), await permissions.HasPermissionAsync("finance", "view", ct),
        await permissions.HasPermissionAsync("purchasing", "view", ct));

    private static List<ProjectCostEntry> Ordered(IEnumerable<ProjectCostEntry> entries) =>
        entries.OrderBy(e => e.Date ?? DateOnly.MaxValue).ThenBy(e => e.Document, StringComparer.Ordinal).ToList();

    private static decimal Hours(int minutes) => Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);

    private sealed record LedgerRow(CostType CostType, ProjectCostSource Source, Guid SourceId, Guid? OrderId, DateOnly OccurredOn, decimal Amount);

    /// <summary>Permissões dos módulos donos dos documentos, só para o link do drill-down (CA 13).</summary>
    private sealed record EntryLinks(bool Projects, bool Inventory, bool Production, bool Finance, bool Purchasing);
}
