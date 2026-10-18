using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

/// <summary>Cópia do <c>ModuleCatalog</c> no banco do tenant (<c>identity.modules</c>), para joins de grupos e relatórios.</summary>
public sealed class CatalogModule : Entity
{
    public required string Code { get; init; }

    public string Name { get; set; } = string.Empty;

    public string Area { get; set; } = string.Empty;

    public int Phase { get; set; }

    public int Order { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>O que "aprovar" significa neste módulo (R-SEC-10), quando <c>approve</c> se aplica.</summary>
    public string? ApproveMeaning { get; set; }
}

/// <summary>Ação aplicável de um módulo (<c>identity.module_actions</c>, ADR 0003): padrão ou específica em snake_case.</summary>
public sealed class CatalogModuleAction : Entity
{
    public required string ModuleCode { get; init; }

    public required string Action { get; init; }

    public string Label { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsStandard { get; set; }
}

/// <summary>Preferência do usuário (<c>identity.user_preferences</c>, FND-09a): tema; na FND-14, ajudas vistas.</summary>
public sealed class UserPreference : Entity
{
    public const string Theme = "theme";

    public Guid UserId { get; init; }

    public required string Key { get; init; }

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
