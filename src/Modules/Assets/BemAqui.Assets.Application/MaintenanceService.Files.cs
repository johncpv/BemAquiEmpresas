using BemAqui.BuildingBlocks.Attachments;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// Fotos e relatório do prestador na OS (CA 6 e 13): dono <c>MaintenanceOrder</c> (PNG, JPG, PDF até 10 MB, fotos sem EXIF/GPS pelo pipeline da
/// PAT-01a), lidos com <c>assets.view</c> e gravados com <c>assets.edit</c> só pela rota da OS. O anexo precisa ser da OS da rota (de outra, 404).
/// </summary>
public sealed partial class MaintenanceService
{
    public async Task<Result<MaintenanceOrderFiles>> FilesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.MaintenanceOrders.AnyAsync(o => o.Id == id, cancellationToken))
        {
            return OrderNotFound();
        }

        return new MaintenanceOrderFiles(await attachments.ListCheckedAsync(AttachmentOwner, id, cancellationToken));
    }

    public async Task<Result<AttachmentDto>> UploadAsync(Guid id, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        if (!await db.MaintenanceOrders.AnyAsync(o => o.Id == id, cancellationToken))
        {
            return OrderNotFound();
        }

        return await attachments.UploadCheckedAsync(AttachmentOwner, id, fileName, content, (tenant.Tenant?.Id ?? Guid.Empty).ToString("N"), cancellationToken);
    }

    public async Task<Result<AttachmentContent>> FileAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        if (!await db.MaintenanceOrders.AnyAsync(o => o.Id == id, cancellationToken))
        {
            return OrderNotFound();
        }

        return await attachments.DownloadCheckedAsync(AttachmentOwner, id, attachmentId, cancellationToken);
    }
}
