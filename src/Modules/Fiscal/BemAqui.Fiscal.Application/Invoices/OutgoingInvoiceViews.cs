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
    DateTimeOffset? DiscardedAt, uint Version);

/// <summary>Nota na lista "Emitidas" (CA 17): número, série, data, cliente, projeto, valor, impostos, ambiente e situação (sem documento do cliente).</summary>
public sealed record OutgoingInvoiceRow(
    Guid Id, int? Series, long? Number, DateOnly? IssuedOn, DateTimeOffset CreatedAt, Guid? PartyId, string PartyName, Guid? ProjectId, string? ProjectNumber, string? ShipmentNumber,
    decimal TotalAmount, decimal TaxAmount, FiscalEnvironment Environment, OutgoingInvoiceStatus Status);

/// <summary>Página da lista com os totais do filtro (valor e impostos, CA 17).</summary>
public sealed record OutgoingInvoicePage(IReadOnlyList<OutgoingInvoiceRow> Items, int Total, int Page, int PageSize, decimal TotalAmount, decimal TaxAmount);

/// <summary>Filtros da lista na URL (CA 17).</summary>
public sealed record OutgoingInvoiceQuery(DateOnly? From, DateOnly? To, OutgoingInvoiceStatus? Status, Guid? PartyId, Guid? ProjectId);

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
