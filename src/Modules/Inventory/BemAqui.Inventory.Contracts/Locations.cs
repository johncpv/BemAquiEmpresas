namespace BemAqui.Inventory.Contracts;

/// <summary>Local de estoque para os seletores dos outros módulos (CAD-03 CA 18).</summary>
public sealed record LocationOption(Guid Id, string Code, string Name, bool IsActive, bool IsDefault);

/// <summary>
/// Locais de estoque para os outros módulos (ADR 0016): só ativos nos seletores, inativos resolvidos por id (documento antigo
/// continua mostrando o local).
/// </summary>
public interface ILocations
{
    Task<IReadOnlyList<LocationOption>> ActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Por id, inclusive inativos; os excluídos não voltam.</summary>
    Task<IReadOnlyList<LocationOption>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>O local padrão, pré-selecionado nos movimentos (CA 4, R-PRP-1); null se o tenant ainda não tem.</summary>
    Task<LocationOption?> DefaultAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Quem usa um local (CA 5 e 6): o EST-01 informa o saldo; movimentos, itens e outros módulos que guardam o id informam o uso.
/// Com saldo, o local não é inativado; com uso, não é excluído, só inativado.
/// </summary>
public interface ILocationUsageProvider
{
    Task<bool> HasBalanceAsync(Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>Descrição curta do uso ("movimentos") ou null se não usa.</summary>
    Task<string?> UsageAsync(Guid locationId, CancellationToken cancellationToken = default);
}
