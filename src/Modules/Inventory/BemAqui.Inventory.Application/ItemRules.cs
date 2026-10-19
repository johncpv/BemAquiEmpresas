using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Regras do item por tipo (CAD-02a CA 7 a 12), as mesmas para a tela e para a importação: NCM obrigatório para item ativo de
/// Matéria-prima, Componente e Produto acabado; Serviço sem estoque mínimo, lote e série; garantia de 0 a 120 meses.
/// </summary>
public static class ItemRules
{
    public const int MaxWarrantyMonths = 120;
    public const string NcmRequired = "Preencha o NCM (8 dígitos) para o item ativo.";

    /// <summary>Só os dígitos ("8431.31.10" vira "84313110"); vazio vira null.</summary>
    public static string? Digits(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    /// <summary>Normaliza o que a regra manda (Serviço sem lote, série e mínimo) e devolve o primeiro problema, ou null.</summary>
    public static Error? Validate(ItemInput input, bool active, out ItemInput normalized)
    {
        ArgumentNullException.ThrowIfNull(input);
        normalized = input with
        {
            Code = (input.Code ?? string.Empty).Trim(),
            Name = (input.Name ?? string.Empty).Trim(),
            Ncm = Digits(input.Ncm),
            Cest = Digits(input.Cest),
        };
        if (normalized.Type == ItemType.Service)
        {
            normalized = normalized with { MinStock = 0, TracksLot = false, TracksSerial = false };
        }

        if (!Enum.IsDefined(normalized.Type))
        {
            return Invalid("type", "Escolha o tipo do item.");
        }

        if (normalized.Code.Length is 0 or > 30)
        {
            return Invalid("code", "Informe o código do item, com até 30 caracteres.");
        }

        if (normalized.Name.Length is 0 or > 150)
        {
            return Invalid("name", "Informe o nome do item, com até 150 caracteres.");
        }

        if (normalized.UnitId == Guid.Empty)
        {
            return Invalid("unitId", "Escolha a unidade de estoque.");
        }

        if (normalized.Ncm is { Length: not 8 } || (normalized.Ncm is null && input.Ncm is { Length: > 0 } && input.Ncm.Trim().Length > 0))
        {
            return Invalid("ncm", "O NCM tem 8 dígitos (ex.: 8431.31.10).");
        }

        if (active && normalized.Ncm is null && Item.RequiresNcm(normalized.Type))
        {
            return Invalid("ncm", NcmRequired);
        }

        if (normalized.Cest is { Length: not 7 })
        {
            return Invalid("cest", "O CEST tem 7 dígitos (ex.: 01.001.00).");
        }

        if (normalized.Origin is < 0 or > 8)
        {
            return Invalid("origin", "Escolha a origem da mercadoria (0 a 8).");
        }

        if (normalized.MinStock < 0 || decimal.Round(normalized.MinStock, 4) != normalized.MinStock || normalized.MinStock >= 100_000_000_000_000m)
        {
            return Invalid("minStock", "O estoque mínimo é zero ou mais, com até 4 casas.");
        }

        if (normalized.WarrantyMonths is < 0 or > MaxWarrantyMonths)
        {
            return Invalid("warrantyMonths", $"Informe a garantia em meses, de 0 a {MaxWarrantyMonths}, ou deixe vazio para sem garantia.");
        }

        return null;
    }

    public static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });
}
