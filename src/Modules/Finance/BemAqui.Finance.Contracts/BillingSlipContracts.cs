using BemAqui.SharedKernel;

namespace BemAqui.Finance.Contracts;

/// <summary>
/// Boleto emitido pelo banco (BNK-01b): só ids, valor e datas, sem dado pessoal. Consumido pela régua de cobrança (FIN-09) e, na Fase 8,
/// pelo portal (POR-01c), que leem a fonte no envio.
/// </summary>
public sealed record BillingSlipIssued(Guid BillingSlipId, Guid ReceivableId, Guid PartyId, decimal Amount, DateOnly DueDate) : IIntegrationEvent;

/// <summary>Boleto cancelado (confirmado pelo banco) ou expirado (BNK-01b CA 16 a 18), com o motivo em código.</summary>
public sealed record BillingSlipCancelled(Guid BillingSlipId, Guid ReceivableId, string Reason, bool Expired) : IIntegrationEvent;
