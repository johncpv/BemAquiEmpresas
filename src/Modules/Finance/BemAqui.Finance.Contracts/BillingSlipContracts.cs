using BemAqui.SharedKernel;

namespace BemAqui.Finance.Contracts;

/// <summary>
/// Boleto emitido pelo banco (BNK-01b): só ids, valor e datas, sem dado pessoal. Consumido pela régua de cobrança (FIN-09) e, na Fase 8,
/// pelo portal (POR-01c), que leem a fonte no envio.
/// </summary>
public sealed record BillingSlipIssued(Guid BillingSlipId, Guid ReceivableId, Guid PartyId, decimal Amount, DateOnly DueDate) : IIntegrationEvent;

/// <summary>Boleto cancelado (confirmado pelo banco) ou expirado (BNK-01b CA 16 a 18), com o motivo em código.</summary>
public sealed record BillingSlipCancelled(Guid BillingSlipId, Guid ReceivableId, string Reason, bool Expired) : IIntegrationEvent;

/// <summary>
/// Boleto pago e baixado (INT-02, ADR 0053 adendo à BNK-02): publicado na mesma transação da baixa automática ou da conferência; o efeito interno
/// síncrono não muda. Só ids, valor e data (para o aviso <c>billing_slip.paid</c>).
/// </summary>
public sealed record BillingSlipPaid(Guid BillingSlipId, Guid ReceivableId, decimal PaidAmount, DateOnly CreditDate) : IIntegrationEvent;
