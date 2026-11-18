namespace BemAqui.Catalog.Domain;

/// <summary>
/// Código de barras GTIN (CAT-01 CA 6): GTIN-8, 12 (UPC-A), 13 (EAN-13) e 14 (DUN-14), só dígitos, com o dígito verificador do módulo 10
/// (pesos 3 e 1 a partir da direita, sem contar o verificador).
/// </summary>
public static class GtinRules
{
    public const string InvalidMessage = "Código de barras inválido. Confira os dígitos.";

    public static readonly IReadOnlySet<int> Lengths = new HashSet<int> { 8, 12, 13, 14 };

    /// <summary>Tira espaços e hífens que o leitor ou a digitação trazem; o resto tem de ser dígito.</summary>
    public static string Normalize(string? value) => new((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    public static bool IsValid(string? gtin)
    {
        if (gtin is null || !Lengths.Contains(gtin.Length) || !gtin.All(char.IsAsciiDigit))
        {
            return false;
        }

        return CheckDigit(gtin[..^1]) == gtin[^1] - '0';
    }

    /// <summary>Dígito verificador para o corpo (todos os dígitos menos o último).</summary>
    public static int CheckDigit(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var digit = body[body.Length - 1 - i] - '0';
            sum += i % 2 == 0 ? digit * 3 : digit;
        }

        return (10 - (sum % 10)) % 10;
    }
}

/// <summary>Custo, margem e markup (CA 11): margem sobre o preço; markup sobre o custo. Sem preço ou sem custo, não há o que calcular.</summary>
public static class MarginRules
{
    /// <summary>Margem em % sobre o preço, com 2 casas: (preço − custo) ÷ preço. Null sem preço (ou preço zero).</summary>
    public static decimal? MarginPercent(decimal? price, decimal cost) =>
        price is > 0m ? decimal.Round((price.Value - cost) / price.Value * 100m, 2, MidpointRounding.AwayFromZero) : null;

    /// <summary>Markup em % sobre o custo, com 2 casas: (preço − custo) ÷ custo. Null sem preço ou sem custo.</summary>
    public static decimal? MarkupPercent(decimal? price, decimal cost) =>
        price is { } p && cost > 0m ? decimal.Round((p - cost) / cost * 100m, 2, MidpointRounding.AwayFromZero) : null;

    /// <summary>
    /// Preço sugerido pela categoria (padrão propositivo): com margem, custo ÷ (1 − margem); com markup, custo × (1 + markup). Arredonda para
    /// cima no centavo. Null sem custo, sem padrão ou com margem de 100% ou mais.
    /// </summary>
    public static decimal? SuggestedPrice(decimal cost, decimal? marginPercent, decimal? markupPercent)
    {
        if (cost <= 0m)
        {
            return null;
        }

        decimal? raw = marginPercent is { } m ? (m < 100m ? cost / (1m - (m / 100m)) : null) : markupPercent is { } k ? cost * (1m + (k / 100m)) : null;
        return raw is { } r ? decimal.Ceiling(r * 100m) / 100m : null;
    }
}

/// <summary>Regras do produto e do preço (CA 3, 8, 14 e 15).</summary>
public static class ProductRules
{
    public const int NameMax = 150;
    public const int PluMaxDigits = 6;
    public const int MaxAttributes = 3;
    public const int AttributeTextMax = 30;
    public const int MaxVariantsPerGrade = 200;
    public const decimal MaxPrice = 9_999_999_999_999_999.99m;
    public const decimal MaxPackQuantity = 99_999_999_999_999.9999m;
    public const decimal MaxPercent = 99_999.9999m;

    /// <summary>PLU: de 1 a 6 dígitos (código curto da balança).</summary>
    public static bool IsValidPlu(string? plu) => plu is { Length: > 0 and <= PluMaxDigits } && plu.All(char.IsAsciiDigit);

    /// <summary>Preço positivo com até 2 casas, dentro de <c>numeric(18,2)</c>.</summary>
    public static bool IsValidPrice(decimal price) => price > 0m && price <= MaxPrice && decimal.Round(price, 2) == price;

    /// <summary>Quantidade da embalagem positiva com até 4 casas, dentro de <c>numeric(18,4)</c>.</summary>
    public static bool IsValidPackQuantity(decimal quantity) => quantity > 0m && quantity <= MaxPackQuantity && decimal.Round(quantity, 4) == quantity;

    /// <summary>Nome da variação: o nome do pai seguido dos valores dos atributos, na ordem da grade ("Camiseta básica Azul M").</summary>
    public static string VariantName(string parentName, IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(parentName);
        ArgumentNullException.ThrowIfNull(attributes);
        var name = string.Join(' ', new[] { parentName.Trim() }.Concat(attributes.Values.Select(v => v.Trim())));
        return name.Length <= NameMax ? name : name[..NameMax];
    }

    /// <summary>
    /// Combinações da grade (CA 15): o produto cartesiano dos valores, na ordem dos atributos ("Cor" × "Tamanho" = Azul P, Azul M, Preto P...).
    /// </summary>
    public static IReadOnlyList<Dictionary<string, string>> Combinations(IReadOnlyList<(string Name, IReadOnlyList<string> Values)> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        IEnumerable<Dictionary<string, string>> combos = [new Dictionary<string, string>(StringComparer.Ordinal)];
        foreach (var (name, values) in attributes)
        {
            combos = combos.SelectMany(c => values.Select(v => new Dictionary<string, string>(c, StringComparer.Ordinal) { [name] = v })).ToList();
        }

        return combos.Where(c => c.Count > 0).ToList();
    }

    /// <summary>Chave da combinação para comparar variações (atributo e valor, sem diferença de maiúscula).</summary>
    public static string CombinationKey(IReadOnlyDictionary<string, string>? attributes) =>
        attributes is null ? string.Empty : string.Join('|', attributes.Select(a => a.Key.Trim().ToUpperInvariant() + "=" + a.Value.Trim().ToUpperInvariant()));
}
