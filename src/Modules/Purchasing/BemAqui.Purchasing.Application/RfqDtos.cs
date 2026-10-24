using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;

namespace BemAqui.Purchasing.Application;

/// <summary>Item digitado na cotação sem SC (CA 1, compra pontual): item ativo, quantidade na unidade de estoque ou noutra com conversão.</summary>
public sealed record RfqLineInput(Guid ItemId, decimal Quantity, Guid? UnitId);

/// <summary>
/// "Criar cotação" (CA 1): as linhas de SC <c>Open</c> (somadas por item) ou os itens digitados, nunca os dois juntos; os fornecedores
/// (terceiros ativos com o papel Fornecedor, sem repetir) e a observação.
/// </summary>
public sealed record RfqInput(IReadOnlyList<Guid>? RequestLineIds, IReadOnlyList<RfqLineInput>? Lines, IReadOnlyList<Guid>? SupplierIds, string? Notes);

/// <summary>Incluir fornecedor (CA 2), com a versão da cotação.</summary>
public sealed record RfqSupplierInput(Guid PartyId, uint Version);

/// <summary>Comando que só leva a versão da cotação (remover fornecedor, "Não cotou", gerar pedidos).</summary>
public sealed record RfqVersionInput(uint Version);

/// <summary>
/// Preço de uma linha (CA 3): o preço unitário na unidade de estoque ou, com <c>InSupplierUnit</c>, na do fornecedor (convertido pelo fator
/// do <c>item_supplier_codes</c>); a quantidade disponível (padrão a pedida) e o prazo em dias.
/// </summary>
public sealed record RfqQuoteInput(Guid LineId, decimal UnitPrice, bool InSupplierUnit, decimal? AvailableQuantity, int LeadDays);

/// <summary>"Salvar resposta" (CA 3): por fornecedor, frete, IPI %, condição (padrão a do fornecedor), validade e os preços; com a versão.</summary>
public sealed record RfqResponseInput(
    decimal Freight, decimal IpiPct, Guid? PaymentTermId, DateOnly? ValidUntil, string? Notes, IReadOnlyList<RfqQuoteInput>? Quotes, uint Version);

/// <summary>Vencedor de uma linha (CA 8): o fornecedor da cotação e, quando não é o de menor custo, o motivo (Outro exige o texto).</summary>
public sealed record RfqSelectionInput(Guid LineId, Guid SupplierId, RfqSelectionReason? Reason, string? Note);

/// <summary>"Concluir cotação" (CA 8): o vencedor de cada linha cotada e a versão.</summary>
public sealed record RfqCloseInput(IReadOnlyList<RfqSelectionInput>? Selections, uint Version);

/// <summary>"Cancelar cotação" (CA 12): motivo obrigatório e a versão.</summary>
public sealed record RfqCancelInput(string? Reason, uint Version);

/// <summary>Linha de SC de origem da linha da cotação (link para a SC).</summary>
public sealed record RfqRequestRef(Guid RequestId, string Number, Guid LineId, decimal Quantity);

/// <summary>"Última compra: {preço} em {data} de {fornecedor}" (CA 7, CMP-04): referência por item.</summary>
public sealed record RfqLastPurchase(decimal UnitPrice, DateTimeOffset ApprovedAt, string PartyName, string OrderNumber);

/// <summary>Unidade do fornecedor para um item (CA 3): 1 {unidade} = {fator} na unidade de estoque.</summary>
public sealed record RfqSupplierUnit(Guid ItemId, string Unit, decimal Factor);

/// <summary>Preço de um fornecedor numa linha, com o total da linha e o custo total da linha (frete rateado e IPI), e a marca de menor custo.</summary>
public sealed record RfqQuoteView(
    Guid LineId, decimal UnitPrice, decimal? SupplierUnitPrice, string? SupplierUnit, decimal AvailableQuantity, int LeadDays, decimal LineTotal, decimal LandedCost, bool IsCheapest);

/// <summary>
/// Fornecedor da cotação (CA 2 a 7): a resposta, quem lançou e quando (CA 5), os totais (subtotal, frete, IPI, custo total), o maior
/// prazo, se o anexo é exigido e se já existe (CA 6), o pedido gerado (CA 9) e o resultado do envio (CA 10).
/// </summary>
public sealed record RfqSupplierView(
    Guid Id, Guid PartyId, string PartyName, int Sequence, bool Responded, bool Declined, Guid? RespondedBy, string? RespondedByName, DateTimeOffset? RespondedAt, decimal Freight,
    decimal IpiPct, Guid? PaymentTermId, string? PaymentTermName, DateOnly? ValidUntil, string? Notes, decimal Subtotal, decimal IpiAmount, decimal Total, int? MaxLeadDays,
    bool CoversAll, bool IsCheapest, bool AttachmentRequired, int AttachmentsCount, Guid? PurchaseOrderId, string? PurchaseOrderNumber, DateTimeOffset? OrderSubmittedAt,
    string? OrderSubmitError, IReadOnlyList<RfqQuoteView> Quotes, IReadOnlyList<RfqSupplierUnit> SupplierUnits);

/// <summary>
/// Linha da cotação (CA 1, 7, 8): item, quantidade, as SC de origem, o de menor custo (sugerido), o vencedor e o motivo, e a última compra.
/// </summary>
public sealed record RfqLineView(
    Guid Id, int Sequence, Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal Quantity, IReadOnlyList<RfqRequestRef> Requests, Guid? CheapestSupplierId,
    Guid? SelectedSupplierId, Guid? SuggestedSupplierId, RfqSelectionReason? SelectionReason, string? SelectionNote, RfqLastPurchase? LastPurchase);

/// <summary>Prévia de "Gerar pedidos de compra" (CA 9): um pedido por fornecedor vencedor, com as linhas e o total; o pedido já gerado, quando há.</summary>
public sealed record RfqOrderPreview(
    Guid SupplierId, Guid PartyId, string PartyName, int Lines, decimal Total, Guid? PurchaseOrderId, string? PurchaseOrderNumber, PurchaseOrderStatus? PurchaseOrderStatus,
    decimal? PurchaseOrderTotal);

/// <summary>
/// Detalhe da cotação (CA 1 a 15): cabeçalho, linhas, fornecedores com as respostas, o comparativo (menor custo por linha e o menor total
/// geral), o aviso "1 cotação", a prévia dos pedidos e o que o usuário pode fazer agora.
/// </summary>
public sealed record RfqDetail(
    Guid Id, string Number, RfqStatus Status, string? Notes, Guid? CreatedBy, string? CreatedByName, DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt, string? ClosedByName,
    string? CancelReason, DateTimeOffset? CancelledAt, string? CancelledByName, IReadOnlyList<RfqLineView> Lines, IReadOnlyList<RfqSupplierView> Suppliers, int QuotesCount,
    Guid? CheapestOverallSupplierId, string? SingleQuoteWarning, decimal AttachmentLimit, IReadOnlyList<RfqOrderPreview> Orders, bool CanEdit, bool CanClose, bool CanCancel,
    bool CanGenerateOrders, bool CanSubmitOrders, uint Version);

/// <summary>Linha da lista "Cotações".</summary>
public sealed record RfqSummary(
    Guid Id, string Number, RfqStatus Status, string ItemSummary, int LineCount, int SupplierCount, int RespondedCount, DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt);

/// <summary>Filtros da lista (na URL): <c>Show</c> = <c>abertas</c> (padrão) ou <c>todas</c>; a situação e o número.</summary>
public sealed record RfqListQuery(string? Show, RfqStatus? Status, string? Term);

/// <summary>Fornecedor para a cotação (CA 1): só com o papel Fornecedor e ativo; "Sugerido" = um dos últimos fornecedores dos itens (R-PRP-1).</summary>
public sealed record RfqSupplierOption(Guid Id, string Name, string? Document, Guid? DefaultPaymentTermId, bool Suggested);

/// <summary>Item da "Cotar" (CA 1): a quantidade somada das linhas de SC, as linhas e as SC, e a última compra.</summary>
public sealed record RfqDefaultLine(PurchaseItemOption Item, decimal Quantity, IReadOnlyList<Guid> RequestLineIds, IReadOnlyList<string> RequestNumbers, RfqLastPurchase? LastPurchase);

/// <summary>"Cotar" preenchido (CA 1, propositivo): os itens das SC e os fornecedores sugeridos (os últimos desses itens, até 3).</summary>
public sealed record RfqDefaults(IReadOnlyList<RfqDefaultLine> Lines, IReadOnlyList<RfqSupplierOption> Suppliers);

/// <summary>Pedido gerado ou já existente da cotação (CA 9).</summary>
public sealed record RfqOrderView(Guid PurchaseOrderId, string Number, Guid PartyId, string PartyName, PurchaseOrderStatus Status, decimal Total, bool Created);

/// <summary>"Gerar pedidos de compra" (CA 9): os pedidos da cotação e quantos nasceram agora.</summary>
public sealed record RfqOrdersResult(IReadOnlyList<RfqOrderView> Orders, int Created);

/// <summary>Resultado de um pedido no "Enviar todos para aprovação" (CA 10): enviado com o nível e quem aprova, ou não enviado com o motivo.</summary>
public sealed record RfqSubmitItem(Guid PurchaseOrderId, string Number, string PartyName, decimal Total, bool Sent, int? Levels, string? ApproverName, string? Code, string? Message);

/// <summary>"Pedidos enviados para aprovação ({x} de {n})" (CA 10).</summary>
public sealed record RfqSubmitResult(IReadOnlyList<RfqSubmitItem> Items, int Sent, int Total, string Message);
