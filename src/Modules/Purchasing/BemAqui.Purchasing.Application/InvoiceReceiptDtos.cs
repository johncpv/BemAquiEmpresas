using BemAqui.Fiscal.Contracts;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// Linha do "Receber pela nota" já preenchida (FIS-04b CA 1 e 3): a linha do pedido ligada aos itens da nota, a quantidade da nota em
/// unidade de estoque (só leitura), o saldo e o máximo com a tolerância, o preço do pedido × o da nota e o lote sugerido pelo <c>rastro</c>.
/// </summary>
public sealed record InvoiceReceiptDefaultLine(
    Guid PoLineId, int Sequence, Guid ItemId, string ItemCode, string ItemName, string UnitCode, bool TracksLot, decimal Quantity, decimal Open, decimal MaxQuantity,
    decimal OrderPrice, decimal InvoicePrice, bool PriceDiffers, string? SuggestedLot, IReadOnlyList<int> InvoiceItems);

/// <summary>Item "Sem item de estoque" da nota (CA 9): não entra no estoque e vira títulos próprios na conta escolhida.</summary>
public sealed record InvoiceNoStockLine(int Sequence, string Description, Guid? AccountId, string? AccountName, decimal Amount);

/// <summary>
/// "Receber pela nota" preenchido (FIS-04b CA 1, 3, 5 e 7): a faixa "Valores da NF-e {número} · {fornecedor}", o pedido, as linhas com a
/// quantidade da nota, frete, seguro, outras despesas e IPI da nota (máximos do pedido para frete e IPI), as duplicatas, a regra de crédito, os
/// itens sem estoque e, quando não pode receber agora, o motivo (<see cref="Blocker"/>). Nota já recebida: o recebimento ligado.
/// </summary>
public sealed record InvoiceReceiptDefaults(
    Guid InvoiceId, long InvoiceNumber, int Series, string SupplierName, DateOnly IssuedOn, IncomingInvoiceStage Stage, Guid? GoodsReceiptId, uint InvoiceVersion,
    Guid? OrderId, string? OrderNumber, uint OrderVersion, string? ProjectNumber, DateOnly Today, Guid? DefaultLocationId, IReadOnlyList<ReceiptLocationOption> Locations,
    IReadOnlyList<InvoiceReceiptDefaultLine> Lines, IReadOnlyList<InvoiceNoStockLine> NoStock, decimal Freight, decimal Insurance, decimal OtherExpenses, decimal IpiAmount,
    decimal FreightMax, decimal IpiMax, decimal Total, IReadOnlyList<IncomingReceiptDuplicate> Duplicates, string CreditRule, bool CreditConfirmed, bool PriceBlocked,
    bool SoleOwner, string? Blocker, string? BlockerCode);

/// <summary>Linha do "Receber pela nota" (CA 1): lote (obrigatório quando o item controla lote; padrão o da nota), validade e local.</summary>
public sealed record InvoiceReceiptLineInput(Guid PoLineId, string? LotNumber, DateOnly? LotExpiry, Guid? LocationId);

/// <summary>
/// "Receber pela nota" (CA 6): data (padrão hoje), observação, lote e local por linha e as versões do pedido e da nota (409 se mudaram).
/// Quantidades, frete, seguro, outras despesas e IPI vêm da nota: o usuário não digita.
/// </summary>
public sealed record InvoiceReceiptInput(DateOnly? ReceivedOn, string? Notes, IReadOnlyList<InvoiceReceiptLineInput>? Lines, uint OrderVersion, uint InvoiceVersion);

/// <summary>Título da parte "Sem item de estoque" que nasce (CA 9): conta, parcela, vencimento e valor.</summary>
public sealed record InvoiceNoStockInstallmentPreview(string AccountName, int Number, int Count, DateOnly DueDate, decimal Amount);

/// <summary>
/// Impacto do "Receber pela nota" (CA 5 a 9): o do recebimento (por item, custo de entrada e custo médio antes → depois; os títulos por
/// duplicata; o previsto antes → depois), os títulos da parte sem item de estoque, a regra de crédito usada, se as parcelas vieram das
/// duplicatas ou da condição do pedido e o aviso das duplicatas que não fecham.
/// </summary>
public sealed record InvoiceReceiptPreview(
    GoodsReceiptPreview Receipt, IReadOnlyList<InvoiceNoStockInstallmentPreview> NoStock, string CreditRule, bool FromDuplicates, bool DuplicatesMismatch, decimal InvoiceTotal);

/// <summary>"Pedir correção de preço" (CA 3): quantos aprovadores foram avisados.</summary>
public sealed record PriceCorrectionRequestResult(int Notified, IReadOnlyList<Guid> Lines);
