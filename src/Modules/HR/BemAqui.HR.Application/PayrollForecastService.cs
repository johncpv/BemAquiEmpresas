using BemAqui.Finance.Contracts;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Folha prevista (RH-02c CA 14 e 15, ADR 0036 §4): só o HR conhece a folha, então é ele que mantém os títulos <c>Planned</c> estimados
/// (origem <c>PayrollForecast</c>, <c>origin_id</c> = linha de <c>hr.payroll_forecasts</c>) dos meses depois da última Mensal fechada. A
/// linha que já existe é atualizada pelo <c>ReplacePlannedAsync</c>; a nova nasce pelo <c>CreatePlannedInstallmentsAsync</c>; a que saiu
/// (o mês ganhou a folha real, a pessoa saiu da janela) é cancelada pela origem. Não grava: quem chama grava na própria transação.
/// </summary>
public sealed class PayrollForecastService(IHrDb db, IPayables payables, HrParametersService parameters, PayrollCostTargets targets, TimeProvider clock)
{
    public const string OutOfWindowReason = "Fora da janela da folha prevista";

    public async Task<Result<PayrollForecastResult>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var months = await parameters.ForecastMonthsAsync(cancellationToken);
        // Consulta rastreada de propósito: a folha que está fechando nesta transação já aparece fechada.
        var allRuns = await db.PayrollRuns.ToListAsync(cancellationToken);
        var last = allRuns.Where(r => r.Type == PayrollRunType.Monthly && r.RectifiesRunId == null && r.Status != PayrollRunStatus.Draft).OrderByDescending(r => r.Period).FirstOrDefault();
        var desired = last is null ? [] : await DesiredAsync(last, allRuns, months, cancellationToken);
        var existing = await db.PayrollForecasts.Where(f => f.CancelledAt == null).ToListAsync(cancellationToken);
        var byKey = existing.GroupBy(f => f.Key).ToDictionary(g => g.Key, g => g.First());
        var now = clock.GetUtcNow();
        var cost = desired.Count == 0 ? null : await targets.ResolveAsync(cancellationToken);
        if (cost is { IsFailure: true })
        {
            return cost.Error!;
        }

        var keep = new HashSet<Guid>();
        foreach (var line in desired)
        {
            var target = cost!.Value[line.Group];
            var plan = PlannedPayables.For(line.Payee, target.AccountId, target.CostCenterId, [new PlannedInstallment(1, line.Line.Amount, line.DueDate, line.Description)]);
            if (byKey.TryGetValue(line.Line.Key, out var row))
            {
                keep.Add(row.Id);
                row.Amount = line.Line.Amount;
                row.DueDate = line.DueDate;
                row.SourceRunId = line.Line.SourceRunId;
                row.UpdatedAt = now;
                var replaced = await payables.ReplacePlannedAsync(DocumentOrigin.PayrollForecast, row.Id, plan, cancellationToken);
                if (replaced.IsFailure)
                {
                    return replaced.Error!;
                }
            }
            else
            {
                var fresh = new PayrollForecast
                {
                    Period = line.Line.Period, Kind = line.Line.Kind, EmployeeId = line.Line.EmployeeId, ChargeKind = line.Line.ChargeKind, Amount = line.Line.Amount, DueDate = line.DueDate,
                    SourceRunId = line.Line.SourceRunId, CreatedAt = now, UpdatedAt = now,
                };
                db.PayrollForecasts.Add(fresh);
                keep.Add(fresh.Id);
                var created = await payables.CreatePlannedInstallmentsAsync(DocumentOrigin.PayrollForecast, fresh.Id, plan, cancellationToken);
                if (created.IsFailure)
                {
                    return created.Error!;
                }
            }
        }

        foreach (var row in existing.Where(f => !keep.Contains(f.Id)))
        {
            var reason = Replaced(row, allRuns) ? PayrollForecastRules.ReplacedReason : OutOfWindowReason;
            var cancelled = await payables.CancelAllFromOriginAsync(DocumentOrigin.PayrollForecast, row.Id, reason, cancellationToken);
            if (cancelled.IsFailure)
            {
                return cancelled.Error!;
            }

            row.CancelledAt = now;
            row.CancelReason = reason;
        }

        var window = last is null ? [] : PayrollForecastRules.Window(last.Period, months);
        return new PayrollForecastResult(desired.Count, months, window.Count > 0 ? window[0] : null, window.Count > 0 ? window[^1] : null);
    }

    private sealed record Desired(ForecastLine Line, Payee Payee, PayrollCostGroup Group, DateOnly DueDate, string Description);

    /// <summary>A folha real daquele mês e tipo já fechou: a previsão foi substituída (CA 15).</summary>
    private static bool Replaced(PayrollForecast row, IReadOnlyList<PayrollRun> runs)
    {
        var type = PayrollForecastRules.RunType(row.Kind);
        return row.Kind switch
        {
            PayrollForecastKind.Salary or PayrollForecastKind.Charge => runs.Any(r => r.Type == PayrollRunType.Monthly && r.Period >= row.Period && r.Status != PayrollRunStatus.Draft),
            PayrollForecastKind.Vacation => runs.Any(r => r.Type == PayrollRunType.Vacation && r.Period == row.Period && r.Status != PayrollRunStatus.Draft),
            _ => runs.Any(r => r.Type == type && r.Period.Year == row.Period.Year && r.Status != PayrollRunStatus.Draft),
        };
    }

    private async Task<List<Desired>> DesiredAsync(PayrollRun last, IReadOnlyList<PayrollRun> runs, int months, CancellationToken ct)
    {
        var slips = await (from p in db.Payslips.AsNoTracking()
                           join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                           join pos in db.JobPositions.IgnoreQueryFilters().AsNoTracking() on e.PositionId equals pos.Id
                           where p.PayrollRunId == last.Id && !e.IsDeleted && e.Status != EmployeeStatus.Terminated
                           select new { p.EmployeeId, p.Net, p.FgtsBase, e.BaseSalary, e.TerminationDate, e.Name, e.Registration, Direct = pos.DefaultWorkCenterId != null }).ToListAsync(ct);
        var people = slips.ToDictionary(s => s.EmployeeId);
        var groupOf = slips.ToDictionary(s => s.EmployeeId, s => s.Direct ? PayrollCostGroup.Direct : PayrollCostGroup.Admin);
        var charges = await db.PayrollRunCharges.AsNoTracking().Where(c => c.PayrollRunId == last.Id && c.PayeePartyId != null).ToListAsync(ct);
        var draftVacations = runs.Where(r => r.Type == PayrollRunType.Vacation && r.Status == PayrollRunStatus.Draft).ToList();
        var vacationIds = draftVacations.Select(r => r.Id).ToList();
        var vacations = await db.Payslips.AsNoTracking().Where(p => vacationIds.Contains(p.PayrollRunId)).Select(p => new { p.EmployeeId, p.Net, p.PayrollRunId }).ToListAsync(ct);
        var vacationIdsOfPeople = vacations.Select(v => v.EmployeeId).Distinct().ToList();
        var vacationPeople = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => vacationIdsOfPeople.Contains(e.Id))
            .Join(db.JobPositions.IgnoreQueryFilters().AsNoTracking(), e => e.PositionId, p => p.Id, (e, p) => new { e.Id, e.Name, e.Registration, Direct = p.DefaultWorkCenterId != null })
            .ToDictionaryAsync(e => e.Id, ct);
        var input = new ForecastInput(last.Period, last.Id, months,
            slips.Select(s => new ForecastEmployee(s.EmployeeId, s.Net, s.BaseSalary, s.TerminationDate)).ToList(),
            charges.GroupBy(c => c.Kind).Select(g => new ForecastCharge(g.Key, g.Sum(c => c.Amount))).ToList(),
            vacations.Select(v => new ForecastVacation(v.EmployeeId, draftVacations.First(r => r.Id == v.PayrollRunId).Period, v.Net, v.PayrollRunId)).ToList(),
            runs.Where(r => r.Type is PayrollRunType.Thirteenth1 or PayrollRunType.Thirteenth2 && r.Status != PayrollRunStatus.Draft).Select(r => (r.Type, r.Period.Year)).ToHashSet());
        var chargeGroup = PayrollClosingRules.ChargeGroup(slips.Select(s => (groupOf[s.EmployeeId], s.FgtsBase)));
        var due = await targets.DueParametersAsync(ct);
        var result = new List<Desired>();
        foreach (var line in PayrollForecastRules.Plan(input))
        {
            var type = PayrollForecastRules.RunType(line.Kind);
            if (line.Kind == PayrollForecastKind.Charge)
            {
                var charge = charges.Where(c => c.Kind == line.ChargeKind).OrderBy(c => c.CreatedAt).First();
                var chargeDue = await ChargeDueAsync(charge, last.Period, line.Period, ct);
                result.Add(new Desired(line, Payee.Party(charge.PayeePartyId!.Value), chargeGroup, chargeDue, PayrollClosingRules.ChargeDescription(charge.Kind, line.Period, forecast: true)));
                continue;
            }

            var employee = line.EmployeeId!.Value;
            var (name, registration, group) = people.TryGetValue(employee, out var p) ? (p.Name, p.Registration, groupOf[employee])
                : vacationPeople.TryGetValue(employee, out var v) ? (v.Name, v.Registration, v.Direct ? PayrollCostGroup.Direct : PayrollCostGroup.Admin) : (string.Empty, null, PayrollCostGroup.Admin);
            var date = line.Kind == PayrollForecastKind.Vacation
                ? draftVacations.First(r => r.Id == line.SourceRunId).PaymentDate ?? line.Period
                : await targets.PaymentDateAsync(PayrollClosingRules.Rule(type, line.Period, due), ct) ?? PayrollRules.PeriodEnd(line.Period.AddMonths(1));
            result.Add(new Desired(line, Payee.Employee(employee), group, date, PayrollClosingRules.SalaryDescription(type, line.Period, registration, name, forecast: true)));
        }

        return result;
    }

    /// <summary>Vencimento da guia prevista: o padrão do tipo (FGTS, INSS e IRRF) ou o dia da última guia no mês seguinte ao previsto, antecipado.</summary>
    private async Task<DateOnly> ChargeDueAsync(PayrollRunCharge last, DateOnly lastPeriod, DateOnly period, CancellationToken ct)
    {
        if (HrParametersService.DueDayCharges.Contains(last.Kind)
            && (await parameters.ChargeDefaultsAsync(period, ct)).FirstOrDefault(d => d.Kind == last.Kind)?.DueDate is { } standard)
        {
            return standard;
        }

        var shift = (period.Year - lastPeriod.Year) * 12 + period.Month - lastPeriod.Month;
        return await targets.PreviousBusinessDayAsync(last.DueDate.AddMonths(shift), ct);
    }
}
