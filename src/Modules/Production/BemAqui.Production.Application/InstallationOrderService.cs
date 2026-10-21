using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// OS de instalação (COM-06): o instalador terceirizado executa as etapas na obra; o valor combinado é um título a pagar
/// previsto até o aceite técnico, que confirma o título e libera as parcelas do cliente pelo Commercial.
/// </summary>
public sealed partial class InstallationOrderService(
    IProductionDb db, IProjects projects, IPayables payables, IParties parties, IPaymentTerms terms, INumberSequence numbers, IPermissionChecker permissions,
    ICurrentUser currentUser, IUserDirectory users, INotificationService notifications, IEventPublisher events, InstallationTemplateService template, TimeProvider clock)
{
    public const string DocType = "OS";
    public const string AttachmentOwner = "ProductionOrder";

    public async Task<InstallationPage> ListAsync(string? situation, Guid? projectId, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var orders = db.ProductionOrders.AsNoTracking().Where(o => o.Type == ProductionOrderType.Installation);
        orders = situation switch
        {
            "aberta" => orders.Where(o => o.Status == ProductionOrderStatus.Open),
            "andamento" => orders.Where(o => o.Status == ProductionOrderStatus.InProgress),
            "concluida" => orders.Where(o => o.Status == ProductionOrderStatus.Completed),
            "cancelada" => orders.Where(o => o.Status == ProductionOrderStatus.Cancelled),
            _ => orders,
        };
        if (projectId is { } project)
        {
            orders = orders.Where(o => o.ProjectId == project);
        }

        var total = await orders.CountAsync(cancellationToken);
        var rows = await orders.OrderByDescending(o => o.CreatedAt).Skip(page.Skip).Take(page.PageSize)
            .Select(o => new { Order = o, Done = db.ProductionOrderSteps.Count(s => s.OrderId == o.Id && s.CompletedAt != null), Total = db.ProductionOrderSteps.Count(s => s.OrderId == o.Id) })
            .ToListAsync(cancellationToken);
        var byId = (await projects.FindManyAsync(rows.Select(r => r.Order.ProjectId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id);
        var names = await NamesAsync(rows.Select(r => r.Order.InstallerId), cancellationToken);
        return new InstallationPage(rows.Select(r => new InstallationSummary(
            r.Order.Id, r.Order.Number, r.Order.ProjectId, byId.GetValueOrDefault(r.Order.ProjectId)?.Number ?? string.Empty, byId.GetValueOrDefault(r.Order.ProjectId)?.Name ?? string.Empty,
            r.Order.InstallerId, names.GetValueOrDefault(r.Order.InstallerId, string.Empty), r.Order.PlannedStart, r.Order.PlannedEnd, r.Order.Status, r.Done, r.Total)).ToList(),
            page.Page, page.PageSize, total);
    }

    public async Task<Result<InstallationDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && o.Type == ProductionOrderType.Installation, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var project = await projects.FindAsync(order.ProjectId, cancellationToken);
        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == id).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        var checklist = await db.ProductionOrderChecklist.AsNoTracking().Where(c => c.OrderId == id).OrderBy(c => c.Sequence).ToListAsync(cancellationToken);
        var covered = await db.ProductionOrderProjectItems.AsNoTracking().Where(i => i.OrderId == id).Select(i => i.ProjectItemId).ToListAsync(cancellationToken);
        var items = (await projects.ItemsAsync(order.ProjectId, cancellationToken)).Where(i => covered.Contains(i.Id))
            .Select(i => new InstallationItemOption(i.Id, i.Sequence, i.Description, i.Quantity, order.Number)).ToList();
        var who = new Dictionary<Guid, string?>();
        foreach (var userId in steps.Select(s => s.CompletedBy).Concat(checklist.Select(c => c.MarkedBy)).Where(u => u is not null).Select(u => u!.Value).Distinct())
        {
            who[userId] = await users.DisplayNameAsync(userId, cancellationToken);
        }

        var installer = (await NamesAsync([order.InstallerId], cancellationToken)).GetValueOrDefault(order.InstallerId, string.Empty);
        return new InstallationDetail(
            order.Id, order.Number, order.ProjectId, project?.Number ?? string.Empty, project?.Name ?? string.Empty, order.ContractId, order.InstallerId, installer, order.ContractValue,
            order.PlannedStart, order.PlannedEnd, order.Status, order.StartedOn, order.AcceptedOn, order.AcceptedByName, order.CancelReason,
            steps.Select(s => new InstallationStepView(s.Id, s.Sequence, s.Name, s.CompletedAt, s.CompletedBy is { } u ? who.GetValueOrDefault(u) : null)).ToList(),
            checklist.Select(c => new InstallationChecklistView(c.Id, c.Sequence, c.Text, c.Result, c.MarkedAt, c.MarkedBy is { } u ? who.GetValueOrDefault(u) : null)).ToList(),
            items, order.Version, await payables.FindByOriginAsync(DocumentOrigin.Installer, order.Id, cancellationToken));
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        (await parties.FindManyAsync(ids.Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);

    internal static Error NotFound() => Error.NotFound("production_order_not_found", "OS não encontrada.");

    internal static Error Stale() => Error.Conflict("concurrency_conflict", "A OS mudou desde que você abriu. Recarregue.");
}
