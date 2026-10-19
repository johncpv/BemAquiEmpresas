using BemAqui.Finance.Contracts;

namespace BemAqui.Finance.Application;

/// <summary>Conta para criar e editar (CA 5 a 11): <c>AcceptsEntries</c> é derivado e não vem da tela.</summary>
public sealed record AccountInput(string Code, string Name, AccountType Type, DreGroup DreGroup, Guid? ParentId);

public sealed record AccountDetail(Guid Id, string Code, string Name, AccountType Type, DreGroup DreGroup, Guid? ParentId, bool AcceptsEntries, bool IsActive, string? SeedKey);

/// <summary>Sugestão para a nova conta filha (CA 5, R-PRP-1): próximo código do nível e tipo e grupo da DRE do pai.</summary>
public sealed record AccountSuggestion(string? Code, AccountType? Type, DreGroup? DreGroup);

public sealed record CostCenterInput(string Code, string Name, Guid? ParentId);

public sealed record CostCenterDetail(Guid Id, string Code, string Name, Guid? ParentId, bool IsActive, string? SeedKey);
