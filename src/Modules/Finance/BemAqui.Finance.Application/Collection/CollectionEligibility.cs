using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Quem pode receber lembrete (FIN-09 CA 3, 5 e 6), conferido ao montar a lista e de novo no envio (<see cref="CollectionReminderGuard"/>):
/// título <c>Open</c>/<c>PartiallyPaid</c> com saldo, de origem <c>Manual</c> ou <c>Contract</c> (implantação fora); nunca com boleto
/// <c>Paid</c> ou <c>PaidPendingReview</c>, com baixa registrada desde as 00:00 do dia, nem com a cobrança pausada (do título ou do cliente).
/// </summary>
public sealed class CollectionEligibility(IFinanceDb db, TimeProvider clock)
{
    public const string PaidReason = "Título já pago";
    public const string CancelledReason = "Título cancelado";
    public const string NotOpenReason = "Título fora da cobrança";
    public const string OpeningReason = "Título de implantação: fora da régua";
    public const string SlipPaidReason = "Boleto pago aguardando conferência";
    public const string SettledTodayReason = "Baixa registrada hoje";
    public const string PausedReason = "Cobrança pausada";

    /// <summary>O que barra o lembrete de vários títulos (uma consulta por regra).</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> BlockedAsync(IReadOnlyCollection<Receivable> receivables, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receivables);
        var result = new Dictionary<Guid, string>();
        if (receivables.Count == 0)
        {
            return result;
        }

        var today = BusinessTime.Today(clock);
        var since = BusinessTime.StartOfDayUtc(today);
        var ids = receivables.Select(r => r.Id).ToList();
        var parties = receivables.Select(r => r.PartyId).Distinct().ToList();
        var slipPaid = (await db.BillingSlips.AsNoTracking()
            .Where(s => ids.Contains(s.ReceivableId) && (s.Status == BillingSlipStatus.Paid || s.Status == BillingSlipStatus.PaidPendingReview))
            .Select(s => new { s.ReceivableId, s.Status }).ToListAsync(cancellationToken)).ToLookup(s => s.ReceivableId);
        var settledToday = (await db.ReceivableSettlements.AsNoTracking().Where(s => ids.Contains(s.ReceivableId) && s.ReversedAt == null && s.CreatedAt >= since)
            .Select(s => s.ReceivableId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var pauses = await db.CollectionPauses.AsNoTracking()
            .Where(p => p.ResumedAt == null && p.From <= today && (p.Until == null || p.Until >= today)
                && ((p.ReceivableId != null && ids.Contains(p.ReceivableId.Value)) || (p.PartyId != null && parties.Contains(p.PartyId.Value))))
            .Select(p => new { p.ReceivableId, p.PartyId }).ToListAsync(cancellationToken);
        var pausedReceivables = pauses.Where(p => p.ReceivableId != null).Select(p => p.ReceivableId!.Value).ToHashSet();
        var pausedParties = pauses.Where(p => p.PartyId != null).Select(p => p.PartyId!.Value).ToHashSet();

        foreach (var r in receivables)
        {
            var reason = StateReason(r)
                ?? (slipPaid[r.Id].Any(s => s.Status == BillingSlipStatus.PaidPendingReview) ? SlipPaidReason : slipPaid[r.Id].Any() ? PaidReason : null)
                ?? (settledToday.Contains(r.Id) ? SettledTodayReason : null)
                ?? (pausedReceivables.Contains(r.Id) || pausedParties.Contains(r.PartyId) ? PausedReason : null);
            if (reason is not null)
            {
                result[r.Id] = reason;
            }
        }

        return result;
    }

    /// <summary>O que barra o lembrete de um título, relido do banco agora (a guarda do envio); título sumido: fora da cobrança.</summary>
    public async Task<string?> ReasonAsync(Guid receivableId, CancellationToken cancellationToken = default)
    {
        var receivable = await db.Receivables.AsNoTracking().FirstOrDefaultAsync(r => r.Id == receivableId, cancellationToken);
        if (receivable is null)
        {
            return NotOpenReason;
        }

        return (await BlockedAsync([receivable], cancellationToken)).GetValueOrDefault(receivable.Id);
    }

    /// <summary>Situação do próprio título (sem consulta).</summary>
    public static string? StateReason(Receivable receivable)
    {
        ArgumentNullException.ThrowIfNull(receivable);
        return receivable.Status switch
        {
            ReceivableStatus.Paid => PaidReason,
            ReceivableStatus.Cancelled => CancelledReason,
            ReceivableStatus.Open or ReceivableStatus.PartiallyPaid when receivable.Balance <= 0 => PaidReason,
            ReceivableStatus.Open or ReceivableStatus.PartiallyPaid when receivable.DueDate is null => NotOpenReason,
            ReceivableStatus.Open or ReceivableStatus.PartiallyPaid => CollectionRules.OriginCovered(receivable.Origin) ? null : OpeningReason,
            _ => NotOpenReason,
        };
    }
}
