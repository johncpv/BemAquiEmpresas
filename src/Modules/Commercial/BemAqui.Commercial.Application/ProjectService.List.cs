using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class ProjectService
{
    /// <summary>Lista de projetos (COM-05 CA 1, 2): por página, com nomes resolvidos em lote; margem e recebido em rotas próprias.</summary>
    public async Task<ProjectListPage> ListAsync(string? q, string? situation, Guid? partyId, Guid? managerId, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var projects = db.Projects.AsNoTracking();
        projects = situation switch
        {
            "andamento" => projects.Where(p => p.Status != ProjectStatus.Closed && p.Status != ProjectStatus.Cancelled && p.Status != ProjectStatus.Delivered),
            "entregue" => projects.Where(p => p.Status == ProjectStatus.Delivered),
            "encerrado" => projects.Where(p => p.Status == ProjectStatus.Closed),
            "cancelado" => projects.Where(p => p.Status == ProjectStatus.Cancelled),
            _ => projects,
        };
        if (partyId is { } party)
        {
            projects = projects.Where(p => p.PartyId == party);
        }

        if (managerId is { } manager)
        {
            projects = projects.Where(p => p.ManagerUserId == manager);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            projects = projects.Where(p => EF.Functions.ILike(p.Number, pattern) || EF.Functions.ILike(p.Name, pattern));
        }

        var total = await projects.CountAsync(cancellationToken);
        var rows = await projects.OrderByDescending(p => p.CreatedAt).Skip(page.Skip).Take(page.PageSize)
            .Join(db.Contracts, p => p.ContractId, c => c.Id, (p, c) => new { Project = p, c.TotalValue })
            .ToListAsync(cancellationToken);
        var names = (await parties.FindManyAsync(rows.Select(r => r.Project.PartyId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var managers = new Dictionary<Guid, string?>();
        foreach (var userId in rows.Where(r => r.Project.ManagerUserId is not null).Select(r => r.Project.ManagerUserId!.Value).Distinct())
        {
            managers[userId] = await users.DisplayNameAsync(userId, cancellationToken);
        }

        var today = BusinessTime.Today(clock);
        return new ProjectListPage(rows.Select(r => new ProjectListRow(
            r.Project.Id, r.Project.Number, r.Project.Name, r.Project.PartyId, names.GetValueOrDefault(r.Project.PartyId, string.Empty), r.Project.Status, r.Project.PromisedDate,
            Delay(r.Project, today), r.TotalValue, r.Project.ManagerUserId, r.Project.ManagerUserId is { } m ? managers.GetValueOrDefault(m) : null, r.Project.ContractId)).ToList(),
            page.Page, page.PageSize, total);
    }

    /// <summary>Margem orçada dos projetos da página (CA 1), com <c>projects.view_cost</c>.</summary>
    public async Task<Result<IReadOnlyList<ProjectMargin>>> MarginsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken))
        {
            return NoCost();
        }

        var rows = await db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id)).Join(db.Contracts, p => p.ContractId, c => c.Id, (p, c) => new { p.Id, c.TotalValue })
            .ToListAsync(cancellationToken);
        var budgets = await db.ProjectBudgets.AsNoTracking().Where(b => ids.Contains(b.ProjectId)).GroupBy(b => b.ProjectId)
            .Select(g => new { g.Key, Total = g.Sum(b => b.BudgetedAmount) }).ToDictionaryAsync(g => g.Key, g => g.Total, cancellationToken);
        return rows.Select(r => new ProjectMargin(r.Id, r.TotalValue == 0 ? null : Math.Round((r.TotalValue - budgets.GetValueOrDefault(r.Id)) / r.TotalValue * 100, 1, MidpointRounding.AwayFromZero))).ToList();
    }

    /// <summary>% recebido do contratado (CA 1), com <c>finance.view</c>.</summary>
    public async Task<Result<IReadOnlyList<ProjectReceived>>> ReceivedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("finance", "view", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "O financeiro do projeto é restrito. Fale com o administrador.");
        }

        var rows = await db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id)).Join(db.Contracts, p => p.ContractId, c => c.Id, (p, c) => new { p.Id, c.TotalValue })
            .ToListAsync(cancellationToken);
        var list = new List<ProjectReceived>();
        foreach (var r in rows)
        {
            var received = (await receivables.ByProjectAsync(r.Id, cancellationToken)).Where(x => x.Status != BemAqui.Finance.Contracts.ReceivableStatus.Cancelled).Sum(x => x.Amount - x.Balance);
            list.Add(new ProjectReceived(r.Id, received, r.TotalValue == 0 ? 0 : Math.Round(received / r.TotalValue * 100, 1, MidpointRounding.AwayFromZero)));
        }

        return list;
    }

    private static int? Delay(Project p, DateOnly today) =>
        p.PromisedDate is { } promised && promised < today && p.Status is not (ProjectStatus.Delivered or ProjectStatus.Closed or ProjectStatus.Cancelled)
            ? today.DayNumber - promised.DayNumber
            : null;

    private static Error NoCost() => Error.Forbidden(Error.Codes.Forbidden, "Custo e margem do projeto são restritos. Fale com o administrador.");
}
