using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Catalog.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Catalog.Application;

/// <summary>
/// Foto do produto (CA 16): dono <c>ProductPhoto</c> (PNG, JPG, WebP até 5 MB, sem EXIF/GPS), sempre no produto raiz. A variação usa a foto do
/// pai: enviar pela variação grava no pai, e todas as variações passam a mostrar a nova. Escrita com <c>catalog.edit</c>, leitura com
/// <c>catalog.view</c>, só pela rota do produto (anexo de outro produto = 404).
/// </summary>
public sealed class ProductPhotoService(ICatalogDb db, AttachmentService attachments, ITenantContext tenant, IEventPublisher events)
{
    public const string Owner = "ProductPhoto";

    public async Task<Result<AttachmentDto>> UploadAsync(Guid id, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        if (await RootAsync(id, cancellationToken) is not { } root)
        {
            return NotFound();
        }

        var uploaded = await attachments.UploadCheckedAsync(Owner, root, fileName, content, (tenant.Tenant?.Id ?? Guid.Empty).ToString("N"), cancellationToken);
        if (uploaded.IsSuccess)
        {
            events.Publish(new ProductChanged(root));
            foreach (var variant in await db.Products.Where(v => v.VariantOfProductId == root).Select(v => v.Id).ToListAsync(cancellationToken))
            {
                events.Publish(new ProductChanged(variant));
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return uploaded;
    }

    public async Task<Result<AttachmentContent>> DownloadAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default) =>
        await RootAsync(id, cancellationToken) is { } root ? await attachments.DownloadCheckedAsync(Owner, root, attachmentId, cancellationToken) : NotFound();

    /// <summary>A foto limpa mais recente de cada produto (a da variação é a do pai).</summary>
    public async Task<IReadOnlyDictionary<Guid, Guid>> PhotosAsync(IReadOnlyCollection<(Guid ProductId, Guid? ParentId)> products, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(products);
        var roots = products.Select(p => p.ParentId ?? p.ProductId).Distinct().ToList();
        var latest = roots.Count == 0 ? new Dictionary<Guid, Guid>() : await attachments.LatestCleanAsync(Owner, roots, cancellationToken);
        return products.Where(p => latest.ContainsKey(p.ParentId ?? p.ProductId)).ToDictionary(p => p.ProductId, p => latest[p.ParentId ?? p.ProductId]);
    }

    private async Task<Guid?> RootAsync(Guid id, CancellationToken ct) =>
        await db.Products.Where(p => p.Id == id).Select(p => (Guid?)(p.VariantOfProductId ?? p.Id)).FirstOrDefaultAsync(ct);

    private static Error NotFound() => Error.NotFound("product_not_found", "Produto não encontrado.");
}
