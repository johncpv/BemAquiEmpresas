using BemAqui.Assets.Contracts;

namespace BemAqui.Assets.Domain;

/// <summary>Bem já cadastrado num centro, para as sugestões (CA 3): quando nasceu, o local e o responsável.</summary>
public sealed record CenterAsset(DateTimeOffset CreatedAt, Guid? SiteId, Guid? ResponsibleEmployeeId);

/// <summary>Sugestão para o bem novo de um centro (CA 3): o local do último bem e o responsável mais comum.</summary>
public sealed record AssetSuggestion(Guid? SiteId, Guid? ResponsibleEmployeeId);

/// <summary>Regras puras do cadastro de bens (PAT-01a).</summary>
public static class AssetRules
{
    public const int NameMax = 150;
    public const int TextMax = 80;
    public const int InvoiceRefMax = 30;
    public const int NotesMax = 1000;

    /// <summary>
    /// CA 3: local = o do bem mais recente do centro que tem local; responsável = o funcionário mais comum entre os bens do centro,
    /// só entre os ativos (empate: o do bem mais recente). Sem bens no centro, nada.
    /// </summary>
    public static AssetSuggestion Suggest(IEnumerable<CenterAsset> assets, IReadOnlySet<Guid> activeEmployees)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(activeEmployees);
        var ordered = assets.OrderByDescending(a => a.CreatedAt).ToList();
        var site = ordered.FirstOrDefault(a => a.SiteId is not null)?.SiteId;
        var responsible = ordered
            .Select((a, index) => (a.ResponsibleEmployeeId, index))
            .Where(x => x.ResponsibleEmployeeId is { } id && activeEmployees.Contains(id))
            .GroupBy(x => x.ResponsibleEmployeeId!.Value)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Min(x => x.index))
            .Select(g => (Guid?)g.Key)
            .FirstOrDefault();
        return new AssetSuggestion(site, responsible);
    }

    /// <summary>
    /// CA 12: só exclui (lógico) o bem sem histórico: ainda <c>Active</c> (manutenção e baixa mudam a situação) e sem uso declarado
    /// pelos provedores (movimentação, manutenção, custo).
    /// </summary>
    public static bool CanDelete(AssetStatus status, IEnumerable<string?> usages)
    {
        ArgumentNullException.ThrowIfNull(usages);
        return status == AssetStatus.Active && usages.All(u => u is null);
    }

    /// <summary>Texto opcional limpo: sem espaços nas pontas; vazio vira null.</summary>
    public static string? Clean(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Chave da série (CA 6): marca, modelo e série sem caixa nem espaços nas pontas.</summary>
    public static string SerialKey(string? brand, string? model, string serial) =>
        string.Join('|', (Clean(brand) ?? string.Empty).ToUpperInvariant(), (Clean(model) ?? string.Empty).ToUpperInvariant(), serial.Trim().ToUpperInvariant());

    /// <summary>Padrão do ILIKE que só casa o texto inteiro (sem os curingas % e _ do usuário), para comparar sem caixa no banco.</summary>
    public static string LikeExact(string text) =>
        (text ?? string.Empty).Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    /// <summary>Valor de aquisição (R-PAT-1): de zero para cima, até 2 casas, que caiba em <c>numeric(18,2)</c>.</summary>
    public static bool IsValidValue(decimal value) => value >= 0 && decimal.Round(value, 2) == value && value < 10_000_000_000_000_000m;

    /// <summary>Máquina e veículo têm plano de manutenção (PAT-03a): o próximo passo "Criar plano de manutenção" só para eles.</summary>
    public static bool HasMaintenancePlan(AssetType type) => type is AssetType.Machine or AssetType.Vehicle;
}
