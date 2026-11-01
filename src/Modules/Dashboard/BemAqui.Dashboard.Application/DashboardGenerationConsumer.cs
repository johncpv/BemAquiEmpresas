using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Fiscal.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Eventos que mudam os números do painel (DSH-01a CA 12, DSH-01b CA 5, ADR 0028 §3): recebimento, OP enviada, aprovada e paga, contrato
/// assinado e aditado, orçamento aprovado, metas, previsão do projeto e os custos do projeto (baixa, devolução, apontamento fechado e
/// corrigido). Só sobem o contador de geração do tenant, o que invalida o painel dele inteiro; a inbox garante uma vez por mensagem (e subir a
/// mais só recalcula antes). O custo do projeto pode ser gravado pelo Commercial depois deste aviso; os 60 s do cache cobrem a diferença.
/// </summary>
public sealed class DashboardGenerationConsumer(DashboardCache cache, ITenantContext tenant)
    : IIntegrationEventConsumer<ReceivableSettled>,
      IIntegrationEventConsumer<PaymentOrderSubmitted>,
      IIntegrationEventConsumer<PaymentOrderApproved>,
      IIntegrationEventConsumer<PaymentOrderPaid>,
      IIntegrationEventConsumer<ContractSigned>,
      IIntegrationEventConsumer<ContractAmended>,
      IIntegrationEventConsumer<QuoteApproved>,
      IIntegrationEventConsumer<RevenueTargetsChanged>,
      IIntegrationEventConsumer<ProjectForecastChanged>,
      IIntegrationEventConsumer<StockIssued>,
      IIntegrationEventConsumer<StockReturned>,
      IIntegrationEventConsumer<LaborEntryClosed>,
      IIntegrationEventConsumer<LaborEntryCorrected>,
      IIntegrationEventConsumer<InvoiceAuthorized>,
      IIntegrationEventConsumer<InvoiceCancelled>
{
    public Task HandleAsync(RevenueTargetsChanged integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(ReceivableSettled integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(PaymentOrderSubmitted integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(PaymentOrderApproved integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(PaymentOrderPaid integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(ContractSigned integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(ContractAmended integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(QuoteApproved integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(ProjectForecastChanged integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(StockIssued integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(StockReturned integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(LaborEntryClosed integrationEvent, CancellationToken cancellationToken) => Bump();

    public Task HandleAsync(LaborEntryCorrected integrationEvent, CancellationToken cancellationToken) => Bump();

    /// <summary>FIS-03a: a nota autorizada muda a receita (pela data da nota) e o imposto realizado do projeto.</summary>
    public Task HandleAsync(InvoiceAuthorized integrationEvent, CancellationToken cancellationToken) => Bump();

    /// <summary>FIS-03b: a nota cancelada sai da receita e o imposto dela sai do custo do projeto.</summary>
    public Task HandleAsync(InvoiceCancelled integrationEvent, CancellationToken cancellationToken) => Bump();

    private Task Bump()
    {
        if (tenant.Tenant is { } current)
        {
            cache.Bump(current.Id);
        }

        return Task.CompletedTask;
    }
}
