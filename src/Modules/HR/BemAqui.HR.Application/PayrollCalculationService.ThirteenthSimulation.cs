using BemAqui.HR.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Application;

/// <summary>
/// Funcionário na simulação do 13º (EXP-05 CA 11): avos, remuneração de base, médias, adiantamento abatido e o valor líquido da parcela
/// pelo mesmo motor da folha calculada, sem gravar nada. <c>Issue</c>: o motivo de não ter valor (sem salário, rubrica faltando).
/// </summary>
public sealed record ThirteenthSimulationRow(string Name, string? Registration, int Avos, decimal? Base, decimal? Averages, decimal? AdvanceDeducted, decimal? Amount, string? Issue);

/// <summary>Simulação do 13º do ano e da parcela da tela (a prévia dos avos mais os valores); <c>Blocked</c>: por que nenhum valor saiu (tabela ou rubrica).</summary>
public sealed record ThirteenthSimulation(ThirteenthPreview Preview, IReadOnlyList<ThirteenthSimulationRow> Rows, string? Blocked);

public sealed partial class PayrollCalculationService
{
    /// <summary>
    /// PDF da prévia do 13º (EXP-05 CA 11): a mesma prévia dos avos da tela (<c>hr.view_sensitive</c>, conferido no
    /// <see cref="ThirteenthPreviewAsync"/>) e, por funcionário, o motor do 13º rodado numa folha que não existe (nada é gravado). Sem as
    /// tabelas ou as rubricas da competência, sai só com os avos e o motivo.
    /// </summary>
    public async Task<Result<ThirteenthSimulation>> ThirteenthSimulationAsync(int? year, PayrollRunType? type, CancellationToken cancellationToken = default)
    {
        var preview = await ThirteenthPreviewAsync(year, type, cancellationToken);
        if (preview.IsFailure)
        {
            return preview.Error!;
        }

        var p = preview.Value;
        var fake = new PayrollRun { Period = PayrollRules.ParsePeriod(p.Period)!.Value, Type = p.Type, Mode = PayrollRunMode.Calculated };
        var context = await ContextForAsync(fake, cancellationToken);
        if (context.IsFailure)
        {
            return new ThirteenthSimulation(p, p.Employees.Select(e => new ThirteenthSimulationRow(e.Name, e.Registration, e.Avos, null, null, null, null, null)).ToList(), context.Error!.Message);
        }

        var (prm13, _) = await parameters.ThirteenthParametersAsync(cancellationToken);
        var snapshots = await ThirteenthSnapshotsAsync(fake, p.Employees.Select(e => e.EmployeeId).ToList(), context.Value.Parameters, prm13, cancellationToken);
        var rows = new List<ThirteenthSimulationRow>();
        foreach (var e in p.Employees)
        {
            var snapshot = snapshots[e.EmployeeId];
            var result = ThirteenthCalculator.Calculate(new ThirteenthInput(snapshot, context.Value.Items, context.Value.Tables));
            if (result.Issues.FirstOrDefault(i => i.Blocking) is { } blocking)
            {
                rows.Add(new ThirteenthSimulationRow(e.Name, e.Registration, e.Avos, null, null, null, null, blocking.Message));
                continue;
            }

            decimal Line(PayrollCalculation calculation) => result.Lines.Where(l => l.Calculation == calculation).Sum(l => l.Amount);
            var (remuneration, _) = ThirteenthCalculator.Remuneration(snapshot, context.Value.Tables);
            rows.Add(new ThirteenthSimulationRow(e.Name, e.Registration, e.Avos, remuneration, Line(PayrollCalculation.ThirteenthAverages),
                Line(PayrollCalculation.ThirteenthAdvanceDeduction), result.Totals.Net, null));
        }

        return new ThirteenthSimulation(p, rows, null);
    }
}
