using System.Globalization;
using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Domain;

/// <summary>Tipo do evento fiscal (FIS-03b): cancelamento e carta de correção da nota, inutilização de faixa de números.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FiscalEventType>))]
public enum FiscalEventType
{
    Cancel,
    Correction,
    Invalidation,
}

/// <summary>Situação do evento: pedido ao Emissor, registrado pela SEFAZ (com protocolo) ou recusado (com o motivo).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FiscalEventStatus>))]
public enum FiscalEventStatus
{
    Requested,
    Registered,
    Rejected,
}

/// <summary>
/// Evento fiscal (<c>fiscal.fiscal_events</c>, FIS-03b CA 1, 8, 10 e 16): cancelamento, carta de correção (sequência 1 a 20 na nota) ou
/// inutilização de faixa (sem nota: série e números). Só inserido e completado (registro, recusa, XML), nunca apagado (CA 13). O texto vai à
/// SEFAZ como está (justificativa ou correção, já saneado).
/// </summary>
[DocumentName("O evento fiscal")]
public sealed class FiscalEvent : Entity, IAuditable
{
    public const int TextMaxLength = 1000;

    public Guid? InvoiceId { get; init; }

    public FiscalEventType Type { get; init; }

    /// <summary>Ordem do evento do mesmo tipo na nota (a carta de correção conta de 1 a 20; cada tentativa de cancelamento soma 1).</summary>
    public int Sequence { get; init; } = 1;

    public string Text { get; init; } = string.Empty;

    public FiscalEventStatus Status { get; private set; } = FiscalEventStatus.Requested;

    public string? Protocol { get; private set; }

    public Guid? EmitterEventId { get; private set; }

    /// <summary>XML do evento guardado como anexo de sistema (dono <c>OutgoingInvoice</c>; na inutilização, o próprio evento).</summary>
    public Guid? XmlAttachmentId { get; private set; }

    public Guid? RequestedBy { get; init; }

    public DateTimeOffset RequestedAt { get; init; }

    public DateTimeOffset? RegisteredAt { get; private set; }

    public int? RejectionCode { get; private set; }

    public string? RejectionReason { get; private set; }

    /// <summary>Inutilização: série e a faixa (inclusive).</summary>
    public int? Series { get; init; }

    public long? FirstNumber { get; init; }

    public long? LastNumber { get; init; }

    /// <summary>Pedido de cancelamento atendido por este evento (CA 3).</summary>
    public Guid? CancellationRequestId { get; init; }

    /// <summary>Avisar o cliente por e-mail (opção marcada por padrão, CA 6 e 8).</summary>
    public bool NotifyCustomer { get; init; } = true;

    public DateTimeOffset? EmailedAt { get; set; }

    public bool IsRegistered => Status == FiscalEventStatus.Registered;

    /// <summary>A SEFAZ registrou o evento (idempotente: o segundo registro não muda nada).</summary>
    public bool Register(string? protocol, Guid? emitterEventId, DateTimeOffset at)
    {
        if (Status == FiscalEventStatus.Registered)
        {
            Protocol ??= protocol;
            EmitterEventId ??= emitterEventId;
            return false;
        }

        Status = FiscalEventStatus.Registered;
        Protocol = protocol;
        EmitterEventId = emitterEventId ?? EmitterEventId;
        RegisteredAt = at;
        RejectionCode = null;
        RejectionReason = null;
        return true;
    }

    /// <summary>Recusado (pelo Emissor antes da SEFAZ, ou pela SEFAZ): o motivo fica na aba Eventos.</summary>
    public void Reject(int? code, string? reason, DateTimeOffset at)
    {
        if (Status == FiscalEventStatus.Registered)
        {
            return;
        }

        Status = FiscalEventStatus.Rejected;
        RejectionCode = code;
        RejectionReason = reason is { Length: > OutgoingInvoice.ReasonMaxLength } ? reason[..OutgoingInvoice.ReasonMaxLength] : reason;
        RegisteredAt = at;
    }

    public void AttachXml(Guid attachmentId) => XmlAttachmentId ??= attachmentId;

    public static string Label(FiscalEventType type) => type switch
    {
        FiscalEventType.Cancel => "Cancelamento",
        FiscalEventType.Correction => "Carta de correção",
        _ => "Inutilização",
    };
}

/// <summary>Situação do pedido de cancelamento (CA 2 e 3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CancellationRequestStatus>))]
public enum CancellationRequestStatus
{
    Pending,
    Done,
    Declined,
    Expired,
}

/// <summary>
/// "Pedir cancelamento" (<c>fiscal.cancellation_requests</c>, D12, CA 2 e 3): quem não tem <c>fiscal.approve</c> pede com o motivo; o pedido
/// não mexe na nota, entra na pauta do Proprietário e expira sozinho no fim do prazo. Um pendente por nota (índice único parcial).
/// </summary>
[DocumentName("O pedido de cancelamento")]
public sealed class CancellationRequest : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid InvoiceId { get; init; }

    public string Reason { get; init; } = string.Empty;

    public Guid? RequestedBy { get; init; }

    public DateTimeOffset RequestedAt { get; init; }

    /// <summary>Fim do prazo de cancelamento da nota: depois dele, o pedido expira.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    public CancellationRequestStatus Status { get; private set; } = CancellationRequestStatus.Pending;

    public Guid? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? DecisionReason { get; private set; }

    public uint Version { get; set; }

    public bool IsPending => Status == CancellationRequestStatus.Pending;

    /// <summary>A nota foi cancelada: o pedido fica atendido (CA 3).</summary>
    public bool Complete(Guid? by, DateTimeOffset at) => Decide(CancellationRequestStatus.Done, by, null, at);

    /// <summary>"Recusar pedido" com motivo (CA 3).</summary>
    public bool Decline(Guid? by, string reason, DateTimeOffset at) => Decide(CancellationRequestStatus.Declined, by, reason, at);

    /// <summary>Fim do prazo sem decisão (CA 2): expira sozinho.</summary>
    public bool Expire(DateTimeOffset at) => at >= ExpiresAt && Decide(CancellationRequestStatus.Expired, null, null, at);

    private bool Decide(CancellationRequestStatus status, Guid? by, string? reason, DateTimeOffset at)
    {
        if (!IsPending)
        {
            return false;
        }

        Status = status;
        DecidedBy = by;
        DecidedAt = at;
        DecisionReason = reason;
        return true;
    }
}

/// <summary>Situação do número a inutilizar (CA 10).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NumberGapStatus>))]
public enum NumberGapStatus
{
    Pending,
    Invalidated,
}

/// <summary>Faixa contínua de números a inutilizar da mesma série (CA 10).</summary>
public sealed record NumberRange(int Series, long First, long Last)
{
    public long Count => Last - First + 1;

    public string Label => First == Last
        ? First.ToString(CultureInfo.InvariantCulture)
        : First.ToString(CultureInfo.InvariantCulture) + " a " + Last.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Regras do cancelamento, da carta de correção e da inutilização (FIS-03b): o prazo legal (horas da FIS-02a contadas da autorização), os
/// limites de texto da SEFAZ (conferidos depois do saneamento da FIS-03a), as 20 cartas por nota e as faixas contínuas.
/// </summary>
public static class FiscalEventRules
{
    public const int JustificationMin = 15;
    public const int JustificationMax = 255;
    public const int CorrectionMin = 15;
    public const int CorrectionMax = 1000;
    public const int MaxCorrections = 20;

    /// <summary>Tempo mínimo entre dois envios do mesmo cancelamento sem resposta (evita o duplo clique depois de recarregar).</summary>
    public static readonly TimeSpan ResendAfter = TimeSpan.FromMinutes(2);

    public const string OutOfWindow = "Fora do prazo de cancelamento. A devolução fica com a contadora.";
    public const string TooManyCorrections = "Esta nota já tem 20 cartas de correção.";
    public const string OnlyOwnerCancels = "Só o Proprietário cancela notas fiscais.";

    /// <summary>Fim do prazo: autorização + horas da configuração (sem autorização, sem prazo).</summary>
    public static DateTimeOffset? Deadline(DateTimeOffset? authorizedAt, int windowHours) => authorizedAt?.AddHours(windowHours);

    /// <summary>Dentro do prazo? O limite é exclusivo: no instante exato do fim, já está fora.</summary>
    public static bool WithinWindow(DateTimeOffset? deadline, DateTimeOffset now) => deadline is { } d && now < d;

    /// <summary>Justificativa do cancelamento ou da inutilização: saneada, de 15 a 255 caracteres. Devolve o texto limpo ou o erro.</summary>
    public static (string? Text, string? Error) Justification(string? value, string what = "o motivo")
    {
        var text = NfeTextRules.Clean(value);
        return text is null || text.Length < JustificationMin || text.Length > JustificationMax
            ? (text, $"Escreva {what} com {JustificationMin} a {JustificationMax} caracteres.")
            : (text, null);
    }

    /// <summary>Texto da carta de correção: saneado, de 15 a 1000 caracteres.</summary>
    public static (string? Text, string? Error) Correction(string? value)
    {
        var text = NfeTextRules.Clean(value);
        return text is null || text.Length < CorrectionMin || text.Length > CorrectionMax
            ? (text, $"Escreva a correção com {CorrectionMin} a {CorrectionMax} caracteres.")
            : (text, null);
    }

    /// <summary>Números da mesma série agrupados em faixas contínuas, em ordem (CA 10).</summary>
    public static IReadOnlyList<NumberRange> Ranges(IEnumerable<(int Series, long Number)> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        var result = new List<NumberRange>();
        foreach (var series in numbers.Distinct().GroupBy(n => n.Series).OrderBy(g => g.Key))
        {
            long? first = null, last = null;
            foreach (var n in series.Select(x => x.Number).Order())
            {
                if (last is { } l && n == l + 1)
                {
                    last = n;
                    continue;
                }

                if (first is { } f)
                {
                    result.Add(new NumberRange(series.Key, f, last!.Value));
                }

                first = n;
                last = n;
            }

            if (first is { } start)
            {
                result.Add(new NumberRange(series.Key, start, last!.Value));
            }
        }

        return result;
    }

    /// <summary>Situação da nota que impede inutilizar o número (CA 11): autorizada, cancelada ou ainda em andamento na SEFAZ.</summary>
    public static string? BlockingSituation(OutgoingInvoiceStatus status) => status switch
    {
        OutgoingInvoiceStatus.Authorized => "autorizada",
        OutgoingInvoiceStatus.Cancelled => "cancelada",
        OutgoingInvoiceStatus.Denied => "denegada",
        OutgoingInvoiceStatus.Processing or OutgoingInvoiceStatus.ContingencyPending => "em processamento",
        _ => null,
    };

    public static string Blocked(long number, string situation) =>
        $"O número {number.ToString(CultureInfo.InvariantCulture)} tem nota {situation} e não pode ser inutilizado.";
}
