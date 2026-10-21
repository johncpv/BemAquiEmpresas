namespace BemAqui.Engineering.Contracts;

/// <summary>Item do checklist da etapa, na ordem (CA 4).</summary>
public sealed record RoutingChecklistItem(int Sequence, string Text);

/// <summary>Etapa do roteiro para os outros módulos (CA 13): a OS copia nome, letra, centro, horas e checklist (PRD-01).</summary>
public sealed record RoutingStepInfo(
    Guid Id,
    int Sequence,
    string Code,
    string Name,
    Guid? WorkCenterId,
    decimal StandardHours,
    bool IsQualityStep,
    bool RequiresApproval,
    IReadOnlyList<RoutingChecklistItem> Checklist);

public sealed record RoutingTemplateInfo(Guid Id, string Name, string? ProductType, bool IsActive, IReadOnlyList<RoutingStepInfo> Steps);

/// <summary>Soma das horas padrão de um roteiro por centro de trabalho (CA 13), para a simulação de prazo da Fase 4.</summary>
public sealed record WorkCenterStandardHours(Guid? WorkCenterId, decimal Hours);

/// <summary>
/// Roteiros para a Produção e o Comercial (ENG-01 CA 13, ADR 0006): só ativos nos seletores e na sugestão pelo produto;
/// por id, inclusive inativos (quem já usa continua vendo, CA 11).
/// </summary>
public interface IRoutingTemplates
{
    Task<IReadOnlyList<RoutingTemplateInfo>> ActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Por id, inclusive inativos; os excluídos não voltam.</summary>
    Task<IReadOnlyList<RoutingTemplateInfo>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Etapas na ordem (vazio se o roteiro não existe).</summary>
    Task<IReadOnlyList<RoutingStepInfo>> StepsAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>Roteiro ativo vinculado ao produto (CA 7); null sem vínculo ou com o roteiro inativo (CA 11).</summary>
    Task<RoutingTemplateInfo?> ForItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Horas padrão somadas por centro, na ordem da primeira etapa de cada centro.</summary>
    Task<IReadOnlyList<WorkCenterStandardHours>> TotalStandardHoursAsync(Guid templateId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Quem usa um roteiro (CA 10 e 12): a Produção (OS) e o Comercial (item do orçamento e do projeto), a partir da PRD-01.
/// Com uso, o roteiro não é excluído, só inativado (ADR 0016 D10).
/// </summary>
public interface IRoutingTemplateUsageProvider
{
    /// <summary>Nome do uso no plural, para a lista ("OS", "itens de orçamento").</summary>
    string Noun { get; }

    /// <summary>Quantos documentos usam cada roteiro; roteiro sem uso fica fora do dicionário.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountAsync(IReadOnlyCollection<Guid> templateIds, CancellationToken cancellationToken = default);
}