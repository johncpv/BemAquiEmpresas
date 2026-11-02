using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

public sealed partial class PayrollCalculationService
{
    /// <summary>
    /// O retrato do 13º do ano do desligamento (FLH-04): o mesmo da 2ª parcela (afastamentos, faltas e variáveis do ano, a 1ª parcela e o
    /// adiantamento das férias pagos), com a 2ª parcela já fechada no ano como o 13º inteiro pago.
    /// </summary>
    private async Task<(TerminationThirteenthSnapshot? Snapshot, List<Guid> Ids)> TerminationThirteenthAsync(Guid employeeId, DateOnly date, PayrollCalcParameters prm, CancellationToken ct)
    {
        var (prm13, ids) = await parameters.ThirteenthParametersAsync(ct);
        var fake = new PayrollRun { Period = new DateOnly(date.Year, 12, 1), Type = PayrollRunType.Thirteenth2, Mode = PayrollRunMode.Calculated };
        var s = (await ThirteenthSnapshotsAsync(fake, [employeeId], prm, prm13, ct))[employeeId];
        var jan = new DateOnly(date.Year, 1, 1);
        var dec = new DateOnly(date.Year, 12, 31);
        var second = await (from p in db.Payslips.AsNoTracking()
                            join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                            where p.EmployeeId == employeeId && r.Type == PayrollRunType.Thirteenth2 && r.Status != PayrollRunStatus.Draft && r.Period >= jan && r.Period <= dec
                            select p.Gross).SumAsync(ct);
        var snapshot = second > 0m
            ? new TerminationThirteenthSnapshot(s.Leaves, s.Absences, s.Variables, second, 0m, prm13)
            : new TerminationThirteenthSnapshot(s.Leaves, s.Absences, s.Variables, s.AdvancePaid, s.VacationAdvance, prm13);
        return (snapshot, ids);
    }
}
