using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>Leituras do apontamento para OS, Kanban e app: minutos líquidos por etapa e quem está na etapa agora.</summary>
public static class LaborReader
{
    /// <summary>Minutos líquidos apontados por etapa (todos os operadores; o aberto conta até agora).</summary>
    public static async Task<Dictionary<Guid, int>> AppliedMinutesAsync(IProductionDb db, IReadOnlyCollection<Guid> stepIds, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(stepIds);
        if (stepIds.Count == 0)
        {
            return [];
        }

        var entries = await db.LaborEntries.AsNoTracking().Where(e => stepIds.Contains(e.OrderStepId)).Select(e => new { e.Id, e.OrderStepId, e.StartedAt, e.EndedAt }).ToListAsync(ct);
        var ids = entries.Select(e => e.Id).ToList();
        var pauses = (await db.StepPauses.AsNoTracking().Where(p => ids.Contains(p.LaborEntryId)).Select(p => new { p.LaborEntryId, p.StartedAt, p.EndedAt }).ToListAsync(ct))
            .ToLookup(p => p.LaborEntryId, p => new PauseSpan(p.StartedAt, p.EndedAt));
        return entries.GroupBy(e => e.OrderStepId).ToDictionary(g => g.Key, g => g.Sum(e => LaborRules.NetMinutes(e.StartedAt, e.EndedAt, pauses[e.Id], now)));
    }

    /// <summary>Apontamentos abertos por etapa: funcionário e a pausa aberta, se houver (Kanban: iniciais e "Pausada: {motivo}").</summary>
    public static async Task<ILookup<Guid, (Guid EmployeeId, PauseReason? Paused)>> OpenByStepAsync(IProductionDb db, IReadOnlyCollection<Guid> stepIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(stepIds);
        var open = await db.LaborEntries.AsNoTracking().Where(e => stepIds.Contains(e.OrderStepId) && e.EndedAt == null)
            .Select(e => new { e.OrderStepId, e.EmployeeId, Paused = db.StepPauses.Where(p => p.LaborEntryId == e.Id && p.EndedAt == null).Select(p => (PauseReason?)p.Reason).FirstOrDefault() })
            .ToListAsync(ct);
        return open.ToLookup(e => e.OrderStepId, e => (e.EmployeeId, e.Paused));
    }
}