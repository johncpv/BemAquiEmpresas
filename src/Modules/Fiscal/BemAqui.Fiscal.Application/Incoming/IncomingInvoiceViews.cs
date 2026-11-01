using System.Text.Json.Serialization;
using BemAqui.Fiscal.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Purchasing.Contracts;

namespace BemAqui.Fiscal.Application.Incoming;

/// <summary>Resultado de um arquivo importado (CA 1 e 5): a nota criada e o que já veio pronto (de-para lembrado, fornecedor achado).</summary>
public sealed record IncomingImportResult(Guid Id, string AccessKey, long Number, int Series, string IssuerName, decimal Total, int Items, int Remembered, bool SupplierFound);

/// <summary>Linha da lista Fiscal → Notas de entrada.</summary>
public sealed record IncomingInvoiceSummary(
    Guid Id, long Number, int Series, string AccessKey, string IssuerName, string IssuerDocument, DateTimeOffset IssuedAt, decimal Total, IncomingInvoiceStatus Status,
    bool WithoutOrder, bool HasOrder, int ItemsCount, int PendingItems, DateTimeOffset ImportedAt);

public sealed record IncomingInvoiceList(IReadOnlyList<IncomingInvoiceSummary> Items, int Page, int PageSize, int Total, bool CanImport);

/// <summary>Situação do fornecedor da nota no Parties (CA 6).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IncomingSupplierState>))]
public enum IncomingSupplierState
{
    /// <summary>Cadastrado com o papel Fornecedor.</summary>
    Found,

    /// <summary>Sem cadastro com o CNPJ: "Cadastrar fornecedor com os dados da nota".</summary>
    NotFound,

    /// <summary>Cadastrado sem o papel Fornecedor: "Adicionar papel".</summary>
    NotSupplier,
}

public sealed record IncomingIssuerView(string Document, string Name, string? TradeName, string? StateRegistration, IncomingIssuerAddress? Address);

public sealed record IncomingSupplierView(IncomingSupplierState State, Guid? PartyId, string? Name, bool IsActive);

/// <summary>Pedido do fornecedor que pode ser ligado à nota (CA 9): número, situação, itens em comum e se é o sugerido.</summary>
public sealed record IncomingOrderOption(Guid Id, string Number, PurchaseOrderStatus Status, DateOnly ExpectedDate, int CommonItems, bool Suggested, bool CitedInInvoice);

/// <summary>
/// Pedido da nota (CA 9 e 10): o ligado (ou o sugerido, para confirmar), as opções e o frete e o IPI da nota × do pedido. Os valores do
/// pedido só aparecem com <c>purchasing.view</c> (<see cref="ValuesVisible"/>).
/// </summary>
public sealed record IncomingOrderView(
    Guid? PurchaseOrderId, string? Number, PurchaseOrderStatus? Status, Guid? SuggestedId, string? SuggestedNumber, string? SuggestionReason, bool ValuesVisible,
    TotalComparison? Freight, TotalComparison? Ipi, IReadOnlyList<IncomingOrderOption> Options, IReadOnlyList<IncomingOrderLineOption> Lines);

/// <summary>Linha do pedido ligado, para escolher a linha do item (CA 9); quantidade e preço só com <c>purchasing.view</c>.</summary>
public sealed record IncomingOrderLineOption(Guid LineId, int Sequence, Guid ItemId, string? ItemCode, string? ItemName, decimal? OpenQuantity, decimal? UnitPrice);

/// <summary>Item da nota com o de-para, a sugestão e a comparação com o pedido (CA 7, 8 e 10).</summary>
public sealed record IncomingInvoiceItemView(
    Guid Id, int Sequence, string SupplierCode, string Description, string? Ncm, string? Cfop, string Unit, decimal Quantity, decimal UnitPrice, decimal Amount,
    IncomingItemTaxes Taxes, string? LotNumber, string? OrderNumber,
    Guid? ItemId, string? ItemCode, string? ItemName, string? StockUnit, decimal? ConversionFactor, bool FactorRequired, decimal? StockQuantity, decimal? StockUnitPrice,
    bool NoStockItem, Guid? ExpenseAccountId, string? ExpenseAccountName, ItemMappingSource? MappingSource, IReadOnlyList<ItemSuggestion> Suggestions,
    Guid? PoLineId, int? PoLineSequence, ItemComparison? Comparison, bool Resolved);

/// <summary>O que o usuário pode fazer na nota (os botões e o motivo do bloqueio).</summary>
public sealed record IncomingInvoicePermissions(bool Import, bool ApproveWithoutOrder, bool ViewOrder, bool CreateParty, bool EditParty, bool CreateItem, bool Receive);

/// <summary>Nota de entrada aberta (Itens e de-para, Pedido e divergências, Documentos, Histórico).</summary>
public sealed record IncomingInvoiceView(
    Guid Id, string AccessKey, long Number, int Series, DateTimeOffset IssuedAt, IncomingInvoiceStatus Status, IncomingIssuerView Issuer, IncomingSupplierView Supplier,
    IncomingInvoiceTotals Totals, IReadOnlyList<IncomingDuplicate> Duplicates, string Protocol, DateTimeOffset? AuthorizedAt, string SignerDocument, bool SefazStatusChecked,
    IncomingOrderView Order, bool WithoutOrder, string? WithoutOrderReason, string? WithoutOrderApprovedByName, IReadOnlyList<IncomingInvoiceItemView> Items,
    IReadOnlyList<IncomingDivergence> Divergences, int PendingItems, string? CheckBlockedReason,
    string? ImportedByName, DateTimeOffset ImportedAt, string? CheckedByName, DateTimeOffset? CheckedAt, string? RejectedReason, string? RejectedByName, DateTimeOffset? RejectedAt,
    Guid? XmlAttachmentId, IncomingInvoicePermissions Can, Guid? GoodsReceiptId = null, string? InputTaxCreditApplied = null, decimal InputTaxCreditAmount = 0m);

/// <summary>
/// De-para de um item (CA 7, 8 e 9): o nosso item (com o fator, obrigatório quando a unidade do fornecedor não tem conversão) ou "Sem item
/// de estoque" com a conta do plano; a linha do pedido ligado (vazia = o sistema liga pelo item). <c>Suggested</c> marca a sugestão aceita.
/// Nenhum dos dois = desfaz o de-para.
/// </summary>
public sealed record IncomingItemMappingRequest(Guid? ItemId, decimal? ConversionFactor, bool NoStockItem, Guid? ExpenseAccountId, Guid? PoLineId, bool Suggested = false);

/// <summary>Pedido da nota (CA 9 e 11): o pedido escolhido ou "Sem pedido".</summary>
public sealed record IncomingOrderRequest(Guid? PurchaseOrderId, bool WithoutOrder);

/// <summary>"Conferir nota" (CA 11 e 12): o motivo, obrigatório na nota sem pedido.</summary>
public sealed record CheckIncomingInvoiceRequest(string? WithoutOrderReason);

/// <summary>"Recusar nota" (CA 12): o motivo.</summary>
public sealed record RejectIncomingInvoiceRequest(string? Reason);

/// <summary>Item do estoque para o seletor do de-para (sem custo nem saldo).</summary>
public sealed record IncomingItemOption(Guid Id, string Code, string Name, string UnitCode, string? Ncm);

/// <summary>Conta de despesa para o item "Sem item de estoque".</summary>
public sealed record IncomingAccountOption(Guid Id, string Code, string Name);
