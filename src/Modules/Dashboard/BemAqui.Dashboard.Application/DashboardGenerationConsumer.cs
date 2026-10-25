using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Eventos que mudam os números do painel (DSH-01a CA 12, ADR 0028 §3): recebimento, OP enviada, aprovada e paga, contrato
/// assinado e aditado e orçamento aprovado. Só sobem o contador de geração do tenant, o que invalida o painel dele inteiro; a
/// inbox garante uma vez por mensagem (e subir a mais só recalcula antes).
/// </summary>
public sealed class DashboardGenerationConsumer(DashboardCache cache, ITenantContext tenant)
    : IIntegrationEventConsumer<ReceivableSettled>,
      IIntegrationEventConsumer<PaymentOrderSubmitted>,
      IIntegrationEventConsumer<PaymentOrderApproved>,
      IIntegrationEventConsumer<PaymentOrderPaid>,
      IIntegrationEventConsumer<ContractSigned>,
      IIntegrationEventConsumer<ContractAmended>,
      IIntegrationEventConsumer<QuoteApproved>
{
    public Task HandleAsync(ReceivableSettled integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(PaymentOrderSubmitted integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(PaymentOrderApproved integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(PaymentOrderPaid integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(ContractSigned integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(ContractAmended integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(QuoteApproved integrationEvent, CancellationToken cancellationToken) => Bump();

    private Task Bump()
    {
        if (tenant.Tenant is { } current)
        {
            cache.Bump(current.Id);
        }

        return Task.CompletedTask;
    }
}
