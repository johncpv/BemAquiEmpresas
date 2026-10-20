using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Contracts;

/// <summary>Situação da ordem de pagamento (04-fluxos §1): Pending → Approved → Paid; Rejected, Cancelled e Reversed (estorno).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentOrderStatus>))]
public enum PaymentOrderStatus
{
    Pending,
    Approved,
    Rejected,
    Paid,
    Cancelled,
    Reversed,
}

/// <summary>OP enviada para aprovação (FIN-03a CA 5).</summary>
public sealed record PaymentOrderSubmitted(Guid PaymentOrderId, decimal Total, Guid? RequestedBy) : IIntegrationEvent;

/// <summary>OP aprovada: quem paga é avisado (CA 7).</summary>
public sealed record PaymentOrderApproved(Guid PaymentOrderId) : IIntegrationEvent;

/// <summary>OP paga (CA 13), sem consumidor nesta fase.</summary>
public sealed record PaymentOrderPaid(Guid PaymentOrderId, DateOnly PaidOn) : IIntegrationEvent;
