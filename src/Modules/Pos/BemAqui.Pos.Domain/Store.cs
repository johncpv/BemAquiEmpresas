using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BemAqui.SharedKernel;

namespace BemAqui.Pos.Domain;

/// <summary>
/// Loja (<c>pos.stores</c>, PDV-01 CA 2): código curto (único, entra no número da venda), nome, local de estoque (Estoque, só o id) e a tabela de
/// preço padrão (Catálogo, só o id). O CNPJ próprio da loja (estabelecimento) é da NFCE-07; o modo fiscal chega com a PP-05. Os parâmetros da loja
/// (PP-10 e PP-22) nasceram na OFF-01 com os padrões decididos pelo PO e descem na foto do caixa. Não é apagada: com caixa, só desativa.
/// </summary>
[DocumentName("Loja")]
public sealed class Store : Entity, IAuditable, IConcurrencyVersioned
{
    public const int MaxNameLength = 80;

    /// <summary>Chave do pacote de dados iniciais do Comércio (<c>loja</c>); null para as criadas pelo tenant.</summary>
    public const string RetailSeedKey = "loja";

    /// <summary>PP-10: padrão e teto da janela sem sincronizar (7 dias).</summary>
    public const int DefaultOfflineWindowHours = 168;

    /// <summary>OFF-02 CA 16: padrão do aviso de caixa sem enviar vendas.</summary>
    public const int DefaultSyncAlertHours = 4;

    /// <summary>PDV-07 (PP-22, decidida pelo PO em 24/11/2026): padrão do alerta de cancelamentos do turno, em % das vendas.</summary>
    public const decimal DefaultCancelAlertPercent = 3m;

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Guid StockLocationId { get; set; }

    public Guid DefaultPriceListId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Janela máxima sem sincronizar, em horas (PP-10, OFF-01): padrão e teto de 7 dias (168 h).</summary>
    public int OfflineWindowHours { get; set; } = DefaultOfflineWindowHours;

    /// <summary>Tolerância de quebra de caixa por forma de pagamento (PP-22), <c>numeric(18,2)</c>: padrão R$ 5,00. Usada pela CX-01.</summary>
    public decimal CashBreakTolerance { get; set; } = 5.00m;

    /// <summary>Prazo para troca, em dias (PP-22): padrão 30. Usado pela PDV-06.</summary>
    public int ExchangeDays { get; set; } = 30;

    /// <summary>
    /// Validade do vale-troca, em dias (PDV-06 CA 7): padrão <see cref="DefaultStoreCreditValidityDays"/>. A PP-22 não fixou o valor: padrão
    /// conservador a confirmar com o PO, por loja.
    /// </summary>
    public int StoreCreditValidityDays { get; set; } = DefaultStoreCreditValidityDays;

    public const int DefaultStoreCreditValidityDays = 90;

    /// <summary>Aviso de sangria a partir deste valor em dinheiro no caixa (PP-22), <c>numeric(18,2)</c>: padrão R$ 1.000,00. Usado pela CX-01 (CA 7).</summary>
    public decimal CashDropAlertAmount { get; set; } = 1000.00m;

    /// <summary>
    /// Teto de dinheiro no caixa (CX-01 CA 8), <c>numeric(18,2)</c>: acima dele a forma Dinheiro fica bloqueada até a sangria. Nulo = sem teto (o
    /// padrão, porque a PP-22 não fixou valor).
    /// </summary>
    public decimal? CashCeilingAmount { get; set; }

    /// <summary>
    /// Documento fiscal da loja (IMP-01, PP-05): <see cref="StoreFiscalMode.Nfce"/> (o padrão; a NFC-e chega na Fase 13) ou "só cupom não fiscal",
    /// que só o Proprietário liga, com aceite registrado. Até existir a NFC-e, a loja só vende com o modo não fiscal aceito.
    /// </summary>
    public StoreFiscalMode FiscalMode { get; private set; } = StoreFiscalMode.Nfce;

    /// <summary>Versão do texto do aceite que o Proprietário aceitou ao ligar o modo não fiscal (nula: nunca aceitou ou o modo está desligado).</summary>
    public int? NonFiscalTermsVersion { get; private set; }

    public DateTimeOffset? NonFiscalAcceptedAt { get; private set; }

    public Guid? NonFiscalAcceptedBy { get; private set; }

    /// <summary>Cupom depois da venda (IMP-01 CA 5): perguntar (o padrão), imprimir sempre ou não imprimir.</summary>
    public ReceiptPrintMode ReceiptPrintMode { get; set; } = ReceiptPrintMode.Ask;

    /// <summary>
    /// OFF-02 (requisito da PDV-02): o último item lançado sai sem supervisor. O servidor confere a remoção do aparelho por este valor, nunca pelo
    /// guardado no aparelho. Padrão: ligado.
    /// </summary>
    public bool FreeRemoveLast { get; set; } = true;

    /// <summary>
    /// PDV-04 (PP-38: por loja): desconto máximo, em % do valor da linha ou da venda, que o operador dá sem <c>pos.discount</c> nem supervisor. O
    /// padrão não está decidido (PP-22, sugestão de 5%): até a decisão, 0 (todo desconto pede a permissão ou o supervisor). A loja edita.
    /// </summary>
    public decimal DiscountLimitPercent { get; set; }

    /// <summary>OFF-02 CA 16: o gerente é avisado quando um caixa fica este número de horas sem enviar vendas (padrão 4 h, de 1 a 72).</summary>
    public int SyncAlertHours { get; set; } = DefaultSyncAlertHours;

    /// <summary>
    /// PDV-07 (PP-22, decidida pelo PO em 24/11/2026): o caixa aberto ganha o alerta "Cancelamentos acima de X%" quando o valor das vendas canceladas
    /// no turno passa deste percentual das vendas do turno (concluídas + canceladas). Padrão 3%; zero desliga.
    /// </summary>
    public decimal CancelAlertPercent { get; set; } = DefaultCancelAlertPercent;

    /// <summary>
    /// OFF-03 CA 13 (PP-22, recomendação conservadora aceita pelo PO em 24/11/2026): teto de cada aprovação de supervisor sem rede, em reais (a
    /// diferença entre antes e depois: o desconto, o item ou a venda cancelados). Padrão R$ 50,00; zero desliga a aprovação sem rede.
    /// </summary>
    public decimal OfflineApprovalMaxAmount { get; set; } = DefaultOfflineApprovalMaxAmount;

    /// <summary>OFF-03 CA 13: soma das aprovações sem rede numa sessão de caixa. Padrão R$ 150,00.</summary>
    public decimal OfflineApprovalSessionAmount { get; set; } = DefaultOfflineApprovalSessionAmount;

    /// <summary>OFF-03 CA 13: quantas aprovações sem rede numa sessão de caixa. Padrão 5.</summary>
    public int OfflineApprovalSessionCount { get; set; } = DefaultOfflineApprovalSessionCount;

    /// <summary>OFF-03 CA 13 (limite de alerta): a sessão com aprovações sem rede acima deste total vai para conferência no fechamento. Padrão R$ 100,00.</summary>
    public decimal OfflineApprovalReviewAmount { get; set; } = DefaultOfflineApprovalReviewAmount;

    /// <summary>OFF-03 CA 13: cancelamento de venda concluída sem rede, só da venda paga toda em dinheiro (padrão: ligado). Desligado, só com rede.</summary>
    public bool OfflineCancelSaleAllowed { get; set; } = true;

    public const decimal DefaultOfflineApprovalMaxAmount = 50.00m;
    public const decimal DefaultOfflineApprovalSessionAmount = 150.00m;
    public const int DefaultOfflineApprovalSessionCount = 5;
    public const decimal DefaultOfflineApprovalReviewAmount = 100.00m;

    public string? SeedKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }

    /// <summary>A loja está no modo só cupom não fiscal com um aceite registrado, de qualquer versão do texto (a tela usa para pedir o aceite novo, CA 13).</summary>
    public bool NonFiscalAccepted => FiscalMode == StoreFiscalMode.NonFiscal && NonFiscalTermsVersion is not null;

    /// <summary>
    /// A loja pode vender só com cupom não fiscal: o modo está ligado e o aceite é da versão vigente do texto. Com texto novo, o caixa para de vender
    /// até o Proprietário aceitar de novo (IMP-01 CA 13).
    /// </summary>
    public bool NonFiscalAcceptedFor(int currentTermsVersion) => FiscalMode == StoreFiscalMode.NonFiscal && NonFiscalTermsVersion == currentTermsVersion;

    /// <summary>Liga o modo "só cupom não fiscal" com o aceite do Proprietário (IMP-01 CA 10 e 13): guarda a versão do texto, quem e quando.</summary>
    public void AcceptNonFiscal(int termsVersion, Guid acceptedBy, DateTimeOffset at)
    {
        FiscalMode = StoreFiscalMode.NonFiscal;
        NonFiscalTermsVersion = termsVersion;
        NonFiscalAcceptedBy = acceptedBy;
        NonFiscalAcceptedAt = at;
    }

    /// <summary>Volta ao modo NFC-e (desliga o não fiscal); o aceite anterior continua no histórico da auditoria.</summary>
    public void DisableNonFiscal()
    {
        FiscalMode = StoreFiscalMode.Nfce;
        NonFiscalTermsVersion = null;
        NonFiscalAcceptedBy = null;
        NonFiscalAcceptedAt = null;
    }
}

/// <summary>Documento fiscal da venda na loja (IMP-01, PP-05): NFC-e (Fase 13) ou só cupom não fiscal, com aceite do Proprietário.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StoreFiscalMode>))]
public enum StoreFiscalMode
{
    Nfce,
    NonFiscal,
}

/// <summary>O que o caixa faz com o cupom ao concluir a venda (IMP-01 CA 5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReceiptPrintMode>))]
public enum ReceiptPrintMode
{
    Ask,
    Always,
    Never,
}

/// <summary>Conexão da impressora (IMP-01 CA 6): nesta story só a do navegador funciona; USB direto, rede e Bluetooth chegam na IMP-02 e na IMP-03.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrinterConnection>))]
public enum PrinterConnection
{
    Browser,
    Usb,
    Network,
    Bluetooth,
}

/// <summary>
/// Impressora de um caixa (<c>pos.printers</c>, IMP-01 CA 6 e 7): bobina de 58 mm (32 colunas) ou 80 mm (42 ou 48), o modelo da lista curta e a
/// conexão. Desce para o aparelho do caixa na sincronização. Não é apagada: desativa.
/// </summary>
[DocumentName("Impressora")]
public sealed class Printer : Entity, IAuditable, IConcurrencyVersioned
{
    public const int MaxNameLength = 60;
    public const int MaxModelLength = 40;

    public Guid StoreId { get; init; }

    public Guid RegisterId { get; init; }

    public string Name { get; set; } = string.Empty;

    public PrinterConnection Connection { get; set; } = PrinterConnection.Browser;

    public int PaperWidthMm { get; set; } = 80;

    public int Columns { get; set; } = 48;

    public string Model { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>"Saiu certo?" respondido com sim na página de teste (CA 6); nulo enquanto não foi confirmada.</summary>
    public DateTimeOffset? TestConfirmedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }
}

/// <summary>O que foi impresso (IMP-01 CA 4): o cupom da venda, a 2ª via, o comprovante do caixa e a página de teste.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrintRecordKind>))]
public enum PrintRecordKind
{
    Receipt,
    Reprint,
    CashReceipt,
    Test,
}

/// <summary>
/// Impressão registrada (<c>pos.print_records</c>, IMP-01 CA 4): eventos <c>ReceiptPrinted</c> e <c>ReceiptReprinted</c> do aparelho, com quem e
/// quando; o id vem do aparelho (reenviar não duplica). Nunca leva o CPF nem o conteúdo do cupom.
/// </summary>
public sealed class PrintRecord : Entity
{
    public Guid StoreId { get; init; }

    public Guid RegisterId { get; init; }

    public Guid? PrinterId { get; init; }

    public PrintRecordKind Kind { get; init; }

    /// <summary>Venda impressa (o id do aparelho) e o número dela; nulos na página de teste e no comprovante do caixa.</summary>
    public Guid? SaleId { get; init; }

    public string? SaleNumber { get; init; }

    /// <summary>1 = original; 2 em diante = 2ª via, 3ª via...</summary>
    public int Copy { get; init; } = 1;

    public Guid OperatorId { get; init; }

    public DateTimeOffset PrintedAt { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }

    public static PrintRecord FromDevice(Guid id, Guid storeId, Guid registerId, Guid? printerId, PrintRecordKind kind, Guid? saleId, string? saleNumber, int copy,
        Guid operatorId, DateTimeOffset printedAt, DateTimeOffset receivedAt) =>
        new()
        {
            Id = id, StoreId = storeId, RegisterId = registerId, PrinterId = printerId, Kind = kind, SaleId = saleId, SaleNumber = saleNumber, Copy = copy,
            OperatorId = operatorId, PrintedAt = printedAt, ReceivedAt = receivedAt,
        };
}

/// <summary>Regras da impressora (IMP-01 CA 3 e 6): 58 mm com 32 colunas; 80 mm com 42 ou 48.</summary>
public static class PrinterRules
{
    public static bool IsValidLayout(int paperWidthMm, int columns) => (paperWidthMm, columns) is (58, 32) or (80, 42) or (80, 48);
}

/// <summary>Modo de tela do caixa (PDV-01 CA 3): toque (grade de produtos) ou teclado e leitor de código de barras.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RegisterUiMode>))]
public enum RegisterUiMode
{
    Touch,
    Keyboard,
}

/// <summary>
/// Caixa de uma loja (<c>pos.registers</c>, PDV-01 CA 3 e 6): número único na loja, a conta do tipo Caixa do Financeiro (só o id, única entre os
/// caixas), o modo de tela e o aparelho ativo. <see cref="DeviceEpoch"/> sobe a cada troca de aparelho: a sequência das vendas recomeça em 1.
/// </summary>
[DocumentName("Caixa")]
public sealed class Register : Entity, IAuditable, IConcurrencyVersioned
{
    public const int MaxNumber = 99;

    public Guid StoreId { get; init; }

    public int Number { get; set; }

    public Guid CashAccountId { get; set; }

    public RegisterUiMode UiMode { get; set; } = RegisterUiMode.Keyboard;

    public Guid? ActiveDeviceId { get; private set; }

    /// <summary>Época do aparelho: 0 antes do primeiro aparelho; 1, 2... a cada aparelho registrado no caixa.</summary>
    public int DeviceEpoch { get; private set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }

    /// <summary>Aparelho novo no caixa (CA 6): a época sobe e devolve o aparelho que saiu.</summary>
    public Guid? AssignDevice(Guid deviceId)
    {
        var previous = ActiveDeviceId;
        ActiveDeviceId = deviceId;
        DeviceEpoch++;
        return previous;
    }

    /// <summary>O aparelho foi revogado: o caixa fica sem aparelho (a época continua; o próximo aparelho abre outra).</summary>
    public void ReleaseDevice(Guid deviceId)
    {
        if (ActiveDeviceId == deviceId)
        {
            ActiveDeviceId = null;
        }
    }
}

/// <summary>Regras de cadastro da loja e do caixa (PDV-01 CA 2 e 3).</summary>
public static partial class PosRules
{
    /// <summary>Código da loja: 2 a 6 letras maiúsculas ou números (entra no número da venda: <c>LOJA-01.1-000001</c>).</summary>
    public static bool IsValidStoreCode(string code) => StoreCodePattern().IsMatch(code);

    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public static bool IsValidRegisterNumber(int number) => number is >= 1 and <= Register.MaxNumber;

    [GeneratedRegex("^[A-Z0-9]{2,6}$")]
    private static partial Regex StoreCodePattern();
}
