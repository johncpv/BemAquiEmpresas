using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
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
        services.TryAddScoped<RecurringExpenseService>();
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
