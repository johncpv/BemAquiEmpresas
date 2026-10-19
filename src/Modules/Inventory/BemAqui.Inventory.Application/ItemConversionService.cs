using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Conversões de unidade do item (CAD-02b CA 1 a 7, 13 e 14) e o contrato <see cref="IUnitConversion"/>. Toda rota filha confere
/// que a conversão é do item da URL (IDOR pai/filho).
/// </summary>
public sealed class ItemConversionService(IInventoryDb db, TimeProvider clock) : IUnitConversion
{
    public const string PairIndex = "ix_item_unit_conversions_pair";

    public async Task<Result<IReadOnlyList<ConversionDetail>>> ListAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        if (!await db.Items.AnyAsync(i => i.Id == itemId, cancellationToken))
        {
            return ItemNotFound();
        }

        var rows = await db.ItemUnitConversions.AsNoTracking().Where(c => c.ItemId == itemId).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var codes = await UnitCodesAsync(rows.SelectMany(r => new[] { r.FromUnitId, r.ToUnitId }), cancellationToken);
        return rows.Select(r => ToDetail(r, codes)).ToList();
    }

    public async Task<Result<ConversionDetail>> CreateAsync(Guid itemId, ConversionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return ItemNotFound();
        }

        var conversion = new ItemUnitConversion { ItemId = itemId, CreatedAt = clock.GetUtcNow() };
        if (await ValidateAsync(item, conversion, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.ItemUnitConversions.Add(conversion);
        return await SaveAsync(conversion, input, cancellationToken);
    }

    /// <summary>Trocar o fator vale para os próximos documentos; os movimentos guardam a quantidade em estoque (CA 7).</summary>
    public async Task<Result<ConversionDetail>> UpdateAsync(Guid itemId, Guid conversionId, ConversionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var conversion = await db.ItemUnitConversions.FirstOrDefaultAsync(c => c.Id == conversionId && c.ItemId == itemId, cancellationToken);
        var item = conversion is null ? null : await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (conversion is null || item is null)
        {
            return NotFound();
        }

        return await ValidateAsync(item, conversion, input, cancellationToken) is { } invalid ? invalid : await SaveAsync(conversion, input, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid itemId, Guid conversionId, CancellationToken cancellationToken = default)
    {
        var conversion = await db.ItemUnitConversions.FirstOrDefaultAsync(c => c.Id == conversionId && c.ItemId == itemId, cancellationToken);
        if (conversion is null)
        {
            return NotFound();
        }

        db.ItemUnitConversions.Remove(conversion);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<decimal>> ConvertAsync(Guid itemId, decimal quantity, Guid fromUnitId, Guid toUnitId, CancellationToken cancellationToken = default)
    {
        var item = await db.Items.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return ItemNotFound();
        }

        var conversions = await db.ItemUnitConversions.AsNoTracking().Where(c => c.ItemId == itemId).ToListAsync(cancellationToken);
        if (UnitConverter.Convert(quantity, fromUnitId, toUnitId, item.UnitId, conversions) is { } converted)
        {
            return converted;
        }

        var codes = await UnitCodesAsync([fromUnitId, toUnitId], cancellationToken);
        return Error.Conflict("unit_conversion_missing",
            $"O item {item.Code} não tem conversão de {codes.GetValueOrDefault(fromUnitId, "?")} para {codes.GetValueOrDefault(toUnitId, "?")}. Cadastre em Estoque → Itens → Conversões.");
    }

    private async Task<Error?> ValidateAsync(Item item, ItemUnitConversion current, ConversionInput input, CancellationToken cancellationToken)
    {
        if (input.FromUnitId == input.ToUnitId)
        {
            return ItemRules.Invalid("toUnitId", "Escolha duas unidades diferentes.");
        }

        if (input.FromUnitId != item.UnitId && input.ToUnitId != item.UnitId)
        {
            return ItemRules.Invalid("toUnitId", "Uma das unidades precisa ser a unidade de estoque do item.");
        }

        if (input.Factor <= 0 || decimal.Round(input.Factor, 6) != input.Factor || input.Factor >= 1_000_000_000_000m)
        {
            return ItemRules.Invalid("factor", "Informe um fator maior que zero, com até 6 casas.");
        }

        var other = input.FromUnitId == item.UnitId ? input.ToUnitId : input.FromUnitId;
        if (!await db.Units.AnyAsync(u => u.Id == other && (u.IsActive || u.Id == current.FromUnitId || u.Id == current.ToUnitId), cancellationToken))
        {
            return ItemRules.Invalid(input.FromUnitId == item.UnitId ? "toUnitId" : "fromUnitId", "Escolha uma unidade ativa da lista.");
        }

        if (await db.ItemUnitConversions.AnyAsync(c => c.ItemId == item.Id && c.Id != current.Id
                && ((c.FromUnitId == input.FromUnitId && c.ToUnitId == input.ToUnitId) || (c.FromUnitId == input.ToUnitId && c.ToUnitId == input.FromUnitId)), cancellationToken))
        {
            var codes = await UnitCodesAsync([input.FromUnitId, input.ToUnitId], cancellationToken);
            return Error.Conflict("unit_conversion_taken", $"Este item já tem conversão entre {codes.GetValueOrDefault(input.FromUnitId)} e {codes.GetValueOrDefault(input.ToUnitId)}.");
        }

        return null;
    }

    private async Task<Result<ConversionDetail>> SaveAsync(ItemUnitConversion conversion, ConversionInput input, CancellationToken cancellationToken)
    {
        conversion.FromUnitId = input.FromUnitId;
        conversion.ToUnitId = input.ToUnitId;
        conversion.Factor = input.Factor;
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(conversion, await UnitCodesAsync([conversion.FromUnitId, conversion.ToUnitId], cancellationToken));
    }

    private async Task<Dictionary<Guid, string>> UnitCodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToList();
        return await db.Units.IgnoreQueryFilters().AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken);
    }

    private static ConversionDetail ToDetail(ItemUnitConversion c, Dictionary<Guid, string> codes) =>
        new(c.Id, c.ItemId, c.FromUnitId, codes.GetValueOrDefault(c.FromUnitId, string.Empty), c.ToUnitId, codes.GetValueOrDefault(c.ToUnitId, string.Empty), c.Factor);

    private static Error ItemNotFound() => Error.NotFound("item_not_found", "Item não encontrado.");

    private static Error NotFound() => Error.NotFound("unit_conversion_not_found", "Conversão não encontrada.");
}
