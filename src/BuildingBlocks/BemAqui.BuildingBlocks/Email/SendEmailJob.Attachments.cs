using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.BuildingBlocks.Email;

public sealed partial class SendEmailJob
{
    public const int MaxScanWaits = 10;
    public const long MaxAttachmentBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Anexos do e-mail (COM-03 CA 12, ADR 0019 §7): lidos pelo id, só se forem do mesmo documento do e-mail e estiverem
    /// <c>Clean</c>. Ainda em varredura: reagenda em 1 min (até 10 vezes) sem gastar tentativa. Infectado ou falha: Failed.
    /// </summary>
    private static async Task<(IReadOnlyList<EmailAttachment> Files, bool Wait, string? Error)> AttachmentsAsync(IServiceProvider services, BemAquiDbContext db, EmailLog log, CancellationToken ct)
    {
        if (log.AttachmentIds.Length == 0)
        {
            return ([], false, null);
        }

        var ids = log.AttachmentIds.ToList();
        var rows = await db.Attachments.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        if (rows.Count != ids.Count || rows.Exists(a => a.OwnerType != log.RelatedType || a.OwnerId != log.RelatedId))
        {
            return ([], false, "Anexo não pertence ao documento do e-mail.");
        }

        if (rows.Exists(a => a.ScanStatus is ScanStatus.Infected or ScanStatus.Failed))
        {
            return ([], false, "O antivírus recusou ou não conseguiu verificar o anexo.");
        }

        if (rows.Exists(a => a.ScanStatus == ScanStatus.Pending))
        {
            return log.ScanWaits >= MaxScanWaits ? ([], false, "O anexo não foi verificado a tempo.") : ([], true, null);
        }

        if (rows.Sum(a => a.Size) > MaxAttachmentBytes)
        {
            return ([], false, "Os anexos passam de 10 MB.");
        }

        var storage = services.GetRequiredService<IFileStorage>();
        var files = new List<EmailAttachment>();
        foreach (var a in rows)
        {
            await using var stream = await storage.OpenReadAsync(a.StoragePath, ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            files.Add(new EmailAttachment(a.FileName, a.ContentType, buffer.ToArray()));
        }

        return (files, false, null);
    }

    private static void ScheduleScanWait(IServiceProvider services, TenantInfo tenant, SendEmailArgs args) =>
        services.GetRequiredService<IBackgroundJobClient>().Schedule<SendEmailJob>(j => j.ExecuteAsync(tenant.Id, args, CancellationToken.None), TimeSpan.FromMinutes(1));
}
