using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Application.Documents;
using BemAqui.HR.Application.TimeClock;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application.Exports;

/// <summary>
/// Exportação das telas do RH (EXP-05 CA 11 a 15, ADR 0045 §3 e §5): cada método usa a mesma consulta da tela, com os mesmos filtros e sem
/// paginação; CPF, salário e valores por pessoa só saem completos com <c>hr.view_sensitive</c> (o spec mascara, sem 2FA, decisão do PO);
/// tipo e motivo do afastamento só com <c>hr.view_health</c>. Teto, auditoria (com <c>sensitive</c>) e download no <see cref="ListExportService"/>.
/// </summary>
public sealed class HrExportService(
    IHrDb db,
    EmployeeService employees,
    PayrollService payroll,
    PositionService positions,
    TimesheetService timesheets,
    PayrollCalculationService calculation,
    IPermissionChecker permissions,
    PdfFrameFactory frames,
    IPdfRenderer renderer,
    IAuditWriter audit,
    TimeProvider clock)
{
    public const string ThirteenthAuditEntity = "PayrollRun.ThirteenthPreview";

    private ValueTask<bool> CanAsync(string action, CancellationToken ct) => permissions.HasPermissionAsync("hr", action, ct);

    /// <summary>Funcionários (CA 12): a busca, os filtros e a ordem da lista; jornada e sindicato pelo nome.</summary>
    public async Task<Result<ExportData<EmployeeExportRow>>> EmployeesAsync(EmployeeListQuery query, string? sort, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = await employees.ListAsync(query, new PageRequest(1, Math.Max(1, max), sort), ct);
        var sensitive = await CanAsync(PositionService.SensitiveAction, ct);
        var health = await CanAsync(EmployeeService.HealthAction, ct);
        var ids = page.Items.Select(e => e.Id).ToList();
        var stored = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.Cpf, e.BaseSalary }).ToDictionaryAsync(e => e.Id, ct);
        var scheduleIds = page.Items.Select(e => e.WorkScheduleId).OfType<Guid>().Distinct().ToList();
        var schedules = await db.WorkSchedules.IgnoreQueryFilters().AsNoTracking().Where(s => scheduleIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var unionIds = page.Items.Select(e => e.UnionId).OfType<Guid>().Distinct().ToList();
        var unions = await db.LaborUnions.IgnoreQueryFilters().AsNoTracking().Where(u => unionIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var today = BusinessTime.Today(clock);
        var leaves = sensitive
            ? (await db.EmployeeHistory.AsNoTracking()
                .Where(h => ids.Contains(h.EmployeeId) && h.Kind == EmployeeHistoryKind.Leave && h.CancelledAt == null && h.EffectiveDate <= today && h.LeaveEndDate >= today)
                .Select(h => new { h.EmployeeId, h.EffectiveDate, h.LeaveEndDate, h.LeaveType, h.Reason }).ToListAsync(ct))
                .GroupBy(h => h.EmployeeId).ToDictionary(g => g.Key, g => g.First())
            : [];
        var rows = page.Items.Select(e =>
        {
            var s = stored.GetValueOrDefault(e.Id);
            (DateOnly, DateOnly, AbsenceReason?, string?)? leave = leaves.TryGetValue(e.Id, out var l) ? (l.EffectiveDate, l.LeaveEndDate ?? l.EffectiveDate, l.LeaveType, l.Reason) : null;
            return new EmployeeExportRow(e.Registration, e.Name, s?.Cpf, e.PositionName, e.AdmissionDate, HrExports.Situation(e.Status, leave, sensitive, health),
                e.WorkScheduleId is { } w ? schedules.GetValueOrDefault(w) : null, e.UnionId is { } u ? unions.GetValueOrDefault(u) : null, sensitive ? s?.BaseSalary : null);
        }).ToList();
        var position = query.PositionId is { } pid ? (await positions.GetAsync(pid, ct)) is { IsSuccess: true } p ? p.Value.Name : null : null;
        ExportFilter[] filters =
        [
            new("Busca", string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim()),
            new("Situação", query.Status is { } st ? HrExports.EmployeeStatusLabel(st) : null),
            new("Cargo", position),
            new("Centro", query.WorkCenterId is null ? null : "O centro escolhido na tela"),
        ];
        var data = new ExportData<EmployeeExportRow>(HrExports.Employees, filters, new ExportRows<EmployeeExportRow>(page.Total, rows));
        return sensitive ? data : data with { Decorate = t => t with { Notes = ["CPF e salário saem mascarados: o completo fica com quem vê os dados sensíveis do RH."] } };
    }

    /// <summary>Folhas (CA 13): competência, tipo, modo, situação e holerites com <c>hr.view</c>; os totais só com <c>hr.view_sensitive</c>.</summary>
    public async Task<Result<ExportData<PayrollRunExportRow>>> PayrollRunsAsync(CancellationToken ct)
    {
        var runs = await payroll.ListAsync(ct);
        var totals = await CanAsync(PositionService.SensitiveAction, ct) ? (await payroll.TotalsAsync(ct)).ToDictionary(t => t.RunId) : [];
        var rows = runs.Select(r =>
        {
            var t = totals.GetValueOrDefault(r.Id);
            var mode = r.Mode == PayrollRunMode.Calculated ? r.RectifiesRunId is null ? "Calculada" : "Calculada (retificação)" : "Lançada";
            return new PayrollRunExportRow(HrExports.Period(r.Period), HrExports.RunTypeLabel(r.Type), r.IsSimulation ? mode + " · simulação" : mode, HrExports.RunStatusLabel(r.Status),
                r.PayslipCount, t?.Gross, t?.Deductions, t?.Net);
        }).ToList();
        return new ExportData<PayrollRunExportRow>(HrExports.PayrollRuns, [], new ExportRows<PayrollRunExportRow>(rows.Count, rows));
    }

    /// <summary>Holerites da folha (CA 13): a rota exige <c>hr.view_sensitive</c>; folha de outro tenant ou inexistente, 404.</summary>
    public async Task<Result<ExportData<PayslipExportRow>>> PayslipsAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            return Error.NotFound("payroll_run_not_found", "Folha não encontrada.");
        }

        var rows = await (from p in db.Payslips.AsNoTracking()
                          join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                          join pos in db.JobPositions.IgnoreQueryFilters().AsNoTracking() on e.PositionId equals pos.Id into joined
                          from pos in joined.DefaultIfEmpty()
                          where p.PayrollRunId == runId
                          orderby e.Name
                          select new { e.Registration, e.Name, Position = pos == null ? null : pos.Name, p.Gross, p.Deductions, p.Net, p.FgtsAmount, p.NotifiedAt, Lines = db.PayslipLines.Count(l => l.PayslipId == p.Id) })
            .ToListAsync(ct);
        var closed = run.Status != PayrollRunStatus.Draft;
        var mapped = rows.Select(r => new PayslipExportRow(r.Registration, r.Name, r.Position, r.Gross, r.Deductions, r.Net, r.FgtsAmount,
            r.Lines == 0 ? run.Mode == PayrollRunMode.Calculated ? "Sem cálculo" : "Sem lançamento" : !closed ? "Em lançamento" : r.NotifiedAt is null ? "Fechado" : "Fechado e avisado", false)).ToList();
        var all = HrExports.PayslipRows(mapped);
        ExportFilter[] filters = [new("Folha", $"{HrExports.RunTypeLabel(run.Type)} {HrExports.Period(run.Period)}"), new("Situação da folha", HrExports.RunStatusLabel(run.Status))];
        return new ExportData<PayslipExportRow>(HrExports.Payslips(run.Period, run.Type), filters, new ExportRows<PayslipExportRow>(all.Count, all)) { Decorate = t => t with { SheetName = "Holerites" } };
    }

    /// <summary>Espelho do período (CA 14): a visão geral da tela (sem tipo de afastamento, que a visão geral não mostra).</summary>
    public async Task<Result<ExportData<TimesheetOverviewRow>>> TimesheetsAsync(string? period, CancellationToken ct)
    {
        var overview = await timesheets.OverviewAsync(period, ct);
        if (overview.IsFailure)
        {
            return overview.Error!;
        }

        var o = overview.Value;
        ExportFilter[] filters =
        [
            new("Competência", HrExports.Period(o.Period)),
            new("Período", $"{o.Start.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)} a {o.End.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)}"),
            new("Situação", o.Status == TimesheetStatus.Closed ? "Fechado" : "Em aberto"),
        ];
        return new ExportData<TimesheetOverviewRow>(HrExports.Timesheets(o.Period), filters, new ExportRows<TimesheetOverviewRow>(o.Rows.Count, o.Rows)) { Decorate = t => t with { SheetName = "Espelho" } };
    }

    /// <summary>Cargos (CA 15): a busca e os inativos da lista; salário base e custo-hora mascarados sem <c>hr.view_sensitive</c>.</summary>
    public async Task<Result<ExportData<PositionExportRow>>> PositionsAsync(PositionListQuery query, string? sort, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = await positions.ListAsync(query, new PageRequest(1, Math.Max(1, max), sort), ct);
        var ids = page.Items.Select(p => p.Id).ToList();
        var salaries = await CanAsync(PositionService.SensitiveAction, ct)
            ? await db.JobPositions.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.ReferenceSalary, ct)
            : [];
        var rows = page.Items.Select(p => new PositionExportRow(p.Name, p.Cbo, p.DefaultWorkCenterName, p.WorkScheduleName, p.MonthlyHours, salaries.GetValueOrDefault(p.Id), p.HourlyCost,
            p.ActiveEmployees, p.IsActive ? "Ativo" : "Inativo")).ToList();
        ExportFilter[] filters = [new("Busca", string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim()), new("Inativos", query.IncludeInactive ? "Incluídos" : null)];
        return new ExportData<PositionExportRow>(HrExports.Positions, filters, new ExportRows<PositionExportRow>(page.Total, rows));
    }

    /// <summary>PDF da prévia do 13º (CA 11): sempre "Simulação", <c>previa-13-{ano}-{parcela}.pdf</c>, auditado.</summary>
    public async Task<Result<(byte[] Content, string FileName)>> ThirteenthPreviewPdfAsync(int? year, PayrollRunType? type, CancellationToken ct)
    {
        var simulation = await calculation.ThirteenthSimulationAsync(year, type, ct);
        if (simulation.IsFailure)
        {
            return simulation.Error!;
        }

        var model = simulation.Value;
        var frame = await frames.CreateAsync(ct);
        var bytes = await renderer.RenderAsync(new ThirteenthPreviewPdfDocument(frame, model), ct);
        await audit.RecordExportAsync(ThirteenthAuditEntity, $"{model.Preview.Year}-{model.Preview.Type}", "pdf", model.Rows.Count, ct);
        return (bytes, ThirteenthPreviewPdfDocument.FileName(model.Preview.Year, model.Preview.Type));
    }
}
