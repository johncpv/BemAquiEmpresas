using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Attachments;

public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long Size, ScanStatus ScanStatus, DateTimeOffset CreatedAt);

public sealed record AttachmentContent(Stream Content, string ContentType, string FileName);

/// <summary>Qual módulo é dono de cada tipo de documento que aceita anexos (permissão do dono vale para o anexo).</summary>
public sealed class AttachmentOwnerRegistry
{
    private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);

    public AttachmentOwnerRegistry Register(string ownerType, string moduleCode)
    {
        _owners[ownerType] = moduleCode;
        return this;
    }

    public string? ModuleOf(string ownerType) => _owners.GetValueOrDefault(ownerType);
}

/// <summary>Upload e download de anexos com allowlist, magic bytes, ClamAV e permissão do documento dono (FND-07 CA 18 a 21).</summary>
public sealed class AttachmentService(
    BemAquiDbContext db,
    IFileStorage storage,
    IVirusScanner scanner,
    AttachmentOwnerRegistry owners,
    IPermissionChecker permissions,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    private const int HeadSize = 4096;

    public async Task<Result<AttachmentDto>> UploadAsync(string ownerType, Guid ownerId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var module = owners.ModuleOf(ownerType);
        if (module is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Este tipo de documento não aceita anexos.");
        }

        if (!await permissions.HasPermissionAsync(module, StandardActions.Edit, cancellationToken))
        {
            return Forbidden();
        }

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > AttachmentPolicy.MaxSize)
        {
            return Error.Validation("attachment.size", "O arquivo precisa ter até 25 MB e não pode estar vazio.");
        }

        var head = buffer.GetBuffer().AsSpan(0, (int)Math.Min(HeadSize, buffer.Length));
        var type = AttachmentPolicy.Match(fileName, head);
        if (type is null)
        {
            return Error.Validation("attachment.type_not_allowed", "Tipo de arquivo não permitido. Envie PDF, imagem (PNG, JPG, WEBP), XML, planilha, documento, CSV, desenho (DWG, DXF) ou ZIP.");
        }

        buffer.Position = 0;
        var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(buffer, cancellationToken));
        buffer.Position = 0;
        var path = await storage.SaveAsync(buffer, cancellationToken);

        buffer.Position = 0;
        var scan = await scanner.ScanAsync(buffer, cancellationToken);
        if (scan.Verdict == ScanVerdict.Infected)
        {
            await storage.DeleteAsync(path, cancellationToken);
            return Error.Validation("attachment.infected", "O arquivo foi recusado: o antivírus encontrou uma ameaça.");
        }

        var attachment = new Attachment
        {
            OwnerType = ownerType,
            OwnerId = ownerId,
            FileName = SafeDisplayName(fileName),
            ContentType = type.ContentType,
            Size = buffer.Length,
            Sha256 = sha256,
            StoragePath = path,
            ScanStatus = scan.Verdict == ScanVerdict.Clean ? ScanStatus.Clean : ScanStatus.Pending,
            ScannedAt = scan.Verdict == ScanVerdict.Clean ? clock.GetUtcNow() : null,
            CreatedAt = clock.GetUtcNow(),
            UploadedBy = currentUser.UserId,
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(attachment);
    }

    public async Task<Result<AttachmentContent>> DownloadAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
        if (attachment is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Anexo não encontrado.");
        }

        var module = owners.ModuleOf(attachment.OwnerType);
        if (module is null || !await permissions.HasPermissionAsync(module, StandardActions.View, cancellationToken))
        {
            return Forbidden();
        }

        if (attachment.ScanStatus != ScanStatus.Clean)
        {
            return Error.Conflict("attachment.not_scanned", "O arquivo ainda está sendo verificado pelo antivírus. Tente de novo em alguns minutos.");
        }

        return new AttachmentContent(await storage.OpenReadAsync(attachment.StoragePath, cancellationToken), attachment.ContentType, attachment.FileName);
    }

    public async Task<Result<IReadOnlyList<AttachmentDto>>> ListAsync(string ownerType, Guid ownerId, CancellationToken cancellationToken = default)
    {
        var module = owners.ModuleOf(ownerType);
        if (module is null || !await permissions.HasPermissionAsync(module, StandardActions.View, cancellationToken))
        {
            return Forbidden();
        }

        var items = await db.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId && a.ScanStatus != ScanStatus.Infected)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
        return items.Select(ToDto).ToList();
    }

    private static Error Forbidden() => Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");

    private static AttachmentDto ToDto(Attachment a) => new(a.Id, a.FileName, a.ContentType, a.Size, a.ScanStatus, a.CreatedAt);

    // Nome só para exibição: sem diretórios nem caracteres de controle, até 200 caracteres.
    private static string SafeDisplayName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace((char)92, '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(name) ? "arquivo" : name[..Math.Min(name.Length, 200)];
    }
}
