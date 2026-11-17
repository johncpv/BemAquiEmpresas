using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Escrita do recebível de venda (VND-01 CA 11 e 13, <see cref="ISaleReceivables"/>): um título por chave (dia, canal, sessão, forma, provedor,
/// vencimento), que vale o líquido e guarda bruto, taxa prevista e líquido. "Grava o valor": o reprocessamento não duplica. Na conta de receita
/// de vendas (parâmetro <see cref="RevenueAccountKey"/>; sem ele, "Receitas a classificar" do plano padrão). O título nasce aberto, com
/// vencimento na data prevista do crédito; o valor só muda por aqui.
/// </summary>
public sealed class SaleReceivableWriter(IFinanceDb db, INumberSequence numbers, TimeProvider clock) : ISaleReceivables
{
    public const string GroupKeyIndex = "ux_receivables_sale_group_key";

    /// <summary>Parâmetro com o id da conta de receita das vendas (texto). Sem ele, vale a conta "1.9 Receitas a classificar" do seed.</summary>
    public const string RevenueAccountKey = "finance.sales_revenue_account";

    public const string CancelReason = "Vendas do grupo canceladas";

    public async Task<Result<SaleReceivableWrite>> PostAsync(SaleReceivableSummary summary, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.Origin is not (DocumentOrigin.Sale or DocumentOrigin.CardSettlement))
        {
            return Error.Validation("sale_receivable_origin", "Recebível de venda tem origem Venda ou Cartão.");
        }

        if (summary.Gross < 0 || summary.ExpectedFee < 0 || summary.Net != Math.Round(summary.Gross - summary.ExpectedFee, 2, MidpointRounding.AwayFromZero))
        {
            return Error.Validation("sale_receivable_amounts", "Líquido tem de ser o bruto menos a taxa prevista, sem valor negativo.");
        }

        var key = summary.Key.Text;
        var receivable = await db.Receivables.FirstOrDefaultAsync(r => r.SaleGroupKey == key, cancellationToken);
        if (receivable is null)
        {
            if (summary.Net <= 0)
            {
                return new SaleReceivableWrite(Guid.Empty, string.Empty, false, false);
            }

            if (await RevenueAccountAsync(cancellationToken) is not { } account)
            {
                return Error.Validation("sales_revenue_account_missing", "Cadastre uma conta de receita no plano de contas para os recebíveis de venda.");
            }

            receivable = new Receivable
            {
                Number = await numbers.NextAsync(ReceivableService.DocType, cancellationToken), PartyId = summary.PartyId ?? Guid.Empty, Description = Cut(summary.Description),
                AccountId = account, IssueDate = summary.Key.BusinessDate, DueDate = summary.Key.DueDate, Status = ReceivableStatus.Open, Origin = summary.Origin,
                OriginId = summary.Key.SaleId, SaleChannel = summary.Key.Channel, CashSessionId = summary.Key.CashSessionId, SettlementDate = summary.Key.DueDate, SaleGroupKey = key,
                CreatedAt = clock.GetUtcNow(),
            };
            Apply(receivable, summary);
            db.Receivables.Add(receivable);
            await db.SaveChangesAsync(cancellationToken);
            return new SaleReceivableWrite(receivable.Id, receivable.Number, true, true);
        }

        if (receivable.GrossAmount == summary.Gross && receivable.ExpectedFee == summary.ExpectedFee && receivable.NetAmount == summary.Net
            && (summary.Net > 0) == (receivable.Status != ReceivableStatus.Cancelled))
        {
            return new SaleReceivableWrite(receivable.Id, receivable.Number, false, false);
        }

        Apply(receivable, summary);
        var settlements = await db.ReceivableSettlements.Where(s => s.ReceivableId == receivable.Id && s.ReversedAt == null).ToListAsync(cancellationToken);
        if (summary.Net <= 0 && settlements.Count == 0)
        {
            receivable.Status = ReceivableStatus.Cancelled;
            receivable.CancelReason = CancelReason;
            receivable.CancelledAt = clock.GetUtcNow();
        }
        else if (receivable.Status == ReceivableStatus.Cancelled)
        {
            receivable.Status = ReceivableStatus.Open;
            receivable.CancelReason = null;
            receivable.CancelledAt = null;
            receivable.CancelledBy = null;
        }

        receivable.Recalculate(settlements);
        await db.SaveChangesAsync(cancellationToken);
        return new SaleReceivableWrite(receivable.Id, receivable.Number, false, true);
    }

    public async Task<int> ZeroMissingAsync(DateOnly businessDate, string channel, Guid? cashSessionId, IReadOnlyCollection<SaleReceivableKey> keep, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keep);
        var keys = keep.Select(k => k.Text).ToList();
        var query = db.Receivables.Where(r => r.SaleGroupKey != null && r.IssueDate == businessDate && r.SaleChannel == channel && r.Status != ReceivableStatus.Cancelled
            && !keys.Contains(r.SaleGroupKey));
        query = cashSessionId is { } session ? query.Where(r => r.CashSessionId == session) : query.Where(r => r.CashSessionId == null);
        var changed = 0;
        foreach (var receivable in await query.ToListAsync(cancellationToken))
        {
            var zero = new SaleReceivableSummary(new SaleReceivableKey(businessDate, channel, cashSessionId, Guid.Empty, null, receivable.DueDate ?? businessDate), receivable.Origin,
                receivable.PartyId, receivable.Description, 0, 0, 0);
            var settlements = await db.ReceivableSettlements.Where(s => s.ReceivableId == receivable.Id && s.ReversedAt == null).ToListAsync(cancellationToken);
            Apply(receivable, zero);
            if (settlements.Count == 0)
            {
                receivable.Status = ReceivableStatus.Cancelled;
                receivable.CancelReason = CancelReason;
                receivable.CancelledAt = clock.GetUtcNow();
            }

            receivable.Recalculate(settlements);
            changed++;
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }

    /// <summary>A conta de receita das vendas: o parâmetro, se aponta para conta de receita que aceita lançamento; senão a "1.9" do seed ou a primeira de receita bruta.</summary>
    public async Task<Guid?> RevenueAccountAsync(CancellationToken cancellationToken = default)
    {
        var configured = await db.FinanceParameters.AsNoTracking().Where(p => p.Key == RevenueAccountKey).Select(p => p.Text).FirstOrDefaultAsync(cancellationToken);
        var accounts = db.Accounts.AsNoTracking().Where(a => a.Type == AccountType.Revenue && a.AcceptsEntries && a.IsActive && !a.IsDeleted);
        if (Guid.TryParse(configured, out var id) && await accounts.AnyAsync(a => a.Id == id, cancellationToken))
        {
            return id;
        }

        return await accounts.Where(a => a.SeedKey == "1.9").Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken)
            ?? await accounts.Where(a => a.DreGroup == DreGroup.GrossRevenue).OrderBy(a => a.Code).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private static void Apply(Receivable receivable, SaleReceivableSummary summary)
    {
        receivable.GrossAmount = summary.Gross;
        receivable.ExpectedFee = summary.ExpectedFee;
        receivable.NetAmount = summary.Net;
        receivable.Amount = summary.Net;
        receivable.Description = Cut(summary.Description);
    }

    private static string Cut(string text) => text.Length <= 200 ? text : text[..200];
}
