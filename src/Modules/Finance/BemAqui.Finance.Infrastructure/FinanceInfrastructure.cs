using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
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
        services.AddAuditableEntity<BankAccount>("finance");
        services.AddAuditableEntity<PaymentTerm>("finance");
        services.AddAuditableEntity<Account>("finance");
        services.AddAuditableEntity<CostCenter>("finance");
        return services;
    }
}
