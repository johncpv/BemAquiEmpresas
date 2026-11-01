using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>"Emitir NF-e" no romaneio ou na fila "A emitir" (CA 1): só o romaneio; o resto vem preenchido.</summary>
public sealed record CreateOutgoingInvoiceRequest(Guid ShipmentId);

/// <summary>Item editável do rascunho (CA 3): quantidade até o expedido, valor, desconto, textos, regra e a alíquota do IPI da TIPI. Nunca imposto.</summary>
public sealed record OutgoingInvoiceItemInput(Guid Id, decimal Quantity, decimal UnitPrice, decimal Discount, string? Description, string? AdditionalInfo, Guid? TaxRuleId, decimal? IpiRate);

/// <summary>"Salvar rascunho" (CA 3): itens, frete e outras despesas da nota (rateados), informações complementares e o envio ao cliente.</summary>
public sealed record UpdateOutgoingInvoiceRequest(
    IReadOnlyList<OutgoingInvoiceItemInput> Items, decimal Freight, decimal OtherExpenses, string? AdditionalInformation, bool SendToCustomer, string? EmailCc, uint Version);

/// <summary>Comando sobre a versão aberta na tela (emitir, corrigir, descartar).</summary>
public sealed record OutgoingInvoiceVersionRequest(uint Version);

/// <summary>Destinatário na tela (dado pessoal: só para quem tem <c>fiscal.view</c>; nunca em lista, evento ou log).</summary>
public sealed record OutgoingInvoiceRecipientView(
    string? Name, string? Document, RecipientType Type, bool FinalConsumer, string? StateRegistration, string? Email, string? Street, string? Number, string? Complement,
    string? District, string? CityName, string? CityCode, string? State, string? PostalCode);

/// <summary>Regra que pode ser escolhida para o item (CA 3): as ativas da operação que se aplicam ao destinatário e ao NCM.</summary>
public sealed record TaxRuleOption(Guid RuleId, string Name, int Version, bool Confirmed);

public sealed record OutgoingInvoiceItemView(
    Guid Id, int Sequence, Guid ItemId, string Code, string Description, string? Ncm, string? Cest, int Origin, string Unit, string QuoteItemKind, TaxOperation Operation,
    decimal ShippedQuantity, decimal Quantity, decimal UnitPrice, decimal Discount, decimal Freight, decimal OtherExpenses, decimal? IpiRate, bool NeedsIpiRate, string? AdditionalInfo,
    IReadOnlyList<string> SerialNumbers, Guid? ChosenRuleId, Guid? TaxRuleId, int? TaxRuleVersion, string? TaxRuleName, string? Cfop, IReadOnlyList<TaxLine> Taxes,
    decimal GrossAmount, decimal Total, decimal CostTax, IReadOnlyList<TaxRuleOption> RuleOptions);

/// <summary>Parcela do contrato mostrada só como referência (CA 3): a receita conta pela data da nota.</summary>
public sealed record ContractInstallmentView(Guid Id, string Number, string? Description, DateOnly? DueDate, decimal Amount, string Status);

/// <summary>O que a tela pode fazer agora (o servidor confere de novo).</summary>
public sealed record OutgoingInvoiceActions(bool CanEdit, bool CanIssue, bool CanPreview, bool CanReopen, bool CanDiscard, bool CanResend, bool CanDownload);

public sealed record OutgoingInvoiceView(
    Guid Id, InvoiceType Type, OutgoingInvoiceStatus Status, FiscalEnvironment Environment, TaxOperation Operation, string? NatureOfOperation, int? Series, long? Number,
    string? AccessKey, string? Protocol, DateTimeOffset? AuthorizedAt, DateOnly? IssuedOn, int Attempt, Guid? ProjectId, string? ProjectNumber, string? ProjectName, Guid? ShipmentId,
    string? ShipmentNumber, Guid? PartyId, OutgoingInvoiceRecipientView Recipient, int FreightMode, Guid? CarrierPartyId, string? CarrierName, int? Volumes, decimal? GrossWeight,
    string? VolumeSpecies, decimal Freight, decimal OtherExpenses, string? AdditionalInformation, IReadOnlyList<OutgoingInvoiceItemView> Items, InvoiceTotals Totals,
    decimal TotalAmount, decimal CostTaxAmount, string? CalculationError, int? RejectionCode, string? RejectionReason, string? SuggestedAction, string? Guidance,
    DateTimeOffset? ProcessingSince, DateTimeOffset? ContingencySince, Guid? XmlAttachmentId, Guid? DanfeAttachmentId, bool SendToCustomer, string? EmailCc,
    DateTimeOffset? EmailedAt, IReadOnlyList<ContractInstallmentView> Installments, OutgoingInvoiceActions Actions, DateTimeOffset CreatedAt, string? CreatedByName,
    DateTimeOffset? DiscardedAt, uint Version, InvoiceCancellationView Cancellation);

/// <summary>Nota na lista "Emitidas" (CA 17): número, série, data, cliente, projeto, valor, impostos, ambiente e situação (sem documento do cliente).</summary>
public sealed record OutgoingInvoiceRow(
    Guid Id, int? Series, long? Number, DateOnly? IssuedOn, DateTimeOffset CreatedAt, Guid? PartyId, string PartyName, Guid? ProjectId, string? ProjectNumber, string? ShipmentNumber,
    decimal TotalAmount, decimal TaxAmount, FiscalEnvironment Environment, OutgoingInvoiceStatus Status);

/// <summary>Página da lista com os totais do filtro (valor e impostos, CA 17).</summary>
public sealed record OutgoingInvoicePage(IReadOnlyList<OutgoingInvoiceRow> Items, int Total, int Page, int PageSize, decimal TotalAmount, decimal TaxAmount);

/// <summary>Filtros da lista na URL (CA 17).</summary>
public sealed record OutgoingInvoiceQuery(DateOnly? From, DateOnly? To, OutgoingInvoiceStatus? Status, Guid? PartyId, Guid? ProjectId, bool? CancellationRequested = null);

// ---------- FIS-03b: cancelamento, carta de correção e inutilização ----------

/// <summary>"Cancelar NF-e" (CA 1 e 3): o motivo (15 a 255), o pedido atendido (se veio do link), o aviso ao cliente e a versão aberta.</summary>
public sealed record CancelInvoiceRequest(string? Reason, Guid? RequestId, bool NotifyCustomer, uint Version);

/// <summary>"Pedir cancelamento" (CA 2): o motivo (15 a 255).</summary>
public sealed record CancellationRequestInput(string? Reason);

/// <summary>"Recusar pedido" (CA 3): o motivo, que vai para quem pediu.</summary>
public sealed record DeclineCancellationInput(string? Reason);

/// <summary>"Registrar carta de correção" (CA 8): o texto (15 a 1000), o aviso ao cliente e a versão aberta.</summary>
public sealed record CorrectInvoiceRequest(string? Text, bool NotifyCustomer, uint Version);

/// <summary>Evento na aba Eventos (CA 16): tipo, sequência, protocolo, texto, quem pediu e quando, e a recusa com o motivo.</summary>
public sealed record FiscalEventView(
    Guid Id, FiscalEventType Type, string TypeLabel, int Sequence, string Text, FiscalEventStatus Status, string? Protocol, DateTimeOffset RequestedAt, string? RequestedByName,
    DateTimeOffset? RegisteredAt, int? RejectionCode, string? RejectionReason, bool HasXml, Guid? CancellationRequestId);

/// <summary>Pedido de cancelamento na nota (CA 2 e 3): motivo, quem pediu, o prazo e a decisão.</summary>
public sealed record CancellationRequestView(
    Guid Id, Guid InvoiceId, string Reason, CancellationRequestStatus Status, DateTimeOffset RequestedAt, string? RequestedByName, DateTimeOffset ExpiresAt, DateTimeOffset? DecidedAt,
    string? DecidedByName, string? DecisionReason, uint Version);

/// <summary>
/// O que a nota mostra do cancelamento e da carta (CA 1 a 4 e 8): o prazo (o servidor confere de novo no envio), o que a pessoa pode fazer
/// agora (<c>fiscal.approve</c> cancela; os demais com <c>fiscal.view</c> pedem; <c>fiscal.edit</c> corrige), o pedido pendente, o envio em
/// andamento e o desfecho.
/// </summary>
public sealed record InvoiceCancellationView(
    DateTimeOffset? Deadline, bool WithinWindow, bool CanCancel, bool CanRequest, bool CanDecline, bool CanCorrect, int Corrections, int CorrectionsLeft,
    DateTimeOffset? CancellationSentAt, CancellationRequestView? PendingRequest, DateTimeOffset? CancelledAt, string? CancelledByName, string? CancellationReason,
    string? CancellationProtocol, bool HasCancelledDanfe, int EventCount);

/// <summary>Resultado de cancelar ou registrar a carta: a situação do evento, a mensagem da tela e a nota de agora.</summary>
public sealed record FiscalEventResultView(FiscalEventStatus Status, string Message, OutgoingInvoiceView Invoice);

/// <summary>"Inutilizar números" (CA 10): a faixa contínua da mesma série e a justificativa (15 a 255).</summary>
public sealed record InvalidateNumbersRequest(int Series, long FirstNumber, long LastNumber, string? Justification);

/// <summary>Número a inutilizar (CA 10): série, número, motivo e data.</summary>
public sealed record NumberGapRow(Guid Id, int Series, long Number, string Reason, DateTimeOffset CreatedAt, Guid InvoiceId);

/// <summary>Faixa contínua a inutilizar (CA 10): os números e se algum passa de 30 dias (CA 12).</summary>
public sealed record NumberGapRangeView(int Series, long FirstNumber, long LastNumber, long Count, string Label, DateTimeOffset OldestAt, bool Old, IReadOnlyList<NumberGapRow> Numbers);

/// <summary>Filtro "A inutilizar" (CA 10), já em faixas; a inutilização só com <c>fiscal.approve</c>.</summary>
public sealed record NumberGapList(IReadOnlyList<NumberGapRangeView> Ranges, int Total, bool CanInvalidate);

/// <summary>Resultado de "Inutilizar números" (CA 10): a situação, a mensagem e o protocolo.</summary>
public sealed record InvalidationResultView(FiscalEventStatus Status, string Message, int Series, long FirstNumber, long LastNumber, string? Protocol, Guid EventId);

/// <summary>Romaneio esperando nota na fila "A emitir" (CA 16).</summary>
public sealed record InvoiceQueueRow(
    Guid ShipmentId, string ShipmentNumber, Guid ProjectId, string ProjectNumber, string ProjectName, Guid PartyId, string PartyName, DateOnly ShippedOn, int DaysSinceShipment,
    bool Late, decimal EstimatedAmount);

/// <summary>Romaneio do projeto no "Faturar" (CA 2): com a nota ativa, se houver.</summary>
public sealed record BillingShipmentView(Guid ShipmentId, string Number, string Status, DateTimeOffset? ShippedAt, Guid? InvoiceId, string? InvoiceLabel, string? InvoiceStatus, bool CanIssue);

/// <summary>Item do contrato que ainda não saiu (CA 2): pede "Novo romaneio". Serviço fica para a NFS-e (FIS-06).</summary>
public sealed record BillingItemView(Guid ProjectItemId, string Description, string Kind, decimal Quantity, decimal Shipped);

/// <summary>"Faturar" no projeto (CA 2): toda NF-e de mercadoria nasce de um romaneio.</summary>
public sealed record ProjectBillingView(
    Guid ProjectId, string ProjectNumber, string ProjectName, IReadOnlyList<BillingShipmentView> Shipments, IReadOnlyList<BillingItemView> PendingItems, int ServiceItems);

/// <summary>
/// O que o <c>ConfirmDialog</c> da emissão mostra (CA 6): o ambiente, cada pré-condição com o link para resolver e o impacto (destinatário,
/// itens, totais, impostos e o que entra no custo do projeto).
/// </summary>
public sealed record IssueCheckView(
    Guid InvoiceId, FiscalEnvironment Environment, IReadOnlyList<IssuePrecondition> Preconditions, IReadOnlyList<string> Warnings, string? RecipientName, int Items,
    InvoiceTotals Totals, decimal TotalAmount, decimal CostTaxAmount, string? ProjectNumber, bool ProjectCostApplies, uint Version);

/// <summary>Resultado de "Emitir NF-e" (CA 6 e 10): a situação, a chave da tentativa e a mensagem da tela.</summary>
public sealed record IssueResultView(Guid InvoiceId, OutgoingInvoiceStatus Status, string IdempotencyKey, string Message, OutgoingInvoiceView Invoice);
