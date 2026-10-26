using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// "Mudou desde a sua última consulta" (CA 9, ponto 13): sem fechamento de mês, o DRE é recalculado a cada leitura; aqui entram os
/// documentos com data já passada na última consulta (data anterior ao dia dela, num mês do período) que foram lançados,
/// cancelados ou estornados depois dela. Folha sem <c>hr.view_sensitive</c> vem sem número e sem valor (CA 14).
/// </summary>
public sealed partial class DreService
{
    public const int MaxChanges = 200;

    internal sealed record ChangeList(int Count, IReadOnlyList<DateOnly> Months, IReadOnlyList<DreChange> Items);

    public async Task<Result<DreChanges>> ChangesAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var period = Period(from, to);
        if (period.Error is { } error)
        {
            return error;
        }

        DateTimeOffset? since = currentUser.UserId is { } me
            ? await db.DreViewMarks.AsNoTracking().Where(m => m.UserId == me).Select(m => (DateTimeOffset?)m.ViewedAt).FirstOrDefaultAsync(cancellationToken)
            : null;
        if (since is not { } s)
        {
            return new DreChanges(null, 0, []);
        }

        var list = await ChangeListAsync(s, period.From, period.To, cancellationToken);
        return new DreChanges(s, list.Count, list.Items);
    }

    /// <summary>A lista para a faixa e para "Ver o que mudou"; a tela pede antes de gravar a marca nova.</summary>
    public async Task<Result<DreChanges>> ChangesSinceAsync(DateTimeOffset since, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var period = Period(from, to);
        if (period.Error is { } error)
        {
            return error;
        }

        if (since > clock.GetUtcNow())
        {
            return ReceivableService.Invalid("since", "A data da última consulta não pode estar no futuro.");
        }

        var list = await ChangeListAsync(since, period.From, period.To, cancellationToken);
        return new DreChanges(since, list.Count, list.Items);
    }

    internal async Task<ChangeList> ChangeListAsync(DateTimeOffset since, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var sinceDay = DateOnly.FromDateTime(BusinessTime.ToBusiness(since).DateTime);
        var limit = sinceDay.AddDays(-1) < to ? sinceDay.AddDays(-1) : to;
        if (limit < from)
        {
            return new ChangeList(0, [], []);
        }

        var changes = new List<DreChange>();
        var receivables = await db.Receivables.AsNoTracking()
            .Where(r => r.Origin != DocumentOrigin.Opening && r.IssueDate >= from && r.IssueDate <= limit
                && ((r.CreatedAt > since && r.Status != ReceivableStatus.Planned && r.Status != ReceivableStatus.Cancelled)
                    || (r.Status == ReceivableStatus.Cancelled && r.CancelledAt > since && r.CreatedAt <= since && r.DueDate != null)))
            .Select(r => new { r.Id, r.Number, r.IssueDate, r.Amount, r.Status, r.CreatedAt, r.CancelledAt }).ToListAsync(ct);
        changes.AddRange(receivables.Select(r => r.Status == ReceivableStatus.Cancelled
            ? new DreChange("Receivable", r.Id, r.Number, r.IssueDate, DreBuilder.MonthOf(r.IssueDate), "Cancelado", -r.Amount, r.CancelledAt!.Value, "/app/financeiro/receber/" + r.Id)
            : new DreChange("Receivable", r.Id, r.Number, r.IssueDate, DreBuilder.MonthOf(r.IssueDate), "Lançado", r.Amount, r.CreatedAt, "/app/financeiro/receber/" + r.Id)));

        var sensitive = await permissions.HasPermissionAsync("hr", "view_sensitive", ct);
        var payables = await db.Payables.AsNoTracking()
            .Where(p => p.Origin != DocumentOrigin.Opening
                && ((p.Competence != null && p.Competence >= from && p.Competence <= limit) || (p.Competence == null && p.IssueDate >= from && p.IssueDate <= limit))
                && ((p.CreatedAt > since && p.Status != PayableStatus.Planned && p.Status != PayableStatus.Cancelled)
                    || (p.Status == PayableStatus.Cancelled && p.CancelledAt > since && p.CreatedAt <= since)))
            .Select(p => new { p.Id, p.Number, Date = p.Competence ?? p.IssueDate, p.Amount, p.Status, p.Origin, p.CreatedAt, p.CancelledAt }).ToListAsync(ct);
        foreach (var p in payables)
        {
            var hidden = !sensitive && p.Origin == DocumentOrigin.Payroll;
            var cancelled = p.Status == PayableStatus.Cancelled;
            changes.Add(new DreChange(hidden ? "Payroll" : "Payable", p.Id, hidden ? null : p.Number, p.Date, DreBuilder.MonthOf(p.Date), cancelled ? "Cancelado" : "Lançado",
                hidden ? null : cancelled ? p.Amount : -p.Amount, cancelled ? p.CancelledAt!.Value : p.CreatedAt, hidden ? null : "/app/financeiro/pagar/" + p.Id));
        }

        var bank = await db.BankEntries.AsNoTracking()
            .Where(e => e.Date >= from && e.Date <= limit && ((e.CreatedAt > since && e.ReversedAt == null) || (e.ReversedAt > since && e.CreatedAt <= since)))
            .Select(e => new { e.Id, e.BankAccountId, e.Date, e.Amount, e.CreatedAt, e.ReversedAt }).ToListAsync(ct);
        changes.AddRange(bank.Select(e => e.ReversedAt is { } at
            ? new DreChange("BankEntry", e.Id, null, e.Date, DreBuilder.MonthOf(e.Date), "Estornado", -e.Amount, at, $"/app/financeiro/contas/{e.BankAccountId}/extrato")
            : new DreChange("BankEntry", e.Id, null, e.Date, DreBuilder.MonthOf(e.Date), "Lançado", e.Amount, e.CreatedAt, $"/app/financeiro/contas/{e.BankAccountId}/extrato")));

        var received = await db.ReceivableSettlements.AsNoTracking()
            .Where(s => s.Date >= from && s.Date <= limit && (s.Interest != 0 || s.Fine != 0 || s.Discount != 0)
                && ((s.CreatedAt > since && s.ReversedAt == null) || (s.ReversedAt > since && s.CreatedAt <= since)))
            .Join(db.Receivables.AsNoTracking(), s => s.ReceivableId, r => r.Id, (s, r) => new { r.Id, r.Number, s.Date, Effect = s.Interest + s.Fine - s.Discount, s.CreatedAt, s.ReversedAt })
            .ToListAsync(ct);
        changes.AddRange(received.Select(s => new DreChange("ReceivableSettlement", s.Id, s.Number, s.Date, DreBuilder.MonthOf(s.Date), s.ReversedAt is null ? "Lançado" : "Estornado",
            s.ReversedAt is null ? s.Effect : -s.Effect, s.ReversedAt ?? s.CreatedAt, "/app/financeiro/receber/" + s.Id)));

        var paid = await db.PayableSettlements.AsNoTracking()
            .Where(s => s.Date >= from && s.Date <= limit && (s.Interest != 0 || s.Fine != 0 || s.Discount != 0)
                && ((s.CreatedAt > since && s.ReversedAt == null) || (s.ReversedAt > since && s.CreatedAt <= since)))
            .Join(db.Payables.AsNoTracking(), s => s.PayableId, p => p.Id, (s, p) => new { p.Id, p.Number, p.Origin, s.Date, Effect = s.Discount - s.Interest - s.Fine, s.CreatedAt, s.ReversedAt })
            .ToListAsync(ct);
        foreach (var s in paid)
        {
            var hidden = !sensitive && s.Origin == DocumentOrigin.Payroll;
            changes.Add(new DreChange(hidden ? "Payroll" : "PayableSettlement", s.Id, hidden ? null : s.Number, s.Date, DreBuilder.MonthOf(s.Date), s.ReversedAt is null ? "Lançado" : "Estornado",
                hidden ? null : s.ReversedAt is null ? s.Effect : -s.Effect, s.ReversedAt ?? s.CreatedAt, hidden ? null : "/app/financeiro/pagar/" + s.Id));
        }

        var months = changes.Select(c => c.Month).Distinct().Order().ToList();
        return new ChangeList(changes.Count, months, changes.OrderByDescending(c => c.At).Take(MaxChanges).ToList());
    }
}
