using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>Título quitado para a pontualidade: valor, vencimento e a data da baixa que quitou.</summary>
public sealed record SettledParcel(Guid PartyId, decimal Amount, DateOnly DueDate, DateOnly SettledOn);

/// <summary>
/// Pontualidade (FIN-06b CA 2 a 4): títulos a receber quitados nos últimos 12 meses, sem os de implantação e sem os quitados
/// só por desconto; atraso = data da baixa que quitou − vencimento, antecipação conta como 0; ponderado pelo valor.
/// Calculado na consulta (sem materializar no MVP).
/// </summary>
public sealed class PaymentStatsService(IFinanceDb db, TimeProvider clock) : ICustomerPaymentStats
{
    public static (decimal AvgDelay, decimal PctLate) Compute(IEnumerable<SettledParcel> parcels)
    {
        var list = parcels.ToList();
        var total = list.Sum(p => p.Amount);
        if (total == 0)
        {
            return (0, 0);
        }

        var weighted = list.Sum(p => p.Amount * Math.Max(0, p.SettledOn.DayNumber - p.DueDate.DayNumber));
        var late = list.Where(p => p.SettledOn > p.DueDate).Sum(p => p.Amount);
        return (Math.Round(weighted / total, 1, MidpointRounding.AwayFromZero), Math.Round(late / total * 100, 1, MidpointRounding.AwayFromZero));
    }

    public async Task<CustomerPaymentStats> ForPartyAsync(Guid partyId, CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        var mine = (await SettledAsync([partyId], cancellationToken)).ToList();
        var (avg, late) = Compute(mine);
        var (tenantAvg, _) = Compute(await SettledAsync(null, cancellationToken));
        var since = today.AddMonths(-12);
        var invoiced = await db.Receivables.AsNoTracking().Where(r => r.PartyId == partyId && r.Status != ReceivableStatus.Cancelled && r.Origin != DocumentOrigin.Opening && r.IssueDate >= since)
            .SumAsync(r => r.Amount, cancellationToken);
        var open = await db.Receivables.AsNoTracking().Where(r => r.PartyId == partyId && (r.Status == ReceivableStatus.Open || r.Status == ReceivableStatus.PartiallyPaid))
            .Select(r => new { Balance = r.Amount - r.PaidAmount, r.DueDate }).ToListAsync(cancellationToken);
        var openTotal = open.Sum(o => o.Balance);
        var overdue = open.Where(o => o.DueDate < today).Sum(o => o.Balance);
        return new CustomerPaymentStats(avg, late, openTotal == 0 ? 0 : Math.Round(overdue / openTotal * 100, 1, MidpointRounding.AwayFromZero), invoiced, mine.Count > 0, tenantAvg);
    }

    /// <summary>Pontualidade de vários clientes de uma vez (coluna da Inadimplência, CA 4).</summary>
    public async Task<Dictionary<Guid, (decimal AvgDelay, decimal PctLate)>> PunctualityAsync(IReadOnlyCollection<Guid> partyIds, CancellationToken cancellationToken = default) =>
        (await SettledAsync(partyIds, cancellationToken)).GroupBy(p => p.PartyId).ToDictionary(g => g.Key, g => Compute(g));

    /// <summary>
    /// Pontualidade e inadimplência em lote (FIN-07 CA 3 e 16): duas consultas, qualquer que seja o número de clientes. A
    /// primeira traz os títulos quitados da empresa (a média da empresa e a de cada cliente saem dela); a segunda, o faturado
    /// em 12 meses (sem <c>Opening</c>) e o vencido há mais de 30 dias (com <c>Opening</c>) por cliente.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, CustomerDefaultStats>> ForPartiesAsync(IReadOnlyCollection<Guid> partyIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partyIds);
        var today = BusinessTime.Today(clock);
        var settled = await SettledAsync(null, cancellationToken);
        var (tenantAvg, _) = Compute(settled);
        var wanted = partyIds.ToHashSet();
        var byParty = settled.Where(p => wanted.Contains(p.PartyId)).GroupBy(p => p.PartyId).ToDictionary(g => g.Key, g => Compute(g).AvgDelay);
        var since = today.AddMonths(-12);
        var limit = today.AddDays(-AdjustedCashFlowCalculator.DefaultOverdueDays);
        var ids = wanted.ToList();
        var amounts = (await db.Receivables.AsNoTracking().Where(r => ids.Contains(r.PartyId) && r.Status != ReceivableStatus.Cancelled)
                .GroupBy(r => r.PartyId)
                .Select(g => new
                {
                    g.Key,
                    Invoiced = g.Sum(r => r.Origin != DocumentOrigin.Opening && r.IssueDate >= since ? r.Amount : 0),
                    Overdue = g.Sum(r => (r.Status == ReceivableStatus.Open || r.Status == ReceivableStatus.PartiallyPaid) && r.DueDate < limit ? r.Amount - r.PaidAmount : 0),
                })
                .ToListAsync(cancellationToken))
            .ToDictionary(a => a.Key);
        return ids.ToDictionary(id => id, id =>
        {
            var a = amounts.GetValueOrDefault(id);
            var invoiced = a?.Invoiced ?? 0m;
            var overdue = a?.Overdue ?? 0m;
            var history = byParty.TryGetValue(id, out var avg);
            return new CustomerDefaultStats(id, history ? avg : 0m, history, tenantAvg, overdue, invoiced, AdjustedCashFlowCalculator.DefaultPct(overdue, invoiced), settled.Count > 0);
        });
    }

    private async Task<List<SettledParcel>> SettledAsync(IReadOnlyCollection<Guid>? partyIds, CancellationToken ct)
    {
        var since = BusinessTime.Today(clock).AddMonths(-12);
        var paid = db.Receivables.AsNoTracking().Where(r => r.Status == ReceivableStatus.Paid && r.Origin != DocumentOrigin.Opening && r.DueDate != null);
        if (partyIds is not null)
        {
            paid = paid.Where(r => partyIds.Contains(r.PartyId));
        }

        var rows = await paid.Join(db.ReceivableSettlements.Where(s => s.ReversedAt == null), r => r.Id, s => s.ReceivableId, (r, s) => new { r.Id, r.PartyId, r.Amount, Due = r.DueDate!.Value, s.Date, s.Principal })
            .ToListAsync(ct);
        return rows.GroupBy(x => x.Id).Where(g => g.Sum(x => x.Principal) > 0 && g.Max(x => x.Date) >= since)
            .Select(g => new SettledParcel(g.First().PartyId, g.First().Amount, g.First().Due, g.Max(x => x.Date))).ToList();
    }
}
