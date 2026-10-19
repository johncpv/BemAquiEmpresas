using System.Globalization;
using System.Text.RegularExpressions;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>Nó de árvore com código hierárquico: conta contábil ou centro de custo.</summary>
public interface ITreeNode
{
    Guid Id { get; }

    string Code { get; }

    string Name { get; }

    Guid? ParentId { get; }

    bool IsActive { get; }
}

/// <summary>Conta do plano gerencial (<c>finance.chart_of_accounts</c>, CAD-05a): código na máscara <c>9.9.99</c>, em árvore.</summary>
public sealed class Account : Entity, IAuditable, ISoftDeletable, ITreeNode
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public AccountType Type { get; set; }

    public DreGroup DreGroup { get; set; }

    public Guid? ParentId { get; set; }

    /// <summary>Analítica (recebe lançamento) quando não tem filhas ativas; derivado, nunca vem da tela (CA 9).</summary>
    public bool AcceptsEntries { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public string? SeedKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>Centro de custo (<c>finance.cost_centers</c>, CA 16): mesma árvore e mesma máscara do plano de contas.</summary>
public sealed class CostCenter : Entity, IAuditable, ISoftDeletable, ITreeNode
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public bool IsActive { get; set; } = true;

    public string? SeedKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>Código hierárquico na máscara <c>9.9.99</c> (CA 5 e 6): três níveis, o filho começa com o código do pai.</summary>
public static partial class TreeCode
{
    public const string MaskMessage = "Use o formato 9.9.99.";

    [GeneratedRegex(@"^[1-9](\.[1-9](\.\d{2})?)?$")]
    private static partial Regex Mask();

    public static bool IsValid(string? code) => code is not null && Mask().IsMatch(code) && !code.EndsWith(".00", StringComparison.Ordinal);

    /// <summary>1, 2 ou 3; 0 se o código não vale.</summary>
    public static int Level(string? code) => IsValid(code) ? code!.Count(c => c == '.') + 1 : 0;

    /// <summary>Código do pai esperado ("2.1.03" → "2.1"; raiz → null).</summary>
    public static string? ParentCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var dot = code.LastIndexOf('.');
        return dot < 0 ? null : code[..dot];
    }

    /// <summary>Próximo código livre no nível abaixo do pai (ou na raiz): depois de 2.1.03 vem 2.1.04 (CA 5); null se o nível acabou.</summary>
    public static string? Next(string? parentCode, IEnumerable<string> siblingCodes)
    {
        ArgumentNullException.ThrowIfNull(siblingCodes);
        var level = parentCode is null ? 1 : Level(parentCode) + 1;
        if (level > 3)
        {
            return null;
        }

        var used = siblingCodes.Select(c => int.TryParse(c.Split('.')[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        var next = used + 1;
        if ((level < 3 && next > 9) || next > 99)
        {
            return null;
        }

        var segment = level == 3 ? next.ToString("00", CultureInfo.InvariantCulture) : next.ToString(CultureInfo.InvariantCulture);
        return parentCode is null ? segment : parentCode + "." + segment;
    }

    /// <summary>Chave para ordenar por segmento numérico (1.10 depois de 1.9 não acontece na máscara, mas 2.1.10 depois de 2.1.09 sim).</summary>
    public static string SortKey(string code) =>
        string.Join('.', (code ?? string.Empty).Split('.').Select(s => s.PadLeft(2, '0')));
}
