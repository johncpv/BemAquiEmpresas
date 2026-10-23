using System.Text.Json.Serialization;
using BemAqui.Engineering.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;

namespace BemAqui.Engineering.Application;

/// <summary>"Criar BOM" (CA 2) ou "Copiar BOM" de outra OS (CA 5): a revisão nasce em rascunho, vazia ou com as linhas da origem.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BomCreateInput(Guid ProductionOrderId, Guid? CopyFromBomId);

/// <summary>
/// Linha no salvar (CA 2, 6): <c>Id</c> nulo é linha nova; com id, é uma linha deste rascunho. Quantidade por unidade do produto; unidade
/// nula é a de estoque do item.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BomLineInput(Guid? Id, Guid ItemId, decimal Quantity, Guid? UnitId, decimal ScrapPct, string? Notes);

/// <summary>"Salvar BOM" com a versão que o cliente abriu (ADR 0019 §2).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BomUpdate(IReadOnlyList<BomLineInput> Lines, uint Version);

/// <summary>"Copiar BOM" para dentro do rascunho: as linhas da origem substituem as do rascunho (CA 5).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BomCopyInput(Guid SourceBomId, uint Version);

/// <summary>Comando sobre a revisão na versão aberta: liberar, revisar e descartar.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BomVersionInput(uint Version);

/// <summary>
/// Linha da BOM na tela (CA 3, padrão 8): a quantidade por unidade e a necessidade da OS na unidade de estoque; físico, reservado e
/// disponível do estoque e a falta (nulos enquanto o estoque não está ligado, EST-01a); custo previsto só com <c>projects.view_cost</c>.
/// </summary>
public sealed record BomLineView(
    Guid Id,
    int Sequence,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    ItemType ItemType,
    bool ItemActive,
    decimal Quantity,
    Guid UnitId,
    string UnitCode,
    decimal ScrapPct,
    string? Notes,
    decimal RequiredQuantity,
    Guid StockUnitId,
    string StockUnitCode,
    decimal? OnHand,
    decimal? Reserved,
    decimal? Available,
    decimal? Shortage,
    decimal? EstimatedCost,
    IReadOnlyList<ItemUnitOption> Units);

/// <summary>Uma revisão da BOM com as linhas (CA 3, 15), quem liberou e a origem da cópia.</summary>
public sealed record BomDetail(
    Guid Id,
    Guid ProductionOrderId,
    int Revision,
    BomStatus Status,
    DateTimeOffset? ReleasedAt,
    Guid? ReleasedBy,
    string? ReleasedByName,
    Guid? CopiedFromBomId,
    string? CopiedFromOrderNumber,
    int? CopiedFromRevision,
    IReadOnlyList<BomLineView> Lines,
    int ShortageCount,
    bool StockConnected,
    decimal? EstimatedCost,
    uint Version,
    DateTimeOffset CreatedAt);

/// <summary>Cabeçalho da aba BOM (CA 4): a OS, o produto (ou "Sem item de estoque") e se a BOM ainda muda (CA 11).</summary>
public sealed record BomOrderHeader(
    Guid Id,
    string Number,
    ProductionOrderState Status,
    Guid ProjectId,
    Guid? ProductId,
    string? ProductCode,
    string? ProductName,
    decimal Quantity,
    bool Closed,
    string? CurrentStepCode,
    string? CurrentStepName,
    int? CurrentStepSequence);

/// <summary>Revisão na lista "Revisões" (CA 15).</summary>
public sealed record BomRevisionSummary(Guid Id, int Revision, BomStatus Status, int LineCount, DateTimeOffset CreatedAt, DateTimeOffset? ReleasedAt, string? ReleasedByName);

/// <summary>Aba BOM da OS: a liberada vigente, o rascunho (se houver) e todas as revisões.</summary>
public sealed record BomOrderView(BomOrderHeader Order, BomDetail? Released, BomDetail? Draft, IReadOnlyList<BomRevisionSummary> Revisions);

/// <summary>
/// Linha da prévia da liberação (CA 8, 13, 14): necessidade (antes → depois na revisão), disponível, vai reservar, falta e a liberar; o
/// baixado só a partir da EST-02 (<c>Issued</c> nulo até lá, e o aviso fica oculto).
/// </summary>
public sealed record BomReleasePreviewLine(
    Guid ItemId,
    string ItemCode,
    string ItemName,
    string StockUnitCode,
    bool ItemActive,
    BomChange Change,
    decimal PreviousRequired,
    decimal Required,
    decimal MoreToReserve,
    decimal ToRelease,
    decimal? Available,
    decimal? ToReserve,
    decimal? Shortage,
    decimal? Issued,
    string? IssuedWarning);

/// <summary>
/// Prévia do diálogo de impacto da liberação (padrão 4); <c>Empty</c>: "BOM sem material" (CA 9). <c>ReservationsConnected</c> false
/// (até a EST-02): "vai reservar" vem nulo.
/// </summary>
public sealed record BomReleasePreview(
    Guid BomId,
    int Revision,
    bool IsRevision,
    int? PreviousRevision,
    bool Empty,
    bool StockConnected,
    bool ReservationsConnected,
    int ShortageCount,
    int InactiveCount,
    IReadOnlyList<BomReleasePreviewLine> Lines);

/// <summary>Linha da comparação lado a lado (CA 15): o item nas duas revisões (nulo onde não está).</summary>
public sealed record BomCompareLine(
    Guid ItemId,
    string ItemCode,
    string ItemName,
    string StockUnitCode,
    BomChange Change,
    decimal? FromQuantity,
    string? FromUnitCode,
    decimal? FromScrapPct,
    decimal? FromRequired,
    decimal? ToQuantity,
    string? ToUnitCode,
    decimal? ToScrapPct,
    decimal? ToRequired);

public sealed record BomComparison(Guid FromBomId, int FromRevision, Guid ToBomId, int ToRevision, IReadOnlyList<BomCompareLine> Lines);

/// <summary>
/// Situação da BOM na lista Engenharia → BOMs (CA 17). <c>Situation</c>: <c>will_stop</c> (OS que vão parar sem BOM), <c>without</c>,
/// <c>draft</c>, <c>released</c> ou vazio (todas as OS em aberto).
/// </summary>
public sealed record BomListQuery(string? Situation, string? Q);

/// <summary>Situação da BOM de uma OS na lista.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BomSituation>))]
public enum BomSituation
{
    None,
    Draft,
    Released,
}

/// <summary>Linha da lista de BOMs: a OS, a etapa atual, a situação da BOM e se a OS vai parar sem BOM (CA 17).</summary>
public sealed record BomOrderSummary(
    Guid ProductionOrderId,
    string Number,
    ProductionOrderState OrderStatus,
    string? ProductCode,
    string? ProductName,
    decimal Quantity,
    string? CurrentStepCode,
    string? CurrentStepName,
    int? CurrentStepSequence,
    int StepCount,
    BomSituation Situation,
    Guid? DraftBomId,
    int DraftLineCount,
    uint? DraftVersion,
    Guid? ReleasedBomId,
    int? ReleasedRevision,
    DateTimeOffset? ReleasedAt,
    bool WillStop);

/// <summary>Item para a linha da BOM (CA 2): ativo, nem Serviço nem o produto da OS, com as unidades usáveis e o disponível.</summary>
public sealed record BomItemOption(Guid Id, string Code, string Name, ItemType Type, Guid StockUnitId, string StockUnitCode, IReadOnlyList<ItemUnitOption> Units, decimal? Available);

/// <summary>Origem da cópia (CA 5): outra OS com BOM liberada; "Sugerido" na última do mesmo produto (R-PRP-1).</summary>
public sealed record BomCopySource(Guid BomId, Guid ProductionOrderId, string OrderNumber, int Revision, string? ProductCode, string? ProductName, int LineCount, DateTimeOffset ReleasedAt, bool Suggested);
