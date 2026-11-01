using System.Globalization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>O que a geração precisa saber: a competência e se quem gera vê o dado de saúde (o tipo do afastamento, CAD-04c1b).</summary>
public sealed record AccountantExportContext(DateOnly Period, bool IncludeHealth);

/// <summary>
/// Resultado de uma seção (um arquivo do pacote, RH-03): se entrou, a linha do resumo ("Movimentação: 3 eventos"), o arquivo, quantas
/// linhas e o que ficou de fora. Sem valor nem nome no resumo e nas notas: eles vão para a tela e o <c>leia-me.txt</c>.
/// </summary>
public sealed record AccountantSectionResult(string Key, bool Included, string Summary, string? FileName, byte[]? Content, int Rows, IReadOnlyList<string> NotIncluded);

/// <summary>
/// Seção do pacote para a contadora (RH-03, regras): cada arquivo é uma seção registrada no DI; a RH-01c acrescenta o <c>ponto.csv</c>
/// registrando a dela (e subindo a versão do layout). Só lê dado fechado; nada é recalculado.
/// </summary>
public interface IAccountantExportSection
{
    string Key { get; }

    int Order { get; }

    Task<AccountantSectionResult> BuildAsync(AccountantExportContext context, CancellationToken cancellationToken = default);
}

/// <summary>Layout do pacote (P-06): o padrão <c>v1</c> é o primeiro; os adicionais entram como novas implementações, escolhidas pelo parâmetro do RH.</summary>
public interface IAccountantExport
{
    string Layout { get; }

    Task<IReadOnlyList<AccountantSectionResult>> BuildAsync(AccountantExportContext context, CancellationToken cancellationToken = default);
}

/// <summary>Layout padrão do sistema <c>v2</c> (RH-01c): as seções do <see cref="AccountantLayoutV1"/> e o <c>ponto.csv</c> (<see cref="AccountantLayoutV2"/>), na ordem.</summary>
public sealed class StandardAccountantExport(IEnumerable<IAccountantExportSection> sections) : IAccountantExport
{
    public string Layout => AccountantLayoutV2.Version;

    public async Task<IReadOnlyList<AccountantSectionResult>> BuildAsync(AccountantExportContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AccountantSectionResult>();
        foreach (var section in sections.OrderBy(s => s.Order))
        {
            results.Add(await section.BuildAsync(context, cancellationToken));
        }

        return results;
    }
}

/// <summary>
/// <c>movimentacao.csv</c> (CA 2): admissões e desligamentos da competência, reajustes e trocas de cargo com vigência nela e os afastamentos
/// que cobrem algum dia dela (não cancelados). Funcionário excluído (cadastro por engano) não entra. O motivo nunca sai; o tipo do afastamento
/// só sai para quem tem <c>hr.view_health</c>.
/// </summary>
public sealed class AccountantMovementsSection(IHrDb db) : IAccountantExportSection
{
    public const string SectionKey = "movements";

    public string Key => SectionKey;

    public int Order => 10;

    public async Task<AccountantSectionResult> BuildAsync(AccountantExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var events = await EventsAsync(context, cancellationToken);
        var rows = AccountantLayoutV1.MovementRows(events);
        var notIncluded = new List<string>();
        if (!context.IncludeHealth && events.Any(e => e.Kind == AccountantEventKind.Leave))
        {
            notIncluded.Add("Tipo do afastamento: não incluído; quem gerou o pacote não tem a permissão de ver dados de saúde.");
        }

        var summary = events.Count == 1 ? "Movimentação: 1 evento" : $"Movimentação: {events.Count} eventos";
        return new AccountantSectionResult(Key, true, summary, AccountantLayoutV1.MovementsFile, SafeCsv.Bytes(SafeCsv.Text(AccountantLayoutV1.MovementsHeader, rows)), rows.Count, notIncluded);
    }

    private async Task<List<AccountantEvent>> EventsAsync(AccountantExportContext context, CancellationToken ct)
    {
        var start = context.Period;
        var end = PayrollRules.PeriodEnd(start);
        var history = await db.EmployeeHistory.AsNoTracking()
            .Where(h => ((h.Kind == EmployeeHistoryKind.Salary || h.Kind == EmployeeHistoryKind.Position) && h.EffectiveDate >= start && h.EffectiveDate <= end)
                || (h.Kind == EmployeeHistoryKind.Leave && h.CancelledAt == null && h.EffectiveDate <= end && (h.LeaveEndDate ?? h.EffectiveDate) >= start))
            .Select(h => new { h.EmployeeId, h.Kind, h.EffectiveDate, h.NewValue, h.LeaveType, h.LeaveEndDate, h.CreatedAt }).ToListAsync(ct);
        var historyIds = history.Select(h => h.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking()
            .Where(e => (e.AdmissionDate >= start && e.AdmissionDate <= end) || (e.TerminationDate >= start && e.TerminationDate <= end) || historyIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Name, e.Registration, e.Cpf, e.BirthDate, e.AdmissionDate, e.TerminationDate, e.PositionId, e.WorkScheduleId, e.BaseSalary })
            .ToDictionaryAsync(e => e.Id, ct);

        var admitted = employees.Values.Where(e => e.AdmissionDate >= start && e.AdmissionDate <= end).ToList();
        var admittedIds = admitted.Select(e => e.Id).ToList();
        // Salário e cargo da admissão: o "de antes" da primeira mudança com vigência depois da admissão, ou o atual.
        var changes = await db.EmployeeHistory.AsNoTracking()
            .Where(h => admittedIds.Contains(h.EmployeeId) && (h.Kind == EmployeeHistoryKind.Salary || h.Kind == EmployeeHistoryKind.Position))
            .Select(h => new { h.EmployeeId, h.Kind, h.EffectiveDate, h.CreatedAt, h.OldValue }).ToListAsync(ct);
        var positions = await db.JobPositions.IgnoreQueryFilters().AsNoTracking().Select(p => new { p.Id, p.Name, p.Cbo, p.WorkScheduleId }).ToListAsync(ct);
        var schedules = await db.WorkSchedules.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        string? CboOf(string? positionName) => positions.FirstOrDefault(p => p.Name == positionName)?.Cbo;

        var events = new List<AccountantEvent>();
        foreach (var e in admitted)
        {
            var position = positions.FirstOrDefault(p => p.Id == e.PositionId);
            var firstPosition = changes.Where(h => h.EmployeeId == e.Id && h.Kind == EmployeeHistoryKind.Position && h.EffectiveDate > e.AdmissionDate)
                .OrderBy(h => h.EffectiveDate).ThenBy(h => h.CreatedAt).FirstOrDefault();
            var firstSalary = changes.Where(h => h.EmployeeId == e.Id && h.Kind == EmployeeHistoryKind.Salary && h.EffectiveDate > e.AdmissionDate)
                .OrderBy(h => h.EffectiveDate).ThenBy(h => h.CreatedAt).FirstOrDefault();
            var positionName = firstPosition?.OldValue ?? position?.Name;
            var cbo = firstPosition is null ? position?.Cbo : CboOf(positionName);
            var salary = firstSalary is not null && decimal.TryParse(firstSalary.OldValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var old) ? old : e.BaseSalary;
            var scheduleId = e.WorkScheduleId ?? position?.WorkScheduleId;
            events.Add(new AccountantEvent(AccountantEventKind.Admission, e.Registration, e.Name, e.Cpf, e.AdmissionDate, e.BirthDate, positionName, cbo, salary,
                scheduleId is { } sid ? schedules.GetValueOrDefault(sid) : null));
        }

        events.AddRange(employees.Values.Where(e => e.TerminationDate is { } t && t >= start && t <= end)
            .Select(e => new AccountantEvent(AccountantEventKind.Termination, e.Registration, e.Name, e.Cpf, e.TerminationDate!.Value)));

        foreach (var h in history)
        {
            if (!employees.TryGetValue(h.EmployeeId, out var e))
            {
                continue;
            }

            events.Add(h.Kind switch
            {
                EmployeeHistoryKind.Salary => new AccountantEvent(AccountantEventKind.SalaryChange, e.Registration, e.Name, e.Cpf, h.EffectiveDate,
                    Salary: decimal.TryParse(h.NewValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var salary) ? salary : null),
                EmployeeHistoryKind.Position => new AccountantEvent(AccountantEventKind.PositionChange, e.Registration, e.Name, e.Cpf, h.EffectiveDate, Position: h.NewValue, Cbo: CboOf(h.NewValue)),
                _ => new AccountantEvent(AccountantEventKind.Leave, e.Registration, e.Name, e.Cpf, h.EffectiveDate, LeaveType: context.IncludeHealth ? h.LeaveType : null, LeaveEnd: h.LeaveEndDate),
            });
        }

        return events;
    }
}

/// <summary>
/// <c>folha.csv</c> (CA 3): as folhas da competência fechadas ou pagas, uma linha por linha de holerite e uma por guia, como estão na folha.
/// Folha em rascunho não entra (e vira aviso). Sem folha fechada, o arquivo não entra no pacote.
/// </summary>
public sealed class AccountantPayrollSection(IHrDb db) : IAccountantExportSection
{
    public const string SectionKey = "payroll";

    public string Key => SectionKey;

    public int Order => 20;

    public async Task<AccountantSectionResult> BuildAsync(AccountantExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var period = context.Period;
        var runs = await db.PayrollRuns.AsNoTracking().Where(r => r.Period == period).OrderBy(r => r.Type).ToListAsync(cancellationToken);
        var closed = runs.Where(r => r.Status is PayrollRunStatus.Closed or PayrollRunStatus.Paid).ToList();
        var notIncluded = runs.Where(r => r.Status == PayrollRunStatus.Draft && !r.IsSimulation)
            .Select(r => $"Folha {PayrollRules.TypeLabel(r.Type)}: em rascunho, não entrou. Feche a folha e gere o pacote de novo.").ToList();
        if (closed.Count == 0)
        {
            notIncluded.Insert(0, $"Folha: nenhuma folha fechada de {PayrollRules.PeriodLabel(period)}; o arquivo folha.csv não entrou.");
            return new AccountantSectionResult(Key, false, "Folha: nenhuma folha fechada", null, null, 0, notIncluded);
        }

        var ids = closed.Select(r => r.Id).ToList();
        var types = closed.ToDictionary(r => r.Id, r => r.Type);
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
        var summary = "Folha: " + string.Join("; ", closed.Select(r => r.Status == PayrollRunStatus.Paid && r.PaidAt is { } paid
            ? $"{PayrollRules.TypeLabel(r.Type)} paga em {AccountantLayoutV1.Date(DateOnly.FromDateTime(BusinessTime.ToBusiness(paid).DateTime))}"
            : $"{PayrollRules.TypeLabel(r.Type)} fechada{(r.ClosedAt is { } at ? " em " + AccountantLayoutV1.Date(DateOnly.FromDateTime(BusinessTime.ToBusiness(at).DateTime)) : string.Empty)}"));
        return new AccountantSectionResult(Key, true, summary, AccountantLayoutV1.PayrollFile, SafeCsv.Bytes(SafeCsv.Text(AccountantLayoutV1.PayrollHeader, rows)), rows.Count, notIncluded);
    }
}

/// <summary>
/// <c>ponto.csv</c> (RH-01c CA 17, layout <c>v2</c>): um funcionário por linha com os totais do espelho fechado da competência (o mês de
/// referência do período de ponto), iguais aos do espelho. Período em aberto (algum espelho não fechado): o arquivo não entra e o
/// <c>leia-me.txt</c> diz como incluir. O motivo de saúde do abono nunca sai pelo nome.
/// </summary>
public sealed class AccountantTimesheetSection(IHrDb db) : IAccountantExportSection
{
    public const string SectionKey = "timeclock";
    public const string NotClosedNote = "Ponto: não incluído; feche o período de ponto para exportar.";

    public string Key => SectionKey;

    public int Order => 30;

    public async Task<AccountantSectionResult> BuildAsync(AccountantExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sheets = await (from t in db.Timesheets.AsNoTracking()
                            join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on t.EmployeeId equals e.Id
                            where t.Period == context.Period
                            select new { Sheet = t, e.Name, e.Registration, e.Cpf }).ToListAsync(cancellationToken);
        if (sheets.Count == 0 || sheets.Any(s => s.Sheet.Status != TimesheetStatus.Closed))
        {
            return new AccountantSectionResult(Key, false, "Ponto: não incluído", null, null, 0, [NotClosedNote]);
        }

        var rows = AccountantLayoutV2.TimesheetRows(sheets.Select(s => new AccountantTimesheet(AccountantLayoutV1.EmployeeKey(s.Registration, s.Cpf), s.Name, s.Sheet.PeriodStart, s.Sheet.PeriodEnd,
            s.Sheet.WorkedDays, s.Sheet.ExpectedMinutes, s.Sheet.WorkedMinutes, s.Sheet.OvertimeMinutes, s.Sheet.AbsenceDays, s.Sheet.AbsenceMinutes, s.Sheet.LateMinutes, s.Sheet.JustifiedMinutes,
            TimesheetRules.ParseByReason(s.Sheet.JustifiedByReason), s.Sheet.BankMonthMinutes, s.Sheet.BankBalanceMinutes)));
        var closedAt = sheets.Max(s => s.Sheet.ClosedAt) ?? DateTimeOffset.MinValue;
        var summary = $"Ponto: incluído (período fechado em {AccountantLayoutV1.Date(DateOnly.FromDateTime(BusinessTime.ToBusiness(closedAt).DateTime))})";
        return new AccountantSectionResult(Key, true, summary, AccountantLayoutV2.TimesheetFile, SafeCsv.Bytes(SafeCsv.Text(AccountantLayoutV2.TimesheetHeader, rows)), rows.Count, []);
    }
}

/// <summary>Layout <c>v1</c> (o de antes da RH-01c): as mesmas seções, sem o <c>ponto.csv</c>; o <c>leia-me.txt</c> diz por quê.</summary>
public sealed class AccountantExportV1(IEnumerable<IAccountantExportSection> sections) : IAccountantExport
{
    public const string Note = "Ponto: não incluído; o layout v1 não tem o arquivo do ponto (o v2 tem).";

    public string Layout => AccountantLayoutV1.Version;

    public async Task<IReadOnlyList<AccountantSectionResult>> BuildAsync(AccountantExportContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AccountantSectionResult>();
        foreach (var section in sections.Where(s => s.Key != AccountantTimesheetSection.SectionKey).OrderBy(s => s.Order))
        {
            results.Add(await section.BuildAsync(context, cancellationToken));
        }

        results.Add(new AccountantSectionResult(AccountantTimesheetSection.SectionKey, false, "Ponto: não incluído", null, null, 0, [Note]));
        return results;
    }
}
