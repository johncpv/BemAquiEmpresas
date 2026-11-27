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
        services.TryAddScoped<SaleCustomerDocumentService>(); // PDV-07 CA 6: máscara da lista e leitura auditada no detalhe
        services.TryAddScoped<SaleBackofficeService>(); // PDV-07: Loja → Vendas (lista, exportação, detalhe e trilha)
        services.TryAddScoped<NonFiscalSalesReportService>(); // IMP-01 CA 12: vendas sem documento fiscal
        services.TryAddScoped<BelowCostSalesReportService>(); // PDV-04 CA 4: vendas abaixo do custo
        services.TryAddScoped<SalesLookupContract>();
        services.TryAddScoped<ISaleLookup>(sp => sp.GetRequiredService<SalesLookupContract>());
        services.TryAddScoped<ISalesRevenue>(sp => sp.GetRequiredService<SalesLookupContract>());
        services.TryAddScoped<ICashSessionSales, CashSessionSalesContract>(); // CX-02: resumo das vendas da sessão para o fechamento do caixa
        services.TryAddScoped<SaleReturnService>(); // PDV-06: devolução, troca e vale-troca no caixa
        services.TryAddScoped<SaleReturnReportService>(); // PDV-06 CA 12: Loja → Devoluções
        services.TryAddSingleton<SaleLookupThrottle>(); // PDV-06, revisão de segurança (achado 8): teto de buscas por CPF por caixa
        services.TryAddScoped<StoreCreditService>();
        services.TryAddScoped<IStoreCreditRedemptions>(sp => sp.GetRequiredService<StoreCreditService>());
        services.AddAuditableEntity<SaleReturn>(SaleService.Module); // pedido, aprovação e conclusão da devolução
        services.AddAuditableEntity<StoreCredit>(SaleService.Module); // emissão e cada uso do vale (saldo antes e depois)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPaymentProvider, ManualPaymentProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPaymentMethodUsageProvider, SalePaymentMethodUsage>());
        services.AddAuditableEntity<Sale>(SaleService.Module);
        // VND-02a: pedido de venda (núcleo), parâmetros, cobrança por boleto e reações à baixa do título.
        services.TryAddScoped<SalesOrderParametersService>();
        services.TryAddScoped<SalesOrderService>();
        services.TryAddScoped<SalesOrderConsumers>();
        services.TryAddScoped<ISalesOrderLookup, SalesOrderLookup>();
        services.AddAuditableEntity<SalesOrder>(SaleService.Module);
        services.AddAuditableEntity<SalesOrderLine>(SaleService.Module);
        services.AddAuditableEntity<SalesOrderParameters>(SaleService.Module);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProductUsageProvider, SaleProductUsage>());
        return services;
    }
}
