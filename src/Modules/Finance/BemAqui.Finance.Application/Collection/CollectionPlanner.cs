using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>Título com o passo que cai no dia.</summary>
public sealed record CollectionDue(Receivable Receivable, int Step);

/// <summary>
/// Agenda da régua (FIN-09 CA 4): num dia útil, os títulos em aberto, de origem coberta, cujo passo cai no dia (o passo de dia não útil vai
/// para o próximo dia útil, pelo calendário padrão do tenant) e que ainda não têm lembrete daquele passo.
/// </summary>
public sealed class CollectionPlanner(IFinanceDb db, IBusinessCalendar calendar, TimeProvider clock)
{
    /// <summary>Próximo dia de envio: hoje, se for útil e o horário ainda não passou; senão o próximo dia útil (até 31 dias).</summary>
    public async Task<DateOnly> NextRunDateAsync(TimeOnly sendTime, CancellationToken cancellationToken = default)
    {
        var now = BusinessTime.ToBusiness(clock.GetUtcNow());
        var today = DateOnly.FromDateTime(now.DateTime);
        var from = TimeOnly.FromDateTime(now.DateTime) < sendTime ? today : today.AddDays(1);
        var dates = await calendar.WorkingDatesAsync(from, from.AddDays(31), cancellationToken);
        return dates.Count > 0 ? dates[0] : from;
    }

    /// <summary>Títulos com passo em <paramref name="day"/> (vazio se o dia não é útil), na ordem do vencimento e do número.</summary>
    public async Task<IReadOnlyList<CollectionDue>> DueOnAsync(IReadOnlyList<CollectionStep> steps, DateOnly day, bool tracking, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0 || !await calendar.IsBusinessDayAsync(day, cancellationToken))
        {
            return [];
        }

        var previous = await calendar.PreviousBusinessDayAsync(day.AddDays(-1), cancellationToken);
        var days = steps.Select(s => s.Days).ToList();
        var (from, to) = CollectionRules.DueWindow(days, day, previous);
        var query = db.Receivables.Where(r => (r.Status == ReceivableStatus.Open || r.Status == ReceivableStatus.PartiallyPaid)
            && (r.Origin == SharedKernel.DocumentOrigin.Manual || r.Origin == SharedKernel.DocumentOrigin.Contract)
            && r.DueDate != null && r.DueDate >= from && r.DueDate <= to && r.Amount > r.PaidAmount);
        var receivables = await (tracking ? query : query.AsNoTracking()).OrderBy(r => r.DueDate).ThenBy(r => r.Number).ToListAsync(cancellationToken);
        var due = receivables.Select(r => (r, Step: CollectionRules.StepOn(r.DueDate!.Value, days, day, previous))).Where(x => x.Step is not null).ToList();
        if (due.Count == 0)
        {
            return [];
        }

        var ids = due.Select(d => d.r.Id).ToList();
        var existing = (await db.CollectionNotices.AsNoTracking().Where(n => ids.Contains(n.ReceivableId)).Select(n => new { n.ReceivableId, n.Step }).ToListAsync(cancellationToken))
            .Select(n => (n.ReceivableId, n.Step)).ToHashSet();
        return due.Where(d => !existing.Contains((d.r.Id, d.Step!.Value))).Select(d => new CollectionDue(d.r, d.Step!.Value)).ToList();
    }

    /// <summary>
    /// Próximo lembrete previsto do título (CA 8): o primeiro passo ainda sem lembrete cujo dia (levado ao dia útil) é hoje ou depois. Nulo
    /// se nenhum passo falta.
    /// </summary>
    public async Task<(DateOnly Date, int Step)?> NextForAsync(Receivable receivable, IReadOnlyList<CollectionStep> steps, IReadOnlyCollection<int> done, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receivable);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(done);
        if (receivable.DueDate is not { } due)
        {
            return null;
        }

        var today = BusinessTime.Today(clock);
        foreach (var step in steps.Where(s => !done.Contains(s.Days)).OrderBy(s => s.Days))
        {
            var target = due.AddDays(step.Days);
            if (target.AddDays(31) < today)
            {
                continue;
            }

            // O passo de dia não útil sai no próximo dia útil.
            var dates = await calendar.WorkingDatesAsync(target, target.AddDays(31), cancellationToken);
            var effective = dates.Count > 0 ? dates[0] : target;
            if (effective >= today)
            {
                return (effective, step.Days);
            }
        }

        return null;
    }
}
