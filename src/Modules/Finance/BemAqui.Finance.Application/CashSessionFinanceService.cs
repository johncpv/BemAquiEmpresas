using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>Título gerado pelo fechamento de uma sessão de caixa (CX-03, seção "No financeiro").</summary>
public sealed record CashSessionReceivableView(
    Guid Id, string Number, string Description, DocumentOrigin Origin, decimal? GrossAmount, decimal? ExpectedFee, decimal Amount, decimal Balance, DateOnly? DueDate,
    ReceivableStatus Status);

/// <summary>Transferência da sessão de caixa: fundo de troco, sangria, suprimento, recolhimento e a quebra do dinheiro.</summary>
public sealed record CashSessionTransferView(Guid Id, string Description, decimal Amount, DateOnly Date, string FromAccountName, string ToAccountName, bool Reversed);

/// <summary>O que o fechamento da sessão lançou no Financeiro (CX-03): os títulos e as transferências, com os estornados.</summary>
public sealed record CashSessionFinanceView(IReadOnlyList<CashSessionReceivableView> Receivables, IReadOnlyList<CashSessionTransferView> Transfers);

/// <summary>
/// Leitura do Financeiro de uma sessão de caixa (CX-03): a seção "No financeiro" da sessão e a trava da reabertura (<see cref="ICashSessionFinance"/>).
/// A sessão é só um id para o Financeiro (ele não lê o <c>Pos</c>): sem nada lançado, as listas vêm vazias.
/// </summary>
public sealed class CashSessionFinanceService(IFinanceDb db) : ICashSessionFinance
{
    public async Task<bool> HasReconciledReceiptAsync(Guid cashSessionId, CancellationToken cancellationToken = default) =>
        await db.Receivables.AsNoTracking().Where(r => r.CashSessionId == cashSessionId)
            .Join(db.ReceivableSettlements.AsNoTracking().Where(s => s.ReversedAt == null && s.Source != SettlementSource.Pos), r => r.Id, s => s.ReceivableId, (r, s) => s.Id)
            .AnyAsync(cancellationToken);

    public async Task<CashSessionFinanceView> ViewAsync(Guid cashSessionId, CancellationToken cancellationToken = default)
    {
        var receivables = await db.Receivables.AsNoTracking().Where(r => r.CashSessionId == cashSessionId).OrderBy(r => r.Number)
            .Select(r => new CashSessionReceivableView(r.Id, r.Number, r.Description, r.Origin, r.GrossAmount, r.ExpectedFee, r.Amount, r.Amount - r.PaidAmount, r.DueDate, r.Status))
            .ToListAsync(cancellationToken);
        var transfers = await db.BankTransfers.AsNoTracking().Where(t => t.PosCashSessionId == cashSessionId).OrderBy(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id, t.Description, t.Amount, t.Date, Reversed = t.ReversedAt != null,
                From = db.BankAccounts.IgnoreQueryFilters().Where(a => a.Id == t.FromAccountId).Select(a => a.Name).FirstOrDefault(),
                To = db.BankAccounts.IgnoreQueryFilters().Where(a => a.Id == t.ToAccountId).Select(a => a.Name).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return new CashSessionFinanceView(receivables,
            transfers.Select(t => new CashSessionTransferView(t.Id, t.Description, t.Amount, t.Date, t.From ?? "—", t.To ?? "—", t.Reversed)).ToList());
    }
}
