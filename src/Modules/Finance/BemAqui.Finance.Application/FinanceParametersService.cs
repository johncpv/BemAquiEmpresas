using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Parâmetros do Finance (ADR 0016 adendo M6, FIN-01): limites de desconto, cancelamento e data retroativa. O seed grava os
/// padrões sem sobrescrever; Configurações → Parâmetros edita com <c>settings.edit</c>, com auditoria.
/// </summary>
public sealed class FinanceParametersService(IFinanceDb db, IPermissionChecker permissions, IUserDirectory users, ICurrentUser currentUser, INotificationService notifications, TimeProvider clock)
    : IFinanceParameters
{
    public const string DiscountPct = "finance.discount_approval_pct";
    public const string DiscountAmount = "finance.discount_approval_amount";
    public const string CancelAmount = "finance.cancel_approval_amount";
    public const string BackdateDays = "finance.backdate_days";
    public const string OwnerCanApproveOwn = "finance.owner_can_approve_own";
    public const string PaymentTolerance = "finance.payment_tolerance";
    public const string TransferNotifyAmount = "finance.transfer_notify_amount";
    public const string AdjustedDefaultThreshold = "finance.adjusted_default_threshold_pct";

    /// <summary>Padrões aprovados pelo PO (D8): 5,00%, R$ 500,00, R$ 1.000,00 e 7 dias.</summary>
    public static readonly IReadOnlyDictionary<string, decimal> Defaults = new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        [DiscountPct] = 5m,
        [DiscountAmount] = 500m,
        [CancelAmount] = 1000m,
        [BackdateDays] = 7m,
        [OwnerCanApproveOwn] = 0m,
        [PaymentTolerance] = 0m,
        [TransferNotifyAmount] = 10_000m,
        [AdjustedDefaultThreshold] = 20m, // FIN-07 CA 3 e 15
    };

    public async Task<FinanceLimits> LimitsAsync(CancellationToken cancellationToken = default)
    {
        var values = await db.FinanceParameters.AsNoTracking().ToDictionaryAsync(p => p.Key, p => p.Value, cancellationToken);
        decimal Get(string key) => values.TryGetValue(key, out var v) ? v : Defaults[key];
        return new FinanceLimits(Get(DiscountPct), Get(DiscountAmount), Get(CancelAmount), (int)Get(BackdateDays), Get(OwnerCanApproveOwn) != 0, Get(PaymentTolerance), Get(TransferNotifyAmount), Get(AdjustedDefaultThreshold));
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

        if (input.PaymentTolerance is < 0 or > 1000)
        {
            return ReceivableService.Invalid("paymentTolerance", "Informe uma tolerância de R$ 0,00 a R$ 1.000,00.");
        }

        if (input.TransferNotifyAmount < 0)
        {
            return ReceivableService.Invalid("transferNotifyAmount", "Informe um valor maior ou igual a zero.");
        }

        if (input.AdjustedDefaultThresholdPct is < 0 or > 100)
        {
            return ReceivableService.Invalid("adjustedDefaultThresholdPct", "Informe um limite de inadimplência entre 0,00% e 100,00%.");
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
            [OwnerCanApproveOwn] = input.OwnerCanApproveOwn ? 1m : 0m,
            [PaymentTolerance] = Math.Round(input.PaymentTolerance, 2),
            [TransferNotifyAmount] = Math.Round(input.TransferNotifyAmount, 2),
        };
        var before = await LimitsAsync(cancellationToken);
        // FIN-07 CA 15: sem o campo (telas antigas), o limite do ajustado fica como está.
        values[AdjustedDefaultThreshold] = Math.Round(input.AdjustedDefaultThresholdPct ?? before.AdjustedDefaultThresholdPct, 2, MidpointRounding.AwayFromZero);
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

        if (before.OwnerCanApproveOwn != input.OwnerCanApproveOwn)
        {
            // FIN-03a CA 10: a troca avisa todos os membros do Proprietário.
            var who = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, cancellationToken) ?? "Um usuário" : "O sistema";
            var title = $"{who} {(input.OwnerCanApproveOwn ? "ligou" : "desligou")} a aprovação da própria ordem pelo Proprietário.";
            foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
            {
                notifications.Notify(owner, "finance.parameters", title, "/app/configuracoes/parametros");
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await LimitsAsync(cancellationToken);
    }
}
