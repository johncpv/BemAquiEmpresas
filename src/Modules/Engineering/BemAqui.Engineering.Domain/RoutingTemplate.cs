using BemAqui.Engineering.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Engineering.Domain;

/// <summary>
/// Roteiro de fabricação (<c>engineering.routing_templates</c>, ENG-01, R-ENG-1): sequência de etapas que a OS de fabricação
/// copia (PRD-01). O template é só ponto de partida: editar não muda as OS já criadas. O exemplo P-04 vem do seed
/// (<see cref="SeedKey"/>), que não recria o roteiro excluído ou alterado pelo tenant.
/// </summary>
[DocumentName("O roteiro")]
public sealed class RoutingTemplate : Entity, IAuditable, ISoftDeletable, IConcurrencyVersioned
{
    public const string ExampleSeedKey = "example-routing";

    public string Name { get; set; } = string.Empty;

    /// <summary>Tipo de produto em texto livre (até 60), com sugestões dos tipos já usados (ponto 1).</summary>
    public string? ProductType { get; set; }

    public bool IsActive { get; set; } = true;

    public string? SeedKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public uint Version { get; set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>
/// Etapa do roteiro (<c>engineering.routing_template_steps</c>): <see cref="Code"/> é a "Letra" (D10), código da etapa no
/// Kanban e no display de andar, única no roteiro. Sem centro só no roteiro de exemplo inativo (CA 8).
/// </summary>
public sealed class RoutingTemplateStep : Entity, IAuditable
{
    public Guid TemplateId { get; init; }

    public int Sequence { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Guid? WorkCenterId { get; set; }

    /// <summary>Horas de trabalho esperadas por unidade (numeric(18,4)); o planejamento usa (R-PLN-3).</summary>
    public decimal StandardHours { get; set; }

    public bool RequiresApproval { get; set; }

    public bool IsQualityStep { get; set; }

    /// <summary>Checklist ordenado em jsonb: <c>[{"sequence":1,"text":"..."}]</c>, de 0 a 30 itens (CA 4).</summary>
    public List<RoutingChecklistItem> Checklist { get; set; } = [];
}

/// <summary>Vínculo do roteiro ao produto (<c>engineering.routing_template_items</c>, CA 7): um produto tem um roteiro só.</summary>
public sealed class RoutingTemplateItem : Entity, IAuditable
{
    public Guid TemplateId { get; init; }

    public Guid ItemId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}