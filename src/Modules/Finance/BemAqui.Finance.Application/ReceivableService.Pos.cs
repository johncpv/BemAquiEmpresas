using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;

namespace BemAqui.Finance.Application;

public sealed partial class ReceivableService
{
    public const string PosReopenReason = "Caixa reaberto";

    /// <summary>
    /// Baixa do fechamento de caixa (CX-03, origem <see cref="SettlementSource.Pos"/>): o saldo inteiro do título entra na conta (o dinheiro da venda
    /// na gaveta, o Pix "Não localizado" na Quebra de caixa), sem usuário e sem aviso (o fato já aconteceu na gaveta). Só a reabertura da sessão
    /// desfaz (<see cref="ReversePosSettlement"/>); o estorno comum recusa com 409.
    /// </summary>
    public static void AddPosSettlement(IFinanceDb db, Receivable receivable, IReadOnlyCollection<ReceivableSettlement> active, Guid bankAccountId, DateOnly date, string? backdateReason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(receivable);
        ArgumentNullException.ThrowIfNull(active);
        var amount = receivable.Balance;
        var settlement = new ReceivableSettlement
        {
            ReceivableId = receivable.Id, BankAccountId = bankAccountId, Date = date, Principal = amount, Total = amount, BalanceBefore = amount, Kind = SettlementKind.Settlement,
            Source = SettlementSource.Pos, BackdateReason = backdateReason, CreatedAt = now,
        };
        var transaction = new FinancialTransaction
        {
            BankAccountId = bankAccountId, Date = date, Amount = amount, Type = TransactionType.Receipt, Description = "Recebimento " + receivable.Number, ReceivableId = receivable.Id,
            SettlementId = settlement.Id, CreatedAt = now,
        };
        settlement.TransactionId = transaction.Id;
        db.ReceivableSettlements.Add(settlement);
        db.FinancialTransactions.Add(transaction);
        receivable.Recalculate([.. active, settlement]);
    }

    /// <summary>Estorno da baixa do fechamento na reabertura da sessão (CX-03 CA 7): a transação inversa com a data de hoje.</summary>
    public static void ReversePosSettlement(IFinanceDb db, Receivable receivable, ReceivableSettlement settlement, DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(receivable);
        ArgumentNullException.ThrowIfNull(settlement);
        settlement.ReversedAt = now;
        settlement.ReversalReason = PosReopenReason;
        if (settlement.TransactionId is { } original && settlement.BankAccountId is { } account)
        {
            var inverse = new FinancialTransaction
            {
                BankAccountId = account, Date = today, Amount = -settlement.Total, Type = TransactionType.Receipt, Description = "Estorno do recebimento " + receivable.Number,
                ReceivableId = receivable.Id, SettlementId = settlement.Id, ReversesTransactionId = original, CreatedAt = now,
            };
            db.FinancialTransactions.Add(inverse);
            settlement.ReversalTransactionId = inverse.Id;
        }
    }
}
