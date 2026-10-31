using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Domain;

/// <summary>Situação da NF-e de entrada (<c>04-fluxos</c>): <c>Imported → Checked → Received</c>; <c>Imported | Checked → Rejected</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IncomingInvoiceStatus>))]
public enum IncomingInvoiceStatus
{
    Imported,
    Checked,
    Received,
    Rejected,
}

/// <summary>De onde veio a nota de entrada: o XML do fornecedor (FIS-04a) ou a leitura do PDF (FIS-05).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IncomingInvoiceSource>))]
public enum IncomingInvoiceSource
{
    Xml,
    Pdf,
}

/// <summary>Como o item da nota foi ligado ao nosso (CA 7): de-para lembrado, sugestão aceita ou escolha do usuário.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ItemMappingSource>))]
public enum ItemMappingSource
{
    Remembered,
    Suggested,
    Manual,
}

/// <summary>Divergência contra o pedido (CA 10): acima do saldo ou do preço é crítica, abaixo é alerta.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DivergenceLevel>))]
public enum DivergenceLevel
{
    Ok,
    Warning,
    Critical,
}

/// <summary>Totais da nota (<c>ICMSTot</c>): produtos, frete, seguro, desconto, outras despesas, IPI, ICMS, ICMS-ST e o total.</summary>
public sealed record IncomingInvoiceTotals(
    decimal Products, decimal Freight, decimal Insurance, decimal Discount, decimal OtherExpenses, decimal Ipi, decimal IcmsBase, decimal Icms, decimal IcmsStBase, decimal IcmsSt, decimal Total);

/// <summary>Duplicata da cobrança (<c>cobr/dup</c>): número, vencimento e valor.</summary>
public sealed record IncomingDuplicate(string Number, DateOnly? DueDate, decimal Amount);

/// <summary>Endereço e inscrição do emitente (pessoa jurídica), para cadastrar o fornecedor com os dados da nota (CA 6, R-PRP-3).</summary>
public sealed record IncomingIssuerAddress(string? Street, string? Number, string? Complement, string? District, string? City, string? IbgeCode, string? State, string? ZipCode);

/// <summary>Impostos do item (<c>imposto</c>): CST/CSOSN do ICMS, bases, alíquotas e valores; PIS e COFINS só o valor.</summary>
public sealed record IncomingItemTaxes(string? IcmsCst, decimal IcmsBase, decimal IcmsRate, decimal Icms, decimal IcmsStBase, decimal IcmsSt, decimal IpiRate, decimal Ipi, decimal Pis, decimal Cofins);

/// <summary>Divergência gravada na conferência (CA 12): por item, contra a linha do pedido, em unidade de estoque.</summary>
public sealed record IncomingDivergence(int Sequence, Guid? PoLineId, decimal QuantityDifference, decimal PriceDifference, decimal? PricePercent, DivergenceLevel QuantityLevel, DivergenceLevel PriceLevel);

/// <summary>
/// NF-e de entrada (<c>fiscal.incoming_invoices</c>, FIS-04a): o XML do fornecedor validado (esquema, protocolo, assinatura, destinatário),
/// guardado como anexo de sistema. A chave de acesso é única entre as não recusadas. Nada de pessoa física é guardado (o transportador não
/// é lido; o emitente é sempre um CNPJ).
/// </summary>
[DocumentName("A nota de entrada")]
public sealed class IncomingInvoice : Entity, IAuditable, IConcurrencyVersioned
{
    public const int ReasonMaxLength = 500;

    public string AccessKey { get; init; } = string.Empty;

    public long Number { get; init; }

    public int Series { get; init; }

    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>CNPJ do emitente (<c>emit/CNPJ</c>), igual ao do certificado da assinatura.</summary>
    public string IssuerDocument { get; init; } = string.Empty;

    public string IssuerName { get; init; } = string.Empty;

    public string? IssuerTradeName { get; init; }

    public string? IssuerStateRegistration { get; init; }

    public IncomingIssuerAddress? IssuerAddress { get; init; }

    /// <summary>O fornecedor no Parties, achado pelo CNPJ; null enquanto não existir o cadastro (CA 6).</summary>
    public Guid? PartyId { get; private set; }

    public IncomingInvoiceSource Source { get; init; } = IncomingInvoiceSource.Xml;

    public IncomingInvoiceStatus Status { get; private set; } = IncomingInvoiceStatus.Imported;

    public Guid? XmlAttachmentId { get; private set; }

    public Guid? PdfAttachmentId { get; private set; }

    public IncomingInvoiceTotals Totals { get; init; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Total da nota (<c>vNF</c>), também em coluna para a lista.</summary>
    public decimal Total { get; init; }

    public IReadOnlyList<IncomingDuplicate> Duplicates { get; init; } = [];

    /// <summary>Assinatura conferida (sempre verdadeira: nota com assinatura inválida não entra).</summary>
    public bool SignatureValid { get; init; }

    /// <summary>CNPJ do certificado que assinou.</summary>
    public string SignerDocument { get; init; } = string.Empty;

    /// <summary>Protocolo de autorização da SEFAZ (<c>nProt</c>) e quando.</summary>
    public string Protocol { get; init; } = string.Empty;

    public DateTimeOffset? AuthorizedAt { get; init; }

    /// <summary>Sempre falso no MVP: o sistema não consulta a situação da nota na SEFAZ (CA 3, ADR 0027).</summary>
    public bool SefazStatusChecked { get; init; }

    /// <summary>Números de pedido citados na nota (<c>xPed</c> dos itens e da compra, informação complementar), para sugerir o pedido (CA 9).</summary>
    public string[] OrderReferences { get; init; } = [];

    public Guid? PurchaseOrderId { get; private set; }

    public bool WithoutOrder { get; private set; }

    public string? WithoutOrderReason { get; private set; }

    public Guid? WithoutOrderApprovedBy { get; private set; }

    public Guid? CheckedBy { get; private set; }

    public DateTimeOffset? CheckedAt { get; private set; }

    public IReadOnlyList<IncomingDivergence> Divergences { get; private set; } = [];

    public string? RejectedReason { get; private set; }

    public Guid? RejectedBy { get; private set; }

    public DateTimeOffset? RejectedAt { get; private set; }

    /// <summary>Recebimento gerado pela nota (FIS-04b).</summary>
    public Guid? GoodsReceiptId { get; private set; }

    public Guid? ImportedBy { get; init; }

    public DateTimeOffset ImportedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; set; }

    public bool IsEditable => Status == IncomingInvoiceStatus.Imported;

    public void AttachXml(Guid attachmentId) => XmlAttachmentId = attachmentId;

    public void SetParty(Guid? partyId, DateTimeOffset now)
    {
        PartyId = partyId;
        UpdatedAt = now;
    }

    /// <summary>Liga ao pedido (CA 9) ou marca "Sem pedido" (CA 11); trocar o pedido limpa o motivo e o aprovador.</summary>
    public void LinkOrder(Guid? purchaseOrderId, bool withoutOrder, DateTimeOffset now)
    {
        PurchaseOrderId = withoutOrder ? null : purchaseOrderId;
        WithoutOrder = withoutOrder;
        WithoutOrderReason = null;
        WithoutOrderApprovedBy = null;
        UpdatedAt = now;
    }

    /// <summary>"Conferir nota" (CA 12): <c>Checked</c> com quem conferiu, as divergências e, sem pedido, o motivo e quem autorizou (CA 11).</summary>
    public void Check(Guid? by, DateTimeOffset now, IReadOnlyList<IncomingDivergence> divergences, string? withoutOrderReason)
    {
        Status = IncomingInvoiceStatus.Checked;
        CheckedBy = by;
        CheckedAt = now;
        Divergences = divergences;
        if (WithoutOrder)
        {
            WithoutOrderReason = withoutOrderReason;
            WithoutOrderApprovedBy = by;
        }

        UpdatedAt = now;
    }

    /// <summary>"Recusar nota" (CA 12): <c>Rejected</c> com o motivo; a chave fica livre para nova importação.</summary>
    public void Reject(Guid? by, DateTimeOffset now, string reason)
    {
        Status = IncomingInvoiceStatus.Rejected;
        RejectedBy = by;
        RejectedAt = now;
        RejectedReason = reason;
        UpdatedAt = now;
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}

/// <summary>
/// Item da NF-e de entrada (<c>fiscal.incoming_invoice_items</c>): o que o fornecedor faturou (código, descrição, NCM, CFOP, unidade,
/// quantidade, preço, impostos, lote) e o de-para para o nosso item, com a quantidade e o preço em unidade de estoque (CA 7 e 8), ou
/// "Sem item de estoque" com a conta do plano.
/// </summary>
[DocumentName("O item da nota de entrada")]
public sealed class IncomingInvoiceItem : Entity, IAuditable
{
    public Guid InvoiceId { get; init; }

    /// <summary><c>nItem</c> da nota.</summary>
    public int Sequence { get; init; }

    public string SupplierCode { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? Ncm { get; init; }

    public string? Cfop { get; init; }

    /// <summary>Unidade do fornecedor (<c>uCom</c>), em texto.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>Quantidade faturada (<c>qCom</c>, 4 casas).</summary>
    public decimal Quantity { get; init; }

    /// <summary>Preço unitário faturado (<c>vUnCom</c>, até 10 casas).</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>Valor do item (<c>vProd</c>).</summary>
    public decimal Amount { get; init; }

    public decimal Discount { get; init; }

    public IncomingItemTaxes Taxes { get; init; } = new(null, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Lote do fornecedor (<c>rastro/nLote</c>), quando houver.</summary>
    public string? LotNumber { get; init; }

    /// <summary>Pedido citado no item (<c>xPed</c>) e o item do pedido (<c>nItemPed</c>).</summary>
    public string? OrderNumber { get; init; }

    public string? OrderItem { get; init; }

    public Guid? ItemId { get; private set; }

    /// <summary>Quanto 1 unidade do fornecedor vale na unidade de estoque (6 casas); null = falta o fator (CA 8).</summary>
    public decimal? ConversionFactor { get; private set; }

    public decimal? StockQuantity { get; private set; }

    public decimal? StockUnitPrice { get; private set; }

    public Guid? PoLineId { get; private set; }

    public bool NoStockItem { get; private set; }

    public Guid? ExpenseAccountId { get; private set; }

    public ItemMappingSource? MappingSource { get; private set; }

    /// <summary>Pronto para conferir: ligado a item com fator, ou sem item de estoque com a conta.</summary>
    public bool IsResolved => NoStockItem ? ExpenseAccountId is not null : ItemId is not null && ConversionFactor is not null;

    /// <summary>Liga ao nosso item; com o fator, calcula a quantidade e o preço em unidade de estoque (CA 8).</summary>
    public void MapTo(Guid itemId, decimal? factor, ItemMappingSource source)
    {
        ItemId = itemId;
        MappingSource = source;
        NoStockItem = false;
        ExpenseAccountId = null;
        SetFactor(factor);
    }

    public void SetFactor(decimal? factor)
    {
        ConversionFactor = factor;
        StockQuantity = factor is { } f ? IncomingInvoiceRules.StockQuantity(Quantity, f) : null;
        StockUnitPrice = factor is { } g ? IncomingInvoiceRules.StockUnitPrice(UnitPrice, g) : null;
    }

    /// <summary>"Sem item de estoque" (CA 7, ponto 59): despesa ou serviço com a conta do plano; não entra no estoque (FIS-04b).</summary>
    public void MarkNoStockItem(Guid accountId)
    {
        ItemId = null;
        ConversionFactor = null;
        StockQuantity = null;
        StockUnitPrice = null;
        PoLineId = null;
        NoStockItem = true;
        ExpenseAccountId = accountId;
        MappingSource = ItemMappingSource.Manual;
    }

    public void Unmap()
    {
        ItemId = null;
        ConversionFactor = null;
        StockQuantity = null;
        StockUnitPrice = null;
        PoLineId = null;
        NoStockItem = false;
        ExpenseAccountId = null;
        MappingSource = null;
    }

    public void LinkLine(Guid? poLineId) => PoLineId = poLineId;
}

/// <summary>Comparação de um item da nota com a linha do pedido (CA 10), em unidade de estoque.</summary>
public sealed record ItemComparison(
    decimal OpenQuantity, decimal BilledQuantity, decimal QuantityDifference, DivergenceLevel QuantityLevel,
    decimal OrderPrice, decimal InvoicePrice, decimal PriceDifference, decimal? PricePercent, decimal ValueDifference, DivergenceLevel PriceLevel);

/// <summary>Total da nota contra o do pedido (frete e IPI, CA 10).</summary>
public sealed record TotalComparison(decimal Order, decimal Invoice, decimal Difference, decimal? Percent, DivergenceLevel Level);

/// <summary>Regras puras da nota de entrada (FIS-04a CA 4, 8, 10 e 14).</summary>
public static class IncomingInvoiceRules
{
    public const string DuplicateCode = "incoming_invoice_duplicate";
    public const string InvalidXmlMessage = "Este arquivo não é um XML de NF-e válido.";
    public const string NoProtocolMessage = "Este XML não tem o protocolo de autorização da SEFAZ. Peça ao fornecedor o XML autorizado.";
    public const string SignatureMismatchMessage = "A assinatura não corresponde a esta nota.";
    public const string NotRecipientMessage = "A nota não é destinada a esta empresa.";
    public const string WithoutOrderBlockedMessage = "Nota sem pedido: peça a conferência a quem aprova compras.";
    public const string SefazNotCheckedTitle = "Situação na SEFAZ não conferida";
    public const string SefazNotCheckedHint = "O sistema confere a assinatura e o protocolo, mas não consulta se a nota foi cancelada depois. Em caso de dúvida, consulte a chave no portal da SEFAZ.";

    /// <summary>Até 2 MB por arquivo e 20 por vez (CA 1); 60 arquivos por hora por usuário na política <c>fiscal-incoming-import</c>.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    public const int MaxFilesPerBatch = 20;

    /// <summary>Quantidade em unidade de estoque: qtd × fator, 4 casas (R-EST-3).</summary>
    public static decimal StockQuantity(decimal quantity, decimal factor) => Math.Round(quantity * factor, 4, MidpointRounding.AwayFromZero);

    /// <summary>Preço por unidade de estoque: preço ÷ fator, 6 casas.</summary>
    public static decimal StockUnitPrice(decimal unitPrice, decimal factor) => factor <= 0 ? 0m : Math.Round(unitPrice / factor, 6, MidpointRounding.AwayFromZero);

    /// <summary>Acima é crítico (vermelho), abaixo é alerta (âmbar), igual é ok (CA 10).</summary>
    public static DivergenceLevel Level(decimal difference) => difference > 0 ? DivergenceLevel.Critical : difference < 0 ? DivergenceLevel.Warning : DivergenceLevel.Ok;

    /// <summary>Faturado × saldo a receber e preço da nota × do pedido; a diferença de valor é a de preço vezes a quantidade faturada.</summary>
    public static ItemComparison Compare(decimal openQuantity, decimal billedQuantity, decimal orderPrice, decimal invoicePrice)
    {
        var quantity = billedQuantity - openQuantity;
        var price = Math.Round(invoicePrice - orderPrice, 6, MidpointRounding.AwayFromZero);
        decimal? percent = orderPrice == 0 ? null : Math.Round(price / orderPrice * 100m, 2, MidpointRounding.AwayFromZero);
        var value = Math.Round(price * billedQuantity, 2, MidpointRounding.AwayFromZero);
        return new ItemComparison(openQuantity, billedQuantity, quantity, Level(quantity), orderPrice, invoicePrice, price, percent, value, Level(price));
    }

    public static TotalComparison CompareTotal(decimal order, decimal invoice)
    {
        var difference = invoice - order;
        decimal? percent = order == 0 ? null : Math.Round(difference / order * 100m, 2, MidpointRounding.AwayFromZero);
        return new TotalComparison(order, invoice, difference, percent, Level(difference));
    }

    /// <summary>Chave mascarada para o log (CA 14): só os últimos 6 dígitos.</summary>
    public static string MaskKey(string? accessKey) => accessKey is { Length: >= 6 } k ? "…" + k[^6..] : "…";

    /// <summary>Dígito verificador da chave de acesso (módulo 11, pesos 2 a 9 da direita para a esquerda; resto 0 ou 1 dá 0).</summary>
    public static bool IsValidAccessKey(string? key)
    {
        if (key is not { Length: 44 } || !key.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        var weight = 2;
        for (var i = 42; i >= 0; i--)
        {
            sum += (key[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var rest = sum % 11;
        var digit = rest < 2 ? 0 : 11 - rest;
        return key[43] - '0' == digit;
    }
}
