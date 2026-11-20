using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Finance.Contracts;
using BemAqui.Catalog.Contracts;
using BemAqui.Sales.Application;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Sales.Infrastructure;

public static class SalesInfrastructure
{
    /// <summary>
    /// Núcleo de vendas (VND-01): contratos <see cref="ISaleIngestion"/>, <see cref="IOrderPayments"/>, <see cref="ISaleLookup"/> e
    /// <see cref="ISalesRevenue"/>; o provedor manual de pagamento; o uso da forma de pagamento (PAG-01 CA 6); a venda auditada (cancelamento); e o
    /// uso do produto (CAT-01: produto vendido não é excluído, só desativado). O produto vendável vem do <see cref="ISellableProducts"/> do Catálogo.
    /// </summary>
    public static IServiceCollection AddSalesInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<SaleService>();
        services.TryAddScoped<ISaleIngestion>(sp => sp.GetRequiredService<SaleService>());
        services.TryAddScoped<IOrderPayments>(sp => sp.GetRequiredService<SaleService>());
        services.TryAddScoped<ISaleCustomerDocuments, SaleCustomerDocumentService>(); // OFF-02 CA 17: CPF/CNPJ cifrado com índice cego
        services.TryAddScoped<NonFiscalSalesReportService>(); // IMP-01 CA 12: vendas sem documento fiscal
        services.TryAddScoped<SalesLookupContract>();
        services.TryAddScoped<ISaleLookup>(sp => sp.GetRequiredService<SalesLookupContract>());
        services.TryAddScoped<ISalesRevenue>(sp => sp.GetRequiredService<SalesLookupContract>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPaymentProvider, ManualPaymentProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPaymentMethodUsageProvider, SalePaymentMethodUsage>());
        services.AddAuditableEntity<Sale>(SaleService.Module);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProductUsageProvider, SaleProductUsage>());
        return services;
    }
}
