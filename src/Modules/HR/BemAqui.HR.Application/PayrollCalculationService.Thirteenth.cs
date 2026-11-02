using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>Um mês nos avos da prévia (sem valor): mês, dias trabalhados e se conta.</summary>
public sealed record ThirteenthPreviewMonth(int Month, string Label, int EmployedDays, int AbsenceDays, int LeaveDays, int WorkedDays, bool Counts, bool Projected);

/// <summary>Funcionário na prévia dos avos (FLH-04, "prévia dos avos por funcionário antes de calcular"): sem valor.</summary>
public sealed record ThirteenthPreviewEmployee(Guid EmployeeId, string Name, string? Registration, int Avos, IReadOnlyList<ThirteenthPreviewMonth> Months);

/// <summary>
/// Prévia da folha de 13º (CA 3 e 8): a competência proposta, a data limite, se a 1ª parcela do ano já fechou (a 2ª sem ela pede
/// confirmação), se já existe a calculada do tipo no ano e os avos de cada funcionário ativo, mês a mês. Nenhum valor.
/// </summary>
public sealed record ThirteenthPreview(int Year, PayrollRunType Type, string Period, DateOnly? DueDate, bool FirstClosed, bool Exists, int MonthMinDays, IReadOnlyList<ThirteenthPreviewEmployee> Employees);

/// <summary>
/// Folha calculada do 13º (FLH-04, ADR 0039): o mesmo caminho da Mensal (calcular, recalcular, segundo plano, guias calculadas, histórico,
/// simulação e fechamento), com o retrato do 13º (<see cref="ThirteenthSnapshot"/>) e o motor <see cref="ThirteenthCalculator"/>. As
/// faltas do ano vêm dos espelhos fechados de cada mês; as médias, das horas da FLH-02b nas folhas Mensais fechadas do ano; o adiantamento,
/// das folhas de 13º 1ª parcela fechadas (lançadas ou calculadas) e das férias fechadas com a rubrica da 1ª parcela (FLH-03).
/// </summary>
public sealed partial class PayrollCalculationService
{
    private static string VersionOf(PayrollRun run) => ThirteenthCalculator.IsThirteenth(run.Type) ? ThirteenthCalculator.Version : PayrollCalculator.Version;

    /// <summary>Oficial pelo modo do tipo e da competência (FLH-06): no 13º, a Mensal virada e o 13º virado com um caso conferido (CA 5).</summary>
    private async Task<bool> OfficialForAsync(PayrollRun run, CancellationToken ct) =>
        await parameters.CalculatedOfficialAsync(run.Type, run.Period, ct);

    private static Error ThirteenthNotRectifiable() =>
        Error.Validation(PayrollCalculationRules.NotSupportedCode, "O 13º calculado é corrigido pelo recálculo enquanto está em rascunho; a retificação do 13º fechado chega com a virada (FLH-06).");

    /// <summary>O contexto da folha (tabelas, rubricas, parâmetros); no 13º, as rubricas do 13º e os parâmetros do 13º também.</summary>
    private async Task<Result<Context>> ContextForAsync(PayrollRun run, CancellationToken ct)
    {
        var context = await ContextAsync(run, ct);
        if (context.IsFailure || !ThirteenthCalculator.IsThirteenth(run.Type))
        {
            return context;
        }

        PayrollCalculation[] required = ThirteenthCalculator.InstallmentOf(run.Type) == ThirteenthInstallment.First
            ? [PayrollCalculation.ThirteenthAdvance]
            : [PayrollCalculation.ThirteenthFinal, PayrollCalculation.ThirteenthAdvanceDeduction];
        if (required.FirstOrDefault(r => context.Value.Items.All(i => i.Calculation != r)) is var missing && missing != PayrollCalculation.None)
        {
            return Error.Validation(PayrollCalculationRules.ItemMissingCode, $"Falta a rubrica com a regra {PayrollCalculator.Label(missing)}: confira em RH → Rubricas.") with
            {
                Data = new Dictionary<string, object?> { ["link"] = "/app/rh/rubricas" },
            };
        }

        var (_, ids) = await parameters.ThirteenthParametersAsync(ct);
        return context.Value with { ParameterIds = [.. context.Value.ParameterIds, .. ids] };
    }

    /// <summary>Roda o motor do 13º nos holerites; quem não tem avos no ano sai da folha (CA 1, estado vazio). Devolve os holerites que ficaram.</summary>
    private async Task<List<Payslip>> ApplyThirteenthAsync(PayrollRun run, Context context, List<Payslip> payslips, CancellationToken ct)
    {
        var (prm13, _) = await parameters.ThirteenthParametersAsync(ct);
        var snapshots = await ThirteenthSnapshotsAsync(run, payslips.Select(p => p.EmployeeId).ToList(), context.Parameters, prm13, ct);
        var ids = payslips.Select(p => p.Id).ToList();
        db.PayslipLines.RemoveRange(await db.PayslipLines.Where(l => ids.Contains(l.PayslipId)).ToListAsync(ct));
        var now = clock.GetUtcNow();
        var kept = new List<Payslip>();
        foreach (var payslip in payslips)
        {
            var snapshot = snapshots[payslip.EmployeeId];
            var result = ThirteenthCalculator.Calculate(new ThirteenthInput(snapshot, context.Items, context.Tables));
            if (result.Issues.Any(i => i.Blocking && i.Code == ThirteenthCalculator.NoAvosCode))
            {
                db.Payslips.Remove(payslip);
                continue;
            }

            db.PayslipLines.AddRange(result.Lines.Select((l, i) => ToLine(payslip.Id, l, i)));
            payslip.ApplyTotals(result.Totals);
            payslip.InssBase = result.InssBase;
            payslip.IrrfBase = result.IrrfBase;
            payslip.FgtsBase = result.FgtsBase;
            payslip.FgtsAmount = result.FgtsAmount;
            payslip.EmployerCharges = result.EmployerCharges;
            payslip.EmployerChargesMemory = PayrollCalculator.WriteMemory(result.EmployerMemory);
            payslip.CalculationIssues = PayrollCalculator.WriteIssues(result.Issues);
            payslip.InputsSnapshot = ThirteenthCalculator.WriteSnapshot(snapshot);
            payslip.TaxTableIds = [.. result.TableIds];
            payslip.ParameterIds = [.. context.ParameterIds];
            payslip.CalculatorVersion = ThirteenthCalculator.Version;
            payslip.CalculatedAt = now;
            payslip.UpdatedAt = now;
            payslip.PdfAttachmentId = null;
            kept.Add(payslip);
        }

        return kept;
    }

    /// <summary>
    /// Retrato do 13º (ADR 0039 §2), em ordem estável: salário de referência (1ª parcela: o do fim do mês anterior; 2ª: o de dezembro),
    /// vínculo, adicionais, dependentes, afastamentos e faltas do ano, variáveis por mês (horas da FLH-02b nas Mensais fechadas), a 1ª parcela
    /// paga (folhas de 13º 1ª parcela fechadas do ano) e o adiantamento pago com as férias (FLH-03).
    /// </summary>
    private async Task<Dictionary<Guid, ThirteenthSnapshot>> ThirteenthSnapshotsAsync(PayrollRun run, IReadOnlyCollection<Guid> employeeIds, PayrollCalcParameters prm, ThirteenthParameters prm13,
        CancellationToken ct)
    {
        var year = run.Period.Year;
        var jan = new DateOnly(year, 1, 1);
        var dec = new DateOnly(year, 12, 31);
        var installment = ThirteenthCalculator.InstallmentOf(run.Type);
        var lastMonth = ThirteenthCalculator.LastMonth(installment, run.Period.Month);
        var reference = installment == ThirteenthInstallment.First ? PayrollRules.PeriodEnd(run.Period.AddMonths(-1)) : PayrollRules.PeriodEnd(run.Period);
        var ids = employeeIds.ToList();
        var employees = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(ct);
        var salaries = await runs.SalariesAtAsync(employees.ToDictionary(e => e.Id, e => e.BaseSalary), reference, ct);
        var dependents = (await db.EmployeeDependents.AsNoTracking().Where(d => ids.Contains(d.EmployeeId)).ToListAsync(ct)).ToLookup(d => d.EmployeeId);
        var leaves = (await db.EmployeeHistory.AsNoTracking()
            .Where(h => ids.Contains(h.EmployeeId) && h.Kind == EmployeeHistoryKind.Leave && h.CancelledAt == null && h.EffectiveDate <= dec && (h.LeaveEndDate == null || h.LeaveEndDate >= jan))
            .ToListAsync(ct)).ToLookup(h => h.EmployeeId);
        var absences = (await db.Timesheets.AsNoTracking().Where(t => ids.Contains(t.EmployeeId) && t.Status == TimesheetStatus.Closed && t.Period >= jan && t.Period <= dec)
            .Select(t => new { t.EmployeeId, t.Period, t.AbsenceDays }).ToListAsync(ct)).ToLookup(t => t.EmployeeId);
        var monthly = await (from l in db.PayslipLines.AsNoTracking()
                             join p in db.Payslips.AsNoTracking() on l.PayslipId equals p.Id
                             join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                             join i in db.PayrollItems.IgnoreQueryFilters().AsNoTracking() on l.PayrollItemId equals i.Id
                             where ids.Contains(p.EmployeeId) && r.Type == PayrollRunType.Monthly && r.Status != PayrollRunStatus.Draft && r.RectifiesRunId == null && r.Period >= jan && r.Period <= dec
                             select new { p.EmployeeId, r.Period, l.Type, i.Calculation, l.Amount }).ToListAsync(ct);
        var variables = monthly.Where(x => ThirteenthCalculator.IsVariable(x.Type, x.Calculation)).ToLookup(x => x.EmployeeId);
        var advances = installment == ThirteenthInstallment.Second
            ? await (from p in db.Payslips.AsNoTracking()
                     join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                     where ids.Contains(p.EmployeeId) && r.Type == PayrollRunType.Thirteenth1 && r.Status != PayrollRunStatus.Draft && r.Period >= jan && r.Period <= dec
                     group p by p.EmployeeId into g
                     select new { g.Key, Gross = g.Sum(x => x.Gross) }).ToDictionaryAsync(x => x.Key, x => x.Gross, ct)
            : new Dictionary<Guid, decimal>();
        var vacation = await (from l in db.PayslipLines.AsNoTracking()
                              join p in db.Payslips.AsNoTracking() on l.PayslipId equals p.Id
                              join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                              join i in db.PayrollItems.IgnoreQueryFilters().AsNoTracking() on l.PayrollItemId equals i.Id
                              where ids.Contains(p.EmployeeId) && r.Type == PayrollRunType.Vacation && r.Status != PayrollRunStatus.Draft && r.Period >= jan && r.Period <= dec
                                  && i.Calculation == PayrollCalculation.ThirteenthAdvance && l.Type == PayrollItemKind.Earning
                              group l by p.EmployeeId into g
                              select new { g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
        var result = new Dictionary<Guid, ThirteenthSnapshot>();
        foreach (var e in employees)
        {
            result[e.Id] = new ThirteenthSnapshot(
                year, installment, lastMonth, salaries.GetValueOrDefault(e.Id), e.AdmissionDate, e.TerminationDate, e.InsalubrityPct, e.HazardPay,
                dependents[e.Id].OrderBy(d => d.BirthDate).ThenBy(d => d.IrDependent).ThenBy(d => d.FamilyAllowance).ThenBy(d => d.FamilyAllowanceDocsOk)
                    .Select(d => new PayrollCalcDependent(d.BirthDate, d.IrDependent, d.FamilyAllowance, d.FamilyAllowanceDocsOk)).ToList(),
                leaves[e.Id].OrderBy(l => l.EffectiveDate).ThenBy(l => l.LeaveEndDate).Select(l => new PayrollCalcLeave(l.EffectiveDate, l.LeaveEndDate ?? dec, l.LeaveType)).ToList(),
                absences[e.Id].GroupBy(a => a.Period.Month).OrderBy(g => g.Key).Select(g => new ThirteenthAbsence(g.Key, g.Sum(a => a.AbsenceDays))).Where(a => a.Days > 0).ToList(),
                variables[e.Id].GroupBy(v => v.Period.Month).OrderBy(g => g.Key).Select(g => new ThirteenthVariable(g.Key, g.Sum(v => v.Amount))).ToList(),
                advances.GetValueOrDefault(e.Id), vacation.GetValueOrDefault(e.Id), prm, prm13);
        }

        return result;
    }

    /// <summary>"Cadastro ou tabela mudou depois do cálculo" no 13º: as mesmas razões da Mensal, comparando o retrato do 13º.</summary>
    private async Task<(int Count, IReadOnlyList<string> Reasons)> ThirteenthStaleAsync(PayrollRun run, List<Payslip> payslips, CancellationToken ct)
    {
        var current = new List<Guid>();
        foreach (var kind in Enum.GetValues<PayrollTaxTableKind>())
        {
            if ((await taxTables.ForCompetenceAsync(kind, run.Period, ct)) is { IsSuccess: true } found)
            {
                current.Add(found.Value.Id);
            }
        }

        var usedIds = payslips.SelectMany(p => p.TaxTableIds).Distinct().ToList();
        var used = await db.PayrollTaxTables.AsNoTracking().Where(t => usedIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var (prm, _) = await parameters.CalculationParametersAsync(ct);
        var (prm13, _) = await parameters.ThirteenthParametersAsync(ct);
        var snapshots = await ThirteenthSnapshotsAsync(run, payslips.Select(p => p.EmployeeId).ToList(), prm, prm13, ct);
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var p in payslips)
        {
            var mine = new List<string>();
            if (p.TaxTableIds.Any(id => used.GetValueOrDefault(id) is { } t && t.EditedAt > p.CalculatedAt))
            {
                mine.Add("Tabela alterada depois do cálculo");
            }

            if (p.TaxTableIds.Any(id => used.GetValueOrDefault(id)?.Status == PayrollTaxTableStatus.Superseded) || !current.Order().SequenceEqual(p.TaxTableIds.Order()))
            {
                mine.Add("Tabela retificada depois do cálculo");
            }

            var stored = ThirteenthCalculator.ReadSnapshot(p.InputsSnapshot);
            if (stored is null || ThirteenthCalculator.WriteSnapshot(stored) != ThirteenthCalculator.WriteSnapshot(snapshots[p.EmployeeId]))
            {
                mine.Add("Cadastro ou parâmetro mudou depois do cálculo");
            }

            if (mine.Count > 0)
            {
                count++;
                reasons.UnionWith(mine);
            }
        }

        return (count, reasons.Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Prévia dos avos antes de criar a folha de 13º (Telas; CA 3 e 8), com <c>hr.view_sensitive</c> (as faltas e os afastamentos do ano
    /// saem dos dias): a competência proposta (o mês da data limite), a data limite, se a 1ª parcela já fechou e os avos mês a mês.
    /// </summary>
    public async Task<Result<ThirteenthPreview>> ThirteenthPreviewAsync(int? year, PayrollRunType? type, CancellationToken cancellationToken = default)
    {
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        if (year is not { } y || y is < 2000 or > 2100)
        {
            return PayrollRules.Field("year", "Escolha o ano do 13º.");
        }

        if (type is not { } t || !ThirteenthCalculator.IsThirteenth(t))
        {
            return PayrollRules.Field("type", "Escolha a 1ª ou a 2ª parcela do 13º.");
        }

        var period = await runs.ThirteenthPeriodAsync(t, y, cancellationToken);
        var due = await parameters.DueParametersAsync(cancellationToken);
        var dueDate = await calendar.PreviousBusinessDayAsync(t == PayrollRunType.Thirteenth1
            ? new DateOnly(y, due.ThirteenthFirstMonth, Math.Min(due.ThirteenthFirstDay, DateTime.DaysInMonth(y, due.ThirteenthFirstMonth)))
            : new DateOnly(y, due.ThirteenthSecondMonth, Math.Min(due.ThirteenthSecondDay, DateTime.DaysInMonth(y, due.ThirteenthSecondMonth))), cancellationToken);
        var end = PayrollRules.PeriodEnd(period);
        var people = await db.Employees.AsNoTracking().Where(e => e.AdmissionDate <= end && (e.TerminationDate == null || e.TerminationDate >= period))
            .OrderBy(e => e.Name).Select(e => new { e.Id, e.Name, e.Registration }).ToListAsync(cancellationToken);
        var (prm, _) = await parameters.CalculationParametersAsync(cancellationToken);
        var (prm13, _) = await parameters.ThirteenthParametersAsync(cancellationToken);
        var fake = new PayrollRun { Period = period, Type = t, Mode = PayrollRunMode.Calculated };
        var snapshots = await ThirteenthSnapshotsAsync(fake, people.Select(p => p.Id).ToList(), prm, prm13, cancellationToken);
        var rows = new List<ThirteenthPreviewEmployee>();
        foreach (var p in people)
        {
            var months = ThirteenthCalculator.MonthsOf(snapshots[p.Id]);
            var avos = months.Count(m => m.Counts);
            if (avos > 0)
            {
                rows.Add(new ThirteenthPreviewEmployee(p.Id, p.Name, p.Registration, avos,
                    months.Select(m => new ThirteenthPreviewMonth(m.Month, ThirteenthCalculator.MonthLabel(m.Month), m.EmployedDays, m.AbsenceDays, m.LeaveDays, m.WorkedDays, m.Counts, m.Projected)).ToList()));
            }
        }

        var jan = new DateOnly(y, 1, 1);
        var dec = new DateOnly(y, 12, 31);
        var firstClosed = await db.PayrollRuns.AsNoTracking().AnyAsync(r => r.Type == PayrollRunType.Thirteenth1 && r.Status != PayrollRunStatus.Draft && r.Period >= jan && r.Period <= dec, cancellationToken);
        var exists = await db.PayrollRuns.AsNoTracking().AnyAsync(r => r.Type == t && r.Mode == PayrollRunMode.Calculated && r.RectifiesRunId == null && r.Period >= jan && r.Period <= dec, cancellationToken);
        await audit.RecordReadAsync(nameof(PayrollRun) + ".ThirteenthPreview", $"{y}-{t}", cancellationToken);
        return new ThirteenthPreview(y, t, period.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), dueDate, firstClosed, exists, prm13.MonthMinDays, rows);
    }
}
