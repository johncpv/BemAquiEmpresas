using System.Globalization;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Folha lançada (RH-02a CA 4 a 15): a folha da competência, os holerites com os valores da contadora (digitados ou importados), as
/// guias da empresa e a conferência. Nenhum cálculo legal. O resumo sai com <c>hr.view</c>; todo valor só por rota <c>/sensitive</c>
/// (com <c>SensitiveRead</c>) e por comandos que exigem <c>hr.view_sensitive</c> (ADR 0016). Folha fora de <c>Draft</c> não muda.
/// </summary>
public sealed partial class PayrollService(
    IHrDb db,
    IPermissionChecker permissions,
    IAuditWriter audit,
    IUserDirectory users,
    IEmployees employees,
    IParties parties,
    HrParametersService parameters,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const string RunIndex = "ix_payroll_runs_period_type";
    public const string PayslipIndex = "ix_payslips_run_employee";

    /// <summary>Lista das folhas (CA 12): só o resumo, sem valor.</summary>
    public async Task<IReadOnlyList<PayrollRunSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var runs = await db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.Period).ThenBy(r => r.Type).ToListAsync(cancellationToken);
        return await SummariesAsync(runs, cancellationToken);
    }

    /// <summary>Totais das folhas para a lista, só com <c>hr.view_sensitive</c> (na rota); a leitura vai para a auditoria.</summary>
    public async Task<IReadOnlyList<PayrollRunTotals>> TotalsAsync(CancellationToken cancellationToken = default)
    {
        var totals = await db.Payslips.AsNoTracking().GroupBy(p => p.PayrollRunId)
            .Select(g => new PayrollRunTotals(g.Key, g.Sum(p => p.Gross), g.Sum(p => p.Deductions), g.Sum(p => p.Net))).ToListAsync(cancellationToken);
        await audit.RecordReadAsync(nameof(PayrollRun) + ".Totals", "list", cancellationToken);
        return totals;
    }

    public async Task<Result<PayrollRunSummary>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return run is null ? RunNotFound() : (await SummariesAsync([run], cancellationToken))[0];
    }

    /// <summary>Funcionários da competência (CA 4), para escolher em Férias e Rescisão: só nome e matrícula.</summary>
    public async Task<Result<IReadOnlyList<PayrollEligibleEmployee>>> EligibleAsync(string? period, CancellationToken cancellationToken = default)
    {
        if (PayrollRules.ParsePeriod(period) is not { } start)
        {
            return PayrollRules.Field("period", "Escolha a competência (mês e ano).");
        }

        var list = await EligibleQuery(start).OrderBy(e => e.Name)
            .Select(e => new PayrollEligibleEmployee(e.Id, e.Name, e.Registration, e.AdmissionDate, e.TerminationDate)).ToListAsync(cancellationToken);
        return list;
    }

    /// <summary>
    /// Nova folha (CA 4 e 5): <c>Draft</c>, modo <c>Entered</c>, um holerite por funcionário da competência (ou pelos escolhidos em Férias
    /// e Rescisão). Na Mensal, o holerite já vem com a linha "Salário" do salário vigente na competência e a referência "30 dias".
    /// Mesmo tipo e competência não se repete (409).
    /// </summary>
    public async Task<Result<PayrollRunSummary>> CreateAsync(PayrollRunCreateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        if (PayrollRules.ParsePeriod(input.Period) is not { } period)
        {
            return PayrollRules.Field("period", "Escolha a competência (mês e ano).");
        }

        if (input.Type is not { } type || !Enum.IsDefined(type))
        {
            return PayrollRules.Field("type", "Escolha o tipo da folha.");
        }

        var mode = input.Mode is { } m && Enum.IsDefined(m) ? m : PayrollRunMode.Entered;
        if (mode == PayrollRunMode.Calculated && type != PayrollRunType.Monthly)
        {
            return Error.Validation(PayrollCalculationRules.NotSupportedCode, $"O cálculo da folha {PayrollRules.TypeLabel(type)} chega nas próximas etapas. Use a folha lançada.");
        }

        if (await db.PayrollRuns.AnyAsync(r => r.Period == period && r.Type == type && r.Mode == mode && r.RectifiesRunId == null, cancellationToken))
        {
            return Duplicate(type, period, mode);
        }

        var eligible = await EligibleQuery(period).Select(e => new { e.Id, e.BaseSalary }).ToListAsync(cancellationToken);
        if (PayrollRules.ChoosesEmployees(type))
        {
            var chosen = (input.EmployeeIds ?? []).Distinct().ToHashSet();
            if (chosen.Count == 0 || chosen.Any(id => eligible.All(e => e.Id != id)))
            {
                return PayrollRules.Field("employeeIds", "Escolha os funcionários desta folha entre os ativos na competência.");
            }

            eligible = eligible.Where(e => chosen.Contains(e.Id)).ToList();
        }

        if (eligible.Count == 0)
        {
            return PayrollRules.Field("period", $"Nenhum funcionário ativo em {PayrollRules.PeriodLabel(period)}. Confira as admissões no cadastro.");
        }

        var now = clock.GetUtcNow();
        // FLH-02a: a calculada nasce sem linha (o "Calcular" faz todas) e, antes da virada (FLH-06), como simulação.
        var run = new PayrollRun { Period = period, Type = type, Mode = mode, CreatedAt = now, CreatedBy = currentUser.UserId, IsSimulation = mode == PayrollRunMode.Calculated && !await parameters.CalculatedOfficialAsync(type, period, cancellationToken) };
        db.PayrollRuns.Add(run);
        var salaryItem = type == PayrollRunType.Monthly && mode == PayrollRunMode.Entered
            ? await db.PayrollItems.AsNoTracking().Where(i => i.IsActive && i.Kind == PayrollItemKind.Earning && (i.SeedKey == PayrollItemService.SalarySeedKey || i.Code == "SAL"))
                .OrderBy(i => i.SeedKey == null).FirstOrDefaultAsync(cancellationToken)
            : null;
        var salaries = salaryItem is null ? new Dictionary<Guid, decimal>() : await SalariesAtAsync(eligible.ToDictionary(e => e.Id, e => e.BaseSalary), PayrollRules.PeriodEnd(period), cancellationToken);
        foreach (var e in eligible)
        {
            var payslip = new Payslip { PayrollRunId = run.Id, EmployeeId = e.Id, CreatedAt = now, UpdatedAt = now };
            var lines = new List<PayslipLine>();
            if (salaryItem is not null && salaries.GetValueOrDefault(e.Id) is var salary and > 0m)
            {
                lines.Add(new PayslipLine
                {
                    PayslipId = payslip.Id, PayrollItemId = salaryItem.Id, Code = salaryItem.Code, Description = salaryItem.Name, Type = salaryItem.Kind,
                    Reference = PayrollRules.MonthlySalaryReference, Amount = PayrollRules.Round(salary), Sort = 0,
                });
            }

            payslip.ApplyTotals(PayrollRules.Totals(lines.Select(l => new PayslipLineValue(l.Type, l.Amount, l.Reference))));
            db.Payslips.Add(payslip);
            db.PayslipLines.AddRange(lines);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, RunIndex))
        {
            return Duplicate(type, period, mode);
        }

        return (await SummariesAsync([run], cancellationToken))[0];
    }

    /// <summary>Holerites da folha com os totais (sensível; a leitura vai para a auditoria).</summary>
    public async Task<Result<IReadOnlyList<PayslipRow>>> PayslipsAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!await db.PayrollRuns.AnyAsync(r => r.Id == runId, cancellationToken))
        {
            return RunNotFound();
        }

        var rows = await (from p in db.Payslips.AsNoTracking()
                          join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                          where p.PayrollRunId == runId
                          orderby e.Name
                          select new PayslipRow(p.Id, p.EmployeeId, e.Name, e.Registration, p.Gross, p.Deductions, p.Net, db.PayslipLines.Count(l => l.PayslipId == p.Id), p.Version))
            .ToListAsync(cancellationToken);
        await audit.RecordReadAsync(nameof(PayrollRun) + ".Payslips", runId.ToString(), cancellationToken);
        return rows;
    }

    /// <summary>Holerite (CA 5 e 12): do holerite da folha da rota (IDOR, 404); leitura sensível auditada.</summary>
    public async Task<Result<PayslipDetail>> PayslipAsync(Guid runId, Guid payslipId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        var payslip = run is null ? null : await db.Payslips.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payslipId && p.PayrollRunId == runId, cancellationToken);
        if (run is null || payslip is null)
        {
            return PayslipNotFound();
        }

        await audit.RecordReadAsync(nameof(Payslip), payslipId.ToString(), cancellationToken);
        return await DetailAsync(run, payslip, cancellationToken);
    }

    /// <summary>
    /// Holerite anterior do mesmo tipo do funcionário (CA 5, "Copiar do mês anterior"): as rubricas, os valores e as bases, para entrar como
    /// rascunho editável (nada é gravado). Sem anterior, vazio.
    /// </summary>
    public async Task<Result<PayslipPrevious>> PreviousAsync(Guid runId, Guid payslipId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        var payslip = run is null ? null : await db.Payslips.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payslipId && p.PayrollRunId == runId, cancellationToken);
        if (run is null || payslip is null)
        {
            return PayslipNotFound();
        }

        var previous = await (from p in db.Payslips.AsNoTracking()
                              join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                              where p.EmployeeId == payslip.EmployeeId && r.Type == run.Type && r.Mode == run.Mode && r.RectifiesRunId == null && r.Period < run.Period
                              orderby r.Period descending
                              select new { Payslip = p, r.Period }).FirstOrDefaultAsync(cancellationToken);
        await audit.RecordReadAsync(nameof(Payslip) + ".Previous", payslipId.ToString(), cancellationToken);
        if (previous is null)
        {
            return new PayslipPrevious(null, [], 0m, 0m, 0m, 0m);
        }

        var lines = await LinesAsync(previous.Payslip.Id, cancellationToken);
        var active = (await db.PayrollItems.AsNoTracking().Where(i => i.IsActive).Select(i => i.Id).ToListAsync(cancellationToken)).ToHashSet();
        var last = previous.Payslip;
        return new PayslipPrevious(previous.Period, lines.Where(l => active.Contains(l.PayrollItemId)).ToList(), last.InssBase, last.FgtsBase, last.IrrfBase, last.FgtsAmount);
    }

    /// <summary>
    /// Salvar o holerite (CA 6, 14 e 15): as linhas substituem as de antes; rubrica ativa (ou já no holerite), referência até 20,
    /// valor maior que zero com 2 casas; os totais saem das linhas (a informativa fora do líquido) e o líquido negativo é recusado
    /// (422). Folha fechada, 409; versão antiga, 409 <c>concurrency_conflict</c>.
    /// </summary>
    public async Task<Result<PayslipDetail>> SavePayslipAsync(Guid runId, Guid payslipId, PayslipInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        var payslip = run is null ? null : await db.Payslips.FirstOrDefaultAsync(p => p.Id == payslipId && p.PayrollRunId == runId, cancellationToken);
        if (run is null || payslip is null)
        {
            return PayslipNotFound();
        }

        if (run.Status != PayrollRunStatus.Draft)
        {
            return PayrollRules.Closed(run.Period);
        }

        if (run.Mode == PayrollRunMode.Calculated)
        {
            return Error.Conflict(PayrollCalculationRules.CalculatedLineCode, PayrollCalculationRules.CalculatedLineMessage);
        }

        var lines = input.Lines ?? [];
        if (lines.Count > PayrollRules.MaxLines)
        {
            return PayrollRules.Field("lines", "O holerite aceita até 100 linhas.");
        }

        var old = await db.PayslipLines.Where(l => l.PayslipId == payslipId).ToListAsync(cancellationToken);
        var ids = lines.Select(l => l.PayrollItemId).OfType<Guid>().Distinct().ToList();
        var items = await db.PayrollItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
        var fresh = new List<PayslipLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.PayrollItemId is not { } itemId || !items.TryGetValue(itemId, out var item) || (!item.IsActive && old.All(o => o.PayrollItemId != itemId)))
            {
                return PayrollRules.Field($"lines[{i}]", "Escolha uma rubrica ativa.");
            }

            if (!PayrollRules.ValidReference(line.Reference))
            {
                return PayrollRules.Field($"lines[{i}]", "A referência aceita até 20 caracteres.");
            }

            if (!PayrollRules.ValidAmount(line.Amount))
            {
                return PayrollRules.Field($"lines[{i}]", "Informe o valor maior que zero, com até 2 casas.");
            }

            var reference = line.Reference?.Trim();
            fresh.Add(new PayslipLine
            {
                PayslipId = payslipId, PayrollItemId = item.Id, Code = item.Code, Description = item.Name, Type = item.Kind,
                Reference = string.IsNullOrEmpty(reference) ? null : reference, Amount = line.Amount, Sort = i,
            });
        }

        foreach (var (field, value) in new[] { ("inssBase", input.InssBase), ("fgtsBase", input.FgtsBase), ("irrfBase", input.IrrfBase), ("fgtsAmount", input.FgtsAmount) })
        {
            if (!PayrollRules.ValidBase(value))
            {
                return PayrollRules.Field(field, "Informe zero ou mais, com até 2 casas.");
            }
        }

        var totals = PayrollRules.Totals(fresh.Select(l => new PayslipLineValue(l.Type, l.Amount, l.Reference)));
        if (totals.Net < 0m)
        {
            return Error.Validation(PayrollRules.NegativeNetCode, "O líquido não pode ser negativo.");
        }

        db.PayslipLines.RemoveRange(old);
        db.PayslipLines.AddRange(fresh);
        payslip.ApplyTotals(totals);
        payslip.InssBase = input.InssBase;
        payslip.FgtsBase = input.FgtsBase;
        payslip.IrrfBase = input.IrrfBase;
        payslip.FgtsAmount = input.FgtsAmount;
        payslip.UpdatedAt = clock.GetUtcNow();
        db.Entry(payslip).Property(p => p.Version).OriginalValue = input.Version;
        db.Entry(payslip).State = EntityState.Modified;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(run, payslip, cancellationToken);
    }

    private IQueryable<Employee> EligibleQuery(DateOnly period)
    {
        var end = PayrollRules.PeriodEnd(period);
        return db.Employees.AsNoTracking().Where(e => e.AdmissionDate <= end && (e.TerminationDate == null || e.TerminationDate >= period));
    }

    /// <summary>
    /// Salário vigente no fim da competência (CA 5): o atual, ou o de antes do primeiro reajuste com vigência depois do fim da
    /// competência (o reajuste futuro não entra na folha de agora).
    /// </summary>
    internal async Task<Dictionary<Guid, decimal>> SalariesAtAsync(Dictionary<Guid, decimal> current, DateOnly end, CancellationToken ct)
    {
        var ids = current.Keys.ToList();
        var later = await db.EmployeeHistory.AsNoTracking().Where(h => ids.Contains(h.EmployeeId) && h.Kind == EmployeeHistoryKind.Salary && h.EffectiveDate > end)
            .Select(h => new { h.EmployeeId, h.EffectiveDate, h.CreatedAt, h.OldValue }).ToListAsync(ct);
        var result = new Dictionary<Guid, decimal>(current);
        foreach (var first in later.GroupBy(h => h.EmployeeId).Select(g => g.OrderBy(h => h.EffectiveDate).ThenBy(h => h.CreatedAt).First()))
        {
            if (decimal.TryParse(first.OldValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var old))
            {
                result[first.EmployeeId] = old;
            }
        }

        return result;
    }

    private async Task<List<PayslipLineDetail>> LinesAsync(Guid payslipId, CancellationToken ct) =>
        await db.PayslipLines.AsNoTracking().Where(l => l.PayslipId == payslipId).OrderBy(l => l.Sort)
            .Select(l => new { l.Id, l.PayrollItemId, l.Code, l.Description, l.Type, l.Reference, l.Amount, l.Calculated, l.Note, l.Memory }).ToListAsync(ct) is var rows
            ? rows.Select(l => new PayslipLineDetail(l.PayrollItemId, l.Code, l.Description, l.Type, l.Reference, l.Amount, l.Id, l.Calculated, l.Note, PayrollCalculator.ReadMemory(l.Memory))).ToList()
            : [];

    private async Task<PayslipDetail> DetailAsync(PayrollRun run, Payslip p, CancellationToken ct)
    {
        var employee = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => e.Id == p.EmployeeId).Select(e => new { e.Name, e.Registration }).FirstAsync(ct);
        return new PayslipDetail(p.Id, run.Id, p.EmployeeId, employee.Name, employee.Registration, await LinesAsync(p.Id, ct), p.Gross, p.Deductions, p.Net,
            p.InssBase, p.FgtsBase, p.IrrfBase, p.FgtsAmount, run.Status == PayrollRunStatus.Draft, p.Version, run.Mode, await CalculationInfoAsync(run, p, ct));
    }

    private async Task<List<PayrollRunSummary>> SummariesAsync(IReadOnlyList<PayrollRun> runs, CancellationToken ct)
    {
        var ids = runs.Select(r => r.Id).ToList();
        var counts = await db.Payslips.AsNoTracking().Where(p => ids.Contains(p.PayrollRunId)).GroupBy(p => p.PayrollRunId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var names = new Dictionary<Guid, string?>();
        foreach (var by in runs.Select(r => r.ClosedBy).OfType<Guid>().Distinct())
        {
            names[by] = await users.DisplayNameAsync(by, ct);
        }

        // RH-01c CA 13: o ponto do mês reaberto depois do fechamento da folha (o espelho voltou a aberto depois de a folha fechar).
        var periods = runs.Where(r => r.ClosedAt is not null).Select(r => r.Period).Distinct().ToList();
        var reopened = await db.Timesheets.AsNoTracking()
            .Where(t => periods.Contains(t.Period) && t.Status == TimesheetStatus.Open && t.ReopenedAt != null)
            .GroupBy(t => t.Period).Select(g => new { g.Key, At = g.Max(t => t.ReopenedAt) }).ToListAsync(ct);
        string? Notice(PayrollRun r) => r.ClosedAt is { } closed && reopened.Any(t => t.Key == r.Period && t.At > closed) ? PayrollTimesheetNotice : null;

        return runs.Select(r => new PayrollRunSummary(r.Id, r.Period, r.Type, r.Mode, r.Status, counts.GetValueOrDefault(r.Id), r.CreatedAt, r.ClosedAt,
            r.ClosedBy is { } by ? names.GetValueOrDefault(by) : null, r.Version, r.PaymentDate, r.PaidAt, r.ReopenReason, r.ClosedPayables, Notice(r),
            r.Mode == PayrollRunMode.Calculated && r.IsSimulation, r.CalculatedAt, r.CalculatorVersion, r.CalculatingSince is { } since && clock.GetUtcNow() - since < TimeSpan.FromMinutes(10), r.RectifiesRunId, r.Number)).ToList();
    }

    /// <summary>Aviso da folha fechada com o ponto reaberto depois (RH-01c CA 13).</summary>
    public const string PayrollTimesheetNotice = "O ponto deste mês foi reaberto depois do fechamento da folha.";

    /// <summary>A folha da rota, para os comandos: existe (404) e está em <c>Draft</c> (409 "A folha de {mês} está fechada.").</summary>
    private async Task<Result<PayrollRun>> DraftRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            return RunNotFound();
        }

        return run.Status == PayrollRunStatus.Draft ? run : PayrollRules.Closed(run.Period);
    }

    private ValueTask<bool> CanSensitiveAsync(CancellationToken ct) => permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, ct);

    private static Error Duplicate(PayrollRunType type, DateOnly period, PayrollRunMode mode = PayrollRunMode.Entered) =>
        Error.Conflict(PayrollRules.DuplicateCode, $"Já existe a folha {PayrollRules.TypeLabel(type)}{(mode == PayrollRunMode.Calculated ? " calculada" : string.Empty)} de {PayrollRules.PeriodLabel(period)}.");

    private static Error RunNotFound() => Error.NotFound("payroll_run_not_found", "Folha não encontrada.");

    private static Error PayslipNotFound() => Error.NotFound("payslip_not_found", "Holerite não encontrado.");

    private static Error Forbidden() => Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso aos valores da folha. Fale com o administrador.");
}
