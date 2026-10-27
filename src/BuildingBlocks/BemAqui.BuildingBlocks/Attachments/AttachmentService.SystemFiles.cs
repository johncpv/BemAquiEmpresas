using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Attachments;

public sealed partial class AttachmentService
{
    /// <summary>
    /// Arquivo gerado pelo sistema (ADR 0019 §7, COM-03 CA 3): interno, sem rota; grava com <c>Origin = System</c> na unidade de
    /// trabalho do chamador (o chamador salva) e passa pela varredura como qualquer anexo.
    /// </summary>
    public Task<Attachment> StoreGeneratedAsync(string ownerType, Guid ownerId, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default) =>
        StoreGeneratedAsync(ownerType, ownerId, fileName, contentType, content, null, cancellationToken);

    /// <summary>
    /// Igual ao anterior, com o arquivo debaixo do prefixo <paramref name="storagePrefix"/> no storage (ex.: o id do tenant, FIS-01 CA 13).
    /// </summary>
    public async Task<Attachment> StoreGeneratedAsync(string ownerType, Guid ownerId, string fileName, string contentType, byte[] content, string? storagePrefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var buffer = new MemoryStream(content, writable: false);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        var path = storagePrefix is null ? await storage.SaveAsync(buffer, cancellationToken) : await storage.SaveAsync(buffer, storagePrefix, cancellationToken);
        buffer.Position = 0;
        var scan = await scanner.ScanAsync(buffer, cancellationToken);
        var attachment = new Attachment
        {
            OwnerType = ownerType, OwnerId = ownerId, FileName = SafeDisplayName(fileName), ContentType = contentType, Size = content.Length, Sha256 = sha256, StoragePath = path,
            ScanStatus = scan.Verdict switch { ScanVerdict.Clean => ScanStatus.Clean, ScanVerdict.Infected => ScanStatus.Infected, _ => ScanStatus.Pending },
            ScannedAt = scan.Verdict == ScanVerdict.Clean ? clock.GetUtcNow() : null, CreatedAt = clock.GetUtcNow(), UploadedBy = currentUser.UserId, Origin = AttachmentOrigin.System,
        };
        db.Attachments.Add(attachment);
        return attachment;
    }

    /// <summary>Anexo por id, para quem já conferiu a permissão do dono (ex.: o PDF atual do orçamento).</summary>
    public Task<Attachment?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Attachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
}
