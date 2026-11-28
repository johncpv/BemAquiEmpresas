using BemAqui.Commercial.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Domain;

/// <summary>
/// Modelo de etapas do projeto de serviço (<c>commercial.stage_templates</c>, EXE-02): sequência de etapas com papel (Execução, Entrega,
/// Aceite), horas por unidade do item e marcos sugeridos. É só ponto de partida: o projeto copia as etapas (EXE-03a), então mudar o modelo não
/// muda projeto existente. Padrão único por segmento (nulo = do tenant) entre os não excluídos. Os quatro modelos do perfil Serviços vêm do
/// pacote <c>projects.services</c> pela <see cref="SeedKey"/>, que fica gravada no excluído para ele não voltar.
/// </summary>
[DocumentName("O modelo de etapas")]
public sealed class StageTemplate : Entity, IAuditable, ISoftDeletable, IConcurrencyVersioned
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Tipo de execução a que o modelo serve; hoje só <see cref="ExecutionKind.Service"/> (o roteiro da Engenharia é o da fabricação).</summary>
    public ExecutionKind Kind { get; init; } = ExecutionKind.Service;

    /// <summary>Segmento do perfil Serviços (ex.: <c>workshop</c>); nulo = modelo do tenant.</summary>
    public string? Segment { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Revisão do conteúdo (CA 2 e 13): 1 na criação e +1 a cada gravação do modelo.</summary>
    public int Revision { get; set; } = 1;

    /// <summary>Marcos sugeridos em jsonb (CA 5): a soma dá 100% quando há algum.</summary>
    public List<SuggestedMilestone> SuggestedMilestones { get; set; } = [];

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

/// <summary>Etapa do modelo (<c>commercial.stage_template_steps</c>): <see cref="Code"/> é a letra (A–Z), única no modelo.</summary>
public sealed class StageTemplateStep : Entity, IAuditable
{
    public Guid TemplateId { get; init; }

    public int Sequence { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public StageRole Role { get; set; }

    /// <summary>Horas previstas por unidade do item (numeric(18,4)), como a composição de custo do orçamento (decisão do mantenedor de 28/11).</summary>
    public decimal StandardHoursPerUnit { get; set; }

    /// <summary>Cargo sugerido (HR, pelo <c>IPositions</c>); só o id, sem custo.</summary>
    public Guid? PositionId { get; set; }

    public bool RequiresApproval { get; set; }

    /// <summary>Checklist em jsonb, de 0 a 30 itens de até 200 caracteres.</summary>
    public List<string> Checklist { get; set; } = [];
}

/// <summary>Modelo sugerido para um item de serviço (<c>commercial.stage_template_items</c>, CA 10): um item tem um modelo só.</summary>
public sealed class StageTemplateItem : Entity, IAuditable
{
    public Guid TemplateId { get; init; }

    public Guid ItemId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
