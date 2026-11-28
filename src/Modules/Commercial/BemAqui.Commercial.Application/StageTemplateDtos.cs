using System.Text.Json.Serialization;
using BemAqui.Commercial.Contracts;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Etapa no salvar (EXE-02 CA 2): <c>Id</c> nulo é etapa nova; com id, é uma etapa deste modelo (reordenar não troca ids). A sequência é a
/// posição na lista, regravada pelo servidor. Campo desconhecido no corpo é recusado.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StageStepInput(Guid? Id, string Name, string Code, StageRole Role, decimal StandardHoursPerUnit, Guid? PositionId, bool RequiresApproval, IReadOnlyList<string> Checklist);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SuggestedMilestoneInput(decimal Percent, SuggestedMilestoneTrigger Trigger, string? StageCode, int DaysAfterEvent);

/// <summary>Criação: nome, segmento opcional (os do perfil Serviços), etapas e marcos sugeridos.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StageTemplateInput(string Name, string? Segment, IReadOnlyList<StageStepInput> Steps, IReadOnlyList<SuggestedMilestoneInput>? SuggestedMilestones);

/// <summary>Edição com a versão (xmin) que o cliente abriu (ADR 0019 §2).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StageTemplateUpdate(string Name, string? Segment, IReadOnlyList<StageStepInput> Steps, IReadOnlyList<SuggestedMilestoneInput>? SuggestedMilestones, uint Version)
{
    public StageTemplateInput AsInput() => new(Name, Segment, Steps, SuggestedMilestones);
}

/// <summary>"Usar como padrão", ativar e inativar: com a versão que o cliente abriu.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StageTemplateVersionInput(uint Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StageTemplateItemLinkInput(Guid ItemId);

/// <summary>Etapa no detalhe: cargo com o nome e se está ativo ("Cargo inativo", CA 4); sem custo.</summary>
public sealed record StageStepView(
    Guid Id,
    int Sequence,
    string Code,
    string Name,
    StageRole Role,
    decimal StandardHoursPerUnit,
    Guid? PositionId,
    string? PositionName,
    bool PositionActive,
    bool RequiresApproval,
    IReadOnlyList<string> Checklist);

/// <summary>Detalhe do modelo (CA 2, 6): <c>IsSuggested</c> = veio do pacote do perfil e ainda não foi editado; <c>UsageCount</c> = projetos que copiaram as etapas.</summary>
public sealed record StageTemplateDetail(
    Guid Id,
    string Name,
    string? Segment,
    string? SegmentName,
    bool IsDefault,
    bool IsSuggested,
    bool IsActive,
    int Revision,
    decimal TotalHoursPerUnit,
    int ItemCount,
    int UsageCount,
    IReadOnlyList<StageStepView> Steps,
    IReadOnlyList<SuggestedMilestone> SuggestedMilestones,
    uint Version,
    DateTimeOffset CreatedAt);

/// <summary>Linha da lista (CA 1): letras e nomes para o indicador, horas por unidade somadas, segmento, chips e uso.</summary>
public sealed record StageTemplateSummary(
    Guid Id,
    string Name,
    string? Segment,
    string? SegmentName,
    bool IsDefault,
    bool IsSuggested,
    bool IsActive,
    IReadOnlyList<string> Codes,
    IReadOnlyList<string> StepNames,
    decimal TotalHoursPerUnit,
    int ItemCount,
    int UsageCount);

/// <summary><c>Situation</c>: <c>active</c>, <c>inactive</c> ou vazio (todos).</summary>
public sealed record StageTemplateListQuery(string? Q, string? Situation);

/// <summary>Cargo para o seletor da etapa (CA 4): só id e nome, nunca custo.</summary>
public sealed record StagePositionOption(Guid Id, string Name);

public sealed record StageSegmentOption(string Code, string Name);

/// <summary>Item de serviço vinculado ao modelo (aba Itens, CA 10).</summary>
public sealed record StageTemplateItemView(Guid Id, Guid ItemId, string Code, string Name, bool IsActive, DateTimeOffset CreatedAt);

/// <summary>Item de serviço para vincular, com o modelo que já usa (um modelo por item).</summary>
public sealed record StageItemOption(Guid Id, string Code, string Name, Guid? StageTemplateId, string? StageTemplateName);

/// <summary>Modelo do item na ficha do item (CA 10), para quem tem <c>projects.view</c>; sem vínculo, os dois nulos.</summary>
public sealed record ItemStageTemplateView(Guid? StageTemplateId, string? StageTemplateName);

/// <summary>"Usar os sugeridos" (CA 11): quantos modelos foram criados agora.</summary>
public sealed record StageTemplateSeedResult(int Created);
