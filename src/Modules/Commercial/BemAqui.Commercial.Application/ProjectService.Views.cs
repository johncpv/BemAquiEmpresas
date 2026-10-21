using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class ProjectService
{
    /// <summary>Editar nome, responsável e obra (CA 4); data prometida e valor só por aditivo.</summary>
    public async Task<Result<ProjectDetail>> UpdateAsync(Guid id, ProjectUpdateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (project.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O projeto mudou desde que você abriu. Recarregue.");
        }

        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            return QuoteService.Invalid("name", "Informe o nome do projeto, com até 200 caracteres.");
        }

        if (input.ManagerUserId is { } manager && !(await users.ActiveUserIdsAsync(cancellationToken)).Contains(manager))
        {
            return QuoteService.Invalid("managerUserId", "Escolha um usuário ativo.");
        }

        if (input.SiteAddressId is { } site && !(await parties.AddressesAsync(project.PartyId, cancellationToken)).Any(a => a.Id == site && a.Kind == AddressKind.Site))
        {
            return QuoteService.Invalid("siteAddressId", "Escolha um endereço de obra do cliente.");
        }

        db.Entry(project).Property(p => p.Version).OriginalValue = input.Version;
        project.Name = name;
        project.ManagerUserId = input.ManagerUserId;
        project.SiteAddressId = input.SiteAddressId;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Aba Financeiro (CA 6, 7): títulos a receber e a pagar do projeto, com <c>finance.view</c>.</summary>
    public async Task<Result<ProjectFinancial>> FinancialAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("finance", "view", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "O financeiro do projeto é restrito. Fale com o administrador.");
        }

        if (!await db.Projects.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var toReceive = await receivables.ByProjectAsync(id, cancellationToken);
        var toPay = await payables.ByProjectAsync(id, cancellationToken);
        var names = (await parties.FindManyAsync(toPay.Select(p => p.PartyId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var activeReceivables = toReceive.Where(r => r.Status != ReceivableStatus.Cancelled).ToList();
        var activePayables = toPay.Where(p => p.Status != PayableStatus.Cancelled).ToList();
        return new ProjectFinancial(
            toReceive.Select(r => new ProjectReceivableView(r.Id, r.Number, r.Description ?? string.Empty, r.Status, r.DueDate, r.Amount, r.Balance)).ToList(),
            toPay.Select(p => new ProjectPayableView(p.Id, p.Number, names.GetValueOrDefault(p.PartyId, string.Empty), p.Origin, p.CostType, p.DueDate, p.Amount, p.Balance, p.Status)).ToList(),
            activeReceivables.Sum(r => r.Amount - r.Balance), activeReceivables.Sum(r => r.Balance), activePayables.Sum(p => p.Amount - p.Balance), activePayables.Sum(p => p.Balance));
    }

    /// <summary>Aba Custos (CA 8 a 10) e margens do Resumo, com <c>projects.view_cost</c>.</summary>
    public async Task<Result<ProjectCostView>> CostsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken))
        {
            return NoCost();
        }

        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var revenue = await db.Contracts.Where(c => c.Id == project.ContractId).Select(c => c.TotalValue).FirstAsync(cancellationToken)
            + await db.ContractAmendments.Where(a => a.ContractId == project.ContractId && a.Status == AmendmentStatus.Approved).SumAsync(a => a.ValueDelta, cancellationToken);
        var budget = await db.ProjectBudgets.AsNoTracking().Where(b => b.ProjectId == id).ToDictionaryAsync(b => b.CostType, b => b.BudgetedAmount, cancellationToken);
        var toPay = await payables.ByProjectAsync(id, cancellationToken);
        return ProjectCosts.Compute(revenue, budget, toPay.Select(p => (p.CostType, p.Amount, p.Status == PayableStatus.Cancelled)));
    }
}
