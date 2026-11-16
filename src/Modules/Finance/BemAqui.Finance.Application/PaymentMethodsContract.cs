using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary><see cref="IPaymentMethods"/> para o PDV, o núcleo de vendas e o fechamento de caixa (PAG-01 CA 5).</summary>
public sealed class PaymentMethodsContract(IFinanceDb db) : IPaymentMethods
{
    public async Task<IReadOnlyList<PaymentMethodInfo>> ActiveAsync(DateOnly on, CancellationToken cancellationToken = default)
    {
        var methods = await db.PaymentMethods.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Name).ToListAsync(cancellationToken);
        var ids = methods.Select(m => m.Id).ToList();
        var fees = await db.AcquirerFees.AsNoTracking()
            .Where(f => ids.Contains(f.PaymentMethodId) && f.ValidFrom <= on && (f.ValidTo == null || f.ValidTo > on)).ToListAsync(cancellationToken);
        return methods.Select(m => new PaymentMethodInfo(
            m.Id, m.Name, m.Type, m.AllowsChange, m.DestinationAccountId, m.AcquirerPartyId, m.NfePaymentCode, m.ConfirmedAt is not null,
            fees.Where(f => f.PaymentMethodId == m.Id && f.AcquirerPartyId == m.AcquirerPartyId).OrderBy(f => f.Brand).ThenBy(f => f.Installments).Select(Terms).ToList())).ToList();
    }

    public async Task<AcquirerFeeTerms?> FeeAsync(Guid paymentMethodId, CardBrand brand, int installments, DateOnly on, CancellationToken cancellationToken = default)
    {
        var method = await db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == paymentMethodId, cancellationToken);
        if (method?.AcquirerPartyId is not { } acquirer)
        {
            return null;
        }

        var fees = await db.AcquirerFees.AsNoTracking().Where(f => f.PaymentMethodId == paymentMethodId).ToListAsync(cancellationToken);
        return AcquirerFeeRules.Pick(fees, acquirer, brand, installments, on) is { } fee ? Terms(fee) : null;
    }

    private static AcquirerFeeTerms Terms(AcquirerFee f) => new(f.Brand, f.Installments, f.FeePercent, f.SettlementDays, f.ValidFrom);
}
