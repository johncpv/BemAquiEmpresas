using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Domain;

/// <summary>
/// Romaneio de expedição (<c>production.shipments</c>, PRD-06b, R-PRD-8): o que sai da fábrica para a obra do projeto, com o número
/// <c>ROM-AAAA-0001</c> (D15), o endereço de entrega copiado, a transportadora, a placa e o motorista (dado pessoal, mascarado no diff),
/// volumes, peso e a data prevista. Nasce <c>Draft</c>; a confirmação baixa o produto acabado de forma síncrona (D4, ADR 0023 §1) e vai a
/// <c>Shipped</c>; com produto do estoque geral passa antes por <c>AwaitingStockApproval</c> (outra pessoa com <c>inventory.approve</c>
/// confirma). Projeto, cliente, transportadora e itens são de outros módulos: só o id, validado pelos Contracts dos donos (ADR 0006).
/// Não é documento fiscal: nenhum valor nem custo aqui.
/// </summary>
[DocumentName("O romaneio")]
public sealed class Shipment : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; set; } = string.Empty;

    public Guid ProjectId { get; init; }

    /// <summary>Cliente do projeto (copiado na montagem).</summary>
    public Guid PartyId { get; init; }

    /// <summary>Endereço de entrega copiado (o da obra do projeto por padrão, editável).</summary>
    public string DeliveryAddress { get; set; } = string.Empty;

    /// <summary>Transportadora (terceiro com o papel Transportadora), opcional.</summary>
    public Guid? CarrierPartyId { get; set; }

    [PersonalData]
    public string? VehiclePlate { get; set; }

    [PersonalData]
    public string? DriverName { get; set; }

    public int? Volumes { get; set; }

    /// <summary>Peso bruto em kg, <c>numeric(18,4)</c>.</summary>
    public decimal? GrossWeight { get; set; }

    public DateOnly? PlannedDate { get; set; }

    public ShipmentState Status { get; set; } = ShipmentState.Draft;

    /// <summary>Quem montou o romaneio (não confirma o produto do estoque geral, CA 6).</summary>
    public Guid? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Pedido de confirmação do estoque geral (CA 6): quem pediu e quando.</summary>
    public Guid? StockApprovalRequestedBy { get; set; }

    public DateTimeOffset? StockApprovalRequestedAt { get; set; }

    /// <summary>Quem confirmou o produto do estoque geral (outra pessoa com <c>inventory.approve</c>).</summary>
    public Guid? StockApprovedBy { get; set; }

    /// <summary>Marca "Autoaprovado" do Proprietário único (ADR 0023 §5).</summary>
    public bool SelfApproved { get; set; }

    public DateTimeOffset? ShippedAt { get; set; }

    public Guid? ShippedBy { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset? ReversedAt { get; set; }

    public Guid? ReversedBy { get; set; }

    public string? ReversalReason { get; set; }

    /// <summary>PDF guardado na confirmação, como arquivo do sistema (dono <c>Shipment</c>, ADR 0019 §7 adendo).</summary>
    public Guid? PdfAttachmentId { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Linha do romaneio (<c>production.shipment_lines</c>): item, quantidade (<c>numeric(18,4)</c>), local de saída, lote e a marca
/// "Estoque geral" (produto acabado sem projeto, CA 2 e 6). <c>MovementId</c> é a saída <c>Shipment</c> lançada na confirmação, com a chave
/// <c>(Shipment, romaneioId, Sequence, Shipment)</c>.
/// </summary>
public sealed class ShipmentLine : Entity, IAuditable
{
    public Guid ShipmentId { get; init; }

    public int Sequence { get; init; }

    public Guid ItemId { get; init; }

    public decimal Quantity { get; init; }

    public Guid LocationId { get; init; }

    public Guid? LotId { get; init; }

    /// <summary>Número do lote copiado na montagem (tela e PDF).</summary>
    public string? LotNumber { get; init; }

    public bool FromGeneralStock { get; init; }

    public Guid? MovementId { get; set; }
}

/// <summary>
/// Série de uma linha do romaneio (<c>production.shipment_line_serials</c>). Índice único parcial de <c>serial_id</c> enquanto
/// <c>released_at</c> é nulo: a mesma série não fica em dois romaneios ao mesmo tempo (CA 2 e 16); cancelar ou estornar o romaneio libera a
/// série. O número é copiado para a lista e o PDF.
/// </summary>
public sealed class ShipmentLineSerial : Entity, IAuditable
{
    public Guid ShipmentId { get; init; }

    public Guid LineId { get; init; }

    public Guid SerialId { get; init; }

    public string SerialNumber { get; init; } = string.Empty;

    public DateTimeOffset? ReleasedAt { get; set; }
}
