using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Projeto, primeira versão (COM-04a CA 6, 7, 17): cabeçalho e itens; o orçado só com <c>projects.view_cost</c>. Também
/// implementa <see cref="IProjects"/> e <see cref="IContracts"/> para os outros módulos (CA 21).
/// </summary>
public sealed class ProjectService(ICommercialDb db, IParties parties, IUserDirectory users, IPermissionChecker permissions) : IProjects, IContracts
{
    public async Task<Result<ProjectDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var contractNumber = await db.Contracts.AsNoTracking().Where(c => c.Id == project.ContractId).Select(c => c.Number).FirstAsync(cancellationToken);
        var items = await db.ProjectItems.AsNoTracking().Where(i => i.ProjectId == id).OrderBy(i => i.Sequence)
            .Select(i => new ProjectItemView(i.Id, i.Sequence, i.Description, i.Quantity, i.ItemId, i.QuoteItemId)).ToListAsync(cancellationToken);
        var party = await parties.FindAsync(project.PartyId, cancellationToken);
        var site = project.SiteAddressId is { } siteId ? (await parties.AddressesAsync(project.PartyId, cancellationToken)).FirstOrDefault(a => a.Id == siteId) : null;
        var manager = project.ManagerUserId is { } userId ? await users.DisplayNameAsync(userId, cancellationToken) : null;
        return new ProjectDetail(
            project.Id, project.Number, project.Name, project.Status, project.PartyId, party is null ? string.Empty : party.TradeName ?? party.LegalName, project.ContractId, contractNumber,
            project.SiteAddressId, site is null ? null : $"{site.Street}, {site.Number} — {site.City}/{site.State}", project.PromisedDate, project.ForecastDate, project.Priority,
            project.ManagerUserId, manager, project.CreatedAt, items, project.Version);
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
        projects.AsNoTracking().Select(p => new ProjectSummary(p.Id, p.Number, p.Name, p.PartyId, p.ContractId, p.Status.ToString(), p.SiteAddressId));

    internal static Error NotFound() => Error.NotFound("project_not_found", "Projeto não encontrado.");
}
