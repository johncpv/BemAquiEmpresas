using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application.Reconciliation;

public sealed partial class ReconciliationService
{
    /// <summary>
    /// O que o motor e a tela precisam de cada transação: o boleto do Inter (identificadores do banco), o título a receber ou a pagar (número
    /// e terceiro), a OP pela baixa e o documento do terceiro; o documento de origem com o link ("Ver origem").
    /// </summary>
    private sealed class TransactionContext
    {
        public Dictionary<Guid, List<string>> References { get; } = [];

        public Dictionary<Guid, (string? Document, string Role)> Parties { get; } = [];

        public Dictionary<Guid, List<string>> Numbers { get; } = [];

        public Dictionary<Guid, (string Origin, string? Link)> Origins { get; } = [];

        public SuggestionTransaction ToSuggestion(FinancialTransaction t)
        {
            var party = Parties.GetValueOrDefault(t.Id);
            return new SuggestionTransaction(t.Id, t.Date, t.Amount, References.GetValueOrDefault(t.Id) ?? [], party.Document, party.Role, Numbers.GetValueOrDefault(t.Id) ?? []);
        }

        public ReconTransaction View(FinancialTransaction t)
        {
            var (origin, link) = Origins.TryGetValue(t.Id, out var o) ? o : ((string?)null, (string?)null);
            return new ReconTransaction(t.Id, t.Date, t.Amount, t.Description, t.Type, origin, link, t.PendingReview, t.Reconciled, t.Version);
        }
    }

    private async Task<TransactionContext> ContextAsync(List<FinancialTransaction> transactions, CancellationToken ct)
    {
        var context = new TransactionContext();
        if (transactions.Count == 0)
        {
            return context;
        }

        var slipIds = transactions.Where(t => t.BillingSlipId != null).Select(t => t.BillingSlipId!.Value).Distinct().ToList();
        var slips = await db.BillingSlips.AsNoTracking().Where(s => slipIds.Contains(s.Id))
            .Select(s => new { s.Id, s.OurReference, s.OurNumber, s.ProviderRequestId }).ToDictionaryAsync(s => s.Id, ct);
        var receivableIds = transactions.Where(t => t.ReceivableId != null).Select(t => t.ReceivableId!.Value).Distinct().ToList();
        var titles = await db.Receivables.AsNoTracking().Where(r => receivableIds.Contains(r.Id)).Select(r => new { r.Id, r.Number, r.PartyId }).ToDictionaryAsync(r => r.Id, ct);
        var payableIds = transactions.Where(t => t.PayableId != null).Select(t => t.PayableId!.Value).Distinct().ToList();
        var payables = await db.Payables.AsNoTracking().Where(p => payableIds.Contains(p.Id)).Select(p => new { p.Id, p.Number, p.PartyId }).ToDictionaryAsync(p => p.Id, ct);
        var settlementIds = transactions.Where(t => t.PayableId != null && t.SettlementId != null).Select(t => t.SettlementId!.Value).Distinct().ToList();
        var settlements = await db.PayableSettlements.AsNoTracking().Where(s => settlementIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.PaymentOrderId, ct);
        var orderIds = settlements.Values.Distinct().ToList();
        var orders = await db.PaymentOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Number, ct);
        var partyIds = titles.Values.Select(t => t.PartyId).Concat(payables.Values.Where(p => p.PartyId != null).Select(p => p.PartyId!.Value)).Distinct().ToList();
        var documents = (await parties.FindManyAsync(partyIds, ct)).ToDictionary(p => p.Id, p => p.Document);

        foreach (var t in transactions)
        {
            var numbers = new List<string>();
            if (t.BillingSlipId is { } slipId && slips.TryGetValue(slipId, out var slip))
            {
                context.References[t.Id] = new[] { slip.OurReference, slip.OurNumber, slip.ProviderRequestId }.OfType<string>().Where(s => s.Length > 0).ToList();
            }

            if (t.ReceivableId is { } r && titles.TryGetValue(r, out var title))
            {
                numbers.Add(title.Number);
                context.Parties[t.Id] = (documents.GetValueOrDefault(title.PartyId), "do cliente");
                context.Origins[t.Id] = ("Título a receber " + title.Number, "/app/financeiro/receber/" + r);
            }
            else if (t.PayableId is { } p && payables.TryGetValue(p, out var payable))
            {
                numbers.Add(payable.Number);
                if (payable.PartyId is { } party)
                {
                    context.Parties[t.Id] = (documents.GetValueOrDefault(party), "do fornecedor");
                }

                if (t.SettlementId is { } s && settlements.TryGetValue(s, out var order) && orders.TryGetValue(order, out var orderNumber))
                {
                    numbers.Add(orderNumber);
                    context.Origins[t.Id] = ("Ordem de pagamento " + orderNumber, "/app/financeiro/ordens-de-pagamento/" + order);
                }
                else
                {
                    context.Origins[t.Id] = ("Título a pagar " + payable.Number, "/app/financeiro/pagar/" + p);
                }
            }
            else if (t.TransferId is not null)
            {
                context.Origins[t.Id] = ("Transferência", $"/app/financeiro/contas/{t.BankAccountId}/extrato");
            }
            else if (t.BankEntryId is not null)
            {
                context.Origins[t.Id] = ("Lançamento sem OP", $"/app/financeiro/contas/{t.BankAccountId}/extrato?semOp=1");
            }

            context.Numbers[t.Id] = numbers;
        }

        return context;
    }

    /// <summary>Conciliações ativas que tocam as linhas conciliadas do período, com todas as linhas e transações de cada uma.</summary>
    private async Task<List<ReconDone>> DoneAsync(Guid accountId, List<Guid> lineIds, DateOnly? closedUntil, bool full, CancellationToken ct)
    {
        if (lineIds.Count == 0)
        {
            return [];
        }

        var reconciliationIds = await db.BankReconciliationLinks.AsNoTracking().Where(k => lineIds.Contains(k.StatementLineId)).Select(k => k.ReconciliationId).Distinct().ToListAsync(ct);
        var reconciliations = await db.BankReconciliations.AsNoTracking()
            .Where(r => reconciliationIds.Contains(r.Id) && r.BankAccountId == accountId && r.UndoneAt == null).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return await DescribeAsync(reconciliations, closedUntil, full, ct);
    }

    private async Task<List<ReconDone>> DescribeAsync(List<BankReconciliation> reconciliations, DateOnly? closedUntil, bool full, CancellationToken ct)
    {
        var ids = reconciliations.Select(r => r.Id).ToList();
        var links = await db.BankReconciliationLinks.AsNoTracking().Where(k => ids.Contains(k.ReconciliationId)).ToListAsync(ct);
        var lineIds = links.Select(k => k.StatementLineId).Distinct().ToList();
        var transactionIds = links.Select(k => k.TransactionId).Distinct().ToList();
        var lines = await db.BankStatementLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).ToListAsync(ct);
        var transactions = await db.FinancialTransactions.AsNoTracking().Where(t => transactionIds.Contains(t.Id)).ToListAsync(ct);
        var lineViews = await LineViewsAsync(lines, full, ct);
        var context = await ContextAsync(transactions, ct);
        var byId = transactions.ToDictionary(t => t.Id);
        var names = await NamesAsync(reconciliations.Select(r => r.CreatedBy), ct);
        return reconciliations.Select(r =>
        {
            var own = links.Where(k => k.ReconciliationId == r.Id).ToList();
            var ownLines = own.Select(k => k.StatementLineId).Distinct().Select(id => lineViews[id]).ToList();
            var ownTransactions = own.Select(k => k.TransactionId).Distinct().Select(id => context.View(byId[id])).ToList();
            var closed = closedUntil is { } until && ownLines.Any(l => l.Date <= until);
            return new ReconDone(r.Id, r.Kind, r.Confidence, r.Reason, ownLines, ownTransactions, r.CreatedBy is { } by ? names.GetValueOrDefault(by) : null, r.CreatedAt, closed, r.Version);
        }).ToList();
    }
}
