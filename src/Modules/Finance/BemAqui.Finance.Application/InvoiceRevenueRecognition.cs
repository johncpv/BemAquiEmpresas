using BemAqui.Finance.Domain;
using BemAqui.Fiscal.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Receita pela nota (FIN-08 CA 4, FIS-03a CA 13, D4): as notas autorizadas e não canceladas do ambiente de produção, pelo valor total, na
/// <b>data da nota</b>, lidas do <see cref="IOutgoingInvoices"/>. A conta é a dos títulos do projeto (a receita do contrato); sem título, a
/// conta 1.1 "Venda de equipamentos". Os títulos de contrato viram só cobrança. O sinal (marco Assinatura), com o parâmetro "Sinal como
/// adiantamento até a nota" ligado, fica em "Adiantamentos de clientes" até a primeira nota do projeto e não soma de novo na receita (o
/// valor da nota já é o da venda).
/// </summary>
public sealed class InvoiceRevenueRecognition(IFinanceDb db, DreParametersService parameters, IOutgoingInvoices invoices) : IRevenueRecognition
{
    public const string FallbackAccountCode = "1.1";

    public RevenueBasis Basis => RevenueBasis.Invoice;

    public async Task<RevenueRecognitionResult> RecognizeAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? projectIds, CancellationToken cancellationToken = default)
    {
        var notes = await invoices.RevenueAsync(from, to, projectIds, cancellationToken);
        var accounts = await AccountsAsync(notes, cancellationToken);
        var amounts = notes.Where(n => accounts.ContainsKey(n.InvoiceId)).GroupBy(n => (Account: accounts[n.InvoiceId], n.ProjectId, n.IssuedOn))
            .Select(g => new RevenueAmount(g.Key.Account, g.Key.ProjectId, g.Key.IssuedOn, g.Sum(n => n.Amount))).ToList();
        var advances = await parameters.DownPaymentAsAdvanceAsync(cancellationToken) ? await DownPaymentsAsync(to, projectIds, cancellationToken) : [];
        return new RevenueRecognitionResult(amounts, advances.Where(a => a.RecognizedOn is null || a.RecognizedOn >= from).ToList());
    }

    public async Task<IReadOnlyList<RevenueDocument>> DocumentsAsync(IReadOnlyCollection<Guid> accountIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        var notes = await invoices.RevenueAsync(from, to, null, cancellationToken);
        var accounts = await AccountsAsync(notes, cancellationToken);
        return notes.Where(n => accounts.TryGetValue(n.InvoiceId, out var a) && accountIds.Contains(a))
            .Select(n => new RevenueDocument("Invoice", n.InvoiceId, Number(n), n.PartyId ?? Guid.Empty, accounts[n.InvoiceId], n.ProjectId, n.IssuedOn, n.Amount, null,
                "/app/fiscal/saida/" + n.InvoiceId.ToString("D"))).ToList();
    }

    private static string Number(InvoicedRevenue n) => (n.Type == InvoiceType.NFe ? "NF-e " : "NFS-e ") + n.Series + "/" + n.Number;

    /// <summary>Conta de cada nota: a mais usada nos títulos não cancelados do projeto; sem título, a 1.1.</summary>
    private async Task<Dictionary<Guid, Guid>> AccountsAsync(IReadOnlyList<InvoicedRevenue> notes, CancellationToken ct)
    {
        var projects = notes.Where(n => n.ProjectId != null).Select(n => n.ProjectId!.Value).Distinct().ToList();
        var byProject = projects.Count == 0 ? new Dictionary<Guid, Guid>() : (await db.Receivables.AsNoTracking()
                .Where(r => r.ProjectId != null && projects.Contains(r.ProjectId.Value) && r.Status != BemAqui.Finance.Contracts.ReceivableStatus.Cancelled)
                .GroupBy(r => new { ProjectId = r.ProjectId!.Value, r.AccountId }).Select(g => new { g.Key.ProjectId, g.Key.AccountId, Count = g.Count() }).ToListAsync(ct))
            .GroupBy(x => x.ProjectId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Count).First().AccountId);
        var fallback = await db.Accounts.AsNoTracking().Where(a => a.Code == FallbackAccountCode).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
        var result = new Dictionary<Guid, Guid>();
        foreach (var note in notes)
        {
            if (note.ProjectId is { } p && byProject.TryGetValue(p, out var account))
            {
                result[note.InvoiceId] = account;
            }
            else if (fallback is { } f)
            {
                result[note.InvoiceId] = f;
            }
        }

        return result;
    }

    /// <summary>Sinais emitidos até <paramref name="to"/>: adiantamento até a primeira nota do projeto (nunca antes da própria emissão).</summary>
    private async Task<List<RevenueAdvance>> DownPaymentsAsync(DateOnly to, IReadOnlyCollection<Guid>? projectIds, CancellationToken ct)
    {
        var query = db.Receivables.AsNoTracking().Where(r => r.Origin == DocumentOrigin.Contract && r.TriggerEvent == TriggerEvent.Signature
            && !DreRules.ExcludedReceivableStatuses.Contains(r.Status) && r.IssueDate <= to && r.ProjectId != null);
        if (projectIds is not null)
        {
            var ids = projectIds.ToList();
            query = query.Where(r => ids.Contains(r.ProjectId!.Value));
        }

        var downs = await query.Select(r => new { r.Id, r.Number, r.PartyId, r.AccountId, r.ProjectId, r.IssueDate, r.Amount }).ToListAsync(ct);
        if (downs.Count == 0)
        {
            return [];
        }

        var first = (await invoices.RevenueAsync(DateOnly.MinValue, DateOnly.MaxValue, downs.Select(d => d.ProjectId!.Value).Distinct().ToList(), ct))
            .GroupBy(n => n.ProjectId!.Value).ToDictionary(g => g.Key, g => g.Min(n => n.IssuedOn));
        return downs.Select(d =>
        {
            DateOnly? on = first.TryGetValue(d.ProjectId!.Value, out var date) ? (date < d.IssueDate ? d.IssueDate : date) : null;
            return new RevenueAdvance(d.Id, d.Number, d.PartyId, d.AccountId, d.ProjectId, d.IssueDate, on, d.Amount, on is null ? null : "Até a primeira nota do projeto", RevenueBasis.Invoice);
        }).ToList();
    }
}

/// <summary>Data de corte fiscal (FIN-08 CA 4): a do Fiscal (<c>fiscal.project_tax_cutover_on</c>, gravada na primeira nota autorizada em produção).</summary>
public sealed class FiscalRevenueCutover(IOutgoingInvoices invoices) : IRevenueCutover
{
    public Task<DateOnly?> CutoverOnAsync(CancellationToken cancellationToken = default) => invoices.CutoverOnAsync(cancellationToken);
}
