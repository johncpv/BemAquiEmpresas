using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>Centros de custo em árvore (CAD-05a CA 16): as mesmas regras do plano de contas, sem tipo e sem grupo da DRE.</summary>
public sealed class CostCenterService(IFinanceDb db, IEnumerable<ICostCenterUsageProvider> usages, TimeProvider clock)
{
    public const string CodeIndex = "ix_cost_centers_code";

    public async Task<IReadOnlyList<CostCenterDetail>> ListAsync(CancellationToken cancellationToken = default) =>
        (await db.CostCenters.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(c => TreeCode.SortKey(c.Code)).Select(ToDetail).ToList();

    public async Task<Result<string?>> SuggestCodeAsync(Guid? parentId, CancellationToken cancellationToken = default)
    {
        CostCenter? parent = null;
        if (parentId is { } pid && (parent = await db.CostCenters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == pid, cancellationToken)) is null)
        {
            return NotFound();
        }

        var siblings = await db.CostCenters.IgnoreQueryFilters().AsNoTracking().Where(c => c.ParentId == parentId).Select(c => c.Code).ToListAsync(cancellationToken);
        return TreeCode.Next(parent?.Code, siblings);
    }

    public async Task<Result<CostCenterDetail>> CreateAsync(CostCenterInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await ValidateAsync(null, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var center = new CostCenter { CreatedAt = clock.GetUtcNow() };
        Apply(center, input);
        db.CostCenters.Add(center);
        return await SaveAsync(center, cancellationToken);
    }

    public async Task<Result<CostCenterDetail>> UpdateAsync(Guid id, CostCenterInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var center = await db.CostCenters.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (center is null)
        {
            return NotFound();
        }

        if (await ValidateAsync(center, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        Apply(center, input);
        return await SaveAsync(center, cancellationToken);
    }

    public async Task<Result<CostCenterDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var center = await db.CostCenters.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (center is null)
        {
            return NotFound();
        }

        if (!active)
        {
            var activeChildren = await db.CostCenters.Where(c => c.ParentId == id && c.IsActive).Select(c => c.Code).ToListAsync(cancellationToken);
            if (activeChildren.Count > 0)
            {
                return Error.Conflict("cost_center_has_active_children", "Inative antes os centros filhos: " + string.Join(", ", activeChildren.OrderBy(TreeCode.SortKey)) + ".");
            }
        }
        else if (center.ParentId is { } pid && await db.CostCenters.AnyAsync(c => c.Id == pid && !c.IsActive, cancellationToken))
        {
            return Error.Conflict("cost_center_parent_inactive", "Reative antes o centro pai.");
        }

        center.IsActive = active;
        return await SaveAsync(center, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var center = await db.CostCenters.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (center is null)
        {
            return NotFound();
        }

        if (await db.CostCenters.AnyAsync(c => c.ParentId == id, cancellationToken))
        {
            return Error.Conflict("cost_center_has_children", "O centro tem centros filhos. Exclua ou mova os filhos antes.");
        }

        foreach (var usage in usages)
        {
            if (await usage.HasEntriesAsync(id, cancellationToken))
            {
                return Error.Conflict("cost_center_in_use", "Centro de custo com lançamentos não pode ser excluído. Inative-o.");
            }
        }

        db.CostCenters.Remove(center);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ValidateAsync(CostCenter? current, CostCenterInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 100)
        {
            return TreeValidation.Invalid("name", "Informe o nome do centro de custo, com até 100 caracteres.");
        }

        return (await TreeValidation.ValidateAsync(db.CostCenters, current, input.Code?.Trim(), input.ParentId, TreeValidation.CostCenter, ct)).Error;
    }

    private static void Apply(CostCenter center, CostCenterInput input)
    {
        center.Code = input.Code.Trim();
        center.Name = input.Name.Trim();
        center.ParentId = input.ParentId;
    }

    private async Task<Result<CostCenterDetail>> SaveAsync(CostCenter center, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, CodeIndex))
        {
            db.CostCenters.Entry(center).State = EntityState.Detached;
            return Error.Conflict("tree_code_taken", $"Já existe o centro de custo {center.Code}.");
        }

        return ToDetail(center);
    }

    public static CostCenterDetail ToDetail(CostCenter c) => new(c.Id, c.Code, c.Name, c.ParentId, c.IsActive, c.SeedKey);

    private static Error NotFound() => Error.NotFound("cost_center_not_found", "Centro de custo não encontrado.");
}
