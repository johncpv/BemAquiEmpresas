using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Importar lançamentos (RH-02a CA 8 a 10) no layout padrão <c>v1</c>: a prévia lê e confere sem gravar; a importação confere de novo e
/// substitui, numa transação, as linhas dos funcionários presentes no arquivo (os ausentes ficam como estão). O arquivo não é guardado:
/// fica só o resumo na auditoria (quantas linhas e funcionários, sem valores). O CPF do arquivo só acha o funcionário: não vai para a
/// mensagem de erro nem para o log.
/// </summary>
public sealed partial class PayrollService
{
    public const int MaxErrorsShown = 100;
    public const string ImportInvalidCode = "payroll_import_invalid";
    public const string ImportFormat = "csv-v1";

    private sealed record ImportPlan(PayrollImportPreview Preview, IReadOnlyDictionary<Guid, List<(PayrollCsvRow Row, PayrollItem Item)>> ByPayslip);

    public async Task<Result<PayrollImportPreview>> PreviewImportAsync(Guid runId, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var run = await DraftRunAsync(runId, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error!;
        }

        if (run.Value.Mode == PayrollRunMode.Calculated)
        {
            return Error.Conflict(PayrollCalculationRules.CalculatedLineCode, "Na folha calculada, os valores vêm do cálculo: a importação é da folha lançada.");
        }

        return (await PlanAsync(run.Value, content, cancellationToken)).Preview;
    }

    public async Task<Result<PayrollImportResult>> ImportAsync(Guid runId, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var run = await DraftRunAsync(runId, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error!;
        }

        if (run.Value.Mode == PayrollRunMode.Calculated)
        {
            return Error.Conflict(PayrollCalculationRules.CalculatedLineCode, "Na folha calculada, os valores vêm do cálculo: a importação é da folha lançada.");
        }

        var plan = await PlanAsync(run.Value, content, cancellationToken);
        if (!plan.Preview.CanImport)
        {
            return Error.Validation(ImportInvalidCode, plan.Preview.ErrorCount == 1 ? "O arquivo tem 1 erro. Corrija e envie de novo." : $"O arquivo tem {plan.Preview.ErrorCount} erros. Corrija e envie de novo.");
        }

        var ids = plan.ByPayslip.Keys.ToList();
        var payslips = await db.Payslips.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        var old = await db.PayslipLines.Where(l => ids.Contains(l.PayslipId)).ToListAsync(cancellationToken);
        db.PayslipLines.RemoveRange(old);
        var now = clock.GetUtcNow();
        foreach (var payslip in payslips)
        {
            var lines = plan.ByPayslip[payslip.Id].Select((x, i) => new PayslipLine
            {
                PayslipId = payslip.Id, PayrollItemId = x.Item.Id, Code = x.Item.Code, Description = x.Item.Name, Type = x.Item.Kind, Reference = x.Row.Reference, Amount = x.Row.Amount, Sort = i,
            }).ToList();
            db.PayslipLines.AddRange(lines);
            payslip.ApplyTotals(PayrollRules.Totals(lines.Select(l => new PayslipLineValue(l.Type, l.Amount, l.Reference))));
            payslip.UpdatedAt = now;
        }

        // Uma gravação só (uma transação): o resumo da auditoria entra junto com as linhas.
        await audit.RecordImportAsync(nameof(PayrollRun), runId.ToString(), ImportFormat, plan.Preview.Lines, payslips.Count, cancellationToken);
        return new PayrollImportResult(plan.Preview.Lines, payslips.Count);
    }

    private async Task<ImportPlan> PlanAsync(PayrollRun run, byte[] content, CancellationToken ct)
    {
        var parsed = PayrollCsv.Parse(content);
        var errors = parsed.Errors.ToList();
        var people = await (from p in db.Payslips.AsNoTracking()
                            join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                            where p.PayrollRunId == run.Id
                            select new { PayslipId = p.Id, e.Name, e.Cpf, e.Registration }).ToListAsync(ct);
        var byCpf = people.GroupBy(p => p.Cpf).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var byRegistration = people.Where(p => p.Registration != null).GroupBy(p => p.Registration!.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var items = (await db.PayrollItems.AsNoTracking().ToListAsync(ct)).GroupBy(i => i.Code).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var byPayslip = new Dictionary<Guid, List<(PayrollCsvRow Row, PayrollItem Item)>>();
        var names = new Dictionary<Guid, string>();
        foreach (var row in parsed.Rows)
        {
            var person = PayrollCsv.IsCpf(row.Key, out var cpf) ? byCpf.GetValueOrDefault(cpf) : byRegistration.GetValueOrDefault(row.Key.Trim().ToUpperInvariant());
            if (person is null)
            {
                errors.Add(PayrollCsv.IsCpf(row.Key, out _) ? $"Linha {row.Line}: CPF não pertence a funcionário ativo." : $"Linha {row.Line}: matrícula não pertence a funcionário ativo.");
                continue;
            }

            var code = PayrollRules.NormalizeCode(row.Code);
            if (code is null || !items.TryGetValue(code, out var item))
            {
                errors.Add($"Linha {row.Line}: rubrica '{PayrollCsv.SafeCode(row.Code)}' não existe.");
                continue;
            }

            if (!item.IsActive)
            {
                errors.Add($"Linha {row.Line}: rubrica '{item.Code}' está inativa.");
                continue;
            }

            if (!byPayslip.TryGetValue(person.PayslipId, out var list))
            {
                byPayslip[person.PayslipId] = list = [];
                names[person.PayslipId] = person.Name;
            }

            list.Add((row, item));
        }

        decimal gross = 0m, deductions = 0m;
        foreach (var (payslipId, list) in byPayslip)
        {
            if (list.Count > PayrollRules.MaxLines)
            {
                errors.Add($"Linha {list[^1].Row.Line}: o holerite de {names[payslipId]} passaria de 100 linhas.");
            }

            var totals = PayrollRules.Totals(list.Select(x => new PayslipLineValue(x.Item.Kind, x.Row.Amount, x.Row.Reference)));
            if (totals.Net < 0m)
            {
                errors.Add($"Linha {list[^1].Row.Line}: o líquido de {names[payslipId]} ficaria negativo.");
            }

            gross += totals.Gross;
            deductions += totals.Deductions;
        }

        var byItem = byPayslip.Values.SelectMany(l => l).GroupBy(x => x.Item.Id)
            .Select(g => new PayrollImportItemTotal(g.First().Item.Code, g.First().Item.Name, g.First().Item.Kind, g.Count(), g.Sum(x => x.Row.Amount)))
            .OrderBy(t => t.Kind).ThenBy(t => t.Code, StringComparer.Ordinal).ToList();
        var lineCount = byPayslip.Values.Sum(l => l.Count);
        errors = errors.OrderBy(LineOf).ToList();
        var preview = new PayrollImportPreview(byPayslip.Count, names.Values.OrderBy(n => n, StringComparer.CurrentCulture).ToList(), lineCount, byItem, gross, deductions, gross - deductions,
            errors.Take(MaxErrorsShown).ToList(), errors.Count, errors.Count == 0 && lineCount > 0);
        return new ImportPlan(preview, byPayslip);
    }

    /// <summary>Número da linha de "Linha 12: ..." para ordenar a lista; o erro do arquivo inteiro vem antes.</summary>
    private static int LineOf(string error) =>
        error.StartsWith("Linha ", StringComparison.Ordinal) && int.TryParse(error.AsSpan(6, Math.Max(error.IndexOf(':', StringComparison.Ordinal) - 6, 0)), out var line) ? line : 0;
}
