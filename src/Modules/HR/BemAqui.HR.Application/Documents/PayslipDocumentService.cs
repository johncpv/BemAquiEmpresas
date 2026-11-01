using System.IO.Compression;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.HR.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application.Documents;

/// <summary>
/// Holerite em PDF (RH-02b CA 1 a 5): o PDF sai do <see cref="PayslipPdfDocument"/> pelo <see cref="IPdfRenderer"/>. Folha em rascunho:
/// gerado na hora, com a marca d'água, sem guardar. Folha fechada ou paga: guardado como arquivo do sistema (dono <c>Payslip</c>) e
/// reaproveitado até a folha ser reaberta. Todo download grava <c>SensitiveRead</c>, sem valor. Nenhum valor vai para log, outbox
/// ou argumento de job: o ZIP em segundo plano leva só o <c>tenantId</c> e os ids.
/// </summary>
public sealed class PayslipDocumentService(
    IHrDb db,
    ICompanyIdentity company,
    IFileStorage storage,
    IPdfRenderer renderer,
    AttachmentService attachments,
    IAuditWriter audit,
    INotificationService notifications,
    IBackgroundJobClient jobs,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const string PdfContentType = "application/pdf";
    public const string ZipContentType = "application/zip";
    public const string NotificationType = "hr.payroll";

    private sealed record Slip(Payslip Payslip, string Name, string? Registration, string Cpf, DateOnly Admission, string Position, string? Cbo);

    /// <summary>"Baixar holerite" pelo RH (CA 1 e 3): holerite da folha da rota (IDOR, 404), com <c>hr.view_sensitive</c> na rota.</summary>
    public async Task<Result<PayslipFile>> PdfAsync(Guid runId, Guid payslipId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        var slip = run is null ? null : await SlipsQuery(p => p.PayrollRunId == runId && p.Id == payslipId).FirstOrDefaultAsync(cancellationToken);
        if (run is null || slip is null)
        {
            return PayslipNotFound();
        }

        var info = await CompanyAsync(cancellationToken);
        var file = await FileAsync(run, slip, info, cancellationToken);
        if (file.IsFailure)
        {
            return file.Error!;
        }

        await RecordReadAsync(slip.Payslip.Id, file.Value.AttachmentId, cancellationToken);
        return file.Value.File;
    }

    /// <summary>
    /// "Meus holerites" (CA 5): só os holerites do funcionário da sessão nas folhas <c>Closed</c> e <c>Paid</c>, do mais recente ao mais
    /// antigo. O líquido é dado pessoal: a leitura vai para a auditoria.
    /// </summary>
    public async Task<IReadOnlyList<MyPayslip>> MineAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var list = await (from p in db.Payslips.AsNoTracking()
                          join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                          where p.EmployeeId == employeeId && (r.Status == PayrollRunStatus.Closed || r.Status == PayrollRunStatus.Paid)
                          orderby r.Period descending, r.Type
                          select new MyPayslip(p.Id, r.Period, r.Type, p.Net, r.PaymentDate, p.FirstViewedAt)).ToListAsync(cancellationToken);
        await audit.RecordReadAsync(nameof(Payslip) + ".Mine", employeeId.ToString(), cancellationToken);
        return list;
    }

    /// <summary>
    /// PDF do próprio holerite (CA 5): o funcionário vem da sessão; holerite de outro funcionário ou de folha em rascunho responde 404
    /// (IDOR). A primeira leitura grava "Visto em".
    /// </summary>
    public async Task<Result<PayslipFile>> MinePdfAsync(Guid employeeId, Guid payslipId, CancellationToken cancellationToken = default)
    {
        var slip = await SlipsQuery(p => p.Id == payslipId && p.EmployeeId == employeeId).FirstOrDefaultAsync(cancellationToken);
        var run = slip is null ? null : await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == slip.Payslip.PayrollRunId, cancellationToken);
        if (slip is null || run is null || run.Status == PayrollRunStatus.Draft)
        {
            return PayslipNotFound();
        }

        var file = await FileAsync(run, slip, await CompanyAsync(cancellationToken), cancellationToken);
        if (file.IsFailure)
        {
            return file.Error!;
        }

        if (slip.Payslip.FirstViewedAt is null)
        {
            var tracked = await db.Payslips.FirstAsync(p => p.Id == payslipId, cancellationToken);
            tracked.FirstViewedAt ??= clock.GetUtcNow();
            await TrySaveAsync(cancellationToken);
        }

        await RecordReadAsync(payslipId, file.Value.AttachmentId, cancellationToken);
        return file.Value.File;
    }

    /// <summary>
    /// "Baixar todos os holerites" (CA 2): um PDF por funcionário num ZIP. Até 20 holerites, na hora; acima, o <see cref="PayslipBatchJob"/>
    /// (só <c>tenantId</c> e ids) gera e avisa no sino. Holerite acima de 300 linhas recusa antes de gerar.
    /// </summary>
    public async Task<Result<PayslipBatchResult>> StartBatchAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        var counts = await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == runId)
            .Select(p => db.PayslipLines.Count(l => l.PayslipId == p.Id)).ToListAsync(cancellationToken);
        if (counts.Count == 0)
        {
            return PayrollRules.Field("payslips", "A folha não tem holerites.");
        }

        if (counts.Any(c => c > PayslipDocumentRules.MaxPdfLines))
        {
            return TooManyLines();
        }

        var fileName = PayslipDocumentRules.BatchFileName(run.Period, run.Type);
        if (counts.Count > PayslipDocumentRules.InlineBatchLimit)
        {
            var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Holerites em lote fora do escopo de tenant.");
            var args = new PayslipBatchArgs(runId, currentUser.UserId);
            jobs.Create<PayslipBatchJob>(j => j.ExecuteAsync(tenantId, args, CancellationToken.None), new EnqueuedState(JobQueues.Default));
            return new PayslipBatchResult(true, counts.Count, null, fileName, $"Gerando {counts.Count} holerites…");
        }

        var stored = await BuildBatchAsync(run, cancellationToken);
        if (stored.IsFailure)
        {
            return stored.Error!;
        }

        return new PayslipBatchResult(false, counts.Count, stored.Value, fileName, counts.Count == 1 ? "1 holerite pronto." : $"{counts.Count} holerites prontos.");
    }

    /// <summary>Segundo plano (CA 2): gera o ZIP e avisa quem pediu no sino, com o link para baixar.</summary>
    public async Task CompleteBatchAsync(Guid runId, Guid? requestedBy, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return;
        }

        var stored = await BuildBatchAsync(run, cancellationToken);
        if (requestedBy is not { } user)
        {
            return;
        }

        var link = $"/app/rh/folha/{runId}?aba=holerites" + (stored.IsSuccess ? $"&lote={stored.Value}" : string.Empty);
        notifications.Notify(user, NotificationType, stored.IsSuccess
            ? $"Holerites de {PayrollRules.PeriodLabel(run.Period)} prontos para baixar."
            : $"Não foi possível gerar os holerites de {PayrollRules.PeriodLabel(run.Period)}: {stored.Error!.Message}", link);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>ZIP gerado (dono <c>PayslipBatch</c>, a folha); o download grava <c>SensitiveRead</c> pelo dono (AuditReads).</summary>
    public async Task<Result<PayslipFile>> BatchFileAsync(Guid runId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        var content = await attachments.DownloadCheckedAsync(PayslipDocumentRules.BatchOwnerType, runId, attachmentId, cancellationToken);
        return content.IsFailure ? content.Error! : new PayslipFile(content.Value.Content, PayslipDocumentRules.BatchFileName(run.Period, run.Type), ZipContentType);
    }

    /// <summary>Folha reaberta (RH-02c CA 13, RH-02b CA 10): os PDFs guardados deixam de valer; o próximo download refaz.</summary>
    public async Task InvalidateAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        foreach (var p in await db.Payslips.Where(p => p.PayrollRunId == runId && p.PdfAttachmentId != null).ToListAsync(cancellationToken))
        {
            p.PdfAttachmentId = null;
        }
    }

    /// <summary>
    /// Folha fechada de novo (CA 10): o holerite já avisado que mudou depois do aviso ganha a marca "Alterado depois do aviso"; devolve
    /// quantos. Grava junto com o fechamento (o chamador salva).
    /// </summary>
    public async Task<int> MarkChangedAfterNoticeAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var changed = await db.Payslips.Where(p => p.PayrollRunId == runId && p.NotifiedAt != null && p.UpdatedAt > p.NotifiedAt).ToListAsync(cancellationToken);
        foreach (var p in changed)
        {
            p.ChangedAfterNotice = true;
        }

        return changed.Count;
    }

    private async Task<Result<Guid>> BuildBatchAsync(PayrollRun run, CancellationToken ct)
    {
        var slips = (await SlipsQuery(p => p.PayrollRunId == run.Id).ToListAsync(ct)).OrderBy(s => s.Name, StringComparer.CurrentCulture).ToList();
        var info = await CompanyAsync(ct);
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var slip in slips)
            {
                var file = await FileAsync(run, slip, info, ct);
                if (file.IsFailure)
                {
                    return file.Error!;
                }

                var name = file.Value.File.FileName;
                if (!used.Add(name))
                {
                    name = PayslipDocumentRules.FileName(run.Period, null, slip.Payslip.Id);
                    used.Add(name);
                }

                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var target = entry.Open();
                await using var source = file.Value.File.Content;
                await source.CopyToAsync(target, ct);
            }
        }

        var stored = await attachments.StoreGeneratedAsync(PayslipDocumentRules.BatchOwnerType, run.Id, PayslipDocumentRules.BatchFileName(run.Period, run.Type), ZipContentType, buffer.ToArray(), ct);
        await db.SaveChangesAsync(ct);
        return stored.Id;
    }

    /// <summary>
    /// O PDF do holerite: no rascunho, gerado agora com a marca d'água; com a folha fechada, o guardado (limpo na varredura) ou, sem ele,
    /// gerado e guardado como arquivo do sistema. Duas gerações ao mesmo tempo: a segunda entrega o PDF sem guardar (versão do holerite).
    /// </summary>
    private async Task<Result<(PayslipFile File, Guid? AttachmentId)>> FileAsync(PayrollRun run, Slip slip, PayslipPdfCompany info, CancellationToken ct)
    {
        var payslip = slip.Payslip;
        var name = PayslipDocumentRules.FileName(run.Period, slip.Registration, payslip.Id);
        var draft = run.Status == PayrollRunStatus.Draft;
        if (!draft && payslip.PdfAttachmentId is { } current && await attachments.FindAsync(current, ct) is { ScanStatus: ScanStatus.Clean } existing
            && existing.OwnerType == PayslipDocumentRules.OwnerType && existing.OwnerId == payslip.Id)
        {
            return (new PayslipFile(await storage.OpenReadAsync(existing.StoragePath, ct), name, PdfContentType), existing.Id);
        }

        var lines = await db.PayslipLines.AsNoTracking().Where(l => l.PayslipId == payslip.Id).OrderBy(l => l.Sort)
            .Select(l => new PayslipPdfLine(l.Code, l.Description, l.Type, l.Reference, l.Amount)).ToListAsync(ct);
        if (lines.Count > PayslipDocumentRules.MaxPdfLines)
        {
            return TooManyLines();
        }

        var model = new PayslipPdfModel(info, new PayslipPdfEmployee(slip.Name, slip.Registration, PayslipDocumentRules.MaskCpf(slip.Cpf), slip.Position, slip.Cbo, slip.Admission),
            run.Period, run.Type, lines, payslip.Gross, payslip.Deductions, payslip.Net, payslip.InssBase, payslip.FgtsBase, payslip.IrrfBase, payslip.FgtsAmount, draft || run.IsSimulation,
            run.IsSimulation ? PayslipDocumentRules.SimulationWatermark : null);
        var bytes = await renderer.RenderAsync(new PayslipPdfDocument(model), ct);
        if (draft)
        {
            return (new PayslipFile(new MemoryStream(bytes, writable: false), name, PdfContentType), null);
        }

        // Guardado e ligado ao holerite na mesma gravação: a troca do pdf_attachment_id vai para a auditoria (quem, quando, qual holerite).
        var tracked = await db.Payslips.FirstAsync(p => p.Id == payslip.Id, ct);
        var stored = await attachments.StoreGeneratedAsync(PayslipDocumentRules.OwnerType, payslip.Id, name, PdfContentType, bytes, ct);
        tracked.PdfAttachmentId = stored.Id;
        if (await TrySaveAsync(ct))
        {
            payslip.PdfAttachmentId = stored.Id;
            return (new PayslipFile(new MemoryStream(bytes, writable: false), name, PdfContentType), stored.Id);
        }

        return (new PayslipFile(new MemoryStream(bytes, writable: false), name, PdfContentType), null);
    }

    /// <summary>Grava; no conflito de versão (outra geração ou leitura ao mesmo tempo), desiste sem erro e solta o que estava pendente.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            foreach (var pending in db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
            {
                pending.State = EntityState.Detached;
            }

            return false;
        }
    }

    private IQueryable<Slip> SlipsQuery(System.Linq.Expressions.Expression<Func<Payslip, bool>> filter) =>
        from p in db.Payslips.AsNoTracking().Where(filter)
        join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
        join pos in db.JobPositions.IgnoreQueryFilters().AsNoTracking() on e.PositionId equals pos.Id
        select new Slip(p, e.Name, e.Registration, e.Cpf, e.AdmissionDate, pos.Name, pos.Cbo);

    /// <summary>A empresa do cabeçalho (também do aviso de férias, FLH-03).</summary>
    internal async Task<PayslipPdfCompany> CompanyAsync(CancellationToken ct)
    {
        var info = await company.GetAsync(ct);
        byte[]? logo = null;
        if (info.LogoStoragePath is { } path)
        {
            await using var stream = await storage.OpenReadAsync(path, ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            logo = buffer.ToArray();
        }

        return new PayslipPdfCompany(info.LegalName, info.Document, logo);
    }

    private Task RecordReadAsync(Guid payslipId, Guid? attachmentId, CancellationToken ct) =>
        attachmentId is { } id
            ? audit.RecordAttachmentReadAsync(PayslipDocumentRules.OwnerType, payslipId, id, ct)
            : audit.RecordReadAsync(PayslipDocumentRules.OwnerType + ".Pdf", payslipId.ToString(), ct);

    private static Error TooManyLines() =>
        Error.Validation(PayslipDocumentRules.TooManyLinesCode, $"O holerite em PDF aceita até {PayslipDocumentRules.MaxPdfLines} linhas.");

    private static Error RunNotFound() => Error.NotFound("payroll_run_not_found", "Folha não encontrada.");

    internal static Error PayslipNotFound() => Error.NotFound("payslip_not_found", "Holerite não encontrado.");
}
