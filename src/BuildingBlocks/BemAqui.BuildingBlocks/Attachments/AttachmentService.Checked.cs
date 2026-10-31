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

        if (await AuditReadAsync(owners.RulesOf(ownerType), attachment, cancellationToken) is { } missing)
        {
            return missing;
        }

        return new AttachmentContent(await storage.OpenReadAsync(attachment.StoragePath, cancellationToken), attachment.ContentType, attachment.FileName);
    }

    /// <summary>
    /// Envio para um dono cujo acesso quem chama já conferiu pelo registro (RH-01b CA 8: o funcionário anexa o atestado ao próprio pedido sem
    /// <c>hr.view_health</c>). Valem as regras do dono (tipos, tamanho, <c>OwnerExists</c>, <c>CheckFile</c>), o limite de envios, os magic
    /// bytes e o ClamAV; o <c>CanAttach</c> do dono não vale aqui (ele fecha só a rota genérica). O arquivo vai para o storage debaixo de
    /// <paramref name="storagePrefix"/> (o id do tenant, revisão pré-Fases 4 a 8 §4.3).
    /// </summary>
    public async Task<Result<AttachmentDto>> UploadCheckedAsync(string ownerType, Guid ownerId, string fileName, Stream content, string storagePrefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var rules = owners.RulesOf(ownerType);
        if (rules is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Este tipo de documento não aceita anexos.");
        }

        if (await TooManyUploadsAsync(cancellationToken))
        {
            return Error.TooManyRequests("attachment.too_many_uploads", "Muitos envios em pouco tempo. Tente de novo em alguns minutos.");
        }

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > AttachmentPolicy.MaxSize)
        {
            return Error.Validation("attachment.size", "O arquivo precisa ter até 25 MB e não pode estar vazio.");
        }

        var head = buffer.GetBuffer().AsSpan(0, (int)Math.Min(HeadSize, buffer.Length));
        var type = AttachmentPolicy.Match(fileName, head, rules.ExtraTypes);
        if (type is null)
        {
            return Error.Validation("attachment.type_not_allowed", "Tipo de arquivo não permitido.");
        }

        if (await CheckRulesAsync(rules with { CanAttach = null }, ownerId, type, buffer, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (StripMetadata(rules, type, buffer) is { } unreadable)
        {
            return unreadable;
        }

        buffer.Position = 0;
        var sha256 = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(buffer, cancellationToken));
        buffer.Position = 0;
        var path = await storage.SaveAsync(buffer, storagePrefix, cancellationToken);
        buffer.Position = 0;
        var scan = await scanner.ScanAsync(buffer, cancellationToken);
        if (scan.Verdict is ScanVerdict.Infected or ScanVerdict.TooLarge)
        {
            await storage.DeleteAsync(path, cancellationToken);
            return scan.Verdict == ScanVerdict.Infected
                ? Error.Validation("attachment.infected", "O arquivo foi recusado: o antivírus encontrou uma ameaça.")
                : Error.Validation("attachment.too_large_to_scan", "O arquivo é grande demais para o antivírus e foi recusado.");
        }

        var attachment = new Attachment
        {
            OwnerType = ownerType, OwnerId = ownerId, FileName = SafeDisplayName(fileName), ContentType = type.ContentType, Size = buffer.Length, Sha256 = sha256, StoragePath = path,
            ScanStatus = scan.Verdict == ScanVerdict.Clean ? ScanStatus.Clean : ScanStatus.Pending, ScannedAt = scan.Verdict == ScanVerdict.Clean ? clock.GetUtcNow() : null,
            CreatedAt = clock.GetUtcNow(), UploadedBy = currentUser.UserId,
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(attachment);
    }

    /// <summary>
    /// Anexo limpo mais recente de cada dono (sem os pendentes e os infectados), numa consulta só: a foto do bem na lista e na ficha
    /// (PAT-01a CA 8), como o logo.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, Guid>> LatestCleanAsync(string ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerIds);
        if (ownerIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var ids = ownerIds.Distinct().ToList();
        var rows = await db.Attachments.AsNoTracking().Where(a => a.OwnerType == ownerType && ids.Contains(a.OwnerId) && a.ScanStatus == ScanStatus.Clean)
            .Select(a => new { a.OwnerId, a.Id, a.CreatedAt }).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.OwnerId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).First().Id);
    }

    /// <summary>Quais destes donos têm anexo (sem os infectados), numa consulta só, para a marca "Documento anexado" de uma lista.</summary>
    public async Task<IReadOnlySet<Guid>> OwnersWithAttachmentsAsync(string ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerIds);
        if (ownerIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = ownerIds.ToList();
        return (await db.Attachments.AsNoTracking().Where(a => a.OwnerType == ownerType && ids.Contains(a.OwnerId) && a.ScanStatus != ScanStatus.Infected)
            .Select(a => a.OwnerId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
    }
}
