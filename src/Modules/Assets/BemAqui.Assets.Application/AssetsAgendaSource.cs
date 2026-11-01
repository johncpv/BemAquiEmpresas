using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Agenda;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// Detectores do Patrimônio para a Minha pauta (PAT-03a CA 11, DSH-04): "Preventivas vencidas" (o plano ativo que passou do vencimento, levando à
/// OS aberta do plano ou ao plano) e "Máquinas paradas" (OS aberta ou em andamento com o bem em manutenção, levando à OS), para quem tem
/// <c>assets.edit</c>. Só etiqueta, nome do bem e número da OS; nenhuma linha leva o problema descrito.
/// </summary>
public sealed class AssetsAgendaSource(IAssetsDb db, MaintenanceService maintenance) : IAgendaSource
{
    public const string MaintenanceOverdue = "assets.maintenance_overdue";
    public const string MachinesStopped = "assets.machines_stopped";

    private static readonly AgendaCategory[] Declared =
    [
        new(MaintenanceOverdue, AssetService.Module, "assets.edit", AgendaTier.Overdue, "as preventivas vencidas"),
        new(MachinesStopped, AssetService.Module, "assets.edit", AgendaTier.Unblocks, "as máquinas paradas"),
    ];

    public string Module => AssetService.Module;

    public IReadOnlyList<AgendaCategory> Categories => Declared;

    public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = new List<AgendaItem>();
        if (request.Wants(MachinesStopped) && await StoppedAsync(cancellationToken) is { } stopped)
        {
            items.Add(stopped);
        }

        if (request.Wants(MaintenanceOverdue) && await OverdueAsync(cancellationToken) is { } overdue)
        {
            items.Add(overdue);
        }

        return items;
    }

    private async Task<AgendaItem?> OverdueAsync(CancellationToken ct)
    {
        var overdue = (await maintenance.DuePlansAsync(null, null, 0, ct)).Where(p => p.Overdue).ToList();
        if (overdue.Count == 0)
        {
            return null;
        }

        return new AgendaItem(
            MaintenanceOverdue,
            AgendaText.Count(overdue.Count, "preventiva está vencida", "preventivas estão vencidas"),
            "A máquina pode parar sem aviso: faça a manutenção e conclua a OS.",
            "/app/patrimonio/manutencao", overdue.Min(p => p.NextDueAt), null, AgendaTier.Overdue, overdue.Count, "Fazer a preventiva",
            [.. overdue.Take(AgendaLimits.EntriesPerCategory).Select(p => new AgendaEntry(
                $"{p.AssetTag} {p.AssetName}", p.OpenOrderNumber is { } n ? $"{p.Description} · {n}" : p.Description,
                p.OpenOrderId is { } o ? MaintenanceService.OrderLink + o : $"/app/patrimonio/bens/{p.AssetId}?aba=manutencao&plano={p.Id}", p.NextDueAt, null))]);
    }

    private async Task<AgendaItem?> StoppedAsync(CancellationToken ct)
    {
        var stopped = await (from o in db.MaintenanceOrders.AsNoTracking()
                             join a in db.Assets.AsNoTracking() on o.AssetId equals a.Id
                             where (o.Status == MaintenanceOrderStatus.Open || o.Status == MaintenanceOrderStatus.InProgress) && a.Status == AssetStatus.Maintenance
                                 && (o.MachineStopped || o.Status == MaintenanceOrderStatus.InProgress)
                             orderby o.CreatedAt
                             select new { o.Id, o.Number, o.ScheduledOn, a.Tag, a.Name }).Take(500).ToListAsync(ct);
        if (stopped.Count == 0)
        {
            return null;
        }

        return new AgendaItem(
            MachinesStopped,
            AgendaText.Count(stopped.Count, "máquina está parada em manutenção", "máquinas estão paradas em manutenção"),
            "Enquanto a OS não é concluída, a máquina fica fora da produção.",
            "/app/patrimonio/manutencao", stopped.Min(s => s.ScheduledOn), null, AgendaTier.Unblocks, stopped.Count, "Ver OS de manutenção",
            [.. stopped.Take(AgendaLimits.EntriesPerCategory).Select(s => new AgendaEntry($"{s.Tag} {s.Name}", s.Number, MaintenanceService.OrderLink + s.Id, s.ScheduledOn, null))]);
    }
}
