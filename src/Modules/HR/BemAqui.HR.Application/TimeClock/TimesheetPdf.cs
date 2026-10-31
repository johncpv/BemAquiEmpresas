using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.HR.Application.Documents;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.HR.Application.TimeClock;

/// <summary>Uma linha do espelho em PDF: sem localização, sem motivo de saúde (o abono de saúde sai como "Ausência justificada").</summary>
public sealed record TimesheetPdfDay(DateOnly Day, string Entries, int Expected, int Worked, int Overtime, int Shortfall, int Justified, string Status);

/// <summary>
/// Espelho em PDF (RH-01c CA 18): o logo e a razão social do tenant, nome, matrícula, cargo, o período, os dias, os totais, o banco, o texto
/// de controle interno e o campo de assinatura. O modelo não tem CPF, salário, motivo de saúde nem localização.
/// </summary>
public sealed record TimesheetPdfModel(
    PayslipPdfCompany Company, string Name, string? Registration, string Position, DateOnly Period, DateOnly Start, DateOnly End, IReadOnlyList<TimesheetPdfDay> Days,
    TimesheetTotals Totals, DateTimeOffset? ClosedAt);

/// <summary>Layout do espelho (ADR 0022): A4, Archivo, dígitos tabulares, cabeçalho da tabela repetido na quebra.</summary>
public sealed class TimesheetPdfDocument(TimesheetPdfModel model) : IDocument
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] Columns = ["Dia", "Marcações", "Esperado", "Trabalhado", "Extra", "Falta/atraso", "Abono", "Situação"];

    private static string H(int minutes) => minutes == 0 ? "—" : TimesheetRules.Clock(minutes);

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Espelho de ponto {TimesheetRules.MonthYear(model.Period)} — {model.Name}", Author = model.Company.LegalName, Creator = "BemAqui Empresas",
    };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(28);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontSize(8).FontColor("#14202b").EnableFontFeature(FontFeatures.TabularFigures));
        page.Header().Element(Header);
        page.Content().PaddingVertical(10).Element(Content);
        page.Footer().Column(col =>
        {
            col.Item().AlignCenter().Text(TimesheetRules.PdfFooter).FontSize(8).SemiBold();
            col.Item().AlignCenter().Text(t =>
            {
                t.Span("Documento gerado pelo BemAqui Empresas. Página ").FontSize(7);
                t.CurrentPageNumber().FontSize(7);
                t.Span(" de ").FontSize(7);
                t.TotalPages().FontSize(7);
            });
        });
    });

    private void Header(IContainer c) => c.Row(row =>
    {
        if (model.Company.Logo is { Length: > 0 } logo)
        {
            row.ConstantItem(110).Height(40).Image(logo).FitArea();
        }

        row.RelativeItem().AlignRight().Column(col =>
        {
            col.Item().Text(model.Company.LegalName).SemiBold();
            col.Item().Text($"Espelho de ponto — {PayrollRules.PeriodLabel(model.Period)}").FontSize(11).Bold();
            col.Item().Text($"Período apurado: {TimesheetRules.Date(model.Start)} a {TimesheetRules.Date(model.End)}");
        });
    });

    private void Content(IContainer c) => c.Column(col =>
    {
        col.Spacing(8);
        col.Item().Background("#eef2f6").Padding(6).Row(r =>
        {
            r.RelativeItem(3).Text("Funcionário: " + model.Name).SemiBold();
            r.RelativeItem(2).Text("Matrícula: " + (model.Registration ?? "—"));
            r.RelativeItem(3).Text("Cargo: " + model.Position);
        });
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(52);
                cols.RelativeColumn(4);
                cols.ConstantColumn(44);
                cols.ConstantColumn(48);
                cols.ConstantColumn(38);
                cols.ConstantColumn(50);
                cols.ConstantColumn(38);
                cols.RelativeColumn(2);
            });
            table.Header(h =>
            {
                foreach (var title in Columns)
                {
                    h.Cell().BorderBottom(1).BorderColor("#9aa7b4").PaddingVertical(3).Text(title).SemiBold();
                }
            });
            foreach (var d in model.Days)
            {
                table.Cell().PaddingVertical(2).Text(d.Day.ToString("dd/MM ddd", PtBr));
                table.Cell().PaddingVertical(2).Text(d.Entries);
                table.Cell().PaddingVertical(2).Text(H(d.Expected));
                table.Cell().PaddingVertical(2).Text(H(d.Worked));
                table.Cell().PaddingVertical(2).Text(H(d.Overtime));
                table.Cell().PaddingVertical(2).Text(H(d.Shortfall));
                table.Cell().PaddingVertical(2).Text(H(d.Justified));
                table.Cell().PaddingVertical(2).Text(d.Status);
            }
        });
        var t = model.Totals;
        col.Item().Background("#eef2f6").Padding(6).Column(tot =>
        {
            tot.Item().Text($"Esperado {TimesheetRules.Duration(t.ExpectedMinutes)} · Trabalhado {TimesheetRules.Duration(t.WorkedMinutes)} · Extras {TimesheetRules.Duration(t.OvertimeMinutes)} · " +
                $"Faltas {TimesheetRules.Duration(t.AbsenceMinutes)} ({t.AbsenceDays} dia(s)) · Atrasos {TimesheetRules.Duration(t.LateMinutes)} · Abonado {TimesheetRules.Duration(t.JustifiedMinutes)}").SemiBold();
            if (t.BankMonthMinutes is { } month && t.BankBalanceMinutes is { } balance)
            {
                tot.Item().Text(TimesheetRules.BankText(month, balance));
            }
        });
        col.Item().PaddingTop(24).Row(r =>
        {
            r.RelativeItem(3).Column(s =>
            {
                s.Item().BorderBottom(1).BorderColor("#14202b").Height(18);
                s.Item().Text("Assinatura do funcionário");
            });
            r.ConstantItem(24);
            r.RelativeItem(1).Column(s =>
            {
                s.Item().BorderBottom(1).BorderColor("#14202b").Height(18);
                s.Item().Text("Data");
            });
        });
    });
}

/// <summary>Argumentos do lote (CA 18): só o tenant (no <see cref="TenantJob{T}"/>), o período e quem pediu, nunca dado pessoal.</summary>
public sealed record TimesheetBatchArgs(DateOnly Period, Guid? RequestedBy);

/// <summary>"Baixar espelhos em PDF" acima de 20 funcionários (CA 18): gera o ZIP no escopo do tenant e avisa quem pediu no sino.</summary>
public sealed class TimesheetBatchJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<TimesheetBatchJob> logger)
    : TenantJob<TimesheetBatchArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, TimesheetBatchArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        return services.GetRequiredService<TimesheetPdfService>().CompleteBatchAsync(args.Period, args.RequestedBy, cancellationToken);
    }
}

/// <summary>
/// PDF do espelho fechado (CA 18) pelo <see cref="IPdfRenderer"/>: guardado como arquivo do sistema (dono <c>Timesheet</c>, leitura com
/// <c>hr.view</c> ou pelo próprio funcionário) e reaproveitado até a reabertura; todo download fica auditado. Os espelhos de todos num ZIP
/// (dono <c>TimesheetBatch</c>, o período): até 20 na hora, acima pelo <see cref="TimesheetBatchJob"/> com o <c>tenantId</c>.
/// </summary>
public sealed class TimesheetPdfService(
    IHrDb db,
    ICompanyIdentity company,
    IFileStorage storage,
    IPdfRenderer renderer,
    AttachmentService attachments,
    IAuditWriter audit,
    INotificationService notifications,
    IBackgroundJobClient jobs,
    ITenantContext tenant,
    ICurrentUser currentUser)
{
    public const string OwnerType = "Timesheet";
    public const string BatchOwnerType = "TimesheetBatch";
    public const string PdfContentType = "application/pdf";
    public const string ZipContentType = "application/zip";
    public const int InlineBatchLimit = 20;

    /// <summary>Dono do ZIP do período: um id fixo por mês de referência (o período não tem linha própria).</summary>
    public static Guid BatchOwnerId(DateOnly period) => new(period.Year * 100 + period.Month, 0x7153, 0, [0, 0, 0, 0, 0, 0, 0, 0]);

    public static DateOnly? PeriodOfBatchOwner(Guid id)
    {
        var bytes = id.ToByteArray();
        var value = BitConverter.ToInt32(bytes, 0);
        return BitConverter.ToInt16(bytes, 4) == 0x7153 && value / 100 is >= 2000 and <= 2100 && value % 100 is >= 1 and <= 12 ? new DateOnly(value / 100, value % 100, 1) : null;
    }

    /// <summary>"Baixar espelho em PDF" pelo RH (<c>hr.view</c>): só com o período fechado (409); funcionário sem espelho no período, 404.</summary>
    public Task<Result<TimesheetFile>> PdfAsync(Guid employeeId, string? period, CancellationToken cancellationToken = default) => FileForAsync(employeeId, period, cancellationToken);

    /// <summary>O próprio espelho em PDF ("Meu espelho"): o funcionário vem da sessão.</summary>
    public Task<Result<TimesheetFile>> MinePdfAsync(EmployeeSummary employee, string? period, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        return FileForAsync(employee.Id, period, cancellationToken);
    }

    /// <summary>"Baixar espelhos em PDF" (todos, um arquivo por funcionário num ZIP).</summary>
    public async Task<Result<TimesheetBatchResult>> StartBatchAsync(string? period, CancellationToken cancellationToken = default)
    {
        if (PayrollRules.ParsePeriod(period) is not { } month)
        {
            return TimesheetService.InvalidPeriod();
        }

        var count = await db.Timesheets.AsNoTracking().CountAsync(t => t.Period == month && t.Status == TimesheetStatus.Closed, cancellationToken);
        if (count == 0)
        {
            return NotClosed(month);
        }

        var fileName = TimesheetRules.BatchFileName(month);
        if (count > InlineBatchLimit)
        {
            var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Espelhos em lote fora do escopo de tenant.");
            var args = new TimesheetBatchArgs(month, currentUser.UserId);
            jobs.Create<TimesheetBatchJob>(j => j.ExecuteAsync(tenantId, args, CancellationToken.None), new EnqueuedState(JobQueues.Default));
            return new TimesheetBatchResult(true, count, null, fileName, $"Gerando {count} espelhos…");
        }

        var stored = await BuildBatchAsync(month, cancellationToken);
        return new TimesheetBatchResult(false, count, stored, fileName, count == 1 ? "1 espelho pronto." : $"{count} espelhos prontos.");
    }

    /// <summary>Segundo plano: gera o ZIP e avisa quem pediu no sino, com o link para baixar.</summary>
    public async Task CompleteBatchAsync(DateOnly period, Guid? requestedBy, CancellationToken cancellationToken = default)
    {
        var stored = await BuildBatchAsync(period, cancellationToken);
        if (requestedBy is { } user)
        {
            notifications.Notify(user, TimesheetService.NotificationType, $"Espelhos de ponto de {PayrollRules.PeriodLabel(period)} prontos para baixar.",
                $"/app/rh/ponto/espelho?mes={period:yyyy-MM}&lote={stored}");
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>O ZIP gerado (dono <c>TimesheetBatch</c>); o download grava a leitura pelo dono (AuditReads).</summary>
    public async Task<Result<TimesheetFile>> BatchFileAsync(string? period, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        if (PayrollRules.ParsePeriod(period) is not { } month)
        {
            return TimesheetService.InvalidPeriod();
        }

        var content = await attachments.DownloadCheckedAsync(BatchOwnerType, BatchOwnerId(month), attachmentId, cancellationToken);
        return content.IsFailure ? content.Error! : new TimesheetFile(content.Value.Content, TimesheetRules.BatchFileName(month), ZipContentType);
    }

    private async Task<Result<TimesheetFile>> FileForAsync(Guid employeeId, string? period, CancellationToken ct)
    {
        if (PayrollRules.ParsePeriod(period) is not { } month)
        {
            return TimesheetService.InvalidPeriod();
        }

        var sheet = await db.Timesheets.AsNoTracking().FirstOrDefaultAsync(t => t.EmployeeId == employeeId && t.Period == month, ct);
        if (sheet is null && !await db.Employees.AnyAsync(e => e.Id == employeeId, ct))
        {
            return Error.NotFound("employee_not_found", "Funcionário não encontrado.");
        }

        if (sheet is null || sheet.Status != TimesheetStatus.Closed)
        {
            return NotClosed(month);
        }

        var (file, attachmentId) = await FileAsync(sheet, await CompanyAsync(ct), ct);
        await audit.RecordAttachmentReadAsync(OwnerType, sheet.Id, attachmentId, ct);
        return file;
    }

    private async Task<(TimesheetFile File, Guid AttachmentId)> FileAsync(Timesheet sheet, PayslipPdfCompany info, CancellationToken ct)
    {
        var person = await (from e in db.Employees.IgnoreQueryFilters().AsNoTracking()
                            join p in db.JobPositions.IgnoreQueryFilters().AsNoTracking() on e.PositionId equals p.Id
                            where e.Id == sheet.EmployeeId
                            select new { e.Name, e.Registration, Position = p.Name }).FirstAsync(ct);
        var name = TimesheetRules.FileName(sheet.Period, person.Registration, person.Name);
        if (sheet.PdfAttachmentId is { } current && await attachments.FindAsync(current, ct) is { ScanStatus: ScanStatus.Clean } existing
            && existing.OwnerType == OwnerType && existing.OwnerId == sheet.Id)
        {
            return (new TimesheetFile(await storage.OpenReadAsync(existing.StoragePath, ct), name, PdfContentType), existing.Id);
        }

        var days = JsonSerializer.Deserialize<List<TimesheetDayResult>>(sheet.DaysSnapshot ?? "[]", TimesheetService.SnapshotJson) ?? [];
        var model = new TimesheetPdfModel(info, person.Name, person.Registration, person.Position, sheet.Period, sheet.PeriodStart, sheet.PeriodEnd,
            days.Select(PdfDay).ToList(), TimesheetService.FrozenTotals(sheet), sheet.ClosedAt);
        var bytes = await renderer.RenderAsync(new TimesheetPdfDocument(model), ct);
        var stored = await attachments.StoreGeneratedAsync(OwnerType, sheet.Id, name, PdfContentType, bytes, ct);
        var tracked = await db.Timesheets.FirstAsync(t => t.Id == sheet.Id, ct);
        tracked.PdfAttachmentId = stored.Id;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra geração ao mesmo tempo: entrega o PDF gerado, sem ligar.
            db.ChangeTracker.Clear();
        }

        return (new TimesheetFile(new MemoryStream(bytes, writable: false), name, PdfContentType), stored.Id);
    }

    /// <summary>O dia no PDF: só as horas das marcações, a situação sem o tipo do afastamento e sem o motivo do abono.</summary>
    internal static TimesheetPdfDay PdfDay(TimesheetDayResult d) =>
        new(d.Day, string.Join(" ", d.Entries.Select(e => e.Time.ToString("HH:mm", CultureInfo.InvariantCulture))), d.ExpectedMinutes, d.WorkedMinutes, d.OvertimeMinutes,
            d.AbsenceMinutes + d.LateMinutes, d.JustifiedMinutes, TimesheetRules.StatusLabel(d.Status));

    private async Task<Guid> BuildBatchAsync(DateOnly period, CancellationToken ct)
    {
        var sheets = await db.Timesheets.AsNoTracking().Where(t => t.Period == period && t.Status == TimesheetStatus.Closed).ToListAsync(ct);
        var info = await CompanyAsync(ct);
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in sheets)
            {
                var (file, _) = await FileAsync(sheet, info, ct);
                var name = used.Add(file.FileName) ? file.FileName : $"espelho-{period:yyyy-MM}-{sheet.Id:N}.pdf";
                used.Add(name);
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var target = entry.Open();
                await using var source = file.Content;
                await source.CopyToAsync(target, ct);
            }
        }

        var stored = await attachments.StoreGeneratedAsync(BatchOwnerType, BatchOwnerId(period), TimesheetRules.BatchFileName(period), ZipContentType, buffer.ToArray(), ct);
        await db.SaveChangesAsync(ct);
        return stored.Id;
    }

    private async Task<PayslipPdfCompany> CompanyAsync(CancellationToken ct)
    {
        var data = await company.GetAsync(ct);
        byte[]? logo = null;
        if (data.LogoStoragePath is { } path)
        {
            await using var stream = await storage.OpenReadAsync(path, ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            logo = buffer.ToArray();
        }

        return new PayslipPdfCompany(data.LegalName, data.Document, logo);
    }

    private static Error NotClosed(DateOnly period) =>
        Error.Conflict(TimesheetRules.NotClosedCode, $"Feche o período de ponto de {PayrollRules.PeriodLabel(period)} para baixar o espelho em PDF.");
}
