using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Attachments;

public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long Size, ScanStatus ScanStatus, DateTimeOffset CreatedAt, string? UploadedByName = null, AttachmentOrigin Origin = AttachmentOrigin.User);

public sealed record AttachmentContent(Stream Content, string ContentType, string FileName);

/// <summary>
/// Regras de um dono de anexos: o módulo e as ações de ler e gravar (padrão <c>view</c> e <c>edit</c>), os tipos aceitos, o
/// tamanho máximo, o maior lado da imagem, a conferência de que o documento dono existe (CAD-06 CA 8, obrigatória nos donos
/// inscritos, teste de arquitetura) e se o dono aceita anexo no estado atual (<c>CanAttach</c>, ADR 0019 §7). <c>AlternateWriteAction</c>
/// é uma segunda ação do mesmo módulo que também grava (ex.: a NC aceita <c>production.edit</c> ou <c>production.approve_quality</c>,
/// ADR 0019 §7 adendo). <c>AuditReads</c>: dono com dado pessoal (ex.: <c>EmployeeDocument</c>, CAD-04c2 CA 12), cujo download
/// grava leitura sensível na auditoria, com o dono, o anexo, quem, quando e o IP.
/// </summary>
public sealed record AttachmentOwnerRules(
    string Module,
    IReadOnlyCollection<string>? ContentTypes = null,
    long? MaxSize = null,
    int? MaxImageSide = null,
    Func<IServiceProvider, Guid, CancellationToken, Task<bool>>? OwnerExists = null,
    string ReadAction = StandardActions.View,
    string WriteAction = StandardActions.Edit,
    Func<IServiceProvider, Guid, CancellationToken, Task<Error?>>? CanAttach = null,
    Func<IServiceProvider, Guid, AttachmentFile, CancellationToken, Task<Error?>>? CheckFile = null,
    string? AlternateWriteAction = null,
    bool AuditReads = false);

/// <summary>Arquivo já aceito pela allowlist, para a regra do dono que depende do documento (ex.: tipo da OS, PRD-01 CA 12).</summary>
public sealed record AttachmentFile(string ContentType, long Size);

/// <summary>Seção <c>Attachments</c> (ADR 0019 §7 adendo): limite de envios por usuário (<c>attachments.max_uploads_per_hour</c>).</summary>
public sealed class AttachmentOptions
{
    public const string Section = "Attachments";

    /// <summary>Envios por usuário numa hora; acima, 429. Zero desliga o limite.</summary>
    public int MaxUploadsPerHour { get; set; } = 30;
}

/// <summary>Inscrição de um dono de anexo por um módulo, no <c>AddServices</c>.</summary>
public sealed record AttachmentOwnerRegistration(string OwnerType, AttachmentOwnerRules Rules);

public static class AttachmentOwnerServiceCollectionExtensions
{
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddAttachmentOwner(this Microsoft.Extensions.DependencyInjection.IServiceCollection services, string ownerType, AttachmentOwnerRules rules)
    {
        ArgumentNullException.ThrowIfNull(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, new AttachmentOwnerRegistration(ownerType, rules));
        return services;
    }
}

/// <summary>Qual módulo é dono de cada tipo de documento que aceita anexos (permissão do dono vale para o anexo).</summary>
public sealed class AttachmentOwnerRegistry
{
    private readonly Dictionary<string, AttachmentOwnerRules> _owners = new(StringComparer.Ordinal);

    public AttachmentOwnerRegistry Register(string ownerType, string moduleCode) => Register(ownerType, new AttachmentOwnerRules(moduleCode));

    public AttachmentOwnerRegistry Register(string ownerType, AttachmentOwnerRules rules)
    {
        _owners[ownerType] = rules;
        return this;
    }

    public string? ModuleOf(string ownerType) => _owners.GetValueOrDefault(ownerType)?.Module;

    public AttachmentOwnerRules? RulesOf(string ownerType) => _owners.GetValueOrDefault(ownerType);
}

/// <summary>Upload e download de anexos com allowlist, magic bytes, ClamAV e permissão do documento dono (FND-07 CA 18 a 21).</summary>
public sealed partial class AttachmentService(
    BemAquiDbContext db,
    IFileStorage storage,
    IVirusScanner scanner,
    AttachmentOwnerRegistry owners,
    IPermissionChecker permissions,
    ICurrentUser currentUser,
    TimeProvider clock,
    IServiceProvider services)
{
    private const int HeadSize = 4096;

    public async Task<Result<AttachmentDto>> UploadAsync(string ownerType, Guid ownerId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var rules = owners.RulesOf(ownerType);
        if (rules is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Este tipo de documento não aceita anexos.");
        }

        if (!await permissions.HasPermissionAsync(rules.Module, rules.WriteAction, cancellationToken)
            && !(rules.AlternateWriteAction is { } alternate && await permissions.HasPermissionAsync(rules.Module, alternate, cancellationToken)))
        {
            return Forbidden();
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
        var type = AttachmentPolicy.Match(fileName, head);
        if (type is null)
        {
            return Error.Validation("attachment.type_not_allowed", "Tipo de arquivo não permitido. Envie PDF, imagem (PNG, JPG, WEBP), XML, planilha, documento, CSV, desenho (DWG, DXF) ou ZIP.");
        }

        if (await CheckRulesAsync(rules, ownerId, type, buffer, cancellationToken) is { } refused)
        {
            return refused;
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

        // Acima do limite do antivírus: recusado, nunca liberado sem varredura (ADR 0019 §7 adendo).
        if (scan.Verdict == ScanVerdict.TooLarge)
        {
            await storage.DeleteAsync(path, cancellationToken);
            return Error.Validation("attachment.too_large_to_scan", "O arquivo é grande demais para o antivírus e foi recusado.");
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

        var rules = owners.RulesOf(attachment.OwnerType);
        if (rules is null || !await permissions.HasPermissionAsync(rules.Module, rules.ReadAction, cancellationToken))
        {
            return Forbidden();
        }

        if (attachment.ScanStatus != ScanStatus.Clean)
        {
            return Error.Conflict("attachment.not_scanned", "O arquivo ainda está sendo verificado pelo antivírus. Tente de novo em alguns minutos.");
        }

        if (await AuditReadAsync(rules, attachment, cancellationToken) is { } missing)
        {
            return missing;
        }

        return new AttachmentContent(await storage.OpenReadAsync(attachment.StoragePath, cancellationToken), attachment.ContentType, attachment.FileName);
    }

    /// <summary>
    /// Dono com <c>AuditReads</c> (CAD-04c2 CA 12): confere que o dono existe (senão, 404) e grava a leitura sensível antes de
    /// entregar o arquivo.
    /// </summary>
    private async Task<Error?> AuditReadAsync(AttachmentOwnerRules? rules, Attachment attachment, CancellationToken ct)
    {
        if (rules is not { AuditReads: true })
        {
            return null;
        }

        if (rules.OwnerExists is { } exists && !await exists(services, attachment.OwnerId, ct))
        {
            return Error.NotFound(Error.Codes.NotFound, "Anexo não encontrado.");
        }

        if (services.GetService(typeof(Persistence.Auditing.IAuditWriter)) is not Persistence.Auditing.IAuditWriter audit)
        {
            throw new InvalidOperationException("Dono de anexo com AuditReads sem o IAuditWriter registrado.");
        }

        await audit.RecordAttachmentReadAsync(attachment.OwnerType, attachment.OwnerId, attachment.Id, ct);
        return null;
    }

    public async Task<Result<IReadOnlyList<AttachmentDto>>> ListAsync(string ownerType, Guid ownerId, CancellationToken cancellationToken = default)
    {
        var rules = owners.RulesOf(ownerType);
        if (rules is null || !await permissions.HasPermissionAsync(rules.Module, rules.ReadAction, cancellationToken))
        {
            return Forbidden();
        }

        var items = await db.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId && a.ScanStatus != ScanStatus.Infected)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        // Quem anexou (COM-04a CA 15): nome do diretório de usuários, quando houver.
        var directory = services.GetService(typeof(IUserDirectory)) as IUserDirectory;
        var names = new Dictionary<Guid, string?>();
        foreach (var userId in items.Where(a => a.UploadedBy is not null).Select(a => a.UploadedBy!.Value).Distinct())
        {
            names[userId] = directory is null ? null : await directory.DisplayNameAsync(userId, cancellationToken);
        }

        return items.Select(a => ToDto(a) with { UploadedByName = a.UploadedBy is { } by ? names.GetValueOrDefault(by) : null }).ToList();
    }

    private async Task<Error?> CheckRulesAsync(AttachmentOwnerRules rules, Guid ownerId, AllowedFileType type, MemoryStream buffer, CancellationToken ct)
    {
        if (rules.ContentTypes is { } types && !types.Contains(type.ContentType))
        {
            var names = string.Join(", ", AttachmentPolicy.Types.Where(t => types.Contains(t.ContentType)).Select(t => t.Name));
            return Error.Validation("attachment.type_not_allowed", "Tipo de arquivo não permitido aqui. Envie " + names + ".");
        }

        if (rules.MaxSize is { } max && buffer.Length > max)
        {
            return Error.Validation("attachment.size", $"O arquivo precisa ter até {max / 1024 / 1024} MB.");
        }

        if (rules.MaxImageSide is { } side && (ImageInfo.TryGetSize(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)) is not { } size || size.Width > side || size.Height > side || size.Width <= 0 || size.Height <= 0))
        {
            return Error.Validation("attachment.image_too_large", $"A imagem precisa ter até {side} px no maior lado.");
        }

        if (rules.OwnerExists is { } exists && !await exists(services, ownerId, ct))
        {
            return Error.NotFound(Error.Codes.NotFound, "O documento deste anexo não existe.");
        }

        if (rules.CheckFile is { } check && await check(services, ownerId, new AttachmentFile(type.ContentType, buffer.Length), ct) is { } refused)
        {
            return refused;
        }

        return rules.CanAttach is { } can ? await can(services, ownerId, ct) : null;
    }

    /// <summary>Limite de envios por usuário numa hora (ADR 0019 §7 adendo); só os enviados pelo usuário contam.</summary>
    private async Task<bool> TooManyUploadsAsync(CancellationToken ct)
    {
        var limit = (services.GetService(typeof(Microsoft.Extensions.Options.IOptions<AttachmentOptions>)) as Microsoft.Extensions.Options.IOptions<AttachmentOptions>)?.Value.MaxUploadsPerHour ?? 30;
        if (limit <= 0 || currentUser.UserId is not { } user)
        {
            return false;
        }

        var since = clock.GetUtcNow().AddHours(-1);
        return await db.Attachments.CountAsync(a => a.UploadedBy == user && a.Origin == AttachmentOrigin.User && a.CreatedAt > since, ct) >= limit;
    }

    private static Error Forbidden() => Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");

    private static AttachmentDto ToDto(Attachment a) => new(a.Id, a.FileName, a.ContentType, a.Size, a.ScanStatus, a.CreatedAt, Origin: a.Origin);

    // Nome só para exibição: sem diretórios nem caracteres de controle, até 200 caracteres.
    private static string SafeDisplayName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace((char)92, '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(name) ? "arquivo" : name[..Math.Min(name.Length, 200)];
    }
}
