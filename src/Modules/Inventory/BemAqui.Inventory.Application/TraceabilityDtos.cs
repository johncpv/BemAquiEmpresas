using System.Text.Json.Serialization;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Cliente na rastreabilidade (CA 9): o nome (ou as iniciais, PF sem <c>parties.view</c>) e o tipo de pessoa. Nunca o documento. O id só
/// para quem tem <c>parties.view</c> (o link para o terceiro).
/// </summary>
public sealed record TraceCustomer(Guid? PartyId, string Name, PartyKind Kind, bool Masked);

/// <summary>Série encontrada pela busca (CA 1, 10).</summary>
public sealed record TraceSerialHit(Guid Id, Guid ItemId, string SerialNumber, string ItemCode, string ItemName, SerialState Status);

/// <summary>Lote encontrado pela busca (CA 3, 10).</summary>
public sealed record TraceLotHit(Guid Id, Guid ItemId, string LotNumber, string ItemCode, string ItemName, DateOnly? Expiry);

/// <summary>
/// Resultado da busca "Série ou lote" (CA 10): exata ou por prefixo, até 50 de cada, os exatos primeiro. <c>LooksLikeSerial</c> diz se o
/// termo tem o formato da série (D15), para a tela mostrar as séries primeiro.
/// </summary>
public sealed record TraceSearchResult(string Term, bool LooksLikeSerial, IReadOnlyList<TraceSerialHit> Serials, IReadOnlyList<TraceLotHit> Lots);

/// <summary>Entrada do lote (recebimento, entrada manual, saldo inicial, produção): documento, data e quantidade, sem custo.</summary>
public sealed record TraceEntry(Guid MovementId, string SourceType, Guid SourceId, string? Number, string Label, DateTimeOffset At, decimal Quantity, bool Reversed);

/// <summary>Lote de matéria-prima consumido pela OS da série (CA 1): item, lote, quantidade, fornecedor, entradas e validade.</summary>
public sealed record TraceConsumedLot(
    Guid ItemId, string ItemCode, string ItemName, string UnitCode, Guid? LotId, string? LotNumber, decimal Quantity, string? SupplierName, DateOnly? Expiry,
    LotExpiryState ExpiryState, IReadOnlyList<TraceEntry> Entries);

/// <summary>
/// Série → origem (CA 1 e 2): o item, a situação, o local, a OS de origem e a conclusão, o projeto e o cliente (CA 9), o romaneio e os lotes
/// de matéria-prima consumidos pela OS inteira, com a observação quando a OS tem mais de uma unidade. Sem custo.
/// </summary>
public sealed record SerialTrace(
    Guid SerialId, string SerialNumber, SerialState Status, Guid ItemId, string ItemCode, string ItemName, Guid? LocationId, string? LocationName, DateTimeOffset CreatedAt,
    Guid? ProductionOrderId, string? ProductionOrderNumber, ProductionOrderState? ProductionOrderStatus, DateTimeOffset? CompletedAt, int OrderUnits, Guid? ProjectId,
    string? ProjectNumber, string? ProjectName, TraceCustomer? Customer, Guid? ShipmentId, string? ShipmentNumber, DateTimeOffset? ShippedAt, string? SharedLotsNote,
    IReadOnlyList<TraceConsumedLot> Lots);

/// <summary>Série produzida por uma OS que consumiu o lote, com o item (o link para a origem dela) e o romaneio quando expedida.</summary>
public sealed record TraceSerial(Guid Id, Guid ItemId, string SerialNumber, SerialState Status, Guid? ShipmentId, string? ShipmentNumber, DateTimeOffset? ShippedAt);

/// <summary>Consumo do lote por uma OS (CA 3): OS, projeto, cliente, data, quantidade líquida, as séries e a nota de sem série.</summary>
public sealed record TraceConsumption(
    Guid ProductionOrderId, string? OrderNumber, ProductionOrderState? OrderStatus, Guid? ProjectId, string? ProjectNumber, string? ProjectName, TraceCustomer? Customer,
    DateTimeOffset At, decimal Quantity, IReadOnlyList<TraceSerial> Serials, string? NoSerialNote, string? SharedLotsNote);

/// <summary>Linha que não é entrada nem consumo de OS (ajuste, saída avulsa, expedição, inventário), com a explicação (CA 3).</summary>
public sealed record TraceOtherLine(
    Guid MovementId, string Number, StockMovementType Type, string SourceType, Guid SourceId, string? SourceNumber, DateTimeOffset At, decimal Quantity, string Explanation);

/// <summary>Saldo do lote num local.</summary>
public sealed record TraceLotBalance(Guid LocationId, string LocationName, decimal OnHand);

/// <summary>
/// Lote → destino (CA 3): fornecedor, entradas, saldo por local, cada consumo por OS com as séries e os romaneios, as outras linhas e a
/// conciliação (recebido − consumido + outras = saldo). Sem custo do lote nem do projeto.
/// </summary>
public sealed record LotTrace(
    Guid LotId, string LotNumber, Guid ItemId, string ItemCode, string ItemName, string UnitCode, DateOnly? Expiry, LotExpiryState ExpiryState, int? DaysToExpiry,
    Guid? SupplierPartyId, string? SupplierName, DateTimeOffset? ReceivedAt, IReadOnlyList<TraceEntry> Entries, decimal TotalReceived, IReadOnlyList<TraceLotBalance> Balances,
    decimal OnHand, IReadOnlyList<TraceConsumption> Consumptions, decimal TotalConsumed, IReadOnlyList<TraceOtherLine> Others, decimal TotalOther, bool Balanced);

/// <summary>
/// Lote na aba "Lotes" do item (CA 5): fornecedor, entrada, validade com a situação, saldo e locais; <c>HasOutflow</c> diz se o número ainda
/// pode ser corrigido (CA 6).
/// </summary>
public sealed record ItemLotRow(
    Guid Id, Guid ItemId, string LotNumber, Guid? SupplierPartyId, string? SupplierName, DateTimeOffset? ReceivedAt, DateOnly? Expiry, LotExpiryState ExpiryState, int? DaysToExpiry,
    decimal OnHand, IReadOnlyList<TraceLotBalance> Balances, bool HasOutflow, string? CorrectionReason, DateTimeOffset? CorrectedAt);

/// <summary>
/// Correção do lote (CA 6): o número (igual ao atual se não muda) e a validade (nula = sem validade), com o motivo. Lote com saída só tem a
/// validade corrigida.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LotCorrectionInput(string LotNumber, DateOnly? Expiry, string Reason);
