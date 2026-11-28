using System.Text.Json.Serialization;

namespace BemAqui.Commercial.Contracts;

/// <summary>
/// Papel da etapa no modelo de etapas do projeto de serviço (EXE-02 CA 2 e 3): <see cref="Execution"/> é trabalho; <see cref="Delivery"/>
/// é a entrega ao cliente (no máximo uma); <see cref="Acceptance"/> é o aceite do cliente (no máximo um, depois da entrega e por último).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StageRole>))]
public enum StageRole
{
    Execution,
    Delivery,
    Acceptance,
}

/// <summary>
/// Gatilho de um marco sugerido do modelo (EXE-02 CA 5): assinatura, conclusão de uma etapa de Execução do modelo, entrega ou aceite do cliente.
/// O <c>StageCompleted</c> do contrato (<c>TriggerEvent</c>) entra na EXE-05a; aqui é só a sugestão que a EXE-05b lê.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SuggestedMilestoneTrigger>))]
public enum SuggestedMilestoneTrigger
{
    Signature,
    StageCompleted,
    Delivered,
    TechnicalAcceptance,
}

/// <summary>Marco sugerido do modelo (<c>suggested_milestones</c> jsonb): % do total, gatilho, letra da etapa (só com <c>StageCompleted</c>) e prazo em dias.</summary>
public sealed record SuggestedMilestone(decimal Percent, SuggestedMilestoneTrigger Trigger, string? StageCode, int DaysAfterEvent);

/// <summary>Etapa do modelo para os outros módulos e para a cópia no projeto (EXE-03a): sem custo; o cargo é só o id.</summary>
public sealed record StageTemplateStepInfo(string Code, string Name, StageRole Role, decimal StandardHoursPerUnit, Guid? PositionId, bool RequiresApproval, IReadOnlyList<string> Checklist);

/// <summary>Modelo de etapas (EXE-02 CA 13): com a <c>Revision</c> (sobe a cada gravação do conteúdo), as etapas e os marcos sugeridos. Nunca custo.</summary>
public sealed record StageTemplateInfo(
    Guid Id,
    string Name,
    string? Segment,
    bool IsDefault,
    bool IsActive,
    int Revision,
    IReadOnlyList<StageTemplateStepInfo> Steps,
    IReadOnlyList<SuggestedMilestone> SuggestedMilestones);

/// <summary>Modelo ativo para os seletores (orçamento e projeto, EXE-05b e EXE-03a).</summary>
public sealed record StageTemplateOption(Guid Id, string Name, string? Segment, bool IsDefault);

/// <summary>
/// Modelos de etapas do projeto de serviço (EXE-02 CA 13), lidos pela EXE-03a (cópia das etapas) e pela EXE-05b (sugestão no orçamento).
/// Excluído não aparece; inativo só por id.
/// </summary>
public interface IStageTemplates
{
    Task<StageTemplateInfo?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Modelos ativos, o padrão primeiro.</summary>
    Task<IReadOnlyList<StageTemplateOption>> ActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Modelo sugerido (proposta §5 item 2), nesta ordem: o ligado a um dos itens de serviço (na ordem recebida) → o padrão do segmento → o padrão
    /// do tenant. Só ativos; sem nenhum, nulo.
    /// </summary>
    Task<StageTemplateInfo?> SuggestAsync(IReadOnlyList<Guid> itemIds, string? segment, CancellationToken cancellationToken = default);
}
