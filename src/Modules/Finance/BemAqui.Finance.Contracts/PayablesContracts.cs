using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Contracts;

/// <summary>
/// Situação gravada do título a pagar (04-fluxos §1). A FIN-02 grava só Planned, Open e Cancelled; AwaitingApproval,
/// Approved, PartiallyPaid e Paid são da ordem de pagamento (FIN-03a). "Vencido" é calculado.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayableStatus>))]
public enum PayableStatus
{
    Planned,
    Open,
    AwaitingApproval,
    Approved,
    PartiallyPaid,
    Paid,
    Cancelled,
}

/// <summary>Forma de pagamento do título (D9): para o aprovador e o pagador verem para onde o dinheiro vai.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentMethodKind>))]
public enum PaymentMethodKind
{
    Pix,
    Boleto,
    Ted,
    Other,
}

[JsonConverter(typeof(JsonStringEnumConverter<PixKeyType>))]
public enum PixKeyType
{
    Cpf,
    Cnpj,
    Email,
    Phone,
    Random,
}

/// <summary>Título a pagar visto por quem o gerou (COM-06, compras, folha).</summary>
public sealed record PayableRef(Guid Id, string Number, PayableStatus Status, decimal Amount, DateOnly? DueDate, bool IsEstimate);

/// <summary>
/// Contas a pagar para os outros módulos (FIN-02 CA 14 e 15): o título nasce na transação do chamador (o chamador grava),
/// idempotente por <c>(origin, originId)</c>. Título já numa OP recusa mudar ou cancelar (<c>payable_in_payment_order</c>).
/// </summary>
public interface IPayables
{
    /// <param name="planned">True: nasce Planned (sem vencimento, ou com a data estimada e a marca de estimativa).</param>
    Task<Result<PayableRef>> CreateFromOriginAsync(
        DocumentOrigin origin, Guid originId, Guid partyId, Guid? projectId, decimal amount, DateOnly? dueDate, bool estimatedDueDate, string description, bool planned,
        CancellationToken cancellationToken = default);

    Task<Result<PayableRef>> ConfirmAsync(DocumentOrigin origin, Guid originId, decimal amount, DateOnly dueDate, CancellationToken cancellationToken = default);

    Task<Result<PayableRef>> UpdatePlannedAsync(DocumentOrigin origin, Guid originId, decimal amount, DateOnly? dueDate, CancellationToken cancellationToken = default);

    Task<Result> CancelFromOriginAsync(DocumentOrigin origin, Guid originId, string reason, CancellationToken cancellationToken = default);
}
