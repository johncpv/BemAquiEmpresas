using BemAqui.BuildingBlocks.Privacy;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Conferência paralela da folha (FLH-06 CA 9, LGPD-01a): a exportação do titular leva, por competência e rubrica, o valor da contadora, o do
/// sistema, a diferença e a classificação com a justificativa (com <c>hr.view_sensitive</c>, como os holerites). Na anonimização (retenção
/// trabalhista), a justificativa vira o marcador e o caso de correção sai; os valores ficam, como os do holerite.
/// </summary>
public sealed partial class HrPersonalDataProvider
{
    private async Task<PersonalDataBlock> ParallelAsync(Guid employeeId, CancellationToken ct)
    {
        var rows = await (from d in db.PayrollParallelDifferences.AsNoTracking()
                          join r in db.PayrollParallelRuns.AsNoTracking() on d.ParallelRunId equals r.Id
                          where d.EmployeeId == employeeId
                          orderby r.Period, r.PayrollType, d.PayrollItemCode
                          select new { r.Period, r.RunType, r.ComparedAt, d.PayrollItemCode, d.ItemName, d.EnteredAmount, d.CalculatedAmount, d.Difference, d.DifferencePct, d.Classification, d.Justification })
            .ToListAsync(ct);
        return new PersonalDataBlock("Conferência paralela da folha", "hr.parallel_differences", rows.Select(x => PersonalDataRecord.Of(x.ComparedAt,
            ("Competência", PayrollRules.PeriodLabel(x.Period)), ("Tipo", PayrollParallelRules.TypeLabel(x.RunType)), ("Rubrica", $"{x.PayrollItemCode} {x.ItemName}"),
            ("Valor da contadora", PersonalDataMask.Money(x.EnteredAmount)), ("Valor do sistema", PersonalDataMask.Money(x.CalculatedAmount)), ("Diferença", PersonalDataMask.Money(x.Difference)),
            ("Diferença (%)", x.DifferencePct?.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)),
            ("Classificação", x.Classification is { } c ? PayrollParallelRules.ClassificationLabel(c) : null), ("Justificativa", x.Justification))).ToList());
    }

    /// <summary>As linhas da conferência com justificativa ou caso de correção, para o plano e a anonimização.</summary>
    private Task<List<PayrollParallelDifference>> ParallelTextsAsync(Guid employeeId, CancellationToken ct) =>
        db.PayrollParallelDifferences.Where(d => d.EmployeeId == employeeId && (d.Justification != null || d.CorrectionCase != null)).ToListAsync(ct);

    private void ClearParallel(List<PayrollParallelDifference> rows, string marker)
    {
        foreach (var d in rows)
        {
            var entry = db.Entry(d);
            entry.Property(x => x.Justification).CurrentValue = d.Justification is null ? null : marker;
            entry.Property(x => x.CorrectionCase).CurrentValue = null;
        }
    }
}
