using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Projeto, primeira versão (COM-04a CA 6, 7, 17): cabeçalho e itens; o orçado só com <c>projects.view_cost</c>. Também
/// implementa <see cref="IProjects"/> e <see cref="IContracts"/> para os outros módulos (CA 21).
/// </summary>
public sealed partial class ProjectService(
    ICommercialDb db, IParties parties, IUserDirectory users, IPermissionChecker permissions, IEventPublisher events, IReceivables receivables, IPayables payables,
    BemAqui.Production.Contracts.IInstallationOrders installations, BemAqui.Production.Contracts.IProductionOrders productionOrders, TimeProvider clock,
    BemAqui.Inventory.Contracts.IStockSerials serials, BemAqui.Production.Contracts.IShipments shipments)
    : IProjects, IContracts
{
    public const string AttachmentOwner = "Project";

    public async Task<Result<ProjectDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var contract = await db.Contracts.AsNoTracking().Where(c => c.Id == project.ContractId).Select(c => new { c.Number, c.Status, c.IsOnboarding }).FirstAsync(cancellationToken);
        var contractNumber = contract.Number;
        var hasFabrication = await db.ContractMilestones.AnyAsync(m => m.ContractId == project.ContractId && m.TriggerEvent == TriggerEvent.FabricationDone, cancellationToken);
        var items = await db.ProjectItems.AsNoTracking().Where(i => i.ProjectId == id).OrderBy(i => i.Sequence)
            .Select(i => new ProjectItemView(i.Id, i.Sequence, i.Description, i.Quantity, i.ItemId, i.QuoteItemId)).ToListAsync(cancellationToken);
        var party = await parties.FindAsync(project.PartyId, cancellationToken);
        var site = project.SiteAddressId is { } siteId ? (await parties.AddressesAsync(project.PartyId, cancellationToken)).FirstOrDefault(a => a.Id == siteId) : null;
        var manager = project.ManagerUserId is { } userId ? await users.DisplayNameAsync(userId, cancellationToken) : null;
        var (fabricationOrders, openFabricationOrders) = await FabricationOrdersAsync(project.Id, cancellationToken);
        return new ProjectDetail(
            project.Id, project.Number, project.Name, project.Status, project.PartyId, party is null ? string.Empty : party.TradeName ?? party.LegalName, project.ContractId, contractNumber,
            project.SiteAddressId, site is null ? null : $"{site.Street}, {site.Number} — {site.City}/{site.State}", project.PromisedDate, project.ForecastDate, project.Priority,
            project.ManagerUserId, manager, project.CreatedAt, items, project.Version, project.FabricatedOn, project.DeliveredOn, project.DeliveredManually, hasFabrication,
            contract.Status.ToString(), contract.IsOnboarding, project.FabricatedByOrders, fabricationOrders, openFabricationOrders);
    }

    public async Task<Result<IReadOnlyList<ProjectBudgetLine>>> BudgetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "O orçado do projeto é restrito. Fale com o administrador.");
        }

        if (!await db.Projects.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return NotFound();
        }

        return await db.ProjectBudgets.AsNoTracking().Where(b => b.ProjectId == id).OrderBy(b => b.CostType)
            .Select(b => new ProjectBudgetLine(b.CostType, b.BudgetedAmount)).ToListAsync(cancellationToken);
    }

    async Task<ProjectSummary?> IProjects.FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await Summaries(db.Projects.Where(p => p.Id == id)).ToListAsync(cancellationToken)).FirstOrDefault();

    async Task<IReadOnlyList<ProjectSummary>> IProjects.FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0 ? [] : await Summaries(db.Projects.Where(p => ids.Contains(p.Id))).ToListAsync(cancellationToken);

    async Task<IReadOnlyList<ProjectSummary>> IProjects.SearchAsync(string? term, int limit, CancellationToken cancellationToken)
    {
        var projects = db.Projects.Where(p => p.Status != ProjectStatus.Closed && p.Status != ProjectStatus.Cancelled);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";
            projects = projects.Where(p => EF.Functions.ILike(p.Number, pattern) || EF.Functions.ILike(p.Name, pattern));
        }

        return await Summaries(projects.OrderByDescending(p => p.CreatedAt).Take(Math.Clamp(limit, 1, 50))).ToListAsync(cancellationToken);
    }

    async Task<IReadOnlyList<ProjectItemInfo>> IProjects.ItemsAsync(Guid projectId, CancellationToken cancellationToken) =>
        await ItemInfosAsync(db.ProjectItems.Where(i => i.ProjectId == projectId), int.MaxValue, cancellationToken);

    async Task<IReadOnlyList<ProjectItemInfo>> IProjects.FindItemsAsync(IReadOnlyCollection<Guid> projectItemIds, CancellationToken cancellationToken) =>
        projectItemIds.Count == 0 ? [] : await ItemInfosAsync(db.ProjectItems.Where(i => projectItemIds.Contains(i.Id)), int.MaxValue, cancellationToken);

    async Task<IReadOnlyList<ProjectItemInfo>> IProjects.FabricationCandidatesAsync(Guid? projectId, int limit, CancellationToken cancellationToken)
    {
        // PRD-01 CA 5 e 6 (D14): equipamento ou componente, projeto planejado ou em produção, contrato ativo e não de implantação.
        var items = db.ProjectItems.Where(i =>
            db.QuoteItems.Any(q => q.Id == i.QuoteItemId && q.Kind != QuoteItemKind.Service)
            && db.Projects.Any(p => p.Id == i.ProjectId && (p.Status == ProjectStatus.Planned || p.Status == ProjectStatus.InProgress)
                && db.Contracts.Any(c => c.Id == p.ContractId && c.Status == ContractStatus.Active && !c.IsOnboarding)));
        if (projectId is { } project)
        {
            items = items.Where(i => i.ProjectId == project);
        }

        return await ItemInfosAsync(items, Math.Clamp(limit, 1, 5000), cancellationToken);
    }

    /// <summary>Itens com o tipo do item do orçamento e a implantação do contrato (PRD-01 CA 22).</summary>
    private async Task<IReadOnlyList<ProjectItemInfo>> ItemInfosAsync(IQueryable<ProjectItem> items, int limit, CancellationToken ct)
    {
        var rows = await items.AsNoTracking()
            .Join(db.QuoteItems, i => i.QuoteItemId, q => q.Id, (i, q) => new { Item = i, q.Kind })
            .Join(db.Projects, x => x.Item.ProjectId, p => p.Id, (x, p) => new { x.Item, x.Kind, p.ContractId, p.CreatedAt })
            .Join(db.Contracts, x => x.ContractId, c => c.Id, (x, c) => new { x.Item, x.Kind, c.IsOnboarding, x.CreatedAt })
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Item.ProjectId).ThenBy(x => x.Item.Sequence).Take(limit)
            .ToListAsync(ct);
        return rows.Select(x => new ProjectItemInfo(x.Item.Id, x.Item.ProjectId, x.Item.Sequence, x.Item.Description, x.Item.Quantity, x.Item.ItemId,
            Enum.Parse<ProjectItemKind>(x.Kind.ToString()), x.IsOnboarding, x.Item.RoutingTemplateId)).ToList();
    }

    async Task<decimal?> IProjects.BudgetedAsync(Guid projectId, CostType costType, CancellationToken cancellationToken) =>
        await db.ProjectBudgets.AsNoTracking().Where(b => b.ProjectId == projectId && b.CostType == costType).Select(b => (decimal?)b.BudgetedAmount).FirstOrDefaultAsync(cancellationToken);

    async Task<bool?> IContracts.IsCancelledAsync(Guid contractId, CancellationToken cancellationToken) =>
        await db.Contracts.AsNoTracking().Where(c => c.Id == contractId).Select(c => (bool?)(c.Status == ContractStatus.Cancelled)).FirstOrDefaultAsync(cancellationToken);

    async Task<ContractInfo?> IContracts.FindAsync(Guid contractId, CancellationToken cancellationToken)
    {
        var row = await db.Contracts.AsNoTracking().Where(c => c.Id == contractId)
            .Select(c => new { c.Id, c.Number, c.Status, c.PartyId, ProjectId = db.Projects.Where(p => p.ContractId == c.Id).Select(p => (Guid?)p.Id).FirstOrDefault() })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new ContractInfo(row.Id, row.Number, Enum.Parse<ContractState>(row.Status.ToString()), row.PartyId, row.ProjectId);
    }

    private static IQueryable<ProjectSummary> Summaries(IQueryable<Project> projects) =>
        projects.AsNoTracking().Select(p => new ProjectSummary(p.Id, p.Number, p.Name, p.PartyId, p.ContractId, p.Status.ToString(), p.SiteAddressId, p.ManagerUserId, p.PromisedDate));

    internal static Error NotFound() => Error.NotFound("project_not_found", "Projeto não encontrado.");
}
