using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Inadimplência (FIN-06a): aging dos títulos a receber vencidos por cliente, pelo saldo em aberto (implantação entra
/// como os demais), e o aviso diário consolidado do que venceu ontem para quem edita o Financeiro.
/// </summary>
public sealed partial class AgingService(IFinanceDb db, IParties parties, IUserDirectory users, IPermissionService permissions, INotificationService notifications, TimeProvider clock)
{
    /// <summary>Ordenações aceitas (CA 6): fora delas, 422.</summary>
    public static readonly IReadOnlyList<string> Sorts = ["-total", "total", "-maxDaysLate", "maxDaysLate", "name", "-name"];

    private static readonly string[] Labels = ["0–30", "31–60", "61–90", "90+"];

    private static readonly ReceivableStatus[] Open = [ReceivableStatus.Open, ReceivableStatus.PartiallyPaid];

    /// <summary>Faixa pelos dias de atraso (hoje − vencimento): 1–30, 31–60, 61–90 e 91 ou mais.</summary>
    public static int BucketOf(int daysLate) => daysLate switch
    {
        <= 30 => 0,
        <= 60 => 1,
        <= 90 => 2,
        _ => 3,
    };

    public async Task<Result<AgingReport>> ReportAsync(string? sort, CancellationToken cancellationToken = default)
    {
        sort ??= "-total";
        if (!Sorts.Contains(sort))
        {
            return ReceivableService.Invalid("sort", "Ordene por total, maior atraso ou nome.");
        }

        var today = BusinessTime.Today(clock);
        var groups = await db.Receivables.AsNoTracking().Where(r => Open.Contains(r.Status) && r.DueDate != null && r.DueDate < today)
            .GroupBy(r => new { r.PartyId, Due = r.DueDate!.Value })
            .Select(g => new { g.Key.PartyId, g.Key.Due, Balance = g.Sum(r => r.Amount - r.PaidAmount), Count = g.Count() })
            .ToListAsync(cancellationToken);
        var names = (await parties.FindManyAsync(groups.Select(g => g.PartyId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var rows = groups.GroupBy(g => g.PartyId).Select(c =>
        {
            var buckets = new decimal[4];
            foreach (var g in c)
            {
                buckets[BucketOf(today.DayNumber - g.Due.DayNumber)] += g.Balance;
            }

            return new AgingCustomer(c.Key, names.GetValueOrDefault(c.Key, string.Empty), buckets, buckets.Sum(), today.DayNumber - c.Min(g => g.Due).DayNumber, c.Sum(g => g.Count));
        });
        var sorted = sort switch
        {
            "total" => rows.OrderBy(r => r.Total),
            "-maxDaysLate" => rows.OrderByDescending(r => r.MaxDaysLate),
            "maxDaysLate" => rows.OrderBy(r => r.MaxDaysLate),
            "name" => rows.OrderBy(r => r.PartyName, StringComparer.CurrentCulture),
            "-name" => rows.OrderByDescending(r => r.PartyName, StringComparer.CurrentCulture),
            _ => rows.OrderByDescending(r => r.Total),
        };
        var list = sorted.ThenBy(r => r.PartyName, StringComparer.CurrentCulture).ToList();
        var buckets = Labels.Select((label, i) =>
        {
            var inBucket = groups.Where(g => BucketOf(today.DayNumber - g.Due.DayNumber) == i).ToList();
            return new AgingBucket(label, i == 0 ? 1 : (i * 30) + 1, i == 3 ? null : (i + 1) * 30, inBucket.Sum(g => g.Balance), inBucket.Sum(g => g.Count));
        }).ToList();
        return new AgingReport(today, list.Sum(r => r.Total), list.Sum(r => r.Count), list.Count, buckets, list);
    }
}
