using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Domain;

/// <summary>
/// Conversão de unidade do item (<c>inventory.item_unit_conversions</c>, CAD-02b): "1 {from} = {factor} {to}", com uma das
/// duas unidades sempre a de estoque; a inversa é calculada, não gravada.
/// </summary>
public sealed class ItemUnitConversion : Entity, IAuditable, ISoftDeletable
{
    public Guid ItemId { get; init; }

    public Guid FromUnitId { get; set; }

    public Guid ToUnitId { get; set; }

    /// <summary><c>numeric(18,6)</c>, maior que zero.</summary>
    public decimal Factor { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>
/// Código que um fornecedor usa para o item (<c>inventory.item_supplier_codes</c>, CAD-02b CA 8 a 12): a unidade dele em texto
/// livre (como vem na nota) e o fator para a unidade de estoque ("1 {supplier_unit} = {conversion_factor} {unidade de estoque}").
/// </summary>
public sealed class ItemSupplierCode : Entity, IAuditable, ISoftDeletable
{
    public Guid ItemId { get; init; }

    /// <summary>Só o id do terceiro (Parties), validado por <c>IParties</c>.</summary>
    public Guid PartyId { get; set; }

    public string SupplierCode { get; set; } = string.Empty;

    public string? SupplierUnit { get; set; }

    public decimal ConversionFactor { get; set; } = 1m;

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>
/// Conta de conversão (CA 3, 4 e 6): leva a quantidade para a unidade de estoque e de lá para o destino, e arredonda uma vez só,
/// no fim, para 4 casas (AwayFromZero).
/// </summary>
public static class UnitConverter
{
    /// <summary>Quanto vale 1 <paramref name="unit"/> na unidade de estoque, ou null se o item não tem a conversão.</summary>
    public static decimal? ToStockFactor(Guid unit, Guid stockUnit, IEnumerable<ItemUnitConversion> conversions)
    {
        ArgumentNullException.ThrowIfNull(conversions);
        if (unit == stockUnit)
        {
            return 1m;
        }

        foreach (var c in conversions)
        {
            if (c.FromUnitId == unit && c.ToUnitId == stockUnit)
            {
                return c.Factor;
            }

            if (c.FromUnitId == stockUnit && c.ToUnitId == unit)
            {
                return 1m / c.Factor;
            }
        }

        return null;
    }

    public static decimal? Convert(decimal quantity, Guid from, Guid to, Guid stockUnit, IReadOnlyCollection<ItemUnitConversion> conversions)
    {
        if (from == to)
        {
            return quantity;
        }

        var fromFactor = ToStockFactor(from, stockUnit, conversions);
        var toFactor = ToStockFactor(to, stockUnit, conversions);
        return fromFactor is { } f && toFactor is { } t ? Math.Round(quantity * f / t, 4, MidpointRounding.AwayFromZero) : null;
    }
}
