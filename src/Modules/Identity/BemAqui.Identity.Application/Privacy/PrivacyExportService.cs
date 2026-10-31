using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BemAqui.Identity.Application.Privacy;

/// <summary>
/// Montagem da exportação do titular (LGPD-01a CA 8 a 10), dentro do job: confere que quem pediu continua no Proprietário, identifica o titular
/// pelo módulo dono, pede a cada módulo o que ele tem (<see cref="IPersonalDataProvider.ExportAsync"/>) com as permissões de quem pediu e monta
/// o ZIP com <c>dados.json</c>, <c>dados.pdf</c> e a pasta <c>anexos/</c> (só os arquivos <c>Clean</c>). O ZIP é um arquivo do sistema do pedido
/// (dono <c>PrivacyRequest</c>, prefixo do tenant no storage); a exportação anterior do mesmo pedido é apagada. Falha de um módulo para tudo e
/// mostra qual foi.
/// </summary>
public sealed partial class PrivacyExportService(
    IIdentityDb db,
    IEnumerable<IPersonalDataProvider> providers,
    PrivacyRequestService requests,
    IPermissionService permissions,
    AttachmentService attachments,
    IFileStorage storage,
    ICompanyIdentity company,
    IPdfRenderer renderer,
    INotificationService notifications,
    IAuditWriter audit,
    ITenantContext tenant,
    TimeProvider clock,
    ILogger<PrivacyExportService> logger)
{
    // Arquivo para o titular (não vai para HTML): acentos legíveis no dados.json.
    private static readonly JsonWriterOptions JsonOptions = new() { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task RunAsync(Guid requestId, Guid requestedBy, CancellationToken cancellationToken = default)
    {
        var request = await db.PrivacyRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null || request.Status != PrivacyRequestStatus.Open || request.ExportStatus != PrivacyExportStatus.Running || !request.IsIdentityVerified)
        {
            return;
        }

        var effective = await permissions.GetAsync(requestedBy, cancellationToken);
        if (!effective.IsOwner)
        {
            await FailAsync(request, requestedBy, "Quem pediu não é mais do grupo Proprietário.", cancellationToken);
            return;
        }

        var subject = await SubjectAsync(request, cancellationToken);
        if (subject is null)
        {
            await FailAsync(request, requestedBy, "O cadastro do titular não existe mais.", cancellationToken);
            return;
        }

        var context = new PersonalDataExportContext(subject, request.Number, effective.Permissions);
        var sections = new List<PersonalDataSection>();
        foreach (var provider in providers.OrderBy(p => p.Module, StringComparer.Ordinal))
        {
            try
            {
                if (await provider.ExportAsync(context, cancellationToken) is { } section)
                {
                    sections.Add(section);
                }
            }
#pragma warning disable CA1031 // A falha de um módulo vira a mensagem "erro com o módulo que falhou" na tela, sem derrubar o job.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogProviderFailed(logger, ex, provider.Module, request.Id);
                await FailAsync(request, requestedBy, $"Não foi possível reunir os dados do módulo {provider.Title}.", cancellationToken);
                return;
            }
        }

        var files = new List<(string Name, byte[] Content)>();
        foreach (var file in sections.SelectMany(s => s.Files ?? []))
        {
            var content = await attachments.DownloadCheckedAsync(file.OwnerType, file.OwnerId, file.AttachmentId, cancellationToken);
            if (content.IsSuccess)
            {
                await using var stream = content.Value.Content;
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                files.Add((file.FileName, buffer.ToArray()));
            }
        }

        var generatedAt = clock.GetUtcNow();
        var json = BuildJson(request.Number, subject, generatedAt, sections);
        var info = await company.GetAsync(cancellationToken);
        byte[]? logo = null;
        if (info.LogoStoragePath is { } path)
        {
            await using var logoStream = await storage.OpenReadAsync(path, cancellationToken);
            using var logoBuffer = new MemoryStream();
            await logoStream.CopyToAsync(logoBuffer, cancellationToken);
            logo = logoBuffer.ToArray();
        }

        var pdf = await renderer.RenderAsync(new PersonalDataPdfDocument(new PersonalDataPdfModel(info.LegalName, logo, request.Number, PersonalDataMask.At(generatedAt) ?? string.Empty, subject.Name, sections)), cancellationToken);
        var zip = BuildZip(json, pdf, files);

        // O pedido pode ter mudado enquanto o job montava o arquivo (xmin): recarrega antes de gravar.
        await db.Entry(request).ReloadAsync(cancellationToken);
        if (request.Status != PrivacyRequestStatus.Open || request.ExportStatus != PrivacyExportStatus.Running)
        {
            return;
        }

        if (request.ExportAttachmentId is { } previous)
        {
            await attachments.DeleteSystemFileAsync(PrivacyRequest.OwnerType, request.Id, previous, cancellationToken);
        }

        var stored = await attachments.StoreGeneratedAsync(PrivacyRequest.OwnerType, request.Id, PrivacyRequestService.FileName(request), PrivacyRequestService.ZipContentType, zip,
            tenant.Tenant?.Id.ToString("N"), cancellationToken);
        request.ExportAttachmentId = stored.Id;
        request.ExportStatus = PrivacyExportStatus.Ready;
        request.ExportedAt = generatedAt;
        request.ExportError = null;
        request.DownloadTokenHash = null;
        request.DownloadTokenExpiresAt = null;
        notifications.Notify(requestedBy, PrivacyRequestService.NotificationType, $"Exportação do pedido {request.Number} pronta para baixar.", PrivacyRequestService.Link(request.Id));
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordExportAsync(nameof(PrivacyRequest), request.Id.ToString(), "zip", sections.Sum(s => s.Blocks.Sum(b => b.Records.Count)), cancellationToken);
    }

    /// <summary>
    /// <c>dados.json</c> (CA 8): o pedido, o titular (nome e tipo) e um bloco por módulo, com a origem (tabela), os campos e as datas de cada
    /// registro. Só o que os provedores devolveram: nenhum hash, segredo nem token (teste de canário).
    /// </summary>
    public static byte[] BuildJson(string requestNumber, PersonalDataSubject subject, DateTimeOffset generatedAt, IReadOnlyList<PersonalDataSection> sections)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(sections);
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, JsonOptions))
        {
            w.WriteStartObject();
            w.WriteString("pedido", requestNumber);
            w.WriteString("geradoEm", generatedAt);
            w.WriteStartObject("titular");
            w.WriteString("tipo", subject.Kind.ToString());
            w.WriteString("nome", subject.Name);
            w.WriteEndObject();
            w.WriteStartArray("modulos");
            foreach (var section in sections)
            {
                w.WriteStartObject();
                w.WriteString("modulo", section.Module);
                w.WriteString("titulo", section.Title);
                if (section.Note is { } note)
                {
                    w.WriteString("observacao", note);
                }

                w.WriteStartArray("blocos");
                foreach (var block in section.Blocks)
                {
                    w.WriteStartObject();
                    w.WriteString("titulo", block.Title);
                    w.WriteString("origem", block.Source);
                    w.WriteStartArray("registros");
                    foreach (var record in block.Records)
                    {
                        w.WriteStartObject();
                        if (record.At is { } at)
                        {
                            w.WriteString("data", at);
                        }

                        w.WriteStartObject("campos");
                        foreach (var field in record.Fields)
                        {
                            if (field.Value is null)
                            {
                                w.WriteNull(field.Name);
                            }
                            else
                            {
                                w.WriteString(field.Name, field.Value);
                            }
                        }

                        w.WriteEndObject();
                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                w.WriteEndArray();
                if (section.Files is { Count: > 0 } files)
                {
                    w.WriteStartArray("anexos");
                    foreach (var file in files)
                    {
                        w.WriteStringValue("anexos/" + file.FileName);
                    }

                    w.WriteEndArray();
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>ZIP com <c>dados.json</c>, <c>dados.pdf</c> e <c>anexos/</c>; nomes repetidos ganham sufixo.</summary>
    public static byte[] BuildZip(byte[] json, byte[] pdf, IReadOnlyList<(string Name, byte[] Content)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            Write(zip, "dados.json", json);
            Write(zip, "dados.pdf", pdf);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, content) in files)
            {
                var safe = Path.GetFileName(name.Replace('\\', '/'));
                var candidate = safe;
                for (var i = 2; !used.Add(candidate); i++)
                {
                    candidate = Path.GetFileNameWithoutExtension(safe) + $" ({i})" + Path.GetExtension(safe);
                }

                Write(zip, "anexos/" + candidate, content);
            }
        }

        return buffer.ToArray();
    }

    private static void Write(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private async Task<PersonalDataSubject?> SubjectAsync(PrivacyRequest request, CancellationToken ct)
    {
        if (request.SubjectType == PersonalDataSubjectKind.Unknown)
        {
            var contact = request.SubjectContact?.Trim().ToLowerInvariant();
            return new PersonalDataSubject(PersonalDataSubjectKind.Unknown, request.Id, request.SubjectName, contact is not null && contact.Contains('@', StringComparison.Ordinal) ? [contact] : []);
        }

        if (request.SubjectId is not { } id || await requests.ResolveAsync(request.SubjectType, id, ct) is not { } subject)
        {
            return null;
        }

        // Funcionário ligado a um usuário (CAD-04b CA 19): o mesmo titular; os acessos, as notificações e o e-mail de login entram junto.
        if (subject.Kind == PersonalDataSubjectKind.Employee && subject.UserId is null
            && await db.Users.AsNoTracking().Where(u => u.EmployeeId == subject.Id).Select(u => new { u.Id, u.NormalizedEmail }).FirstOrDefaultAsync(ct) is { } user)
        {
            subject = subject with { UserId = user.Id, Emails = subject.Emails.Append(user.NormalizedEmail).Distinct(StringComparer.Ordinal).ToList() };
        }

        return subject;
    }

    private async Task FailAsync(PrivacyRequest request, Guid requestedBy, string message, CancellationToken ct)
    {
        request.ExportStatus = PrivacyExportStatus.Failed;
        request.ExportError = message;
        notifications.Notify(requestedBy, PrivacyRequestService.NotificationType, $"Exportação do pedido {request.Number} falhou: {message}", PrivacyRequestService.Link(request.Id));
        await db.SaveChangesAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Exportação do titular: o provedor {Module} falhou no pedido {RequestId}.")]
    private static partial void LogProviderFailed(ILogger logger, Exception ex, string module, Guid requestId);
}
