using System.Globalization;
using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Domain;

/// <summary>Situação do rascunho de leitura (FIS-05): lendo no worker, pronto para conferir, leitura que não serviu, conferido (virou nota).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IncomingReadingStatus>))]
public enum IncomingReadingStatus
{
    Reading,
    Ready,
    Failed,
    Checked,
}

/// <summary>Resultado da leitura do PDF no worker <c>fiscal-pdf-reader</c> (CA 1 a 3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PdfReadingOutcome>))]
public enum PdfReadingOutcome
{
    /// <summary>Texto lido e campos extraídos (todos ainda a conferir).</summary>
    Read,

    /// <summary>PDF só com imagem (escaneado): sem OCR no MVP (ponto 65).</summary>
    NoText,

    /// <summary>JavaScript, anexo embutido, formulário com ação ou criptografia: recusado sem ler.</summary>
    Unsafe,

    /// <summary>Fora de 1 a 10 páginas.</summary>
    TooManyPages,

    /// <summary>O arquivo não abre como PDF.</summary>
    Invalid,

    /// <summary>Erro na leitura ou leitor indisponível.</summary>
    Failed,

    /// <summary>Passou dos 30 s: o processo do worker foi morto ("falhou por tempo").</summary>
    TimedOut,
}

/// <summary>Situação de um campo lido, sempre mostrada em texto além da cor (CA 7).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReadingFieldState>))]
public enum ReadingFieldState
{
    /// <summary>"Lido".</summary>
    Read,

    /// <summary>"Leitura incerta: confira".</summary>
    Uncertain,

    /// <summary>"Não encontrado".</summary>
    NotFound,
}

/// <summary>
/// Campo lido do PDF (CA 7): a chave do campo (<c>issuedAt</c>, <c>totals.products</c>, <c>items.0.quantity</c>…), o valor normalizado
/// (data ISO, número com ponto) e a situação. Só sugestão: nada vale sem a conferência humana.
/// </summary>
public sealed record ReadingField(string Key, string? Value, ReadingFieldState State);

/// <summary>
/// Rascunho da leitura do PDF da DANFE ou da nota digitada (<c>fiscal.incoming_invoice_readings</c>, FIS-05): o que o worker leu (campos
/// com a situação), o resultado e a duração; na conferência, o que o usuário corrigiu, quem conferiu e a nota que nasceu
/// (<see cref="InvoiceId"/>: o histórico da nota mostra a leitura por ele). O PDF é anexo de sistema do rascunho. Nunca guarda o texto
/// extraído do PDF, só os campos.
/// </summary>
[DocumentName("A leitura da nota de entrada")]
public sealed class IncomingInvoiceReading : Entity, IAuditable, IConcurrencyVersioned
{
    public const int FileNameMaxLength = 200;
    public const int WarningMaxLength = 300;

    /// <summary>A nota que nasceu da conferência (null até conferir).</summary>
    public Guid? InvoiceId { get; private set; }

    /// <summary><c>Pdf</c> (leitura assistida) ou <c>Manual</c> ("Digitar a nota").</summary>
    public IncomingInvoiceSource Source { get; private set; } = IncomingInvoiceSource.Pdf;

    public IncomingReadingStatus Status { get; private set; } = IncomingReadingStatus.Reading;

    /// <summary>Resultado da leitura do worker (null enquanto lê e na nota digitada sem PDF).</summary>
    public PdfReadingOutcome? Result { get; private set; }

    public Guid? PdfAttachmentId { get; private set; }

    public string? FileName { get; private set; }

    public int? Pages { get; private set; }

    /// <summary>Duração da leitura no worker, em milissegundos.</summary>
    public int? DurationMs { get; private set; }

    /// <summary>Chave de acesso lida e válida (DV conferido), para a duplicidade e o pedido do XML.</summary>
    public string? AccessKey { get; private set; }

    public IReadOnlyList<ReadingField> Fields { get; private set; } = [];

    /// <summary>Avisos da leitura (chave com DV errado descartada, CNPJ do texto diferente do da chave…).</summary>
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    /// <summary>Chaves dos campos que o usuário corrigiu na conferência (o que foi lido fica em <see cref="Fields"/>).</summary>
    public IReadOnlyList<string> CorrectedFields { get; private set; } = [];

    public Guid? CorrectedBy { get; private set; }

    public Guid? CheckedBy { get; private set; }

    public DateTimeOffset? CheckedAt { get; private set; }

    /// <summary>Último "Pedir o XML ao fornecedor" (CA 6).</summary>
    public DateTimeOffset? XmlRequestedAt { get; private set; }

    public Guid? XmlRequestedBy { get; private set; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReadAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; set; }

    public bool IsOpen => Status is not IncomingReadingStatus.Checked;

    /// <summary>PDF enviado (CA 1): o rascunho fica "Lendo o PDF…" até o job devolver o resultado.</summary>
    public static IncomingInvoiceReading ForPdf(Guid? by, DateTimeOffset now, string fileName)
    {
        var reading = new IncomingInvoiceReading { CreatedBy = by, CreatedAt = now };
        reading.FileName = fileName.Length > FileNameMaxLength ? fileName[..FileNameMaxLength] : fileName;
        reading.UpdatedAt = now;
        return reading;
    }

    /// <summary>"Digitar a nota" sem PDF (CA 9): o formulário vazio, pronto para preencher.</summary>
    public static IncomingInvoiceReading ForManual(Guid? by, DateTimeOffset now)
    {
        var reading = new IncomingInvoiceReading { CreatedBy = by, CreatedAt = now };
        reading.Source = IncomingInvoiceSource.Manual;
        reading.Status = IncomingReadingStatus.Ready;
        reading.UpdatedAt = now;
        return reading;
    }

    public void AttachPdf(Guid attachmentId) => PdfAttachmentId = attachmentId;

    /// <summary>Resultado do worker (CA 2 a 4): <c>Read</c> fica pronto para conferir; o resto fica "falhou" com o motivo.</summary>
    public void Complete(PdfReadingOutcome result, int? pages, int durationMs, string? accessKey, IReadOnlyList<ReadingField> fields, IReadOnlyList<string> warnings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(warnings);
        Result = result;
        Status = result == PdfReadingOutcome.Read ? IncomingReadingStatus.Ready : IncomingReadingStatus.Failed;
        Pages = pages;
        DurationMs = durationMs;
        AccessKey = accessKey;
        Fields = fields;
        Warnings = warnings.Select(w => w.Length > WarningMaxLength ? w[..WarningMaxLength] : w).ToList();
        ReadAt = now;
        UpdatedAt = now;
    }

    /// <summary>"Digitar a nota" depois de uma leitura que não serviu (CA 9): o mesmo formulário, vazio; o PDF fica de anexo.</summary>
    public void SwitchToManual(DateTimeOffset now)
    {
        Source = IncomingInvoiceSource.Manual;
        Status = IncomingReadingStatus.Ready;
        Fields = [];
        AccessKey = null;
        UpdatedAt = now;
    }

    public void MarkXmlRequested(Guid? by, DateTimeOffset now)
    {
        XmlRequestedAt = now;
        XmlRequestedBy = by;
        UpdatedAt = now;
    }

    /// <summary>"Conferir nota" (CA 7 a 9): quem conferiu, quando e o que corrigiu; o rascunho não muda mais.</summary>
    public void MarkChecked(Guid invoiceId, string accessKey, IReadOnlyList<string> corrected, Guid? by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(corrected);
        InvoiceId = invoiceId;
        Status = IncomingReadingStatus.Checked;
        AccessKey = accessKey;
        CorrectedFields = corrected;
        CorrectedBy = corrected.Count > 0 ? by : null;
        CheckedBy = by;
        CheckedAt = now;
        UpdatedAt = now;
    }
}

/// <summary>A chave de acesso desmontada (CA 4): UF, ano e mês, CNPJ do emitente, modelo, série, número, forma de emissão e o DV.</summary>
public sealed record AccessKeyParts(string Key, string StateCode, int Year, int Month, string IssuerDocument, string Model, int Series, long Number, string EmissionType);

/// <summary>Regras puras da leitura assistida e da nota digitada (FIS-05).</summary>
public static class IncomingReadingRules
{
    public const string RateLimitPolicy = "fiscal-pdf-reading";

    /// <summary>PDF de até 5 MB, de 1 a 10 páginas, lido em até 30 s (CA 1 e 2).</summary>
    public const int MaxPdfBytes = 5 * 1024 * 1024;

    public const int MaxPages = 10;

    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Itens por nota (o leiaute da NF-e vai até 990).</summary>
    public const int MaxItems = 990;

    public const string UnsafeMessage = "Este PDF não pode ser lido com segurança. Peça o XML ao fornecedor.";
    public const string NoTextMessage = "Este PDF não tem texto para ler. Peça o XML ao fornecedor ou digite a nota.";
    public const string FailedMessage = "Não conseguimos ler este PDF. Peça o XML ou digite a nota.";
    public const string PagesMessage = "O PDF precisa ter de 1 a 10 páginas. Peça o XML ao fornecedor ou digite a nota.";
    public const string NoDfeMessage = "Não conseguimos buscar o XML desta nota automaticamente.";
    public const string KeyDiscardedWarning = "A chave de acesso lida tem o dígito verificador errado e foi descartada. Digite a chave da nota.";
    public const string IssuerMismatchWarning = "O CNPJ do emitente no texto do PDF não bate com o da chave e foi ignorado: vale o CNPJ da chave.";
    public const string PdfMark = "Lida do PDF, sem XML";
    public const string ManualMark = "Digitada, sem XML";

    /// <summary>Marca da nota sem XML, para as telas e os títulos a pagar (CA 8 e 9); null na nota do XML.</summary>
    public static string? MarkOf(IncomingInvoiceSource source) => source switch
    {
        IncomingInvoiceSource.Pdf => PdfMark,
        IncomingInvoiceSource.Manual => ManualMark,
        _ => null,
    };

    /// <summary>
    /// O PDF passou pela conferência de segurança do worker (sem conteúdo ativo nem criptografia) e pode ser mostrado na tela e ficar como
    /// anexo da nota: lido, sem texto ou com páginas demais. Recusado, inválido, que falhou ou estourou o tempo não é mostrado.
    /// </summary>
    public static bool PassedSafety(PdfReadingOutcome? outcome) => outcome is PdfReadingOutcome.Read or PdfReadingOutcome.NoText or PdfReadingOutcome.TooManyPages;

    public const string PdfNotShownMessage = "Este PDF não passou pela conferência de segurança e não é mostrado. Peça o XML ao fornecedor ou digite a nota.";

    public static string MessageOf(PdfReadingOutcome outcome) => outcome switch
    {
        PdfReadingOutcome.Unsafe => UnsafeMessage,
        PdfReadingOutcome.NoText => NoTextMessage,
        PdfReadingOutcome.TooManyPages => PagesMessage,
        PdfReadingOutcome.Read => string.Empty,
        _ => FailedMessage,
    };

    /// <summary>Campos do cabeçalho do formulário, na ordem da tela (a chave primeiro).</summary>
    public static IReadOnlyList<string> HeaderKeys { get; } =
    [
        "accessKey", "issuedAt", "issuerName", "recipientDocument",
        "totals.products", "totals.freight", "totals.insurance", "totals.discount", "totals.otherExpenses", "totals.ipi",
        "totals.icmsBase", "totals.icms", "totals.icmsStBase", "totals.icmsSt", "totals.total",
    ];

    /// <summary>Campos de cada item (CA 7: código, descrição, NCM, unidade, quantidade, valor unitário e total).</summary>
    public static IReadOnlyList<string> ItemFieldKeys { get; } = ["code", "description", "ncm", "unit", "quantity", "unitPrice", "amount"];

    public static string ItemKey(int index, string field) => string.Create(CultureInfo.InvariantCulture, $"items.{index}.{field}");

    /// <summary>Todas as chaves que precisam ser conferidas para uma nota com <paramref name="items"/> itens (CA 7).</summary>
    public static IReadOnlyList<string> RequiredKeys(int items) =>
        [.. HeaderKeys, .. Enumerable.Range(0, Math.Max(0, items)).SelectMany(i => ItemFieldKeys.Select(f => ItemKey(i, f)))];

    /// <summary>Chave lida que é aceita: só as do formulário (o worker não inventa campo).</summary>
    public static bool IsKnownKey(string key)
    {
        if (HeaderKeys.Contains(key) || key == "issuerDocument")
        {
            return true;
        }

        var parts = key.Split('.');
        return parts.Length == 3 && parts[0] == "items" && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < MaxItems && ItemFieldKeys.Contains(parts[2]);
    }

    /// <summary>Desmonta a chave válida (44 dígitos e DV módulo 11); null se inválida.</summary>
    public static AccessKeyParts? Parse(string? key)
    {
        if (!IncomingInvoiceRules.IsValidAccessKey(key))
        {
            return null;
        }

        var k = key!;
        int Int(int start, int length) => int.Parse(k.AsSpan(start, length), NumberStyles.None, CultureInfo.InvariantCulture);
        return new AccessKeyParts(k, k[..2], 2000 + Int(2, 2), Int(4, 2), k.Substring(6, 14), k.Substring(20, 2), Int(22, 3), long.Parse(k.AsSpan(25, 9), NumberStyles.None, CultureInfo.InvariantCulture),
            k.Substring(34, 1));
    }

    /// <summary>Emitente pessoa física na chave (CPF com três zeros na frente e CNPJ inválido): recusado, como na FIS-04a.</summary>
    public static bool IsPersonIssuer(AccessKeyParts key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return !BrazilianDocuments.IsValidCnpj(key.IssuerDocument) && key.IssuerDocument.StartsWith("000", StringComparison.Ordinal) && BrazilianDocuments.IsValidCpf(key.IssuerDocument[3..]);
    }

    /// <summary>"A soma dos itens (R$ X) não bate com o total da nota (R$ Y)." (CA 8), ou null quando bate.</summary>
    public static string? SumMismatch(IEnumerable<decimal> amounts, decimal products)
    {
        var sum = amounts.Sum();
        return sum == products ? null : $"A soma dos itens ({Money(sum)}) não bate com o total da nota ({Money(products)}).";
    }

    /// <summary>Total da nota pela fórmula do <c>ICMSTot</c> (produtos − desconto + frete + seguro + outras despesas + IPI + ICMS-ST).</summary>
    public static decimal ExpectedTotal(IncomingInvoiceTotals t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return t.Products - t.Discount + t.Freight + t.Insurance + t.OtherExpenses + t.Ipi + t.IcmsSt;
    }

    public static string Money(decimal value) => "R$ " + value.ToString("#,##0.00", CultureInfo.GetCultureInfo("pt-BR"));
}
