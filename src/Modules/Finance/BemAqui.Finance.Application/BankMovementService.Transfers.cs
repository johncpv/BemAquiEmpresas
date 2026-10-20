using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class BankMovementService
{
    /// <summary>
    /// Transferir (CA 5 a 8): duas transações ligadas, na mesma transação; conta de destino com menos de 30 dias só com
    /// <c>finance.approve</c>; valor acima de <c>finance.transfer_notify_amount</c> avisa o Proprietário.
    /// </summary>
    public async Task<Result<BankTransferDetail>> TransferAsync(TransferInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.FromAccountId == input.ToAccountId)
        {
            return ReceivableService.Invalid("toAccountId", "Escolha uma conta de destino diferente da origem.");
        }

        if (input.Amount <= 0)
        {
            return ReceivableService.Invalid("amount", "Informe um valor maior que zero.");
        }

        var accounts = await db.BankAccounts.Where(a => (a.Id == input.FromAccountId || a.Id == input.ToAccountId) && a.IsActive).ToListAsync(cancellationToken);
        var from = accounts.FirstOrDefault(a => a.Id == input.FromAccountId);
        var to = accounts.FirstOrDefault(a => a.Id == input.ToAccountId);
        if (from is null || to is null)
        {
            return ReceivableService.Invalid(from is null ? "fromAccountId" : "toAccountId", "Escolha uma conta ativa.");
        }

        if (await DateErrorAsync(input.Date, input.BackdateReason, accounts, cancellationToken) is { } dateError)
        {
            return dateError;
        }

        var age = (clock.GetUtcNow() - to.CreatedAt).TotalDays;
        if (age < 30 && !await permissions.HasPermissionAsync("finance", "approve", cancellationToken))
        {
            return Error.Forbidden("transfer_to_new_account", $"A conta {to.Name} foi criada há {(int)age} dias: só quem aprova em Caixa e pagamentos pode transferir para ela.");
        }

        var backdated = await BackdatedAsync(input.Date, cancellationToken);
        var description = Trim(input.Description) ?? $"Transferência de {from.Name} para {to.Name}";
        var transfer = new BankTransfer
        {
            FromAccountId = from.Id, ToAccountId = to.Id, Amount = input.Amount, Date = input.Date, Description = Cut(description, 200),
            BackdateReason = backdated ? Trim(input.BackdateReason) : null, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        db.BankTransfers.Add(transfer);
        db.FinancialTransactions.AddRange(TransferLine(transfer, from.Id, -input.Amount, null), TransferLine(transfer, to.Id, input.Amount, null));
        var limits = await parameters.LimitsAsync(cancellationToken);
        var link = $"/app/financeiro/contas/{from.Id}/extrato";
        if (input.Amount > limits.TransferNotifyAmount)
        {
            await NotifyOwnersAsync($"Transferência de {ReceivableService.Money(input.Amount)} de {from.Name} para {to.Name}, por {await WhoAsync(cancellationToken)}.", link, cancellationToken);
        }

        if (backdated)
        {
            await NotifyOwnersAsync($"Transferência com data de {input.Date:dd/MM/yyyy}, lançada por {await WhoAsync(cancellationToken)}.", link, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Detail(transfer);
    }

    /// <summary>Estornar a transferência (CA 10): as duas inversas, a transferência marcada e o Proprietário avisado.</summary>
    public async Task<Result<BankTransferDetail>> ReverseTransferAsync(Guid id, ReasonInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 200)
        {
            return ReceivableService.Invalid("reason", "Informe o motivo, com até 200 caracteres.");
        }

        var transfer = await db.BankTransfers.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (transfer is null)
        {
            return Error.NotFound("transfer_not_found", "Transferência não encontrada.");
        }

        if (transfer.ReversedAt is not null || transfer.Version != input.Version)
        {
            return AlreadyReversed();
        }

        db.Entry(transfer).Property(t => t.Version).OriginalValue = input.Version;
        var originals = await db.FinancialTransactions.Where(t => t.TransferId == transfer.Id && t.ReversesTransactionId == null).ToListAsync(cancellationToken);
        db.FinancialTransactions.AddRange(originals.Select(o => TransferLine(transfer, o.BankAccountId, -o.Amount, o.Id)));
        transfer.ReversedAt = clock.GetUtcNow();
        transfer.ReversedBy = currentUser.UserId;
        transfer.ReversalReason = input.Reason.Trim();
        await NotifyOwnersAsync($"Transferência de {ReceivableService.Money(transfer.Amount)} estornada por {await WhoAsync(cancellationToken)}: {Cut(transfer.ReversalReason, 100)}",
            $"/app/financeiro/contas/{transfer.FromAccountId}/extrato", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Detail(transfer);
    }

    private FinancialTransaction TransferLine(BankTransfer transfer, Guid account, decimal amount, Guid? reverses) => new()
    {
        BankAccountId = account, Date = reverses is null ? transfer.Date : Today, Amount = amount, Type = TransactionType.Transfer,
        Description = reverses is null ? transfer.Description : "Estorno: " + Cut(transfer.Description, 180), TransferId = transfer.Id, ReversesTransactionId = reverses,
        CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
    };

    private static BankTransferDetail Detail(BankTransfer t) =>
        new(t.Id, t.FromAccountId, t.ToAccountId, t.Amount, t.Date, t.Description, t.ReversedAt != null, t.ReversalReason, t.Version);
}
