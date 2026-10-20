using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class BankStatementService
{
    /// <summary>Documento de origem de cada linha: título a receber, OP, transferência (o extrato da outra conta) ou lançamento sem OP.</summary>
    private async Task<List<StatementLine>> LinesAsync(Guid accountId, List<FinancialTransaction> rows, decimal running, CancellationToken ct)
    {
        var settlementIds = rows.Where(r => r.PayableId != null && r.SettlementId != null).Select(r => r.SettlementId!.Value).ToList();
        var orders = await db.PayableSettlements.AsNoTracking().Where(s => settlementIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.PaymentOrderId, ct);
        var transferIds = rows.Where(r => r.TransferId != null).Select(r => r.TransferId!.Value).ToList();
        var transfers = await db.BankTransfers.AsNoTracking().Where(t => transferIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var entryIds = rows.Where(r => r.BankEntryId != null).Select(r => r.BankEntryId!.Value).ToList();
        var entries = await db.BankEntries.AsNoTracking().Where(e => entryIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var result = new List<StatementLine>();
        foreach (var t in rows)
        {
            running += t.Amount;
            string? origin = null;
            string? link = null;
            var reversed = false;
            uint? version = null;
            if (t.ReceivableId is { } receivable)
            {
                (origin, link) = ("Título a receber", "/app/financeiro/receber/" + receivable);
            }
            else if (t.SettlementId is { } s && orders.TryGetValue(s, out var order))
            {
                (origin, link) = ("Ordem de pagamento", "/app/financeiro/ordens-de-pagamento/" + order);
            }
            else if (t.TransferId is { } tr && transfers.TryGetValue(tr, out var transfer))
            {
                var other = transfer.FromAccountId == accountId ? transfer.ToAccountId : transfer.FromAccountId;
                (origin, link, reversed, version) = ("Transferência", $"/app/financeiro/contas/{other}/extrato", transfer.ReversedAt != null, transfer.Version);
            }
            else if (t.BankEntryId is { } en && entries.TryGetValue(en, out var entry))
            {
                (origin, reversed, version) = ("Lançamento sem OP", entry.ReversedAt != null, entry.Version);
            }

            result.Add(new StatementLine(t.Id, t.Date, t.Description, t.Type.ToString(), origin, link, t.Amount, running, t.BankEntryId != null, t.TransferId, t.BankEntryId,
                t.ReversesTransactionId != null, reversed, version));
        }

        return result;
    }
}
