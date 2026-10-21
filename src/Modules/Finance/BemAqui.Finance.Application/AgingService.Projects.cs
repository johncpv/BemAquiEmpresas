using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class AgingService
{
    /// <summary>Aging por projeto (FIN-06b CA 1): vencido por faixa; títulos sem projeto em "Sem projeto"; nomes em lote.</summary>
    public async Task<AgingProjectReport> ProjectsAsync(CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        var groups = await db.Receivables.AsNoTracking().Where(r => Open.Contains(r.Status) && r.DueDate != null && r.DueDate < today)
            .GroupBy(r => new { r.ProjectId, r.PartyId, Due = r.DueDate!.Value })
            .Select(g => new { g.Key.ProjectId, g.Key.PartyId, g.Key.Due, Balance = g.Sum(r => r.Amount - r.PaidAmount), Count = g.Count() })
            .ToListAsync(cancellationToken);
        var projectIds = groups.Where(g => g.ProjectId is not null).Select(g => g.ProjectId!.Value).Distinct().ToList();
        var byId = (await projects.FindManyAsync(projectIds, cancellationToken)).ToDictionary(p => p.Id);
        var names = (await parties.FindManyAsync(groups.Select(g => g.PartyId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var rows = groups.GroupBy(g => g.ProjectId).Select(p =>
        {
            var buckets = new decimal[4];
            foreach (var g in p)
            {
                buckets[BucketOf(today.DayNumber - g.Due.DayNumber)] += g.Balance;
            }

            var project = p.Key is { } id ? byId.GetValueOrDefault(id) : null;
            var party = project?.PartyId ?? (p.Select(g => g.PartyId).Distinct().Count() == 1 ? p.First().PartyId : null);
            return new AgingProject(p.Key, project?.Number, project?.Name ?? (p.Key is null ? "Sem projeto" : null), party, party is { } pid ? names.GetValueOrDefault(pid) : null,
                buckets, buckets.Sum(), today.DayNumber - p.Min(g => g.Due).DayNumber, p.Sum(g => g.Count));
        }).OrderBy(r => r.ProjectId is null).ThenByDescending(r => r.Total).ToList();
        return new AgingProjectReport(today, rows.Sum(r => r.Total), rows);
    }

    /// <summary>Resumo financeiro do terceiro (FIN-06b CA 5): a receber e a pagar, com a pontualidade.</summary>
    public async Task<PartyFinanceSummary> PartySummaryAsync(Guid partyId, CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        var since = today.AddMonths(-12);
        var receivables = await db.Receivables.AsNoTracking().Where(r => r.PartyId == partyId && r.Status != ReceivableStatus.Cancelled)
            .Select(r => new { r.Status, r.DueDate, Balance = r.Amount - r.PaidAmount }).ToListAsync(cancellationToken);
        var received = await db.ReceivableSettlements.AsNoTracking().Where(s => s.ReversedAt == null && s.Date >= since)
            .Join(db.Receivables.Where(r => r.PartyId == partyId), s => s.ReceivableId, r => r.Id, (s, r) => s.Principal).SumAsync(cancellationToken);
        var payables = await db.Payables.AsNoTracking().Where(p => p.PartyId == partyId && p.Status != PayableStatus.Cancelled)
            .Select(p => new { p.Status, p.DueDate, Balance = p.Amount - p.RetentionIss - p.RetentionInss - p.RetentionIr - p.RetentionPcc - p.PaidAmount }).ToListAsync(cancellationToken);
        var paid = await db.PayableSettlements.AsNoTracking().Where(s => s.ReversedAt == null && s.Date >= since)
            .Join(db.Payables.Where(p => p.PartyId == partyId), s => s.PayableId, p => p.Id, (s, p) => s.Total).SumAsync(cancellationToken);
        var openReceivables = receivables.Where(r => r.Status is ReceivableStatus.Open or ReceivableStatus.PartiallyPaid).ToList();
        var openPayables = payables.Where(p => p.Status is not (PayableStatus.Paid or PayableStatus.Planned)).ToList();
        return new PartyFinanceSummary(
            new PartyFinanceTotals(openReceivables.Sum(r => r.Balance), openReceivables.Where(r => r.DueDate < today).Sum(r => r.Balance), received),
            new PartyFinanceTotals(openPayables.Sum(p => p.Balance), openPayables.Where(p => p.DueDate < today).Sum(p => p.Balance), paid),
            await stats.ForPartyAsync(partyId, cancellationToken));
    }
}
