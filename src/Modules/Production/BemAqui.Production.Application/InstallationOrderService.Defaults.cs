using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class InstallationOrderService
{
    /// <summary>Sugestões do diálogo (CA 2, propositivo): itens ainda sem OS, o orçado de instalação livre e o último instalador do cliente.</summary>
    public async Task<Result<InstallationDefaults>> DefaultsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await projects.FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return Error.NotFound("project_not_found", "Projeto não encontrado.");
        }

        var coverage = await CoverageAsync(projectId, cancellationToken);
        var items = (await projects.ItemsAsync(projectId, cancellationToken))
            .Select(i => new InstallationItemOption(i.Id, i.Sequence, i.Description, i.Quantity, coverage.GetValueOrDefault(i.Id))).ToList();
        decimal? suggested = null;
        if (await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken) && await projects.BudgetedAsync(projectId, CostType.Installation, cancellationToken) is { } budget)
        {
            var used = await db.ProductionOrders.Where(o => o.ProjectId == projectId && o.Type == ProductionOrderType.Installation && o.Status != ProductionOrderStatus.Cancelled)
                .SumAsync(o => o.ContractValue, cancellationToken);
            suggested = Math.Max(budget - used, 0);
        }

        var sameCustomer = (await projects.SearchAsync(null, 50, cancellationToken)).Where(p => p.PartyId == project.PartyId).Select(p => p.Id).ToList();
        var last = await db.ProductionOrders.AsNoTracking().Where(o => o.Type == ProductionOrderType.Installation && sameCustomer.Contains(o.ProjectId))
            .OrderByDescending(o => o.CreatedAt).Select(o => (Guid?)o.InstallerId).FirstOrDefaultAsync(cancellationToken);
        var lastName = last is { } installer ? (await NamesAsync([installer], cancellationToken)).GetValueOrDefault(installer) : null;
        return new InstallationDefaults(items, suggested == 0 ? null : suggested, last, lastName);
    }

    /// <summary>Instaladores ativos para o seletor da OS (CA 2), sem exigir acesso a Cadastros.</summary>
    public async Task<IReadOnlyList<InstallerOption>> InstallersAsync(string? q, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(q, BemAqui.Parties.Contracts.PartyRoleKind.Installer, 20, cancellationToken)).Where(p => p.IsActive && !p.IsDeleted)
            .Select(p => new InstallerOption(p.Id, p.TradeName ?? p.LegalName)).ToList();

    /// <summary>Item do projeto → número da OS de instalação não cancelada que o cobre (CA 5).</summary>
    private async Task<Dictionary<Guid, string>> CoverageAsync(Guid projectId, CancellationToken ct) =>
        await db.ProductionOrderProjectItems.AsNoTracking()
            .Join(db.ProductionOrders, i => i.OrderId, o => o.Id, (i, o) => new { i.ProjectItemId, o.Number, o.ProjectId, o.Status, o.Type })
            .Where(x => x.ProjectId == projectId && x.Type == ProductionOrderType.Installation && x.Status != ProductionOrderStatus.Cancelled)
            .ToDictionaryAsync(x => x.ProjectItemId, x => x.Number, ct);
}
