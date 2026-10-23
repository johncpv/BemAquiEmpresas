using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Contracts;

/// <summary>
/// Tipo do movimento de estoque (<c>inventory.stock_movements.type</c>, ADR 0016 e 0023): entrada, saída, transferência, ajuste,
/// devolução, produção, expedição e saldo inicial.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StockMovementType>))]
public enum StockMovementType
{
    Receipt,
    Issue,
    Transfer,
    Adjustment,
    Return,
    Production,
    Shipment,
    OpeningBalance,
}

/// <summary>Origem do documento que gera o movimento (<c>source_type</c>); o lançamento manual é do próprio estoque.</summary>
public static class StockSources
{
    public const string ManualPosting = "ManualPosting";
    public const string Adjustment = "StockAdjustment";
    public const string Requisition = "MaterialRequisition";
    public const string GoodsReceipt = "GoodsReceipt";
    public const string ProductionOrder = "ProductionOrder";
    public const string Shipment = "Shipment";
    public const string InventoryCount = "InventoryCount";
}

/// <summary>
/// Uma perna de movimento pedida ao motor (ADR 0023 §1 e §4): quantidade com sinal e na unidade de estoque (entrada positiva,
/// saída negativa); custo unitário só nas entradas (a saída usa o custo médio do momento). A chave de idempotência é
/// <c>(SourceType, SourceId, Leg, Type)</c>. <c>Number</c> nulo gera um número <c>MOV</c> novo; as pernas do mesmo lançamento
/// repetem o número da primeira. <c>At</c> é a data de negócio (nulo = agora). <c>SourceNumber</c> é o número legível do
/// documento de origem (ex.: <c>REQ-2026-0001</c>), para o link e a mensagem "Estorne pelo documento de origem".
/// </summary>
public sealed record StockPosting(
    StockMovementType Type,
    Guid ItemId,
    Guid LocationId,
    Guid? LotId,
    decimal Quantity,
    decimal? UnitCost,
    string SourceType,
    Guid SourceId,
    int Leg = 1,
    string? Number = null,
    Guid? ProjectId = null,
    Guid? ProductionOrderId = null,
    DateTimeOffset? At = null,
    string? Reason = null,
    Guid? SerialId = null,
    string? SourceNumber = null);

/// <summary>
/// Estorno de um movimento pelo documento de origem (ADR 0023 §4): só o dono da origem estorna (<c>SourceType</c> e
/// <c>SourceId</c> conferem com o original). Nasce o movimento inverso, pelo custo do original.
/// </summary>
public sealed record StockReversal(Guid MovementId, string SourceType, Guid SourceId, string? Reason = null, string? Number = null);

/// <summary>Movimento gravado (ou já existente, <c>Replayed</c>), com o físico da posição e o custo médio depois dele.</summary>
public sealed record StockMovementResult(Guid MovementId, string Number, decimal BalanceAfter, decimal AverageCostAfter, bool Replayed);

/// <summary>Custo de material de uma OS por item (ADR 0025 §2): baixas menos devoluções e estornos, lido dos movimentos.</summary>
public sealed record OrderMaterialCostLine(Guid ItemId, decimal Quantity, decimal Amount);

public sealed record OrderMaterialCost(Guid ProductionOrderId, IReadOnlyList<OrderMaterialCostLine> Lines, decimal Total);

/// <summary>
/// Motor de estoque (ADR 0023 §1 a §4): o efeito de estoque de um documento é esta chamada síncrona, na transação do documento.
/// Exige transação aberta (<see cref="InvalidOperationException"/> sem ela); trava item → posição (item, local) → lote; falta de
/// saldo devolve 409 <c>insufficient_stock</c> e o chamador desfaz a transação. Saída ou devolução com projeto publica
/// <see cref="StockIssued"/> ou <see cref="StockReturned"/> no outbox.
/// </summary>
public interface IStockMovements
{
    Task<Result<StockMovementResult>> PostAsync(StockPosting posting, CancellationToken cancellationToken = default);

    /// <summary>Várias pernas do mesmo documento, travadas em ordem crescente de <c>item_id</c> (sem deadlock entre documentos).</summary>
    Task<Result<IReadOnlyList<StockMovementResult>>> PostManyAsync(IReadOnlyList<StockPosting> postings, CancellationToken cancellationToken = default);

    Task<Result<StockMovementResult>> ReverseAsync(StockReversal reversal, CancellationToken cancellationToken = default);

    /// <summary>Lote do item pelo número, criado na primeira entrada (único por item e número, também em paralelo).</summary>
    Task<Guid> EnsureLotAsync(Guid itemId, string lotNumber, Guid? supplierPartyId = null, DateOnly? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>Custo de material da OS (PRD-06a2), sem depender da permissão de quem chama e sem passar pelo Commercial.</summary>
    Task<OrderMaterialCost> CostByOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);
}

/// <summary>Físico, reservado e disponível (físico − reservado) por item e local, lidos da posição; sem custo.</summary>
public sealed record StockAvailability(Guid ItemId, Guid LocationId, decimal OnHand, decimal Reserved, decimal Available);

/// <summary>Saldos para ENG-02, EST-02, CMP-01 e EST-06 (ADR 0023 §2): uma consulta para a lista toda.</summary>
public interface IStockBalances
{
    Task<IReadOnlyList<StockAvailability>> GetAsync(IReadOnlyCollection<Guid> itemIds, Guid? locationId = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Saída de material com projeto (ADR 0025 §2): valor anterior e novo da fonte, para o Commercial recompor o realizado sem
/// depender da ordem dos eventos. Lançamento <c>Seq</c> 1 de 0 → total; estorno <c>Seq</c> 2 de total → 0. Sem dado pessoal.
/// <c>MovementId</c> é o do movimento de saída original, também no estorno.
/// </summary>
public sealed record StockIssued(
    Guid MovementId, string SourceType, Guid SourceId, int Seq, Guid ProjectId, Guid? ProductionOrderId, Guid ItemId, decimal Quantity,
    decimal PreviousAmount, decimal NewAmount, DateTimeOffset At) : IIntegrationEvent;

/// <summary>Devolução de material com projeto (EST-03), com o mesmo formato de <see cref="StockIssued"/>.</summary>
public sealed record StockReturned(
    Guid MovementId, string SourceType, Guid SourceId, int Seq, Guid ProjectId, Guid? ProductionOrderId, Guid ItemId, decimal Quantity,
    decimal PreviousAmount, decimal NewAmount, DateTimeOffset At) : IIntegrationEvent;

/// <summary>
/// Custo médio atual por item (<c>numeric(18,6)</c>, ADR 0023 §3), para o custo previsto da BOM (ENG-02), que só aparece para quem tem
/// <c>projects.view_cost</c> e nunca é gravado. Item que não existe fica fora do dicionário.
/// </summary>
public interface IItemCosts
{
    Task<IReadOnlyDictionary<Guid, decimal>> AverageCostsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);
}
