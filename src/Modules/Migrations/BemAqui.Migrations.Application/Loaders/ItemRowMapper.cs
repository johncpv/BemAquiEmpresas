using System.Globalization;
using BemAqui.Inventory.Contracts;
using BemAqui.Migrations.Application.Snapshot;

namespace BemAqui.Migrations.Application.Loaders;

/// <summary>O que o produto do Dolibarr vira, antes de resolver unidade, categoria e garantia padrão (MIG-01c CA 3 a 7).</summary>
public sealed record MappedProduct(ItemType Type, string Code, string Name, string? Ncm, decimal MinStock, bool TracksLot, bool TracksSerial, int? WarrantyMonths, bool UseDefaultWarranty, bool SellOrBuy, IReadOnlyList<string> Warnings);

/// <summary>Regras de conversão do <c>llx_product</c>, sem banco (testadas em unidade).</summary>
public static class ItemRowMapper
{
    private static readonly Dictionary<string, ItemType> TypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RawMaterial"] = ItemType.RawMaterial, ["materia-prima"] = ItemType.RawMaterial, ["matéria-prima"] = ItemType.RawMaterial,
        ["Component"] = ItemType.Component, ["componente"] = ItemType.Component,
        ["Finished"] = ItemType.Finished, ["produto-acabado"] = ItemType.Finished, ["produto acabado"] = ItemType.Finished,
        ["Consumable"] = ItemType.Consumable, ["consumo"] = ItemType.Consumable, ["material de consumo"] = ItemType.Consumable,
    };

    /// <summary>Tipo: 1 = Serviço; 0 pelo de-para <c>item-types</c> (ref ou id); sem de-para, Componente com aviso (CA 4).</summary>
    public static (ItemType Type, string? Warning) Type(SourceRow row, ImportMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(mapping);
        if (row.WholeNumber("fk_product_type") == 1)
        {
            return (ItemType.Service, null);
        }

        var mapped = mapping.Get("item-types", row.Text("ref") ?? string.Empty) ?? mapping.Get("item-types", row.Id);
        if (mapped is null)
        {
            return (ItemType.Component, "tipo não informado no de-para: entrou como Componente");
        }

        return TypeNames.TryGetValue(mapped.Trim(), out var type) ? (type, null) : (ItemType.Component, $"tipo \"{mapped}\" do de-para não existe: entrou como Componente");
    }

    public static MappedProduct Map(SourceRow row, ImportMapping mapping, string? warrantyText)
    {
        ArgumentNullException.ThrowIfNull(row);
        var warnings = new List<string>();
        var (type, typeWarning) = Type(row, mapping);
        if (typeWarning is not null)
        {
            warnings.Add(typeWarning);
        }

        var service = type == ItemType.Service;
        var batch = row.WholeNumber("tobatch") ?? 0;
        var digits = DolibarrText.Digits(row.Text("customcode"));
        string? ncm = digits.Length == 8 ? digits : null;
        if (ncm is null && digits.Length > 0)
        {
            warnings.Add($"NCM \"{row.Text("customcode")}\" não tem 8 dígitos: ficou sem NCM");
        }

        var (months, useDefault) = Warranty(warrantyText, warnings);
        var min = DolibarrConvert.Quantity(row["seuil_stock_alerte"]) ?? 0m;
        return new MappedProduct(
            type,
            (row.Text("ref") ?? row.Id).Trim(),
            (row.Text("label") ?? row.Text("ref") ?? row.Id).Trim(),
            ncm,
            service || min < 0 ? 0 : min,
            !service && batch == 1,
            !service && batch == 2,
            months,
            useDefault,
            row.WholeNumber("tosell") == 1 || row.WholeNumber("tobuy") == 1,
            warnings);
    }

    /// <summary><c>elev_garantia</c> em meses (CA 6): vazio usa o padrão do tipo; não numérico vira aviso e também usa o padrão.</summary>
    public static (int? Months, bool UseDefault) Warranty(string? text, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return (null, true);
        }

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var months) && months <= 120)
        {
            return (months, false);
        }

        warnings.Add($"garantia \"{value}\" não é um número de meses (0 a 120): usou o padrão do tipo");
        return (null, true);
    }
}
