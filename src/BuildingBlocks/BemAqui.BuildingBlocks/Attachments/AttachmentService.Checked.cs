using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Attachments;

public sealed partial class AttachmentService
{
    /// <summary>
    /// Lista dos anexos de um dono para uma tela que já conferiu o acesso por outra regra (PRD-03a CA 6: o app do operador lê os
    /// anexos da OS pela fila dele, sem <c>production.view</c>). Quem chama é responsável pela conferência; aqui só o dono e a
    /// varredura.
    /// </summary>
    public async Task<IReadOnlyList<AttachmentDto>> ListCheckedAsync(string ownerType, Guid ownerId, CancellationToken cancellationToken = default) =>
        (await db.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId && a.ScanStatus != ScanStatus.Infected)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken))
        .Select(ToDto).ToList();

    /// <summary>
    /// Download de um anexo que precisa ser daquele dono (PRD-03a CA 6); acesso conferido por quem chama. Anexo de outro dono
    /// responde como inexistente (404) e o arquivo só sai com a varredura limpa.
    /// </summary>
    public async Task<Result<AttachmentContent>> DownloadCheckedAsync(string ownerType, Guid ownerId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId && a.OwnerType == ownerType && a.OwnerId == ownerId, cancellationToken);
        if (attachment is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Anexo não encontrado.");
        }

        if (attachment.ScanStatus != ScanStatus.Clean)
        {
            return Error.Conflict("attachment.not_scanned", "O arquivo ainda está sendo verificado pelo antivírus. Tente de novo em alguns minutos.");
        }

        return new AttachmentContent(await storage.OpenReadAsync(attachment.StoragePath, cancellationToken), attachment.ContentType, attachment.FileName);
    }
}