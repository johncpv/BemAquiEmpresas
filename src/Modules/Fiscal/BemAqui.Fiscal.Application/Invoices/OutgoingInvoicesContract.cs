using System.Globalization;
using BemAqui.BuildingBlocks.Agenda;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>
/// <see cref="IOutgoingInvoices"/> (FIS-03a CA 12, 13 e 15): só as tabelas do Fiscal, sem depender de outros contratos (a Produção e o
/// Finance usam sem ciclo no DI). Receita e faturamento: só nota autorizada (sem as canceladas) do ambiente de produção.
/// </summary>
public sealed class OutgoingInvoicesContract(IFiscalDb db) : IOutgoingInvoices
{
    public async Task<OutgoingInvoiceSummary?> ActiveForShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        (await ActiveForShipmentsAsync([shipmentId], cancellationToken)).GetValueOrDefault(shipmentId);

    public async Task<IReadOnlyDictionary<Guid, OutgoingInvoiceSummary>> ActiveForShipmentsAsync(IReadOnlyCollection<Guid> shipmentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shipmentIds);
        if (shipmentIds.Count == 0)
        {
            return new Dictionary<Guid, OutgoingInvoiceSummary>();
        }

        var ids = shipmentIds.Distinct().ToList();
        var rows = await db.OutgoingInvoices.AsNoTracking()
            .Where(i => i.ShipmentId != null && ids.Contains(i.ShipmentId.Value) && i.DiscardedAt == null && !OutgoingInvoice.InactiveStatuses.Contains(i.Status))
            .ToListAsync(cancellationToken);
        return rows.GroupBy(i => i.ShipmentId!.Value).ToDictionary(g => g.Key, g => Summary(g.OrderByDescending(i => i.CreatedAt).First()));
    }

    public async Task<IReadOnlyList<OutgoingInvoiceSummary>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        (await db.OutgoingInvoices.AsNoTracking().Where(i => i.ProjectId == projectId && i.DiscardedAt == null).OrderByDescending(i => i.CreatedAt).ToListAsync(cancellationToken))
            .Select(Summary).ToList();

    public async Task<IReadOnlyList<InvoicedRevenue>> RevenueAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? projectIds, CancellationToken cancellationToken = default)
    {
        var query = Counted().Where(i => i.IssuedOn >= from && i.IssuedOn <= to);
        if (projectIds is not null)
        {
            var ids = projectIds.ToList();
            query = query.Where(i => i.ProjectId != null && ids.Contains(i.ProjectId.Value));
        }

        return await query.OrderBy(i => i.IssuedOn).ThenBy(i => i.Number)
            .Select(i => new InvoicedRevenue(i.Id, i.Type, i.ProjectId, i.PartyId, i.Series, i.Number, i.IssuedOn!.Value, i.TotalAmount)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, ProjectInvoicing>> InvoicedByProjectAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        var ids = projectIds.Distinct().ToList();
        var rows = await Counted().Where(i => i.ProjectId != null && ids.Contains(i.ProjectId.Value))
            .GroupBy(i => i.ProjectId!.Value)
            .Select(g => new
            {
                ProjectId = g.Key,
                Equipment = g.Sum(i => i.Type == InvoiceType.NFe ? i.TotalAmount : 0m),
                Service = g.Sum(i => i.Type == InvoiceType.NFSe ? i.TotalAmount : 0m),
                Count = g.Count(),
            }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.ProjectId, r => new ProjectInvoicing(r.ProjectId, r.Equipment, r.Service, r.Count));
    }

    public async Task<DateOnly?> CutoverOnAsync(CancellationToken cancellationToken = default) =>
        await db.FiscalSettings.AsNoTracking().Select(s => s.ProjectTaxCutoverOn).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Autorizadas, de produção e com data: as que contam como faturamento.</summary>
    private IQueryable<OutgoingInvoice> Counted() =>
        db.OutgoingInvoices.AsNoTracking().Where(i => i.Status == OutgoingInvoiceStatus.Authorized && i.Environment == FiscalEnvironment.Production && i.IssuedOn != null);

    public static OutgoingInvoiceSummary Summary(OutgoingInvoice i)
    {
        ArgumentNullException.ThrowIfNull(i);
        return new(i.Id, i.Type, i.Status.ToString(), i.Series, i.Number, i.AccessKey, i.ProjectId, i.ShipmentId, i.PartyId, i.IssuedOn, i.TotalAmount, i.Environment.ToString(),
            OutgoingInvoiceEffects.Link(i.Id));
    }
}

/// <summary>
/// "Notas a emitir" na Minha pauta (CA 16): os romaneios expedidos sem nota, para quem tem <c>fiscal.create</c>; com algum parado há mais
/// de 1 dia útil, a categoria sobe para "atrasado" e as linhas atrasadas vêm primeiro com a marca.
/// </summary>
public sealed class OutgoingInvoiceAgendaSource(OutgoingInvoiceService invoices) : IAgendaSource
{
    public const string ToIssue = "fiscal.invoices_to_issue";

    private static readonly AgendaCategory[] Declared = [new(ToIssue, FiscalModuleInfo.Code, "fiscal.create", AgendaTier.DueSoon, "as notas a emitir")];

    public string Module => FiscalModuleInfo.Code;

    public IReadOnlyList<AgendaCategory> Categories => Declared;

    public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Wants(ToIssue))
        {
            return [];
        }

        var queue = await invoices.ToIssueAsync(cancellationToken);
        if (queue.Count == 0)
        {
            return [];
        }

        var late = queue.Count(q => q.Late);
        var entries = queue.OrderByDescending(q => q.Late).ThenBy(q => q.ShippedOn).Take(AgendaLimits.EntriesPerCategory)
            .Select(q => new AgendaEntry($"{q.ShipmentNumber} — {q.ProjectNumber}", $"{q.PartyName} · expedido há {q.DaysSinceShipment.ToString(CultureInfo.InvariantCulture)} dias",
                OutgoingInvoiceService.QueueLink + "?romaneio=" + q.ShipmentId.ToString("D"), q.ShippedOn, q.EstimatedAmount, q.Late ? "Atrasada" : null)).ToList();
        var title = queue.Count == 1 ? "1 romaneio expedido espera a NF-e" : $"{queue.Count.ToString(CultureInfo.InvariantCulture)} romaneios expedidos esperam a NF-e";
        var why = late > 0 ? $"{late.ToString(CultureInfo.InvariantCulture)} há mais de 1 dia útil: a receita só conta com a nota." : "Emita a nota no dia da entrega: a receita conta pela data da nota.";
        return
        [
            new AgendaItem(ToIssue, title, why, OutgoingInvoiceService.QueueLink, queue.Min(q => q.ShippedOn), queue.Sum(q => q.EstimatedAmount), late > 0 ? AgendaTier.Overdue : AgendaTier.DueSoon,
                queue.Count, "Emitir NF-e", entries),
        ];
    }
}
