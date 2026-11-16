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

/// <summary>
/// Perfil de negócio do tenant (<c>catalog.tenant_profiles</c>, PERF-01, ADR 0046): um ou mais por tenant, combinados por união.
/// Remover só marca <see cref="RemovedAt"/>: o histórico fica e nenhum dado do tenant é apagado. <see cref="AppliedBy"/> nulo é o
/// sistema (backfill da migration e provisionamento).
/// </summary>
public sealed class TenantProfile : Entity
{
    public Guid TenantId { get; init; }

    public required string ProfileCode { get; init; }

    public string? Segment { get; init; }

    public DateTimeOffset AppliedAt { get; init; }

    public Guid? AppliedBy { get; init; }

    public DateTimeOffset? RemovedAt { get; set; }

    public Guid? RemovedBy { get; set; }
}
