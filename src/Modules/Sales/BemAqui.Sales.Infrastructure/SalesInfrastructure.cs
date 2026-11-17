using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
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
    /// <see cref="ISalesRevenue"/>; o provedor manual de pagamento; o uso da forma de pagamento (PAG-01 CA 6); a venda auditada (cancelamento); e a
    /// porta do produto vendável com o adaptador provisório pelo item de estoque (até a CAT-01).
    /// </summary>
    public static IServiceCollection AddSalesInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<SaleService>();
        services.TryAddScoped<ISaleIngestion>(sp => sp.GetRequiredService<SaleService>());
        services.TryAddScoped<IOrderPayments>(sp => sp.GetRequiredService<SaleService>());
        services.TryAddScoped<SalesLookupContract>();
        services.TryAddScoped<ISaleLookup>(sp => sp.GetRequiredService<SalesLookupContract>());
        services.TryAddScoped<ISalesRevenue>(sp => sp.GetRequiredService<SalesLookupContract>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPaymentProvider, ManualPaymentProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPaymentMethodUsageProvider, SalePaymentMethodUsage>());
        services.AddAuditableEntity<Sale>(SaleService.Module);

        // Único acoplamento com o catálogo (CAT-01): troca-se só este registro quando o contrato do Catalog entrar na main.
        services.TryAddScoped<ISaleProductResolver, InventoryItemProductResolver>();
        return services;
    }
}

/// <summary>
/// Adaptador <b>provisório</b> da porta <see cref="ISaleProductResolver"/>, até a CAT-01 (catálogo) entrar na <c>main</c>: o id do produto é o
/// id do item de estoque (<see cref="IItems"/>), sem grade, sem preço e sem tabela (a venda grava o preço que o canal informou). Item excluído ou
/// inativo volta como inativo. Na CAT-01, este adaptador passa a ler o <c>ISellableProducts.FindManyAsync</c>, que tem a mesma forma.
/// </summary>
public sealed class InventoryItemProductResolver(IItems items) : ISaleProductResolver
{
    public async Task<IReadOnlyList<SellableProductInfo>> FindManyAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        return (await items.FindAsync(productIds.Distinct().ToList(), cancellationToken))
            .Select(i => new SellableProductInfo(i.Id, null, i.Name, i.UnitId, i.UnitCode, i.Id, null, Guid.Empty, 0, i.IsActive && !i.IsDeleted, false, null))
            .ToList();
    }
}
