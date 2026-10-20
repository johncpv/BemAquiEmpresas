using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Parâmetros do Finance (ADR 0016 adendo M6, FIN-01): limites de desconto, cancelamento e data retroativa. O seed grava os
/// padrões sem sobrescrever; Configurações → Parâmetros edita com <c>settings.edit</c>, com auditoria.
/// </summary>
public sealed class FinanceParametersService(IFinanceDb db, IPermissionChecker permissions, TimeProvider clock) : IFinanceParameters
{
    public const string DiscountPct = "finance.discount_approval_pct";
    public const string DiscountAmount = "finance.discount_approval_amount";
    public const string CancelAmount = "finance.cancel_approval_amount";
    public const string BackdateDays = "finance.backdate_days";

    /// <summary>Padrões aprovados pelo PO (D8): 5,00%, R$ 500,00, R$ 1.000,00 e 7 dias.</summary>
    public static readonly IReadOnlyDictionary<string, decimal> Defaults = new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        [DiscountPct] = 5m,
        [DiscountAmount] = 500m,
        [CancelAmount] = 1000m,
        [BackdateDays] = 7m,
    };

    public async Task<FinanceLimits> LimitsAsync(CancellationToken cancellationToken = default)
    {
        var values = await db.FinanceParameters.AsNoTracking().ToDictionaryAsync(p => p.Key, p => p.Value, cancellationToken);
        decimal Get(string key) => values.TryGetValue(key, out var v) ? v : Defaults[key];
        return new FinanceLimits(Get(DiscountPct), Get(DiscountAmount), Get(CancelAmount), (int)Get(BackdateDays));
    }

    public async Task<Result<FinanceLimits>> UpdateAsync(FinanceLimitsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (input.DiscountApprovalPct is < 0 or > 100)
        {
            return ReceivableService.Invalid("discountApprovalPct", "Informe um percentual entre 0 e 100.");
        }

        if (input.DiscountApprovalAmount < 0 || input.CancelApprovalAmount < 0)
        {
            return ReceivableService.Invalid(input.DiscountApprovalAmount < 0 ? "discountApprovalAmount" : "cancelApprovalAmount", "Informe um valor maior ou igual a zero.");
        }

        if (input.BackdateDays is < 0 or > 365)
        {
            return ReceivableService.Invalid("backdateDays", "Informe de 0 a 365 dias.");
        }

        var values = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [DiscountPct] = Math.Round(input.DiscountApprovalPct, 4),
            [DiscountAmount] = Math.Round(input.DiscountApprovalAmount, 2),
            [CancelAmount] = Math.Round(input.CancelApprovalAmount, 2),
            [BackdateDays] = input.BackdateDays,
        };
        var existing = await db.FinanceParameters.Where(p => values.Keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, cancellationToken);
        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var parameter))
            {
                if (parameter.Value != value)
                {
                    parameter.Value = value;
                    parameter.UpdatedAt = clock.GetUtcNow();
                }
            }
            else
            {
                db.FinanceParameters.Add(new FinanceParameter { Key = key, Value = value, UpdatedAt = clock.GetUtcNow() });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await LimitsAsync(cancellationToken);
    }
}
