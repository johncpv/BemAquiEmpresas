using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class BankMovementService
{
    private static readonly Dictionary<BankEntryKind, string> KindNames = new()
    {
        [BankEntryKind.BankFee] = "Tarifa bancária", [BankEntryKind.Iof] = "IOF", [BankEntryKind.InterestPaid] = "Juros pagos",
        [BankEntryKind.InterestReceived] = "Juros recebidos", [BankEntryKind.Yield] = "Rendimento", [BankEntryKind.OtherOut] = "Outra saída", [BankEntryKind.OtherIn] = "Outra entrada",
    };

    /// <summary>Nome do tipo na tela e nos avisos ("Tarifa bancária", "IOF"...).</summary>
    public static string KindName(BankEntryKind kind) => KindNames[kind];

    /// <summary>
    /// Lançar tarifa ou rendimento (CA 11 e 12): conta do plano analítica do lado certo (despesa na saída, receita na entrada),
    /// motivo obrigatório, transação <c>BankEntry</c> com o sinal do tipo e o Proprietário avisado.
    /// </summary>
    public async Task<Result<BankEntryDetail>> CreateEntryAsync(BankEntryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Amount <= 0)
        {
            return ReceivableService.Invalid("amount", "Informe um valor maior que zero.");
        }

        if (Trim(input.Reason) is not { Length: <= 200 } reason)
        {
            return ReceivableService.Invalid("reason", "Informe o motivo, com até 200 caracteres.");
        }

        var account = await db.BankAccounts.FirstOrDefaultAsync(a => a.Id == input.BankAccountId && a.IsActive, cancellationToken);
        if (account is null)
        {
            return ReceivableService.Invalid("bankAccountId", "Escolha uma conta ativa.");
        }

        if (await DateErrorAsync(input.Date, input.BackdateReason, [account], cancellationToken) is { } dateError)
        {
            return dateError;
        }

        // BNK-03b CA 11: data no período de conciliação fechado pede finance.approve e motivo (no campo do motivo da data).
        var closed = await period.IsClosedAsync(account.Id, input.Date, cancellationToken);
        if (closed && await period.CheckAsync(account.Id, input.Date, input.BackdateReason, "backdateReason", "lançar", cancellationToken) is { } closedError)
        {
            return closedError;
        }

        var outflow = BankEntry.IsOutflow(input.Kind);
        var options = await chart.PostableAsync(outflow ? AccountType.Expense : AccountType.Revenue, cancellationToken);
        var planAccount = input.AccountId ?? options.FirstOrDefault(a => a.Code == BankEntry.SuggestedAccountCode(input.Kind))?.Id;
        if (planAccount is null || options.All(a => a.Id != planAccount))
        {
            return ReceivableService.Invalid("accountId", outflow ? "Escolha uma conta de despesa analítica." : "Escolha uma conta de receita analítica.");
        }

        if (input.CostCenterId is { } center && !await db.CostCenters.AnyAsync(c => c.Id == center && c.IsActive, cancellationToken))
        {
            return ReceivableService.Invalid("costCenterId", "Escolha um centro de custo ativo.");
        }

        var amount = outflow ? -input.Amount : input.Amount;
        var backdated = await BackdatedAsync(input.Date, cancellationToken);
        var entry = new BankEntry
        {
            BankAccountId = account.Id, Kind = input.Kind, Date = input.Date, Amount = amount, AccountId = planAccount.Value, CostCenterId = input.CostCenterId, Reason = reason,
            BackdateReason = backdated || closed ? Trim(input.BackdateReason) : null, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        var transaction = EntryLine(entry, amount, $"{KindNames[input.Kind]}: {Cut(reason, 150)}", null);
        entry.TransactionId = transaction.Id;
        db.BankEntries.Add(entry);
        db.FinancialTransactions.Add(transaction);
        var link = $"/app/financeiro/contas/{account.Id}/extrato?semOp=1";
        await NotifyOwnersAsync($"{KindNames[input.Kind]} de {ReceivableService.Money(input.Amount)} lançado sem OP em {account.Name} por {await WhoAsync(cancellationToken)}: {Cut(reason, 100)}.", link, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Detail(entry);
    }

    /// <summary>Estornar o lançamento (CA 13): transação inversa, o lançamento marcado e o Proprietário avisado.</summary>
    public async Task<Result<BankEntryDetail>> ReverseEntryAsync(Guid id, ReasonInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 200)
        {
            return ReceivableService.Invalid("reason", "Informe o motivo, com até 200 caracteres.");
        }

        var entry = await db.BankEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entry is null)
        {
            return Error.NotFound("bank_entry_not_found", "Lançamento não encontrado.");
        }

        if (entry.ReversedAt is not null || entry.Version != input.Version)
        {
            return AlreadyReversed();
        }

        if (await period.CheckAsync(entry.BankAccountId, Today, input.Reason, "reason", "estornar um lançamento", cancellationToken) is { } closedError)
        {
            return closedError;
        }

        db.Entry(entry).Property(e => e.Version).OriginalValue = input.Version;
        db.FinancialTransactions.Add(EntryLine(entry, -entry.Amount, "Estorno: " + KindNames[entry.Kind], entry.TransactionId));
        entry.ReversedAt = clock.GetUtcNow();
        entry.ReversedBy = currentUser.UserId;
        entry.ReversalReason = input.Reason.Trim();
        await NotifyOwnersAsync($"Lançamento sem OP ({KindNames[entry.Kind]}) estornado por {await WhoAsync(cancellationToken)}: {Cut(entry.ReversalReason, 100)}",
            $"/app/financeiro/contas/{entry.BankAccountId}/extrato?semOp=1", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Detail(entry);
    }

    private FinancialTransaction EntryLine(BankEntry entry, decimal amount, string description, Guid? reverses) => new()
    {
        BankAccountId = entry.BankAccountId, Date = reverses is null ? entry.Date : Today, Amount = amount, Type = TransactionType.BankEntry, Description = description,
        BankEntryId = entry.Id, ReversesTransactionId = reverses, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
    };

    private static BankEntryDetail Detail(BankEntry e) => new(e.Id, e.BankAccountId, e.Kind, e.Date, e.Amount, e.AccountId, e.Reason, e.ReversedAt != null, e.ReversalReason, e.Version);
}
