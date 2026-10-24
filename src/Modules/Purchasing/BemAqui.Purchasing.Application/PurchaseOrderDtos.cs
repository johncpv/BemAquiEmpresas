using BemAqui.Finance.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Purchasing.Application;

/// <summary>Padrões dos títulos do pedido (CA 19): conta (código do plano) e natureza dos itens e do frete.</summary>
public sealed record PurchasePayableDefaults(string AccountCode, CostType CostType, string FreightAccountCode, CostType FreightCostType);

/// <summary>
/// Linha do pedido (CA 1 e 3): item ativo, quantidade na unidade de estoque ou noutra com conversão, preço unitário (até 6 casas) e as
/// linhas de SC <c>Open</c> que ela atende (do mesmo item).
/// </summary>
public sealed record PurchaseOrderLineInput(Guid ItemId, decimal Quantity, Guid? UnitId, decimal UnitPrice, IReadOnlyList<Guid>? RequestLineIds);

/// <summary>
/// "Novo pedido" (CA 1) e a API da CMP-02 (CA 2): fornecedor com papel Fornecedor, destino (projeto em andamento ou estoque geral; com
/// linhas de SC, o destino vem delas), condição (padrão do fornecedor), data prevista (padrão hoje + prazo; com <c>LeadDays</c>, o
/// prazo da resposta), frete, IPI (%) e as linhas. <c>RfqId</c> e <c>QuotesCount</c> só na criação pela cotação.
/// </summary>
public sealed record PurchaseOrderInput(
    Guid PartyId, Guid? ProjectId, Guid? PaymentTermId, DateOnly? ExpectedDate, decimal Freight, decimal IpiPct, string? Notes, IReadOnlyList<PurchaseOrderLineInput>? Lines,
    Guid? RfqId = null, int? QuotesCount = null, int? LeadDays = null);

/// <summary>Editar o pedido (CA 3, 15): os mesmos campos e a versão (409 se mudou). Fora do rascunho, volta à aprovação.</summary>
public sealed record PurchaseOrderUpdateInput(
    Guid PartyId, Guid? ProjectId, Guid PaymentTermId, DateOnly ExpectedDate, decimal Freight, decimal IpiPct, string? Notes, IReadOnlyList<PurchaseOrderLineInput>? Lines, uint Version);

/// <summary>Editar uma linha (quantidade e preço) pelo pedido da URL (IDOR, CA 21), com a versão do pedido.</summary>
public sealed record PurchaseOrderLineUpdate(decimal Quantity, decimal UnitPrice, uint Version);

/// <summary>Enviar para aprovação, com a versão.</summary>
public sealed record PurchaseOrderVersionInput(uint Version);

/// <summary>Aprovar ou reprovar (CA 9, 14): comentário (obrigatório na reprovação) e a versão.</summary>
public sealed record PurchaseOrderDecisionInput(string? Comment, uint Version);

/// <summary>"Cancelar pedido" (CA 16): motivo obrigatório e a versão.</summary>
public sealed record PurchaseOrderCancelInput(string? Reason, uint Version);

public sealed record PurchaseOrderLineView(
    Guid Id, int Sequence, Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal Quantity, decimal? EnteredQuantity, string? EnteredUnitCode, decimal UnitPrice,
    decimal LineTotal, CostType CostType, decimal QuantityReceived, IReadOnlyList<Guid> RequestLineIds, IReadOnlyList<PurchaseOrderRequestRef> Requests);

/// <summary>Linha de SC atendida pela linha do pedido (link para a SC).</summary>
public sealed record PurchaseOrderRequestRef(Guid RequestId, string Number, Guid LineId);

/// <summary>Nível de aprovação de uma tentativa (CA 9, 23).</summary>
public sealed record PurchaseOrderLevelView(int Attempt, int Level, Guid RuleId, string ApproverName, decimal ReferenceAmount, string? SplitWarning, PurchaseLevelStatus Status);

/// <summary>Evento da linha do tempo de decisões (CA 23): envio, aprovação, reprovação, edição e cancelamento.</summary>
public sealed record PurchaseOrderDecisionView(
    int Attempt, int Level, PurchaseDecision Decision, string? Comment, Guid? DecidedBy, string? DecidedByName, DateTimeOffset DecidedAt, bool SelfApproved, decimal? ReferenceAmount,
    string? ApproverName);

/// <summary>Aba Decisões (CA 23): os níveis e a linha do tempo de todas as tentativas, imutáveis.</summary>
public sealed record PurchaseOrderDecisions(IReadOnlyList<PurchaseOrderLevelView> Levels, IReadOnlyList<PurchaseOrderDecisionView> Events);

/// <summary>Parcela prevista calculada pela condição (impacto no caixa do diálogo de aprovação, CA 12): número, data estimada e valor.</summary>
public sealed record PurchaseOrderInstallmentPreview(int Number, DateOnly EstimatedDueDate, decimal Amount);

/// <summary>
/// Detalhe do pedido (CA 3 a 23): cabeçalho, totais, linhas, os níveis da tentativa atual, os avisos (fracionamento, "1 cotação",
/// alterado depois do envio), as parcelas previstas pela condição e o que o usuário pode fazer agora.
/// </summary>
public sealed record PurchaseOrderDetail(
    Guid Id, string Number, PurchaseOrderStatus Status, Guid PartyId, string PartyName, Guid? ProjectId, string? ProjectNumber, string? ProjectName, Guid PaymentTermId,
    string? PaymentTermName, DateOnly ExpectedDate, int? DaysLate, decimal Subtotal, decimal Freight, decimal IpiPct, decimal IpiAmount, decimal Total, string? Notes, Guid? RfqId,
    int? QuotesCount, string? SingleQuoteWarning, Guid? RequestedBy, string? RequestedByName, DateTimeOffset CreatedAt, DateTimeOffset? SubmittedAt, DateTimeOffset? ApprovedAt,
    int Attempt, decimal? ReferenceAmount, string? SplitWarning, bool SelfApproved, bool ChangedAfterSent, string? RejectionComment, string? CancelReason,
    DateTimeOffset? CancelledAt, string? CancelledByName, decimal QuantityReceived, IReadOnlyList<PurchaseOrderLineView> Lines, IReadOnlyList<PurchaseOrderLevelView> Levels,
    IReadOnlyList<PurchaseOrderInstallmentPreview> Installments, bool IsRequester, bool CanEdit, bool CanSubmit, bool CanDecide, bool CanCancel, string? DecideBlocker, uint Version);

/// <summary>Linha da lista "Pedidos" (CA 20).</summary>
public sealed record PurchaseOrderSummary(
    Guid Id, string Number, PurchaseOrderStatus Status, Guid PartyId, string PartyName, Guid? ProjectId, string? ProjectNumber, string? ProjectName, decimal Total,
    DateOnly ExpectedDate, int? DaysLate, string ItemSummary, DateTimeOffset CreatedAt);

/// <summary>
/// Filtros da lista (na URL): <c>Show</c> = <c>aguardando-voce</c> (o que o usuário pode decidir agora; padrão de quem aprova),
/// <c>abertos</c> (não recebidos nem cancelados) ou <c>todos</c>.
/// </summary>
public sealed record PurchaseOrderListQuery(string? Show, PurchaseOrderStatus? Status, Guid? PartyId, Guid? ProjectId, string? Term);

/// <summary>Título previsto do pedido na aba Financeiro (com <c>finance.view</c>).</summary>
public sealed record PurchaseOrderPayableView(Guid Id, string Number, short Installment, PayableStatus Status, decimal Amount, DateOnly? DueDate, bool IsEstimate, Guid? PaymentOrderId);

/// <summary>Fornecedor para o pedido: só com o papel Fornecedor e ativo, com a condição padrão (R-PRP-1).</summary>
public sealed record PurchaseSupplierOption(Guid Id, string Name, string? Document, Guid? DefaultPaymentTermId);

public sealed record PurchasePaymentTermOption(Guid Id, string Name, IReadOnlyList<Installment> Installments);

/// <summary>
/// Linha sugerida do "Novo pedido" (propositivo, CA 1): o item, a quantidade das linhas de SC, o último preço pago (ou o custo médio)
/// com a marca "Sugerido" e as linhas de SC atendidas.
/// </summary>
public sealed record PurchaseOrderDefaultLine(
    PurchaseItemOption Item, decimal Quantity, decimal? UnitPrice, bool PriceSuggested, string? PriceSource, IReadOnlyList<Guid> RequestLineIds, IReadOnlyList<string> RequestNumbers);

/// <summary>
/// "Novo pedido" já preenchido (CA 1): o fornecedor sugerido (o último do item), a condição padrão dele, a data prevista (hoje + prazo
/// padrão), o destino pelas SC e as linhas.
/// </summary>
public sealed record PurchaseOrderDefaults(
    Guid? PartyId, string? PartyName, bool PartySuggested, Guid? PaymentTermId, string? PaymentTermName, DateOnly ExpectedDate, int LeadDays, Guid? ProjectId, string? ProjectNumber,
    string? ProjectName, IReadOnlyList<PurchaseOrderDefaultLine> Lines);
