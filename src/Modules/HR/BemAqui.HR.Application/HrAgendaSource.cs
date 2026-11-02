using BemAqui.BuildingBlocks.Agenda;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Detectores do RH para a Minha pauta (DSH-04 CA 2): cargo com apontamento marcado "Sem custo-hora" (PRD-04 CA 5), para quem tem
/// <c>hr.edit</c>; e os ajustes de ponto aguardando a decisão (RH-01b CA 17), para quem tem <c>hr.approve</c>, sem os que a pessoa
/// pediu nem os das próprias marcações (salvo o Proprietário único). Os apontamentos vêm do contrato <see cref="ILaborCostGaps"/> da
/// Produção; nenhuma linha leva justificativa nem motivo de ausência.
/// </summary>
public sealed class HrAgendaSource(IHrDb db, ILaborCostGaps gaps, IUserEmployeeLink links, IUserDirectory users, HrParametersService parameters) : IAgendaSource
{
    public const string PositionsWithoutCost = "hr.positions_without_cost";
    public const string TimeAdjustmentsPending = "hr.time_adjustments_pending";
    public const string ThirteenthDue = "hr.thirteenth_due";

    /// <summary>Quantos dias antes da data limite a pauta lembra a parcela do 13º (FLH-04, Propositivo: "em novembro").</summary>
    public const int ThirteenthReminderDays = 30;

    private static readonly AgendaCategory[] Declared =
    [
        new(PositionsWithoutCost, "hr", "hr.edit", AgendaTier.Overdue, "os cargos sem custo-hora"),
        new(TimeAdjustmentsPending, "hr", TimeAdjustmentRules.ApprovePermission, AgendaTier.Unblocks, "os ajustes de ponto aguardando você"),
        new(ThirteenthDue, "hr", "hr.create", AgendaTier.DueSoon, "as parcelas do 13º"),
    ];

    public string Module => "hr";

    public IReadOnlyList<AgendaCategory> Categories => Declared;

    public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = new List<AgendaItem>();
        if (request.Wants(TimeAdjustmentsPending) && await AdjustmentsAsync(request.UserId, cancellationToken) is { } adjustments)
        {
            items.Add(adjustments);
        }

        if (request.Wants(PositionsWithoutCost) && await PositionsAsync(cancellationToken) is { } positions)
        {
            items.Add(positions);
        }

        if (request.Wants(ThirteenthDue) && await ThirteenthAsync(request.Today, cancellationToken) is { } thirteenth)
        {
            items.Add(thirteenth);
        }

        return items;
    }

    /// <summary>
    /// "13º 1ª parcela vence em 30/11" (FLH-04, Propositivo): nos 30 dias antes da data limite, enquanto a folha da parcela do ano não fechou;
    /// o link abre a lista das folhas, onde "Nova folha → 13º" já vem com a parcela e o ano.
    /// </summary>
    private async Task<AgendaItem?> ThirteenthAsync(DateOnly today, CancellationToken ct)
    {
        var due = await parameters.DueParametersAsync(ct);
        var jan = new DateOnly(today.Year, 1, 1);
        var dec = new DateOnly(today.Year, 12, 31);
        var closed = await db.PayrollRuns.AsNoTracking().Where(r => (r.Type == PayrollRunType.Thirteenth1 || r.Type == PayrollRunType.Thirteenth2) && r.Status != PayrollRunStatus.Draft && r.Period >= jan && r.Period <= dec)
            .Select(r => r.Type).Distinct().ToListAsync(ct);
        foreach (var (type, month, day, label) in new[] { (PayrollRunType.Thirteenth1, due.ThirteenthFirstMonth, due.ThirteenthFirstDay, "1ª"), (PayrollRunType.Thirteenth2, due.ThirteenthSecondMonth, due.ThirteenthSecondDay, "2ª") })
        {
            var limit = new DateOnly(today.Year, month, Math.Min(day, DateTime.DaysInMonth(today.Year, month)));
            if (closed.Contains(type) || today > limit || limit.DayNumber - today.DayNumber > ThirteenthReminderDays)
            {
                continue;
            }

            var date = limit.ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture);
            return new AgendaItem(ThirteenthDue, $"13º {label} parcela vence em {date}", "A folha da parcela ainda não fechou: crie, calcule e feche antes da data limite.",
                "/app/rh/folha", limit, null, AgendaTier.DueSoon, 1, "Criar folha do 13º",
                [new AgendaEntry($"13º {label} parcela de {today.Year}", $"Data limite {date}", "/app/rh/folha?novo13=" + (type == PayrollRunType.Thirteenth1 ? "1" : "2"), limit, null)]);
        }

        return null;
    }

    /// <summary>"Ajustes de ponto aguardando você" (RH-01b CA 17): os pendentes que a pessoa pode decidir, do mais antigo.</summary>
    private async Task<AgendaItem?> AdjustmentsAsync(Guid userId, CancellationToken ct)
    {
        var pending = await (from a in db.TimeAdjustments.AsNoTracking()
                             join e in db.Employees.AsNoTracking() on a.EmployeeId equals e.Id
                             where a.Status == TimeAdjustmentStatus.Pending
                             orderby a.RequestedAt
                             select new { a.Id, a.EmployeeId, a.RequestedBy, a.Day, a.Kind, a.AbsenceMinutes, a.EntryType, a.At, e.Name }).Take(500).ToListAsync(ct);
        if (pending.Count == 0)
        {
            return null;
        }

        var mine = await links.EmployeeOfUserAsync(userId, ct);
        var sole = await SoleOwnerRule.NoOtherEligibleAsync(users, TimeAdjustmentRules.ApprovePermission, userId, [userId], ct);
        var decidable = pending.Where(p => sole || (p.RequestedBy != userId && p.EmployeeId != mine)).ToList();
        if (decidable.Count == 0)
        {
            return null;
        }

        return new AgendaItem(
            TimeAdjustmentsPending,
            AgendaText.Count(decidable.Count, "ajuste de ponto aguarda você", "ajustes de ponto aguardam você"),
            "O espelho e a folha só usam o ajuste depois da aprovação.",
            "/app/rh/ponto/ajustes?situacao=Pending", decidable[0].Day, null, AgendaTier.Unblocks, decidable.Count, "Decidir ajuste",
            [.. decidable.Take(AgendaLimits.EntriesPerCategory).Select(p => new AgendaEntry(
                p.Name, TimeAdjustmentRules.Summary(p.Kind, p.EntryType, p.At is { } at ? BusinessTime.ToBusiness(at) : null, p.AbsenceMinutes, null, showReason: false) + " · " + TimeAdjustmentRules.Date(p.Day),
                "/app/rh/ponto/ajustes?pedido=" + p.Id, p.Day, null))]);
    }

    private async Task<AgendaItem?> PositionsAsync(CancellationToken cancellationToken)
    {
        var missing = (await gaps.ByPositionAsync(cancellationToken)).ToDictionary(g => g.PositionId, g => g.Entries);
        if (missing.Count == 0)
        {
            return null;
        }

        var ids = missing.Keys.ToList();
        var positions = await db.JobPositions.AsNoTracking().Where(p => ids.Contains(p.Id) && p.HourlyCost <= 0)
            .Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
        if (positions.Count == 0)
        {
            return null;
        }

        var ordered = positions.Select(p => (p.Id, p.Name, Entries: missing[p.Id])).OrderByDescending(p => p.Entries).ThenBy(p => p.Name, StringComparer.CurrentCulture).ToList();
        var entries = ordered.Sum(p => p.Entries);
        return new AgendaItem(
            PositionsWithoutCost,
            AgendaText.Count(ordered.Count, "cargo está sem custo-hora", "cargos estão sem custo-hora"),
            AgendaText.Sentence(AgendaText.Count(entries, "apontamento ficou sem custo no projeto", "apontamentos ficaram sem custo no projeto")),
            "/app/rh/cargos", null, null, AgendaTier.Overdue, ordered.Count, "Informar custo-hora",
            [.. ordered.Take(AgendaLimits.EntriesPerCategory).Select(p => new AgendaEntry(
                p.Name, AgendaText.Count(p.Entries, "apontamento sem custo", "apontamentos sem custo"), "/app/rh/cargos?cargo=" + p.Id, null, null))]);
    }
}
