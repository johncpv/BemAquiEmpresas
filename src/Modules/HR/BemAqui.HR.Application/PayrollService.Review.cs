using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Conferência da folha (RH-02a CA 11, padrão 3 das Fases 4 a 8): totais por rubrica com os holerites que os compõem (a mesma soma), os
/// totais da folha, a variação do líquido contra o mesmo tipo do mês anterior (alerta acima do limite do CA 3), "Sem lançamento",
/// "Novos na folha" (primeira folha do funcionário) e "Conta alterada" (conta em quarentena, pelo <c>IEmployees</c>). A RH-02c repete
/// as duas últimas no fechamento.
/// </summary>
public sealed partial class PayrollService
{
    public async Task<Result<PayrollReview>> ReviewAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        var payslips = await (from p in db.Payslips.AsNoTracking()
                              join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                              where p.PayrollRunId == runId
                              orderby e.Name
                              select new { p.Id, p.EmployeeId, e.Name, p.Gross, p.Deductions, p.Net }).ToListAsync(cancellationToken);
        var ids = payslips.Select(p => p.Id).ToList();
        var lines = await db.PayslipLines.AsNoTracking().Where(l => ids.Contains(l.PayslipId)).ToListAsync(cancellationToken);
        var names = payslips.ToDictionary(p => p.Id, p => p.Name);
        var byItem = lines.GroupBy(l => l.PayrollItemId)
            .Select(g =>
            {
                var latest = g.OrderBy(l => l.Code, StringComparer.Ordinal).First();
                var parts = g.GroupBy(l => l.PayslipId).Select(x => new PayrollReviewPayslip(x.Key, names[x.Key], x.Sum(l => l.Amount))).OrderBy(x => x.EmployeeName, StringComparer.CurrentCulture).ToList();
                return new PayrollReviewItem(g.Key, latest.Code, latest.Description, latest.Type, g.Sum(l => l.Amount), parts);
            })
            .OrderBy(i => i.Kind).ThenBy(i => i.Code, StringComparer.Ordinal).ToList();

        var previousPeriod = run.Period.AddMonths(-1);
        var previousRun = await db.PayrollRuns.AsNoTracking().Where(r => r.Type == run.Type && r.Period == previousPeriod).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken);
        var previousNet = previousRun is { } prevId
            ? await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == prevId).ToDictionaryAsync(p => p.EmployeeId, p => p.Net, cancellationToken)
            : new Dictionary<Guid, decimal>();
        var alert = await parameters.NetVariationAlertPctAsync(cancellationToken);
        var variations = payslips.Select(p =>
        {
            decimal? before = previousNet.TryGetValue(p.EmployeeId, out var n) ? n : null;
            var variation = PayrollRules.Variation(p.Net, before);
            return new PayrollReviewVariation(p.Id, p.EmployeeId, p.Name, p.Net, before, variation, PayrollRules.AboveLimit(variation, alert));
        }).ToList();

        var withLines = lines.Select(l => l.PayslipId).ToHashSet();
        var empty = payslips.Where(p => !withLines.Contains(p.Id)).Select(p => new PayrollReviewEmployee(p.Id, p.EmployeeId, p.Name, null)).ToList();

        var employeeIds = payslips.Select(p => p.EmployeeId).ToList();
        var veterans = (await (from p in db.Payslips.AsNoTracking()
                               join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                               where employeeIds.Contains(p.EmployeeId) && r.Id != runId && r.Period < run.Period
                               select p.EmployeeId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var newcomers = payslips.Where(p => !veterans.Contains(p.EmployeeId)).Select(p => new PayrollReviewEmployee(p.Id, p.EmployeeId, p.Name, null)).ToList();

        var accounts = (await employees.BankAccountStatusAsync(employeeIds, cancellationToken)).Where(s => s.InQuarantine).ToDictionary(s => s.EmployeeId);
        var changed = payslips.Where(p => accounts.ContainsKey(p.EmployeeId)).Select(p => new PayrollReviewEmployee(p.Id, p.EmployeeId, p.Name, accounts[p.EmployeeId].QuarantineUntil)).ToList();

        await audit.RecordReadAsync(nameof(PayrollRun) + ".Review", runId.ToString(), cancellationToken);
        return new PayrollReview(payslips.Count, payslips.Sum(p => p.Gross), payslips.Sum(p => p.Deductions), payslips.Sum(p => p.Net), alert, previousRun is null ? null : previousPeriod,
            byItem, variations, empty, newcomers, changed);
    }
}
