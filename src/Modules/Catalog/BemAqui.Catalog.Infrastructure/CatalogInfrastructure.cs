using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Catalog.Application;
using BemAqui.Catalog.Contracts;
using BemAqui.Catalog.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Catalog.Infrastructure;

public static class CatalogInfrastructure
{
    /// <summary>
    /// Catálogo de produtos (CAT-01): serviços, os contratos <see cref="ISellableProducts"/> e <see cref="ICatalogLookup"/>, o provedor de uso do
    /// item (item com produto não é excluído no Estoque), a auditoria de produto, categoria, código de barras, tabela e preço e o dono de anexo
    /// <c>ProductPhoto</c>, gravado só pela rota do produto.
    /// </summary>
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<CatalogDefaults>();
        services.TryAddScoped<CategoryService>();
        services.TryAddScoped<PriceService>();
        services.TryAddScoped<ProductPhotoService>();
        services.TryAddScoped<ProductService>();
        services.TryAddScoped<ProductExportService>();
        services.TryAddScoped<CatalogReadContract>();
        services.TryAddScoped<ISellableProducts>(sp => sp.GetRequiredService<CatalogReadContract>());
        services.TryAddScoped<ICatalogLookup>(sp => sp.GetRequiredService<CatalogReadContract>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IItemUsageProvider, CatalogItemUsage>());
        services.AddAuditableEntity<Product>(ProductService.Module);
        services.AddAuditableEntity<ProductCategory>(ProductService.Module);
        services.AddAuditableEntity<ProductBarcode>(ProductService.Module);
        services.AddAuditableEntity<PriceList>(ProductService.Module);
        services.AddAuditableEntity<PriceListItem>(ProductService.Module);
        services.AddAttachmentOwner(ProductPhotoService.Owner, new AttachmentOwnerRules(ProductService.Module, ["image/png", "image/jpeg", "image/webp"], 5 * 1024 * 1024,
            OwnerExists: ProductExists, CanAttach: ByProductOnly, StripImageMetadata: true));
        return services;
    }

    private static async Task<bool> ProductExists(IServiceProvider services, Guid id, CancellationToken cancellationToken) =>
        await services.GetRequiredService<ICatalogDb>().Products.AnyAsync(p => p.Id == id, cancellationToken);

    // A rota genérica de anexos não grava a foto do produto: o envio é pela rota do produto (catalog.edit).
    private static Task<Error?> ByProductOnly(IServiceProvider services, Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<Error?>(Error.Validation("attachment.by_product_only", "Envie a foto pela ficha do produto."));
}
