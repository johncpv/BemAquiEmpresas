namespace BemAqui.Inventory.Application;

/// <summary>Criação e edição de local (CA 2 e 4). <c>IsDefault</c> quer dizer "tornar padrão": falso não desmarca (CA 4).</summary>
public sealed record LocationInput(string Code, string Name, bool IsDefault = false);

public sealed record LocationDetail(Guid Id, string Code, string Name, bool IsActive, bool IsDefault, string? SeedKey, DateTimeOffset CreatedAt);

public sealed record LocationListQuery(string? Q, bool IncludeInactive);
