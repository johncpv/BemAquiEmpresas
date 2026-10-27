using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Situação do boleto (<c>04-fluxos-estados</c> §1 "Boleto (Inter)", BNK-01b): <c>Requested → Issued → Paid | Cancelled | Expired</c>, e
/// <c>Requested → Failed → Requested</c> ("Tentar de novo"). <c>Requested</c> e <c>Issued</c> são os ativos (um por título, índice único
/// parcial); <c>PaidPendingReview</c> é da BNK-02.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BillingSlipStatus>))]
public enum BillingSlipStatus
{
    Requested,
    Issued,
    Failed,
    Paid,
    Cancelled,
    Expired,
}

/// <summary>
/// Motivo do cancelamento (CA 16 e 17), mapeado para o texto que vai ao Inter (<c>motivoCancelamento</c>, conferido no sandbox). Os
/// quatro últimos são automáticos: o título mudou depois da emissão.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BillingSlipCancelReason>))]
public enum BillingSlipCancelReason
{
    PaidOtherMeans,
    CustomerRequest,
    IssueError,
    Replacement,
    ReceivableCancelled,
    AmountChanged,
    DueDateChanged,
    SettledOtherMeans,
}

/// <summary>De onde contam a multa e os juros do boleto de título vencido (CA 1 e 15): do vencimento original (padrão) ou do novo.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OverdueFineFrom>))]
public enum OverdueFineFrom
{
    OriginalDueDate,
    SlipDueDate,
}

/// <summary>
/// Boleto com Pix (bolepix) de um título a receber (<c>finance.billing_slips</c>, BNK-01b, ADR 0032 §6). Nunca é apagado: só é cancelado
/// ou expira. O boleto não muda o título (valor, situação e vencimento): só a baixa (BNK-02 ou FIN-01) muda. Os dados do pagador (nome,
/// documento, endereço e e-mail) são lidos do Parties no envio ao banco e nunca ficam aqui; <see cref="ProviderPayload"/> guarda só ids,
/// situação, datas e valores do retorno do banco e nunca sai pela API.
/// </summary>
[DocumentName("O boleto")]
public sealed class BillingSlip : Entity, IAuditable, IConcurrencyVersioned
{
    public const int MaxMessageLines = 3;
    public const int MaxMessageLineLength = 78;

    public Guid ReceivableId { get; init; }

    public Guid BankAccountId { get; init; }

    public BankProvider Provider { get; init; } = BankProvider.Inter;

    /// <summary>"Seu número" (curto e opaco, base 36, único por conta): reusado na nova tentativa, porque o Inter recusa duplicidade.</summary>
    public string OurReference { get; init; } = string.Empty;

    /// <summary>"Nosso número" do banco.</summary>
    public string? OurNumber { get; private set; }

    /// <summary>Código da solicitação no Inter (emissão assíncrona: os dados chegam por consulta).</summary>
    public string? ProviderRequestId { get; private set; }

    public string? Barcode { get; private set; }

    public string? DigitableLine { get; private set; }

    public string? PixCopyPaste { get; private set; }

    /// <summary>Texto do QR Code (o mesmo do Pix copia-e-cola); a imagem é desenhada na tela.</summary>
    public string? QrCode { get; private set; }

    /// <summary>Saldo do título na emissão (nunca maior).</summary>
    public decimal Amount { get; init; }

    public DateOnly DueDate { get; init; }

    /// <summary>Vencimento do título na emissão: no título vencido, a multa e os juros contam desta data (CA 15).</summary>
    public DateOnly ReceivableDueDate { get; init; }

    /// <summary>Data de onde contam a multa e os juros (Configurações → Cobrança): base da divisão do valor pago na BNK-02.</summary>
    public DateOnly FineFrom { get; init; }

    public decimal FinePct { get; init; }

    public decimal InterestMonthPct { get; init; }

    public decimal DiscountAmount { get; init; }

    public DateOnly? DiscountUntil { get; init; }

    /// <summary>Mensagem ao pagador: até 3 linhas, sem dado de outro cliente.</summary>
    public string? Message { get; init; }

    /// <summary>Quem confirmou acima dos limites (CA 5): fica no histórico do boleto.</summary>
    public Guid? ApprovedBy { get; init; }

    public Guid? PdfAttachmentId { get; private set; }

    public BillingSlipStatus Status { get; private set; } = BillingSlipStatus.Requested;

    /// <summary>Motivo traduzido da última falha (PT-BR, sem dado do pagador).</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Ação sugerida na falha: <c>fix_party</c> ("Corrigir cadastro do cliente") ou <c>retry</c> ("Tentar de novo").</summary>
    public string? FailureAction { get; private set; }

    /// <summary>Envios ao Inter já tentados: a partir da segunda, a emissão consulta pelo "seu número" antes de criar (CA 8).</summary>
    public int IssueAttempts { get; private set; }

    /// <summary>Consultas feitas esperando o banco confirmar a emissão (padrão 6).</summary>
    public int PollAttempts { get; private set; }

    public BillingSlipCancelReason? CancelReason { get; private set; }

    /// <summary>Cancelamento pedido e ainda não confirmado pelo banco (o job leva ao Inter).</summary>
    public DateTimeOffset? CancelRequestedAt { get; private set; }

    public Guid? CancelRequestedBy { get; private set; }

    /// <summary>Motivo da recusa do último cancelamento no banco (o boleto continua emitido).</summary>
    public string? CancelFailureReason { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public DateTimeOffset? PaidAt { get; set; }

    public decimal? PaidAmount { get; set; }

    public DateTimeOffset? ExpiredAt { get; private set; }

    /// <summary>JSONB do retorno do banco, só com ids, situação, datas e valores (nunca o pagador). Não sai pela API.</summary>
    public string? ProviderPayload { get; private set; }

    /// <summary><c>Idempotency-Key</c> do pedido (CA 6 e 9): a mesma chave devolve o boleto original.</summary>
    public Guid? ClientRequestId { get; init; }

    /// <summary>"Enviar por e-mail ao emitir" (CA 4 e 13): o envio sai quando o banco confirmar.</summary>
    public bool SendOnIssue { get; init; }

    /// <summary>Envio adiado pelo limite diário (CA 13): sai neste horário (08:00 do dia seguinte), para este destinatário.</summary>
    public DateTimeOffset? ScheduledEmailAt { get; private set; }

    [PersonalData]
    public string? ScheduledEmailTo { get; private set; }

    [PersonalData]
    public string? ScheduledEmailCc { get; private set; }

    public DateTimeOffset? LastEmailAt { get; private set; }

    public DateTimeOffset RequestedAt { get; init; }

    public Guid? RequestedBy { get; init; }

    public DateTimeOffset? IssuedAt { get; private set; }

    public uint Version { get; set; }

    public bool IsActive => Status is BillingSlipStatus.Requested or BillingSlipStatus.Issued;

    /// <summary>Ainda ocupa o título (ativo ou falha à espera de "Tentar de novo" ou do cancelamento).</summary>
    public bool HoldsReceivable => IsActive || Status == BillingSlipStatus.Failed;

    public bool CancelPending => CancelRequestedAt is not null && Status is not (BillingSlipStatus.Cancelled or BillingSlipStatus.Expired or BillingSlipStatus.Paid);

    /// <summary>Chegou ao banco (ou pode ter chegado): o cancelamento precisa ir ao Inter.</summary>
    public bool MayExistAtBank => ProviderRequestId is not null || IssueAttempts > 0;

    public void RecordAttempt() => IssueAttempts++;

    public void RecordRequestId(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ProviderRequestId = requestId.Length > 64 ? requestId[..64] : requestId;
    }

    public void RecordPoll() => PollAttempts++;

    /// <summary>Banco confirmou (CA 7): linha digitável, código de barras, nosso número, Pix e o PDF guardado.</summary>
    public void MarkIssued(BillingSlipBankData data, Guid pdfAttachmentId, string? payload, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(data);
        Require(BillingSlipStatus.Requested);
        OurNumber = Cut(data.OurNumber, 40);
        Barcode = Cut(data.Barcode, 60);
        DigitableLine = Cut(data.DigitableLine, 60);
        PixCopyPaste = Cut(data.PixCopyPaste, 500);
        QrCode = Cut(data.PixCopyPaste, 500);
        PdfAttachmentId = pdfAttachmentId;
        ProviderPayload = payload;
        FailureReason = null;
        FailureAction = null;
        Status = BillingSlipStatus.Issued;
        IssuedAt = at;
    }

    /// <summary>Falha da emissão (CA 8): o título continua como estava.</summary>
    public void MarkFailed(string reason, string action, string? payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Require(BillingSlipStatus.Requested);
        FailureReason = Cut(reason, 300);
        FailureAction = action;
        if (payload is not null)
        {
            ProviderPayload = payload;
        }

        Status = BillingSlipStatus.Failed;
    }

    /// <summary>"Tentar de novo" (CA 8): o mesmo "seu número"; a emissão consulta o banco antes de criar outra cobrança.</summary>
    public void Retry()
    {
        Require(BillingSlipStatus.Failed);
        Status = BillingSlipStatus.Requested;
        PollAttempts = 0;
        FailureReason = null;
        FailureAction = null;
    }

    /// <summary>Pedido de cancelamento (manual, CA 16, ou automático, CA 17): o job leva ao banco e confirma.</summary>
    public void RequestCancel(BillingSlipCancelReason reason, Guid? by, DateTimeOffset at)
    {
        if (!HoldsReceivable)
        {
            throw new InvalidOperationException("Boleto " + Status + " não se cancela.");
        }

        CancelReason = reason;
        CancelRequestedAt = at;
        CancelRequestedBy = by;
        CancelFailureReason = null;
    }

    /// <summary>O banco recusou o cancelamento: o boleto continua como estava, com o motivo.</summary>
    public void CancelRefused(string reason)
    {
        CancelRequestedAt = null;
        CancelFailureReason = Cut(reason, 300);
    }

    /// <summary>Cancelado (confirmado pelo banco ou, sem nada no banco, só aqui). O título fica sem boleto.</summary>
    public void Cancel(DateTimeOffset at, string? payload)
    {
        if (!HoldsReceivable)
        {
            throw new InvalidOperationException("Boleto " + Status + " não se cancela.");
        }

        CancelReason ??= BillingSlipCancelReason.IssueError;
        Status = BillingSlipStatus.Cancelled;
        CancelledAt = at;
        CancelledBy = CancelRequestedBy;
        if (payload is not null)
        {
            ProviderPayload = payload;
        }
    }

    /// <summary>Vencido além do prazo de baixa do banco (CA 18): o título continua em aberto, vencido.</summary>
    public void Expire(DateTimeOffset at, string? payload)
    {
        Require(BillingSlipStatus.Issued);
        Status = BillingSlipStatus.Expired;
        ExpiredAt = at;
        if (payload is not null)
        {
            ProviderPayload = payload;
        }
    }

    /// <summary>Envio adiado pelo limite diário (CA 13).</summary>
    public void ScheduleEmail(DateTimeOffset at, string to, IReadOnlyList<string> cc)
    {
        ArgumentNullException.ThrowIfNull(cc);
        ScheduledEmailAt = at;
        ScheduledEmailTo = to;
        ScheduledEmailCc = cc.Count > 0 ? string.Join(",", cc) : null;
    }

    public void EmailSent(DateTimeOffset at)
    {
        LastEmailAt = at;
        ScheduledEmailAt = null;
        ScheduledEmailTo = null;
        ScheduledEmailCc = null;
    }

    private void Require(BillingSlipStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Boleto {Status}: esperado {expected}.");
        }
    }

    private static string? Cut(string? value, int max) => value is { Length: > 0 } v ? (v.Length > max ? v[..max] : v) : null;
}

/// <summary>Dados do boleto confirmados pelo banco.</summary>
public sealed record BillingSlipBankData(string? OurNumber, string? Barcode, string? DigitableLine, string? PixCopyPaste);

/// <summary>Sequência do "seu número" por conta (<c>finance.billing_reference_sequences</c>), incrementada na transação do pedido.</summary>
public sealed class BillingReferenceSequence
{
    public Guid BankAccountId { get; init; }

    public long LastValue { get; set; }
}

/// <summary>"Seu número" curto e opaco (CA 6): a sequência da conta em base 36, com 6 caracteres no mínimo; nunca o número do título.</summary>
public static class BillingReference
{
    public const int MinLength = 6;

    /// <summary>Limite de caracteres do "seu número" no Inter (15, conferido no sandbox).</summary>
    public const int MaxLength = 15;

    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string ToBase36(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        var chars = new Stack<char>();
        while (value > 0)
        {
            chars.Push(Digits[(int)(value % 36)]);
            value /= 36;
        }

        var text = new string(chars.ToArray());
        return text.Length < MinLength ? text.PadLeft(MinLength, '0') : text;
    }

    public static long FromBase36(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return text.Aggregate(0L, (acc, c) => (acc * 36) + Digits.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal));
    }
}
