using BemAqui.BuildingBlocks.Export;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// <c>folha-calculada.csv</c> (FLH-06 CA 8): enquanto a conferência paralela está em andamento (a calculada da competência ainda é simulação),
/// o pacote leva a folha calculada pelo sistema no mesmo layout do <c>folha.csv</c>, para a contadora comparar do lado dela. Depois da virada,
/// a calculada é a oficial e sai no <c>folha.csv</c> quando fechada.
/// </summary>
public sealed class AccountantCalculatedPayrollSection(IHrDb db, HrParametersService parameters) : IAccountantExportSection
{
    public const string SectionKey = "calculated-payroll";
    public const string FileName = "folha-calculada.csv";

    public string Key => SectionKey;

    public int Order => 25;

    public async Task<AccountantSectionResult> BuildAsync(AccountantExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var period = context.Period;
        var runs = await db.PayrollRuns.AsNoTracking()
            .Where(r => r.Period == period && r.Mode == PayrollRunMode.Calculated && r.RectifiesRunId == null && r.CalculatedAt != null && r.Status == PayrollRunStatus.Draft)
            .OrderBy(r => r.Type).ToListAsync(cancellationToken);
        var simulation = new List<PayrollRun>();
        foreach (var run in runs)
        {
            if (!await parameters.CalculatedOfficialAsync(run.Type, run.Period, cancellationToken))
            {
                simulation.Add(run);
            }
        }

        if (simulation.Count == 0)
        {
            return new AccountantSectionResult(Key, false, "Folha calculada: fora da conferência paralela", null, null, 0, []);
        }

        var ids = simulation.Select(r => r.Id).ToList();
        var types = simulation.ToDictionary(r => r.Id, r => r.Type);
        var lines = await (from l in db.PayslipLines.AsNoTracking()
                           join p in db.Payslips.AsNoTracking() on l.PayslipId equals p.Id
                           join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                           where ids.Contains(p.PayrollRunId)
                           select new { p.PayrollRunId, e.Name, e.Registration, e.Cpf, l.Code, l.Description, l.Reference, l.Amount, l.Sort, p.Id }).ToListAsync(cancellationToken);
        var charges = await db.PayrollRunCharges.AsNoTracking().Where(c => ids.Contains(c.PayrollRunId))
            .Select(c => new { c.PayrollRunId, c.Kind, c.Amount, c.DueDate, c.CreatedAt }).ToListAsync(cancellationToken);
        var rows = AccountantLayoutV1.PayrollRows(
            lines.OrderBy(l => types[l.PayrollRunId]).ThenBy(l => l.Name, StringComparer.Ordinal).ThenBy(l => l.Id).ThenBy(l => l.Sort)
                .Select(l => new AccountantPayslipLine(types[l.PayrollRunId], AccountantLayoutV1.EmployeeKey(l.Registration, l.Cpf), l.Code, l.Description, l.Reference, l.Amount)),
            charges.OrderBy(c => types[c.PayrollRunId]).ThenBy(c => c.Kind).ThenBy(c => c.DueDate).ThenBy(c => c.CreatedAt)
                .Select(c => new AccountantCharge(types[c.PayrollRunId], c.Kind, c.Amount, c.DueDate)));
        var summary = "Folha calculada (conferência paralela): " + string.Join("; ", simulation.Select(r => PayrollRules.TypeLabel(r.Type)));
        return new AccountantSectionResult(Key, true, summary, FileName, SafeCsv.Bytes(SafeCsv.Text(AccountantLayoutV1.PayrollHeader, rows)), rows.Count, []);
    }
}
