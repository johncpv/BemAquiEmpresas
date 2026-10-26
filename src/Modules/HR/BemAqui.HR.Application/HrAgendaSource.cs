using BemAqui.BuildingBlocks.Agenda;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Detector do RH para a Minha pauta (DSH-04 CA 2): cargo com apontamento marcado "Sem custo-hora" (PRD-04 CA 5), para quem tem
/// <c>hr.edit</c>. Os apontamentos vêm do contrato <see cref="ILaborCostGaps"/> da Produção; o item some quando o cargo ganha custo-hora
/// (o recálculo dos apontamentos é da supervisão, com <c>production.approve</c>).
/// </summary>
public sealed class HrAgendaSource(IHrDb db, ILaborCostGaps gaps) : IAgendaSource
{
    public const string PositionsWithoutCost = "hr.positions_without_cost";

    private static readonly AgendaCategory[] Declared =
    [
        new(PositionsWithoutCost, "hr", "hr.edit", AgendaTier.Overdue, "os cargos sem custo-hora"),
    ];

    public string Module => "hr";

    public IReadOnlyList<AgendaCategory> Categories => Declared;

    public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Wants(PositionsWithoutCost))
        {
            return [];
        }

        var missing = (await gaps.ByPositionAsync(cancellationToken)).ToDictionary(g => g.PositionId, g => g.Entries);
        if (missing.Count == 0)
        {
            return [];
        }

        var ids = missing.Keys.ToList();
        var positions = await db.JobPositions.AsNoTracking().Where(p => ids.Contains(p.Id) && p.HourlyCost <= 0)
            .Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
        if (positions.Count == 0)
        {
            return [];
        }

        var ordered = positions.Select(p => (p.Id, p.Name, Entries: missing[p.Id])).OrderByDescending(p => p.Entries).ThenBy(p => p.Name, StringComparer.CurrentCulture).ToList();
        var entries = ordered.Sum(p => p.Entries);
        return
        [
            new AgendaItem(
                PositionsWithoutCost,
                AgendaText.Count(ordered.Count, "cargo está sem custo-hora", "cargos estão sem custo-hora"),
                AgendaText.Sentence(AgendaText.Count(entries, "apontamento ficou sem custo no projeto", "apontamentos ficaram sem custo no projeto")),
                "/app/rh/cargos", null, null, AgendaTier.Overdue, ordered.Count, "Informar custo-hora",
                [.. ordered.Take(AgendaLimits.EntriesPerCategory).Select(p => new AgendaEntry(
                    p.Name, AgendaText.Count(p.Entries, "apontamento sem custo", "apontamentos sem custo"), "/app/rh/cargos?cargo=" + p.Id, null, null))]),
        ];
    }
}
