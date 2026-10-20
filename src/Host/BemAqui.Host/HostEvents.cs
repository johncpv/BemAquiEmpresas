using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Identity.Contracts;

namespace BemAqui.Host;

/// <summary>Eventos de integração conhecidos pelo processo (API e CLI usam a mesma lista).</summary>
public static class HostEvents
{
    public static void Register(IntegrationEventRegistry events)
    {
        ArgumentNullException.ThrowIfNull(events);
        events.AddEmailEvents().AddEvent<UserActivated>()
            // CAD-06 → CAD-04a: o % de encargos recalcula o custo-hora dos cargos.
            .AddHandler<PayrollChargesChanged, BemAqui.HR.Application.PayrollChargesChangedHandler>()
            .AddEvent<BemAqui.HR.Contracts.HourlyCostChanged>()
            .AddEvent<BemAqui.HR.Contracts.EmployeeTerminated>()
            .AddEvent<BemAqui.HR.Contracts.EmployeeSalaryChanged>()
            // Orçamentos e contratos (COM-01b a COM-04b).
            .AddEvent<BemAqui.Commercial.Contracts.QuoteLost>()
            .AddEvent<BemAqui.Commercial.Contracts.QuoteExpired>()
            .AddEvent<BemAqui.Commercial.Contracts.QuoteApproved>()
            .AddEvent<BemAqui.Commercial.Contracts.ProjectItemCreated>()
            .AddHandler<BemAqui.Commercial.Contracts.ContractSigned, BemAqui.Finance.Application.ContractSignedConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.ProjectFabricated, BemAqui.Finance.Application.MilestoneReachedConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.ProjectDelivered, BemAqui.Finance.Application.MilestoneReachedConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.ProjectDelivered, BemAqui.Commercial.Application.ContractCompletionConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.TechnicalAcceptance, BemAqui.Finance.Application.MilestoneReachedConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.ContractCancelled, BemAqui.Finance.Application.ContractCancelledConsumer>()
            // Financeiro (FIN-01, FIN-03a).
            .AddHandler<BemAqui.Finance.Contracts.ReceivableSettled, BemAqui.Commercial.Application.ContractCompletionConsumer>()
            .AddEvent<BemAqui.Finance.Contracts.PaymentOrderSubmitted>()
            .AddEvent<BemAqui.Finance.Contracts.PaymentOrderApproved>()
            .AddEvent<BemAqui.Finance.Contracts.PaymentOrderPaid>();
    }
}
