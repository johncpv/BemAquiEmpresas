using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class ReceivableService
{
    /// <summary>
    /// Baixa (CA 8 a 10, 13 a 15, 24): baixa, transação de entrada e título na mesma gravação; o xmin do título barra a
    /// segunda baixa simultânea.
    /// </summary>
    public async Task<Result<ReceivableDetail>> SettleAsync(Guid id, SettlementInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var receivable = await db.Receivables.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receivable is null)
        {
            return NotFound();
        }

        if (Stale(receivable, input.Version) is { } stale)
        {
            return stale;
        }

        if (!receivable.IsOpen)
        {
            return NotOpen(receivable);
        }

        if (new[] { (input.Principal, "principal"), (input.Interest, "interest"), (input.Fine, "fine"), (input.Discount, "discount") }.FirstOrDefault(v => v.Item1 < 0) is { Item2: { } negative })
        {
            return Invalid(negative, "Informe um valor maior ou igual a zero.");
        }

        if (input.Principal <= 0)
        {
            return Invalid("principal", "Informe o valor recebido de principal.");
        }

        if (input.Principal > receivable.Balance)
        {
            return Invalid("principal", $"O principal passa do saldo de {Money(receivable.Balance)}.");
        }

        if (input.Discount > input.Principal)
        {
            return Invalid("discount", "O desconto não pode passar do principal da baixa.");
        }

        var total = ReceivableSettlement.TotalOf(input.Principal, input.Interest, input.Fine, input.Discount);
        if (total <= 0)
        {
            return Invalid("discount", "O total recebido precisa ser maior que zero. Para zerar o saldo, use Quitar o saldo com desconto.");
        }

        var account = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == input.BankAccountId, cancellationToken);
        if (account is null || !account.IsActive)
        {
            return Invalid("bankAccountId", "Escolha uma conta ativa.");
        }

        if (await DateErrorAsync(input.Date, input.BackdateReason, account.OpeningDate, cancellationToken) is { } badDate)
        {
            return badDate;
        }

        if (await DiscountErrorAsync(input.Discount, receivable.Balance, cancellationToken) is { } overLimit)
        {
            return overLimit;
        }

        // BNK-03b CA 11: recebimento com data no período de conciliação fechado da conta.
        if (await period.CheckAsync(account.Id, input.Date, input.BackdateReason, "backdateReason", "baixar um título", cancellationToken) is { } closed)
        {
            return closed;
        }

        if (await billing.SettlementGuardAsync(receivable, input.CancelBillingSlip, cancellationToken) is { } slipOpen)
        {
            return slipOpen;
        }

        Expect(receivable, input.Version);
        var settlement = new ReceivableSettlement
        {
            ReceivableId = receivable.Id, BankAccountId = account.Id, Date = input.Date, Principal = input.Principal, Interest = input.Interest, Fine = input.Fine,
            Discount = input.Discount, Total = total, BalanceBefore = receivable.Balance, Kind = SettlementKind.Settlement, BackdateReason = input.BackdateReason?.Trim(),
            CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        var transaction = new FinancialTransaction
        {
            BankAccountId = account.Id, Date = input.Date, Amount = total, Type = TransactionType.Receipt, Description = "Recebimento " + receivable.Number,
            ReceivableId = receivable.Id, SettlementId = settlement.Id, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        settlement.TransactionId = transaction.Id;
        return await ApplyAsync(receivable, settlement, transaction, cancellationToken);
    }

    /// <summary>Quitação do saldo com desconto (CA 11): principal = desconto = saldo, total 0, sem transação (ADR 0019 §8).</summary>
    public async Task<Result<ReceivableDetail>> WriteOffAsync(Guid id, WriteOffInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var receivable = await db.Receivables.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receivable is null)
        {
            return NotFound();
        }

        if (Stale(receivable, input.Version) is { } stale)
        {
            return stale;
        }

        if (!receivable.IsOpen)
        {
            return NotOpen(receivable);
        }

        if (await DateErrorAsync(input.Date, input.BackdateReason, null, cancellationToken) is { } badDate)
        {
            return badDate;
        }

        var balance = receivable.Balance;
        if (await DiscountErrorAsync(balance, balance, cancellationToken) is { } overLimit)
        {
            return overLimit;
        }

        if (await billing.SettlementGuardAsync(receivable, input.CancelBillingSlip, cancellationToken) is { } slipOpen)
        {
            return slipOpen;
        }

        Expect(receivable, input.Version);
        var settlement = new ReceivableSettlement
        {
            ReceivableId = receivable.Id, Date = input.Date, Principal = balance, Discount = balance, Total = 0, BalanceBefore = balance, Kind = SettlementKind.DiscountWriteOff,
            BackdateReason = input.BackdateReason?.Trim(), CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        return await ApplyAsync(receivable, settlement, null, cancellationToken);
    }

    /// <summary>
    /// Estorno da baixa (CA 16): nada é apagado; a transação inversa sai na data do estorno; o Proprietário é avisado. A baixa feita pelo
    /// banco (BNK-02 CA 14) só se estorna pela rota com <c>finance.approve</c> e o 2FA de novo (<paramref name="bankReversal"/>); pela rota
    /// comum, 403 <c>bank_settlement_requires_approval</c>.
    /// </summary>
    public async Task<Result<ReceivableDetail>> ReverseAsync(Guid id, Guid settlementId, ReasonInput input, CancellationToken cancellationToken = default) =>
        await ReverseAsync(id, settlementId, input, bankReversal: false, cancellationToken);

    public const string BankSettlementRequiresApproval = "bank_settlement_requires_approval";

    public async Task<Result<ReceivableDetail>> ReverseAsync(Guid id, Guid settlementId, ReasonInput input, bool bankReversal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var receivable = await db.Receivables.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        var settlement = receivable is null ? null : await db.ReceivableSettlements.FirstOrDefaultAsync(s => s.Id == settlementId && s.ReceivableId == id, cancellationToken);
        if (receivable is null || settlement is null)
        {
            return Error.NotFound("settlement_not_found", "Baixa não encontrada neste título.");
        }

        if (Stale(receivable, input.Version) is { } stale)
        {
            return stale;
        }

        if (settlement.Source == SettlementSource.Bank && !bankReversal)
        {
            return Error.Forbidden(BankSettlementRequiresApproval, "Este pagamento foi confirmado pelo Inter: o estorno pede quem aprova em Caixa e pagamentos e o código do 2FA.");
        }

        if (settlement.Source != SettlementSource.Bank && bankReversal)
        {
            return Error.Conflict("settlement_not_from_bank", "Esta baixa não veio do banco: use o estorno comum.");
        }

        if (settlement.ReversedAt is not null)
        {
            return Error.Conflict("settlement_reversed", "Esta baixa já foi estornada.");
        }

        if (ReasonError(input.Reason) is { } missing)
        {
            return missing;
        }

        if (settlement.TransactionId is not null && settlement.BankAccountId is { } reversedAccount
            && await period.CheckAsync(reversedAccount, Today, input.Reason, "reason", "estornar uma baixa", cancellationToken) is { } closed)
        {
            return closed;
        }

        Expect(receivable, input.Version);
        settlement.ReversedAt = clock.GetUtcNow();
        settlement.ReversedBy = currentUser.UserId;
        settlement.ReversalReason = input.Reason.Trim();
        FinancialTransaction? inverse = null;
        if (settlement.TransactionId is { } original && settlement.BankAccountId is { } account)
        {
            inverse = new FinancialTransaction
            {
                BankAccountId = account, Date = Today, Amount = -settlement.Total, Type = TransactionType.Receipt, Description = "Estorno do recebimento " + receivable.Number,
                ReceivableId = receivable.Id, SettlementId = settlement.Id, ReversesTransactionId = original, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
            };
            db.FinancialTransactions.Add(inverse);
            settlement.ReversalTransactionId = inverse.Id;
        }

        await RecalculateAsync(receivable, null, cancellationToken);
        await NotifyOwnersAsync($"Baixa de {receivable.Number} estornada por {await WhoAsync(cancellationToken)}: {settlement.ReversalReason}", receivable.Id, cancellationToken);
        Publish(receivable, settlement, true);
        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([receivable], cancellationToken))[0];
    }

    private async Task<Result<ReceivableDetail>> ApplyAsync(Receivable receivable, ReceivableSettlement settlement, FinancialTransaction? transaction, CancellationToken ct)
    {
        db.ReceivableSettlements.Add(settlement);
        if (transaction is not null)
        {
            db.FinancialTransactions.Add(transaction);
        }

        await RecalculateAsync(receivable, settlement, ct);
        if (settlement.Date < Today.AddDays(-(await parameters.LimitsAsync(ct)).BackdateDays))
        {
            await NotifyOwnersAsync($"Baixa de {receivable.Number} com data de {settlement.Date:dd/MM/yyyy}, lançada por {await WhoAsync(ct)}.", receivable.Id, ct);
        }

        Publish(receivable, settlement, false);
        await db.SaveChangesAsync(ct);
        return (await DetailsAsync([receivable], ct))[0];
    }

    /// <summary>Invariante: saldo e situação a partir de todas as baixas do título, mais a nova ainda não gravada.</summary>
    private async Task RecalculateAsync(Receivable receivable, ReceivableSettlement? added, CancellationToken ct)
    {
        var all = await db.ReceivableSettlements.Where(s => s.ReceivableId == receivable.Id).ToListAsync(ct);
        if (added is not null && !all.Contains(added))
        {
            all.Add(added);
        }

        receivable.Recalculate(all);
    }

    private void Publish(Receivable receivable, ReceivableSettlement settlement, bool reversal) =>
        events.Publish(new ReceivableSettled(receivable.Id, settlement.Id, receivable.PartyId, receivable.ProjectId, receivable.DueDate, settlement.Date, settlement.Principal, reversal));

    /// <summary>Data da baixa (CA 10, 14 e 24): nunca futura; mais de N dias no passado exige motivo; nunca antes do saldo inicial.</summary>
    private async Task<Error?> DateErrorAsync(DateOnly date, string? reason, DateOnly? openingDate, CancellationToken ct)
    {
        if (date > Today)
        {
            return Invalid("date", "A data do recebimento não pode ser futura.");
        }

        if (openingDate is { } opening && date < opening)
        {
            return Invalid("date", $"A data é anterior ao saldo inicial da conta ({opening:dd/MM/yyyy}).");
        }

        var limits = await parameters.LimitsAsync(ct);
        if (date < Today.AddDays(-limits.BackdateDays))
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Invalid("backdateReason", "Informe o motivo da data retroativa.");
            }

            if (reason.Trim().Length > 200)
            {
                return Invalid("backdateReason", "Informe o motivo com até 200 caracteres.");
            }
        }

        return null;
    }

    /// <summary>Desconto acima do % ou do valor do parâmetro (CA 13): só com <c>finance.approve</c>.</summary>
    private async Task<Error?> DiscountErrorAsync(decimal discount, decimal balanceBefore, CancellationToken ct)
    {
        if (discount <= 0)
        {
            return null;
        }

        var limits = await parameters.LimitsAsync(ct);
        var pct = balanceBefore > 0 ? discount / balanceBefore * 100 : 100;
        if ((pct > limits.DiscountApprovalPct || discount > limits.DiscountApprovalAmount) && !await permissions.HasPermissionAsync("finance", "approve", ct))
        {
            return Error.Forbidden("discount_requires_approval", "Desconto acima do limite: só quem aprova em Caixa e pagamentos pode confirmar.");
        }

        return null;
    }

    private static Error NotOpen(Receivable receivable) =>
        Error.Conflict("receivable_not_open", $"O título {receivable.Number} não está em aberto.");
}
