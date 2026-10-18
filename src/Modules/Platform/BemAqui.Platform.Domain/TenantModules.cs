using BemAqui.SharedKernel;

namespace BemAqui.Platform.Domain;

/// <summary>Módulo incluído num plano (<c>catalog.plan_modules</c>).</summary>
public sealed class PlanModule : Entity
{
    public Guid PlanId { get; init; }

    public required string ModuleCode { get; init; }
}

/// <summary>
/// Liberação de um módulo para o tenant (<c>catalog.tenant_modules</c>, FND-06). Herda do plano; o que o super admin
/// muda à mão fica como exceção e sobrevive a uma troca de plano (CA 7).
/// </summary>
public sealed class TenantModule : Entity
{
    public Guid TenantId { get; init; }

    public required string ModuleCode { get; init; }

    public bool Enabled { get; set; }

    public bool IsException { get; set; }

    public DateTimeOffset? ReleasedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
