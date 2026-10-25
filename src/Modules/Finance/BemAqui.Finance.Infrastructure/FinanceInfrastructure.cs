using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Finance.Infrastructure;

public static class FinanceInfrastructure
{
    /// <summary>Contas, caixas e condições de pagamento (CAD-05b): serviços, contrato <see cref="IPaymentTerms"/>, seed e histórico.</summary>
    public static IServiceCollection AddFinanceInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<BankAccountService>();
        services.TryAddScoped<PaymentTermService>();
        services.TryAddScoped<IPaymentTerms, PaymentTermsContract>();
        services.TryAddScoped<IPaymentTermImport, PaymentTermImport>();
        services.TryAddScoped<IBankAccountImport, BankAccountImport>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, PaymentTermSeeder>());
        // Plano de contas e centros de custo (CAD-05a).
        services.TryAddScoped<AccountService>();
        services.TryAddScoped<IChartOfAccounts>(sp => sp.GetRequiredService<AccountService>());
        services.TryAddScoped<CostCenterService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, ChartOfAccountsSeeder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, CostCentersSeeder>());
        // Contas a receber (FIN-01): títulos, baixas, transações, parâmetros e uso dos cadastros.
        services.TryAddScoped<ReceivableService>();
        services.TryAddScoped<ReceivableReports>();
        services.TryAddScoped<FinanceParametersService>();
        services.TryAddScoped<IFinanceParameters>(sp => sp.GetRequiredService<FinanceParametersService>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, FinanceParametersSeeder>());
        services.TryAddScoped<FinanceUsage>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IBankAccountUsageProvider, FinanceUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccountUsageProvider, FinanceUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICostCenterUsageProvider, FinanceUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPartyUsageProvider, FinanceUsage>());
        // Contas a pagar e recorrentes (FIN-02).
        services.TryAddScoped<PayableService>();
        services.TryAddScoped<IPayables, PayablesContract>();
        // Custo do projeto pelos títulos (DSH-02): somas em lote e o drill-down, com a regra única da PayableCostRules.
        services.TryAddScoped<IProjectPayableCosts, ProjectPayableCostsContract>();
        services.TryAddScoped<IReceivables, ReceivablesContract>();
        services.TryAddScoped<IFinancialOpening, FinancialOpeningContract>();
        services.TryAddScoped<PaymentStatsService>();
        services.TryAddScoped<ApprovalRuleService>();
        services.TryAddScoped<ApprovalResolver>();
        services.TryAddScoped<ApprovalSimulator>();
        // Motor de alçadas para os outros módulos (CMP-03a, ADR 0024 §4): o pedido de compra resolve a regra e confere a permissão do tipo.
        services.TryAddScoped<IApprovalRouting, ApprovalRoutingContract>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, ApprovalRuleSeeder>());
        services.AddAuditableEntity<ApprovalRule>("finance");
        services.AddAuditableEntity<PaymentOrderLevel>("finance");
        services.TryAddScoped<ICustomerPaymentStats>(sp => sp.GetRequiredService<PaymentStatsService>());
        services.TryAddScoped<FinanceDeadLetters>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Messaging.IDeadLetterObserver, FinanceDeadLetters>());
        services.TryAddScoped<RecurringExpenseService>();
        // Implantação financeira (FIN-00).
        services.TryAddScoped<OpeningService>();
        // Fluxo de caixa base (FIN-05).
        services.TryAddScoped<CashFlowService>();
        // Resumo do caixa projetado, base e ajustado, para o painel do dono (FIN-07 CA 14).
        services.TryAddScoped<ICashForecast>(sp => sp.GetRequiredService<CashFlowService>());
        // Inadimplência e aviso dos vencidos (FIN-06a).
        services.TryAddScoped<AgingService>();
        // DRE gerencial (FIN-08): regra da receita pelo título (a FIS-03a acrescenta a pela nota e a data de corte; a PRD-06b, o
        // romaneio), parâmetros "A confirmar com a contadora" e o resumo da receita para o painel do dono.
        services.TryAddScoped<DreParametersService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRevenueRecognition, TitleRevenueRecognition>());
        services.TryAddScoped<IRevenueCutover, NoRevenueCutover>();
        services.TryAddScoped<IProjectShipments, NoProjectShipments>();
        services.TryAddScoped<RevenueRecognitionRouter>();
        services.TryAddScoped<DreService>();
        services.TryAddScoped<IRevenueSummary, RevenueSummaryContract>();
        // Ordens de pagamento (FIN-03a): comprovante só em OP paga, lido com finance.view e gravado com finance.pay.
        services.TryAddScoped<PaymentOrderService>();
        // Minha pauta (DSH-04): OP aguardando, OP para pagar, recebíveis vencidos e títulos a pagar vencendo sem OP.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Agenda.IAgendaSource, FinanceAgendaSource>());
        // Extrato, transferências e lançamentos sem OP (FIN-04).
        services.TryAddScoped<BankStatementService>();
        services.TryAddScoped<BankMovementService>();
        services.AddAuditableEntity<BankTransfer>("finance");
        services.AddAuditableEntity<BankEntry>("finance");
        services.AddAuditableEntity<PaymentOrder>("finance");
        services.AddAuditableEntity<PaymentOrderItem>("finance");
        services.AddAuditableEntity<PaymentOrderApproval>("finance");
        services.AddAuditableEntity<PayableSettlement>("finance");
        services.AddAttachmentOwner(PaymentOrderService.AttachmentOwner, new AttachmentOwnerRules("finance", ["application/pdf", "image/png", "image/jpeg"], 10 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IFinanceDb>().PaymentOrders.AnyAsync(o => o.Id == id, ct),
            ReadAction: "view", WriteAction: "pay",
            CanAttach: async (sp, id, ct) => await sp.GetRequiredService<IFinanceDb>().PaymentOrders.AnyAsync(o => o.Id == id && o.Status == PaymentOrderStatus.Paid, ct)
                ? null
                : Error.Conflict("attachment.owner_state", "Anexe o comprovante depois do pagamento.")));
        services.AddAuditableEntity<OpeningSetup>("finance");
        services.AddAuditableEntity<OpeningBatch>("finance");
        services.AddAuditableEntity<Payable>("finance");
        services.AddAuditableEntity<RecurringExpense>("finance");
        services.AddAuditableEntity<Receivable>("finance");
        services.AddAuditableEntity<ReceivableSettlement>("finance");
        services.AddAuditableEntity<FinancialTransaction>("finance");
        services.AddAuditableEntity<FinanceParameter>("settings");
        services.AddAuditableEntity<BankAccount>("finance");
        services.AddAuditableEntity<PaymentTerm>("finance");
        services.AddAuditableEntity<Account>("finance");
        services.AddAuditableEntity<CostCenter>("finance");
        return services;
    }
}
