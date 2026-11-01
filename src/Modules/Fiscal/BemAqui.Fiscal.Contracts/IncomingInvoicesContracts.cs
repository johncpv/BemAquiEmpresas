using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Contracts;

/// <summary>Situação da NF-e de entrada vista de fora do Fiscal (FIS-04b): só a conferida é recebida; a recebida volta a conferida no estorno.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IncomingInvoiceStage>))]
public enum IncomingInvoiceStage
{
    Imported,
    Checked,
    Received,
    Rejected,
}

/// <summary>Duplicata da nota (<c>cobr/dup</c>): número, vencimento e valor.</summary>
public sealed record IncomingReceiptDuplicate(string Number, DateOnly? DueDate, decimal Amount);

/// <summary>
/// Item conferido da nota para o recebimento (FIS-04b CA 1, 5 e 9): o nosso item e a linha do pedido (ou "Sem item de estoque" com a conta),
/// a quantidade e o preço em unidade de estoque, o valor, o lote do <c>rastro</c> e os tributos do item (ICMS, IPI, PIS e COFINS).
/// </summary>
public sealed record IncomingReceiptItem(
    Guid Id, int Sequence, string Description, Guid? ItemId, Guid? PoLineId, decimal? StockQuantity, decimal? StockUnitPrice, decimal Amount, decimal Discount, string? LotNumber,
    bool NoStockItem, Guid? ExpenseAccountId, decimal Icms, decimal Ipi, decimal Pis, decimal Cofins);

/// <summary>
/// Regra "Crédito de impostos na entrada" de Configurações → Fiscal (FIS-04b CA 5, P-01): os tributos recuperáveis (vazio = nenhum: ICMS,
/// IPI, PIS e COFINS da nota ficam no custo), se a contadora já conferiu e o texto que o diálogo, a nota e o histórico mostram.
/// </summary>
public sealed record InputTaxCreditRule(IReadOnlyList<string> Taxes, bool Confirmed, string Label);

/// <summary>A nota de entrada para o recebimento pela nota (FIS-04b): cabeçalho, totais, duplicatas, itens conferidos, a regra de crédito e a marca da nota sem XML ("Lida do PDF, sem XML", FIS-05 CA 8).</summary>
public sealed record IncomingInvoiceForReceipt(
    Guid Id, long Number, int Series, DateOnly IssuedOn, string IssuerName, Guid? PartyId, Guid? PurchaseOrderId, bool WithoutOrder, IncomingInvoiceStage Stage, Guid? GoodsReceiptId,
    uint Version, decimal Products, decimal Freight, decimal Insurance, decimal OtherExpenses, decimal Discount, decimal Ipi, decimal Total,
    IReadOnlyList<IncomingReceiptDuplicate> Duplicates, IReadOnlyList<IncomingReceiptItem> Items, InputTaxCreditRule Credit, string? SourceMark = null);

/// <summary>
/// NF-e de entrada para o Purchasing (FIS-04b, ADR 0024 §1): os dados conferidos da nota e as transições <c>Checked → Received</c> e de volta,
/// na transação de quem chama (o recebimento da CMP-04). A transição confere a versão (<c>xmin</c>): dois recebimentos da mesma nota, só um
/// passa. Nenhum outro módulo grava nas tabelas do Fiscal.
/// </summary>
public interface IIncomingInvoiceReceipts
{
    /// <summary>A nota com os itens conferidos e a regra de crédito em uso; null se não existe no tenant.</summary>
    Task<IncomingInvoiceForReceipt?> FindForReceiptAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>Checked → Received</c> com o recebimento ligado, a regra de crédito usada e o crédito guardado na nota. Já recebida: 409
    /// <c>incoming_invoice_received</c> com o link do recebimento; outra situação: 409 <c>incoming_invoice_not_checked</c>; versão diferente:
    /// 409 <c>concurrency_conflict</c>.
    /// </summary>
    Task<Result> MarkReceivedAsync(Guid id, uint version, Guid goodsReceiptId, string creditRule, decimal creditAmount, CancellationToken cancellationToken = default);

    /// <summary>Estorno do recebimento (CA 11): <c>Received → Checked</c>, sem o recebimento; a nota pode ser recebida de novo.</summary>
    Task<Result> ReturnToCheckedAsync(Guid id, Guid goodsReceiptId, CancellationToken cancellationToken = default);
}

/// <summary>Códigos e textos do recebimento pela nota (FIS-04b) que o Purchasing e o Fiscal compartilham.</summary>
public static class IncomingInvoiceReceiptCodes
{
    public const string Received = "incoming_invoice_received";
    public const string NotChecked = "incoming_invoice_not_checked";
    public const string ScreenLink = "/app/fiscal/entrada/";
}

/// <summary>
/// NF-e de entrada para os outros módulos (PAT-01a CA 7): id, número, série, emitente (o fornecedor no <c>IParties</c>, quando
/// identificado), nome do emitente e data de emissão. Sem valor nem itens.
/// </summary>
public sealed record IncomingInvoiceSummary(Guid Id, string Number, string Series, Guid? IssuerPartyId, string IssuerName, DateOnly IssueDate);

/// <summary>
/// Notas de entrada importadas pela FIS-04a, para o seletor da NF de entrada do bem (PAT-01a CA 7, R-PRP-3: o fornecedor e a data
/// de aquisição vêm da nota). Só notas importadas; sem permissão do Fiscal no request (quem chama confere a própria).
/// </summary>
public interface IIncomingInvoices
{
    /// <summary>Busca por número, série ou emitente, das mais novas às mais antigas.</summary>
    Task<IReadOnlyList<IncomingInvoiceSummary>> SearchAsync(string? term, int limit = 20, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IncomingInvoiceSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
