using System.Security.Cryptography;
using BemAqui.SharedKernel;
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

    /// <summary>
    /// Apaga um arquivo do sistema do dono (LGPD-01a CA 10: a exportação do titular sai 30 dias depois da conclusão, ou quando é refeita): o
    /// arquivo no storage e a linha (a exclusão fica na auditoria do anexo). Só arquivo gerado pelo sistema; o chamador salva.
    /// </summary>
    public async Task<bool> DeleteSystemFileAsync(string ownerType, Guid ownerId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await db.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.OwnerType == ownerType && a.OwnerId == ownerId && a.Origin == AttachmentOrigin.System, cancellationToken);
        if (attachment is null)
        {
            return false;
        }

        await storage.DeleteAsync(attachment.StoragePath, cancellationToken);
        db.Attachments.Remove(attachment);
        return true;
    }

    /// <summary>
    /// Anonimização do titular (LGPD-01b CA 7): apaga do storage o conteúdo dos anexos do dono e mantém só a linha, com o nome trocado pelo
    /// marcador. Idempotente (anexo já apagado fica como está). O chamador confere o dono e salva (a auditoria leva só o nome dos campos).
    /// Devolve quantos anexos o dono tem (apagados agora ou antes).
    /// </summary>
    public async Task<int> RemoveContentAsync(string ownerType, IReadOnlyCollection<Guid> ownerIds, string marker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerIds);
        if (ownerIds.Count == 0)
        {
            return 0;
        }

        var list = await db.Attachments.Where(a => a.OwnerType == ownerType && ownerIds.Contains(a.OwnerId)).ToListAsync(cancellationToken);
        foreach (var attachment in list.Where(a => a.ContentRemovedAt is null))
        {
            if (!string.IsNullOrEmpty(attachment.StoragePath))
            {
                await storage.DeleteAsync(attachment.StoragePath, cancellationToken);
            }

            db.Entry(attachment).Property(a => a.FileName).CurrentValue = SafeDisplayName(marker);
            attachment.ContentRemovedAt = clock.GetUtcNow();
        }

        return list.Count;
    }

    /// <summary>Quantos anexos o dono tem (prévia da anonimização, CA 7).</summary>
    public Task<int> CountAsync(string ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken = default) =>
        ownerIds is { Count: > 0 } ? db.Attachments.CountAsync(a => a.OwnerType == ownerType && ownerIds.Contains(a.OwnerId), cancellationToken) : Task.FromResult(0);

    private static Error ContentRemoved() => Error.NotFound("attachment.content_removed", "O arquivo foi apagado na anonimização do titular.");

    /// <summary>Anexo por id, para quem já conferiu a permissão do dono (ex.: o PDF atual do orçamento).</summary>
    public Task<Attachment?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Attachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
}
