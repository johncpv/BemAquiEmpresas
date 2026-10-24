using System.Text.Json.Serialization;
using BemAqui.Purchasing.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Purchasing.Domain;

/// <summary>
/// Pedido de compra (<c>purchasing.purchase_orders</c>, CMP-03a): número <c>PC-AAAA-0001</c>, fornecedor, destino (projeto ou estoque
/// geral, R-CMP-6), condição de pagamento, data prevista de entrega e os totais recalculados pelo servidor (subtotal + frete + IPI). A
/// alçada é resolvida a cada envio (<see cref="Attempt"/>); os títulos previstos nascem na aprovação (ADR 0024 §1). Nunca é apagado.
/// </summary>
[DocumentName("O pedido")]
public sealed class PurchaseOrder : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; init; } = string.Empty;

    public Guid PartyId { get; set; }

    /// <summary>Cotação de origem (CMP-02); null no pedido manual ou da SC.</summary>
    public Guid? RfqId { get; init; }

    /// <summary>Respostas recebidas na cotação (aviso "1 cotação", D8); null sem cotação.</summary>
    public int? QuotesCount { get; init; }

    /// <summary>Destino: o projeto; null = estoque geral.</summary>
    public Guid? ProjectId { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    public Guid PaymentTermId { get; set; }

    /// <summary>Data prevista de entrega: base das datas estimadas das parcelas (ADR 0019 §17).</summary>
    public DateOnly ExpectedDate { get; set; }

    public decimal Subtotal { get; set; }

    public decimal Freight { get; set; }

    /// <summary>Percentual do IPI sobre o subtotal (<c>numeric(7,4)</c>).</summary>
    public decimal IpiPct { get; set; }

    public decimal IpiAmount { get; set; }

    public decimal Total { get; set; }

    public string? Notes { get; set; }

    /// <summary>Conta do plano dos títulos previstos (padrão de <c>purchasing.parameters</c>), gravada no envio.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Tentativa de aprovação atual (0 = nunca enviado); cada envio abre uma tentativa com os próprios níveis.</summary>
    public int Attempt { get; set; }

    /// <summary>Valor de referência da última resolução (fracionamento: pedidos do fornecedor em 30 dias, CA 5 e 6).</summary>
    public decimal? ReferenceAmount { get; set; }

    /// <summary>Aprovado pelo próprio solicitante como Proprietário único (<c>SoleOwnerRule</c>, marca "Autoaprovado").</summary>
    public bool SelfApproved { get; set; }

    /// <summary>Editado depois do envio ao fornecedor (CA 15): a faixa "Alterado depois do envio: reenvie ao fornecedor.".</summary>
    public bool ChangedAfterSent { get; set; }

    /// <summary>Comentário da última reprovação (CA 14).</summary>
    public string? RejectionComment { get; set; }

    public Guid? RequestedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Linha do pedido (<c>purchasing.purchase_order_lines</c>): item, quantidade na unidade de estoque (<c>numeric(18,4)</c>), preço
/// unitário (<c>numeric(18,6)</c>), total da linha (2 casas), natureza de custo e as linhas de SC que ela atende.
/// </summary>
[DocumentName("A linha do pedido")]
public sealed class PurchaseOrderLine : Entity, IAuditable
{
    public Guid OrderId { get; init; }

    public int Sequence { get; set; }

    public Guid ItemId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Quantidade e unidade digitadas, quando não eram as de estoque.</summary>
    public decimal? EnteredQuantity { get; set; }

    public Guid? EnteredUnitId { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    /// <summary>Natureza de custo do projeto (CA 4): <c>Material</c> nos itens.</summary>
    public CostType CostType { get; set; } = CostType.Material;

    /// <summary>Recebido (CMP-04); 0 nesta story.</summary>
    public decimal QuantityReceived { get; set; }

    /// <summary>Linhas de SC atendidas por esta linha (vínculo; voltam a <c>Open</c> no cancelamento).</summary>
    public Guid[] RequestLineIds { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<PurchaseLevelStatus>))]
public enum PurchaseLevelStatus
{
    Pending,
    Approved,
    Rejected,

    /// <summary>A tentativa foi encerrada sem decidir o nível (reprovação de outro nível, edição ou cancelamento).</summary>
    Closed,
}

/// <summary>
/// Nível resolvido no envio (<c>purchasing.purchase_order_levels</c>, ADR 0024 §4): por tentativa, com a regra, o aprovador, o valor de
/// referência e o aviso de fracionamento. Imutável depois de decidido ou encerrado.
/// </summary>
public sealed class PurchaseOrderLevel : Entity, IAuditable
{
    public Guid OrderId { get; init; }

    public int Attempt { get; init; }

    public int Level { get; init; }

    public Guid ApprovalRuleId { get; init; }

    public string ApproverName { get; init; } = string.Empty;

    public decimal ReferenceAmount { get; init; }

    public string? SplitWarning { get; init; }

    public PurchaseLevelStatus Status { get; set; } = PurchaseLevelStatus.Pending;
}

[JsonConverter(typeof(JsonStringEnumConverter<PurchaseDecision>))]
public enum PurchaseDecision
{
    Submitted,
    Approved,
    Rejected,
    Changed,
    Cancelled,
}

/// <summary>
/// Linha do tempo das decisões (<c>purchasing.purchase_order_approvals</c>, CA 23): envio, aprovação e reprovação por nível, edição que
/// voltou à aprovação e cancelamento, com a tentativa, a regra, o valor de referência e a marca "Autoaprovado". Imutável.
/// </summary>
public sealed class PurchaseOrderApproval : Entity, IAuditable
{
    public Guid OrderId { get; init; }

    public int Attempt { get; init; }

    public int Level { get; init; }

    public PurchaseDecision Decision { get; init; }

    public Guid? ApprovalRuleId { get; init; }

    public decimal? ReferenceAmount { get; init; }

    public string? Comment { get; init; }

    public Guid? DecidedBy { get; init; }

    public DateTimeOffset DecidedAt { get; init; }

    public bool SelfApproved { get; init; }
}
