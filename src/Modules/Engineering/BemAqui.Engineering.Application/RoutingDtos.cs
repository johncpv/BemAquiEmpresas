using System.Text.Json.Serialization;
using BemAqui.Engineering.Contracts;
using BemAqui.Inventory.Contracts;

namespace BemAqui.Engineering.Application;

/// <summary>
/// Etapa no salvar (CA 3 a 5): <c>Id</c> nulo é etapa nova; com id, é uma etapa deste roteiro (reordenar não troca ids). A
/// sequência é a posição na lista, regravada 1..n pelo servidor. Campo desconhecido no corpo é recusado (CA 16).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RoutingStepInput(Guid? Id, string Name, string Code, Guid? WorkCenterId, decimal StandardHours, bool IsQualityStep, bool RequiresApproval, IReadOnlyList<string> Checklist);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RoutingTemplateInput(string Name, string? ProductType, IReadOnlyList<RoutingStepInput> Steps);

/// <summary>Edição com a versão que o cliente abriu (ADR 0019 §2, CA 17).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RoutingTemplateUpdate(string Name, string? ProductType, IReadOnlyList<RoutingStepInput> Steps, uint Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RoutingItemLinkInput(Guid ItemId);

public sealed record RoutingStepView(
    Guid Id,
    int Sequence,
    string Code,
    string Name,
    Guid? WorkCenterId,
    string? WorkCenterName,
    bool WorkCenterActive,
    decimal StandardHours,
    bool IsQualityStep,
    bool RequiresApproval,
    IReadOnlyList<RoutingChecklistItem> Checklist);

/// <summary>Detalhe do roteiro: <c>Usage</c> é o texto "{n} OS" dos provedores de uso (CA 12); <c>IsExample</c>, o do seed.</summary>
public sealed record RoutingTemplateDetail(
    Guid Id,
    string Name,
    string? ProductType,
    bool IsActive,
    bool IsExample,
    decimal TotalStandardHours,
    int ItemCount,
    int UsageCount,
    string? Usage,
    IReadOnlyList<RoutingStepView> Steps,
    uint Version,
    DateTimeOffset CreatedAt);

/// <summary>Linha da lista: letras e nomes das etapas para o indicador de etapa, horas totais, produtos e uso.</summary>
public sealed record RoutingTemplateSummary(
    Guid Id,
    string Name,
    string? ProductType,
    bool IsActive,
    IReadOnlyList<string> Codes,
    IReadOnlyList<string> StepNames,
    decimal TotalStandardHours,
    int ItemCount,
    int UsageCount,
    string? Usage);

/// <summary><c>Situation</c>: <c>active</c>, <c>inactive</c> ou vazio (todos).</summary>
public sealed record RoutingTemplateListQuery(string? Q, string? Situation, string? ProductType);

/// <summary>Produto vinculado (aba Produtos, CA 7).</summary>
public sealed record RoutingItemView(Guid Id, Guid ItemId, string Code, string Name, ItemType Type, bool IsActive, DateTimeOffset CreatedAt);

/// <summary>Produto para vincular: produto acabado ou componente ativo, com o roteiro que já usa (um roteiro por produto).</summary>
public sealed record RoutingProductOption(Guid Id, string Code, string Name, ItemType Type, Guid? RoutingTemplateId, string? RoutingTemplateName);

public sealed record RoutingWorkCenterOption(Guid Id, string Name, string? Line);

/// <summary>Último centro usado numa etapa com este nome (propositivo, tag "Sugerido").</summary>
public sealed record SuggestedWorkCenter(string StepName, Guid WorkCenterId);