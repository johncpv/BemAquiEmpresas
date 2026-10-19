using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>Garantia padrão por tipo (CAD-02a CA 4 e 5): vale para os itens novos; os já cadastrados não mudam.</summary>
public sealed class WarrantyDefaultService(IInventoryDb db)
{
    public async Task<IReadOnlyList<WarrantyDefaultDetail>> ListAsync(CancellationToken cancellationToken = default) =>
        (await db.WarrantyDefaults.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(w => w.ItemType).Select(w => new WarrantyDefaultDetail(w.Id, w.ItemType, w.Months)).ToList();

    public async Task<Result<WarrantyDefaultDetail>> UpdateAsync(ItemType type, WarrantyDefaultInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = await db.WarrantyDefaults.FirstOrDefaultAsync(w => w.ItemType == type, cancellationToken);
        if (row is null)
        {
            return Error.NotFound("warranty_default_not_found", "Tipo de item sem garantia padrão.");
        }

        if (input.Months is < 0 or > ItemRules.MaxWarrantyMonths)
        {
            return ItemRules.Invalid("months", $"Informe de 0 a {ItemRules.MaxWarrantyMonths} meses, ou deixe vazio para sem garantia.");
        }

        row.Months = input.Months;
        await db.SaveChangesAsync(cancellationToken);
        return new WarrantyDefaultDetail(row.Id, row.ItemType, row.Months);
    }
}
