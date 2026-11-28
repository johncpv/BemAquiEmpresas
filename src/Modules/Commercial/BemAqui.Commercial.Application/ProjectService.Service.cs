using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class ProjectService
{
    /// <summary>
    /// Pauta "Aceite do cliente pendente" (EXE-01b CA 16): projetos de serviço entregues sem aceite vigente, com o valor das parcelas de aceite
    /// (contrato + aditivos aprovados). Lê só tabelas do Commercial; sem o recurso, nada.
    /// </summary>
    async Task<IReadOnlyList<ProjectAwaitingAcceptance>> IProjects.AwaitingAcceptanceAsync(DateOnly today, CancellationToken cancellationToken)
    {
        if (!ServiceExecutionReleased)
        {
            return [];
        }

        var rows = await db.Projects.AsNoTracking()
            .Where(p => p.ExecutionKind == ExecutionKind.Service && p.Status == ProjectStatus.Delivered && p.DeliveredOn != null && p.AcceptedOn == null
                && !db.ProjectAcceptances.Any(a => a.ProjectId == p.Id && a.RevertedAt == null))
            .Select(p => new { p.Id, p.Number, p.Name, p.ManagerUserId, DeliveredOn = p.DeliveredOn!.Value, p.ContractId })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var contracts = rows.Select(r => r.ContractId).Distinct().ToList();
        var own = await db.ContractMilestones.AsNoTracking().Where(m => contracts.Contains(m.ContractId) && m.TriggerEvent == TriggerEvent.TechnicalAcceptance)
            .Select(m => new { m.ContractId, m.Amount }).ToListAsync(cancellationToken);
        var amended = await (from m in db.ContractAmendmentMilestones.AsNoTracking()
                             join a in db.ContractAmendments.AsNoTracking() on m.AmendmentId equals a.Id
                             where contracts.Contains(a.ContractId) && a.Status == AmendmentStatus.Approved && m.TriggerEvent == TriggerEvent.TechnicalAcceptance
                             select new { a.ContractId, m.Amount }).ToListAsync(cancellationToken);
        var amounts = own.Concat(amended).GroupBy(m => m.ContractId).ToDictionary(g => g.Key, g => g.Sum(m => m.Amount));
        return rows.OrderBy(r => r.DeliveredOn).ThenBy(r => r.Number, StringComparer.Ordinal)
            .Select(r => new ProjectAwaitingAcceptance(r.Id, r.Number, r.Name, r.ManagerUserId, r.DeliveredOn, today.DayNumber - r.DeliveredOn.DayNumber,
                amounts.TryGetValue(r.ContractId, out var amount) ? amount : null))
            .ToList();
    }
}
