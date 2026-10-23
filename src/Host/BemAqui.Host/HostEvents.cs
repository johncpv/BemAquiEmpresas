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
            // PRD-01 CA 1 a 4: o equipamento vira OS de fabricação.
            .AddHandler<BemAqui.Commercial.Contracts.ProjectItemCreated, BemAqui.Production.Application.FabricationProjectItemCreatedConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.ContractSigned, BemAqui.Finance.Application.ContractSignedConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.ProjectFabricated, BemAqui.Finance.Application.MilestoneReachedConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.ProjectDelivered, BemAqui.Finance.Application.MilestoneReachedConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.ProjectDelivered, BemAqui.Commercial.Application.ContractCompletionConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.TechnicalAcceptance, BemAqui.Finance.Application.MilestoneReachedConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.ContractCancelled, BemAqui.Finance.Application.ContractCancelledConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.ContractAmended, BemAqui.Finance.Application.ContractAmendedConsumer>()
            .AddHandler<BemAqui.Commercial.Contracts.ContractCancelled, BemAqui.Production.Application.InstallationContractCancelledConsumer>()
            // PRD-01 CA 18: handler próprio da fabricação; CA 16: a EST-02 consome o cancelamento da OS.
            .AddHandler<BemAqui.Commercial.Contracts.ContractCancelled, BemAqui.Production.Application.FabricationContractCancelledConsumer>()
            .AddEvent<BemAqui.Production.Contracts.ProductionOrderCancelled>()
            // ENG-02 CA 8 e 13: BOM liberada e revisada; a EST-02 reconcilia as reservas a partir do IBoms (ADR 0023 §6).
            .AddEvent<BemAqui.Engineering.Contracts.BomReleased>()
            .AddEvent<BemAqui.Engineering.Contracts.BomRevised>()
            // PRD-05: decisões de qualidade no outbox para a DSH-03 e o PNL-01.
            .AddEvent<BemAqui.Production.Contracts.QualityApproved>()
            .AddEvent<BemAqui.Production.Contracts.QualityRejected>()
            // PRD-02 CA 8: "Concluir etapa" tira a OS de Aberta; o Commercial passa o projeto a InProgress.
            .AddHandler<BemAqui.Production.Contracts.ProductionStepStarted, BemAqui.Commercial.Application.FabricationConsumers>()
            // PRD-06a1 CA 7 e 10: a última OS de fabricação concluída passa o projeto a Fabricated (ProjectFabricated libera a parcela); o
            // estorno da conclusão volta o projeto a InProgress e avisa o Financeiro. A PRD-06a2 acrescenta a entrada no estoque.
            .AddHandler<BemAqui.Production.Contracts.ProductionOrderCompleted, BemAqui.Commercial.Application.FabricationCompletionConsumers>()
            .AddHandler<BemAqui.Production.Contracts.ProductionOrderCompletionReversed, BemAqui.Commercial.Application.FabricationCompletionConsumers>()
            // PRD-03a e PRD-04: apontamento fechado e corrigido viram custo de mão de obra na razão commercial.project_costs (ADR 0025 §2);
            // o que foi fechado antes da PRD-04 entra pela recomposição (D3, LaborCostBackfillJob).
            .AddHandler<BemAqui.Production.Contracts.LaborEntryClosed, BemAqui.Commercial.Application.LaborCostConsumer>()
            .AddHandler<BemAqui.Production.Contracts.LaborEntryCorrected, BemAqui.Commercial.Application.LaborCostConsumer>()
            // EST-01a: saída e devolução de material com projeto (ADR 0025 §2); o Commercial consome a partir da EST-03.
            .AddEvent<BemAqui.Inventory.Contracts.StockIssued>()
            .AddEvent<BemAqui.Inventory.Contracts.StockReturned>()
            // OS de instalação (COM-06).
            .AddHandler<BemAqui.Production.Contracts.InstallationStarted, BemAqui.Commercial.Application.InstallationConsumers>()
            .AddHandler<BemAqui.Production.Contracts.InstallationAccepted, BemAqui.Commercial.Application.InstallationConsumers>()
            // Financeiro (FIN-01, FIN-03a).
            .AddHandler<BemAqui.Finance.Contracts.ReceivableSettled, BemAqui.Commercial.Application.ContractCompletionConsumer>()
            .AddEvent<BemAqui.Finance.Contracts.PaymentOrderSubmitted>()
            .AddEvent<BemAqui.Finance.Contracts.PaymentOrderApproved>()
            .AddEvent<BemAqui.Finance.Contracts.PaymentOrderPaid>()
            .AddEvent<BemAqui.Finance.Contracts.PaymentOrderLevelReleased>();
    }
}
