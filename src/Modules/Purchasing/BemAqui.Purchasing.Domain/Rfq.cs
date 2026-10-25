using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Purchasing.Domain;

/// <summary>Situação da cotação (CMP-02, 04-fluxos §1): <c>Open → Closed</c> (concluída com os vencedores) ou <c>Cancelled</c>. Não reabre.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RfqStatus>))]
public enum RfqStatus
{
    Open,
    Closed,
    Cancelled,
}

/// <summary>Motivo da escolha quando o vencedor da linha não é o de menor custo total (CA 8).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RfqSelectionReason>))]
public enum RfqSelectionReason
{
    LeadTime,
    Quality,
    PaymentTerm,
    ApprovedSupplier,
    Other,
}

/// <summary>
/// Cotação (<c>purchasing.rfqs</c>, CMP-02): número <c>COT-AAAA-0001</c>, situação e quem concluiu ou cancelou. Sem efeito de estoque nem
/// financeiro: os pedidos nascem pelo serviço de pedido da CMP-03a. Toda mudança (fornecedor, resposta, conclusão) passa pela versão
/// (<c>xmin</c>) da cotação: duas gravações sobre a mesma versão dão 409, e a concluída não muda mais (CA 14).
/// </summary>
[DocumentName("A cotação")]
public sealed class Rfq : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; init; } = string.Empty;

    public RfqStatus Status { get; set; } = RfqStatus.Open;

    public string? Notes { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public Guid? ClosedBy { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Fornecedor da cotação (<c>purchasing.rfq_suppliers</c>): a resposta por fornecedor (frete, IPI %, condição, validade), quem lançou e
/// quando (CA 5, ADR 0024 §5), o "Não cotou", o pedido gerado (CA 9) e o resultado do envio para aprovação (CA 10, no histórico).
/// </summary>
public sealed class RfqSupplier : Entity, IAuditable
{
    public Guid RfqId { get; init; }

    public int Sequence { get; init; }

    public Guid PartyId { get; init; }

    public decimal Freight { get; set; }

    /// <summary>IPI % sobre o subtotal dos itens (<c>numeric(7,4)</c>, ponto 37).</summary>
    public decimal IpiPct { get; set; }

    public Guid? PaymentTermId { get; set; }

    public DateOnly? ValidUntil { get; set; }

    public string? Notes { get; set; }

    public bool Responded { get; set; }

    /// <summary>"Não cotou" (CA 3): o fornecedor respondeu que não tem ou não quis cotar.</summary>
    public bool Declined { get; set; }

    public Guid? RespondedBy { get; set; }

    public DateTimeOffset? RespondedAt { get; set; }

    /// <summary>Pedido gerado a partir do vencedor (CA 9); o último não cancelado.</summary>
    public Guid? PurchaseOrderId { get; set; }

    /// <summary>"Enviar todos para aprovação" (CA 10): quando o pedido deste fornecedor foi enviado.</summary>
    public DateTimeOffset? OrderSubmittedAt { get; set; }

    /// <summary>Motivo de o pedido não ter sido enviado na última tentativa (CA 10).</summary>
    public string? OrderSubmitError { get; set; }
}

/// <summary>
/// Linha da cotação (<c>purchasing.rfq_lines</c>): item e quantidade na unidade de estoque (soma das linhas de SC de origem), o vencedor
/// (<c>selected_supplier_id</c> = o fornecedor da cotação), o sugerido pelo menor custo total e o motivo quando a escolha foi outra (CA 8).
/// </summary>
public sealed class RfqLine : Entity, IAuditable
{
    public Guid RfqId { get; init; }

    public int Sequence { get; init; }

    public Guid ItemId { get; init; }

    public decimal Quantity { get; init; }

    public Guid? SelectedSupplierId { get; set; }

    /// <summary>O de menor custo total da linha na conclusão (padrão 6), para o histórico.</summary>
    public Guid? SuggestedSupplierId { get; set; }

    public RfqSelectionReason? SelectionReason { get; set; }

    public string? SelectionNote { get; set; }
}

/// <summary>Vínculo da linha da cotação com cada linha de SC de origem (<c>purchasing.rfq_line_requests</c>, CA 1).</summary>
public sealed class RfqLineRequest : Entity, IAuditable
{
    public Guid RfqId { get; init; }

    public Guid RfqLineId { get; init; }

    public Guid RequestLineId { get; init; }

    public decimal Quantity { get; init; }
}

/// <summary>
/// Preço de um fornecedor para uma linha (<c>purchasing.rfq_quotes</c>, CA 3): preço unitário na unidade de estoque (<c>numeric(18,6)</c>;
/// o digitado na unidade do fornecedor fica junto), quantidade disponível e prazo em dias.
/// </summary>
public sealed class RfqQuote : Entity, IAuditable
{
    public Guid RfqId { get; init; }

    public Guid RfqLineId { get; init; }

    public Guid RfqSupplierId { get; init; }

    public decimal UnitPrice { get; set; }

    /// <summary>Preço digitado na unidade do fornecedor (fator do <c>item_supplier_codes</c>); null quando veio na de estoque.</summary>
    public decimal? SupplierUnitPrice { get; set; }

    public string? SupplierUnit { get; set; }

    public decimal AvailableQuantity { get; set; }

    public int LeadDays { get; set; }
}
