using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Conferência paralela e virada da folha calculada (FLH-06, ADR 0039 §5): para cada competência, a folha lançada (da contadora, oficial) e a
/// calculada (simulação) lado a lado por funcionário e rubrica, com a diferença em R$ e em % e tolerância zero; a classificação e a justificativa
/// de cada diferença; o aceite pelo Proprietário com a contadora responsável e o termo assinado; a virada depois de <c>hr.parallel_months</c>
/// competências seguidas aceitas e a volta com motivo. Até a virada, quem gera títulos e caixa é a lançada; nada aqui recalcula a folha (o
/// motor é o da FLH-02a). Valores só para <c>hr.view_sensitive</c>; aceitar, virar e voltar, só o Proprietário. Nenhum valor vai para log ou evento.
/// </summary>
public sealed partial class PayrollParallelService(
    IHrDb db,
    IPermissionChecker permissions,
    IPermissionService effective,
    HrParametersService parameters,
    AttachmentService attachments,
    IPdfRenderer renderer,
    ICompanyIdentity company,
    IEventPublisher events,
    INotificationService notifications,
    IUserDirectory users,
    IAuditWriter audit,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    /// <summary>Dono de anexo do termo de conferência assinado (pdf, png ou jpg, varrido pelo antivírus antes de valer).</summary>
    public const string TermOwner = "PayrollParallelTerm";
    public const string NotificationType = "hr.payroll";
    public const string Route = "/app/rh/conferencia-folha";
    public const string ParametersLink = "/app/configuracoes/parametros?secao=rh";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Link(DateOnly period) => $"{Route}?mes={PayrollParallelRules.Month(period)}";

    private sealed record State(List<PayrollParallelRun> Runs, List<PayrollModeChange> Changes, IReadOnlyDictionary<PayrollParallelType, (bool Calculated, DateOnly? From)> Modes, int Required, IReadOnlyList<DateOnly> Consecutive)
    {
        public bool CanSwitch => !Modes[PayrollParallelType.Monthly].Calculated && Consecutive.Count >= Required;
    }

    /// <summary>A tela (CA 1, 5 e 6), sem valor: competências, pendentes, a contagem das seguidas, a virada e a situação por tipo.</summary>
    public async Task<PayrollParallelOverview> OverviewAsync(CancellationToken cancellationToken = default)
    {
        var state = await StateAsync(cancellationToken);
        var names = await NamesAsync(state.Runs.SelectMany(r => new[] { r.ComparedBy, r.AcceptedBy }), cancellationToken);
        var rows = state.Runs.OrderByDescending(r => r.Period).ThenBy(r => r.PayrollType).Select(r => Row(r, names, state)).ToList();
        var last = state.Runs.Where(r => r.Status == PayrollParallelStatus.Accepted).OrderByDescending(r => r.AcceptedAt).FirstOrDefault();
        return new PayrollParallelOverview(rows, await PendingAsync(state.Runs, cancellationToken), state.Required, state.Consecutive, state.CanSwitch,
            state.CanSwitch ? PayrollParallelRules.FromPeriod(state.Consecutive) : null, Modes(state), await IsOwnerAsync(cancellationToken), last?.AccountantName, last?.AccountantCrc);
    }

    /// <summary>
    /// "Comparar" (CA 1 e 3): a lançada e a calculada da competência e do tipo, por funcionário e rubrica (o código, com o de-para), tolerância
    /// zero. Sem diferença, a competência fica em conferência (pronta para aceitar); com qualquer diferença, reprovada até nova comparação. A
    /// classificação de uma linha que continua diferente fica; a que ficou igual some. Competência aceita não é comparada de novo.
    /// </summary>
    public async Task<Result<PayrollParallelDetail>> CompareAsync(PayrollParallelCompareInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        if (PayrollParallelRules.ParseMonth(input.Period) is not { } period)
        {
            return PayrollRules.Field("period", "Escolha a competência (mês e ano).");
        }

        var type = input.Type ?? PayrollRunType.Monthly;
        if (PayrollParallelRules.TypeOf(type) is not { } parallelType)
        {
            return Error.Validation(PayrollParallelRules.NotSupportedCode, "O adiantamento não entra na conferência: compare a folha Mensal.");
        }

        var calculated = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Period == period && r.Type == type && r.Mode == PayrollRunMode.Calculated && r.RectifiesRunId == null, cancellationToken);
        if (calculated?.CalculatedAt is null)
        {
            return Error.Validation(PayrollParallelRules.CalculatedMissingCode, "Calcule a folha da competência para comparar com a da contadora.") with
            {
                Data = new Dictionary<string, object?> { ["link"] = calculated is null ? "/app/rh/folha" : PayrollClosingService.RunLink(calculated.Id, "holerites") },
            };
        }

        var entered = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Period == period && r.Type == type && r.Mode == PayrollRunMode.Entered && r.RectifiesRunId == null, cancellationToken);
        var enteredLines = entered is null ? [] : await LinesAsync(entered.Id, cancellationToken);
        if (entered is null || enteredLines.Count == 0)
        {
            return EnteredMissing(period, entered?.Id);
        }

        var run = await db.PayrollParallelRuns.FirstOrDefaultAsync(r => r.Period == period && r.PayrollType == type, cancellationToken);
        if (run?.Status == PayrollParallelStatus.Accepted)
        {
            return Error.Conflict(PayrollParallelRules.AcceptedCode, $"A competência {PayrollRules.PeriodLabel(period)} já foi aceita: a comparação não muda.");
        }

        var rows = PayrollParallelRules.Compare(enteredLines, await LinesAsync(calculated.Id, cancellationToken), await MappingsAsync(cancellationToken));
        var now = clock.GetUtcNow();
        if (run is null)
        {
            run = new PayrollParallelRun { Period = period, RunType = parallelType, PayrollType = type, CreatedAt = now };
            db.PayrollParallelRuns.Add(run);
        }

        run.EnteredRunId = entered.Id;
        run.CalculatedRunId = calculated.Id;
        run.ComparedAt = now;
        run.ComparedBy = currentUser.UserId;
        run.Differences = rows.Count(r => !r.Equal);
        run.Employees = rows.Select(r => r.EmployeeId).Distinct().Count();
        run.EnteredNet = PayrollParallelRules.Net(rows, false);
        run.CalculatedNet = PayrollParallelRules.Net(rows, true);
        run.Status = PayrollParallelRules.CanAccept(rows) ? PayrollParallelStatus.Open : PayrollParallelStatus.Rejected;
        await SyncRowsAsync(run, rows, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(run.Id, cancellationToken);
    }

    /// <summary>
    /// A competência lado a lado (CA 1, só por <c>/sensitive</c>): por funcionário (os com mais diferenças primeiro) e por rubrica (as diferentes
    /// primeiro), os dois valores, a diferença em R$ e em %, a classificação e a memória de cálculo do sistema; os líquidos e as rubricas com
    /// mais diferença. A leitura vai para a auditoria.
    /// </summary>
    public async Task<Result<PayrollParallelDetail>> DetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollParallelRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        var state = await StateAsync(cancellationToken);
        var diffs = await db.PayrollParallelDifferences.AsNoTracking().Where(d => d.ParallelRunId == id).ToListAsync(cancellationToken);
        var employeeIds = diffs.Select(d => d.EmployeeId).Distinct().ToList();
        var people = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => employeeIds.Contains(e.Id)).Select(e => new { e.Id, e.Name, e.Registration }).ToDictionaryAsync(e => e.Id, cancellationToken);
        var memory = await MemoryAsync(run.CalculatedRunId, cancellationToken);
        var names = await NamesAsync([.. diffs.Select(d => d.ClassifiedBy), run.ComparedBy, run.AcceptedBy], cancellationToken);
        var employees = diffs.GroupBy(d => d.EmployeeId).Select(g =>
        {
            var asRows = g.Select(ToRow).ToList();
            var enteredNet = PayrollParallelRules.Net(asRows, false);
            var calculatedNet = PayrollParallelRules.Net(asRows, true);
            var cells = g.OrderBy(d => d.Difference == 0m).ThenByDescending(d => Math.Abs(d.Difference)).ThenBy(d => d.PayrollItemCode, StringComparer.Ordinal)
                .Select(d => Cell(d, names, memory)).ToList();
            var person = people.GetValueOrDefault(g.Key);
            return new PayrollParallelEmployee(g.Key, person?.Name ?? "Funcionário removido", person?.Registration, enteredNet, calculatedNet, calculatedNet - enteredNet, cells.Count(c => !c.Equal), cells);
        }).OrderByDescending(e => e.Differences).ThenBy(e => e.Name, StringComparer.Create(PtBr, false)).ToList();
        var items = diffs.Where(d => d.Difference != 0m).GroupBy(d => d.PayrollItemCode)
            .Select(g => new PayrollParallelItemSummary(g.Key, g.First().ItemName, g.Count(), g.Sum(d => d.Difference)))
            .OrderByDescending(s => s.Differences).ThenByDescending(s => Math.Abs(s.TotalDifference)).ThenBy(s => s.Code, StringComparer.Ordinal).ToList();
        var canAccept = run.Status == PayrollParallelStatus.Open && run.Differences == 0;
        PayrollNextStep? next = run.Status switch
        {
            PayrollParallelStatus.Open when canAccept => new PayrollNextStep($"Aceitar competência {PayrollRules.PeriodLabel(run.Period)}", Link(run.Period)),
            PayrollParallelStatus.Accepted when state.CanSwitch => new PayrollNextStep("Virar para a folha calculada", Route),
            _ => null,
        };
        await audit.RecordReadAsync(nameof(PayrollParallelRun), id.ToString(), cancellationToken);
        return new PayrollParallelDetail(Row(run, names, state), run.EnteredRunId, run.CalculatedRunId, run.EnteredNet, run.CalculatedNet, run.CalculatedNet - run.EnteredNet, canAccept,
            employees, items, next);
    }

    /// <summary>Histórico (CA 10): comparações, classificações, aceites, viradas e voltas; a justificativa só com <c>hr.view_sensitive</c>.</summary>
    public async Task<IReadOnlyList<PayrollParallelHistoryEntry>> HistoryAsync(CancellationToken cancellationToken = default)
    {
        var sensitive = await CanSensitiveAsync(cancellationToken);
        var runs = await db.PayrollParallelRuns.AsNoTracking().ToDictionaryAsync(r => r.Id, cancellationToken);
        var classified = await db.PayrollParallelDifferences.AsNoTracking().Where(d => d.ClassifiedAt != null)
            .Select(d => new { d.ParallelRunId, d.PayrollItemCode, d.Classification, d.Justification, d.ClassifiedAt, d.ClassifiedBy }).ToListAsync(cancellationToken);
        var changes = await db.PayrollModeChanges.AsNoTracking().ToListAsync(cancellationToken);
        var names = await NamesAsync([.. runs.Values.SelectMany(r => new[] { r.ComparedBy, r.AcceptedBy }), .. classified.Select(c => c.ClassifiedBy), .. changes.Select(c => c.ChangedBy)], cancellationToken);
        string? Name(Guid? id) => id is { } u ? names.GetValueOrDefault(u) : null;
        string Of(PayrollParallelRun r) => $"{PayrollParallelRules.TypeLabel(r.RunType)} de {PayrollRules.PeriodLabel(r.Period)}";
        var entries = new List<PayrollParallelHistoryEntry>();
        foreach (var r in runs.Values)
        {
            entries.Add(new(r.ComparedAt, Name(r.ComparedBy), "Comparou", Of(r), r.Differences == 0 ? "Sem diferença" : r.Differences == 1 ? "1 diferença" : $"{r.Differences} diferenças"));
            if (r.AcceptedAt is { } at)
            {
                entries.Add(new(at, Name(r.AcceptedBy), "Aceitou a competência", Of(r), $"Contadora {r.AccountantName}, CRC {r.AccountantCrc}"));
            }
        }

        foreach (var c in classified.Where(c => runs.ContainsKey(c.ParallelRunId)))
        {
            var label = c.Classification is { } k ? PayrollParallelRules.ClassificationLabel(k) : "Sem classificação";
            entries.Add(new(c.ClassifiedAt!.Value, Name(c.ClassifiedBy), "Classificou uma diferença", Of(runs[c.ParallelRunId]),
                sensitive ? $"Rubrica {c.PayrollItemCode}: {label}. {c.Justification}" : $"Rubrica {c.PayrollItemCode}: {label}."));
        }

        foreach (var c in changes)
        {
            entries.Add(c.Mode == PayrollRunMode.Calculated
                ? new(c.ChangedAt, Name(c.ChangedBy), "Virou para a folha calculada", $"{PayrollParallelRules.TypeLabel(c.RunType)} a partir de {PayrollRules.PeriodLabel(c.FromPeriod)}",
                    c.AccountantName is null ? null : $"{c.ParallelRunIds.Count} {(c.ParallelRunIds.Count == 1 ? "competência aceita" : "competências aceitas")}; contadora {c.AccountantName}, CRC {c.AccountantCrc}")
                : new(c.ChangedAt, Name(c.ChangedBy), "Voltou para a folha lançada", PayrollParallelRules.TypeLabel(c.RunType), c.Reason));
        }

        return entries.OrderByDescending(e => e.At).Take(200).ToList();
    }

    /// <summary>De-para de rubricas (CA 1): o código da contadora e o do sistema.</summary>
    public async Task<IReadOnlyList<PayrollItemMappingRow>> MappingsListAsync(CancellationToken cancellationToken = default)
    {
        var items = await db.PayrollItems.AsNoTracking().ToDictionaryAsync(i => i.Code, i => i.Name, cancellationToken);
        return (await db.PayrollItemMappings.AsNoTracking().OrderBy(m => m.EnteredCode).ToListAsync(cancellationToken))
            .Select(m => new PayrollItemMappingRow(m.Id, m.EnteredCode, m.CalculatedCode, items.GetValueOrDefault(m.CalculatedCode))).ToList();
    }

    /// <summary>Grava o de-para (um por código da contadora): o código do sistema precisa ser de uma rubrica cadastrada.</summary>
    public async Task<Result<IReadOnlyList<PayrollItemMappingRow>>> SaveMappingAsync(PayrollItemMappingInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (PayrollRules.NormalizeCode(input.EnteredCode) is not { } from)
        {
            return PayrollRules.Field("enteredCode", "Informe o código da rubrica na folha da contadora (até 10 letras ou dígitos).");
        }

        if (PayrollRules.NormalizeCode(input.CalculatedCode) is not { } to || !await db.PayrollItems.AnyAsync(i => i.Code == to, cancellationToken))
        {
            return PayrollRules.Field("calculatedCode", "Escolha uma rubrica cadastrada no sistema.");
        }

        if (from == to)
        {
            return PayrollRules.Field("calculatedCode", "Os códigos são iguais: a correspondência já é automática.");
        }

        var now = clock.GetUtcNow();
        var mapping = await db.PayrollItemMappings.FirstOrDefaultAsync(m => m.EnteredCode == from, cancellationToken);
        if (mapping is null)
        {
            db.PayrollItemMappings.Add(new PayrollItemMapping { EnteredCode = from, CalculatedCode = to, CreatedAt = now, UpdatedAt = now });
        }
        else
        {
            mapping.CalculatedCode = to;
            mapping.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await MappingsListAsync(cancellationToken)).ToList();
    }

    public async Task<Result<IReadOnlyList<PayrollItemMappingRow>>> DeleteMappingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var mapping = await db.PayrollItemMappings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (mapping is null)
        {
            return Error.NotFound("payroll_item_mapping_not_found", "Correspondência não encontrada.");
        }

        db.PayrollItemMappings.Remove(mapping);
        await db.SaveChangesAsync(cancellationToken);
        return (await MappingsListAsync(cancellationToken)).ToList();
    }

    private async Task<State> StateAsync(CancellationToken ct)
    {
        var runs = await db.PayrollParallelRuns.AsNoTracking().ToListAsync(ct);
        var changes = await db.PayrollModeChanges.AsNoTracking().OrderBy(c => c.ChangedAt).ToListAsync(ct);
        var modes = await parameters.ModesAsync(ct);
        var required = await parameters.ParallelMonthsAsync(ct);
        var consecutive = PayrollParallelRules.ConsecutiveAccepted(
            runs.Where(r => r.RunType == PayrollParallelType.Monthly).Select(r => new PayrollParallelMonth(r.Period, r.Status, r.AcceptedAt)), RevertedAt(changes, PayrollParallelType.Monthly));
        return new State(runs, changes, modes, required, consecutive);
    }

    /// <summary>A última volta para a lançada que vale para o tipo (a do próprio tipo ou a da Mensal): a contagem recomeça dali (CA 7).</summary>
    private static DateTimeOffset? RevertedAt(IEnumerable<PayrollModeChange> changes, PayrollParallelType type) =>
        changes.Where(c => c.Mode == PayrollRunMode.Entered && (c.RunType == type || c.RunType == PayrollParallelType.Monthly)).Max(c => (DateTimeOffset?)c.ChangedAt);

    /// <summary>O caso conferido do tipo (CA 6): a competência aceita mais recente depois da última volta.</summary>
    private static PayrollParallelRun? AcceptedCase(State state, PayrollParallelType type)
    {
        var since = RevertedAt(state.Changes, type);
        return state.Runs.Where(r => r.RunType == type && r.Status == PayrollParallelStatus.Accepted && (since is null || r.AcceptedAt > since)).OrderByDescending(r => r.AcceptedAt).FirstOrDefault();
    }

    private static List<PayrollModeView> Modes(State state) => Enum.GetValues<PayrollParallelType>().Select(t =>
    {
        var (calculated, from) = state.Modes[t];
        var since = calculated ? state.Changes.Where(c => c.RunType == t && c.Mode == PayrollRunMode.Calculated).Max(c => (DateTimeOffset?)c.ChangedAt) : null;
        var accepted = t == PayrollParallelType.Monthly ? null : AcceptedCase(state, t);
        var situation = calculated
            ? "Oficial desde " + (since is { } s ? BusinessTime.ToBusiness(s).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "a virada") + (from is { } f ? $", a partir de {PayrollRules.PeriodLabel(f)}" : string.Empty)
            : t == PayrollParallelType.Monthly
                ? $"{state.Consecutive.Count} de {state.Required} competências seguidas aceitas"
                : accepted is not null ? $"Caso conferido em {PayrollRules.PeriodLabel(accepted.Period)}: vira junto com a Mensal" : "Aguardando um caso conferido";
        return new PayrollModeView(t, PayrollParallelRules.TypeLabel(t), calculated ? PayrollRunMode.Calculated : PayrollRunMode.Entered, from, since, situation, accepted?.Period);
    }).ToList();

    /// <summary>Folhas calculadas ainda sem comparação (Propositivo): o que falta (calcular, importar a da contadora) ou "Comparar".</summary>
    private async Task<List<PayrollParallelPending>> PendingAsync(List<PayrollParallelRun> runs, CancellationToken ct)
    {
        var done = runs.Select(r => (r.Period, r.PayrollType)).ToHashSet();
        var all = await db.PayrollRuns.AsNoTracking().Where(r => r.RectifiesRunId == null && r.Type != PayrollRunType.Advance).Select(r => new { r.Id, r.Period, r.Type, r.Mode, r.CalculatedAt }).ToListAsync(ct);
        var enteredIds = all.Where(r => r.Mode == PayrollRunMode.Entered).Select(r => r.Id).ToList();
        var withLines = (await (from l in db.PayslipLines.AsNoTracking() join p in db.Payslips.AsNoTracking() on l.PayslipId equals p.Id where enteredIds.Contains(p.PayrollRunId) select p.PayrollRunId)
            .Distinct().ToListAsync(ct)).ToHashSet();
        var list = new List<PayrollParallelPending>();
        foreach (var calc in all.Where(r => r.Mode == PayrollRunMode.Calculated && !done.Contains((r.Period, r.Type))).OrderByDescending(r => r.Period).ThenBy(r => r.Type))
        {
            var entered = all.FirstOrDefault(r => r.Mode == PayrollRunMode.Entered && r.Period == calc.Period && r.Type == calc.Type);
            var month = PayrollRules.PeriodLabel(calc.Period);
            list.Add(calc.CalculatedAt is null
                ? new(calc.Period, calc.Type, calc.Id, entered?.Id, false, $"Calcule a folha de {month} para comparar com a da contadora.", "Calcular folha", PayrollClosingService.RunLink(calc.Id, "holerites"))
                : entered is null || !withLines.Contains(entered.Id)
                    ? new(calc.Period, calc.Type, calc.Id, entered?.Id, false, $"Importe a folha da contadora de {month} para comparar.", "Importar a folha da contadora",
                        entered is null ? "/app/rh/folha" : PayrollClosingService.RunLink(entered.Id, "holerites"))
                    : new(calc.Period, calc.Type, calc.Id, entered.Id, true, $"As duas folhas de {month} estão prontas para comparar.", "Comparar", null));
        }

        return list;
    }

    private static PayrollParallelMonthRow Row(PayrollParallelRun r, IReadOnlyDictionary<Guid, string?> names, State state) =>
        new(r.Id, r.Period, r.RunType, r.PayrollType, r.Status, PayrollParallelRules.StatusLabel(r.Status), r.Employees, r.Differences, r.ComparedAt,
            r.ComparedBy is { } c ? names.GetValueOrDefault(c) : null, r.AcceptedAt, r.AcceptedBy is { } a ? names.GetValueOrDefault(a) : null, r.AccountantName, r.AccountantCrc,
            r.SignedTermAttachmentId is not null, r.RunType == PayrollParallelType.Monthly && state.Consecutive.Contains(r.Period), r.Version);

    private static PayrollParallelRow ToRow(PayrollParallelDifference d) => new(d.EmployeeId, d.PayrollItemCode, d.ItemName, d.Kind, d.EnteredAmount, d.CalculatedAmount);

    private static PayrollParallelCell Cell(PayrollParallelDifference d, IReadOnlyDictionary<Guid, string?> names, IReadOnlyDictionary<(Guid, string), List<string>> memory) =>
        new(d.Id, d.PayrollItemCode, d.ItemName, d.Kind, d.EnteredAmount, d.CalculatedAmount, d.Difference, d.DifferencePct, d.Difference == 0m, d.Classification,
            d.Classification is { } k ? PayrollParallelRules.ClassificationLabel(k) : null, d.Justification, d.ClassifiedBy is { } by ? names.GetValueOrDefault(by) : null, d.ClassifiedAt,
            d.CorrectionCase is not null, memory.GetValueOrDefault((d.EmployeeId, d.PayrollItemCode)) ?? []);

    /// <summary>As linhas de uma folha para a comparação, com o código e o nome do momento (o histórico sobrevive à troca do código).</summary>
    private async Task<List<PayrollParallelLine>> LinesAsync(Guid runId, CancellationToken ct) =>
        await (from l in db.PayslipLines.AsNoTracking()
               join p in db.Payslips.AsNoTracking() on l.PayslipId equals p.Id
               where p.PayrollRunId == runId
               select new PayrollParallelLine(p.EmployeeId, l.Code, l.Description, l.Type, l.Amount)).ToListAsync(ct);

    private async Task<Dictionary<string, string>> MappingsAsync(CancellationToken ct) =>
        await db.PayrollItemMappings.AsNoTracking().ToDictionaryAsync(m => m.EnteredCode, m => m.CalculatedCode, StringComparer.Ordinal, ct);

    /// <summary>A memória de cálculo de cada linha da calculada, por funcionário e código (drill-down da célula, FLH-02a CA 12).</summary>
    private async Task<Dictionary<(Guid, string), List<string>>> MemoryAsync(Guid calculatedRunId, CancellationToken ct) =>
        (await (from l in db.PayslipLines.AsNoTracking()
                join p in db.Payslips.AsNoTracking() on l.PayslipId equals p.Id
                where p.PayrollRunId == calculatedRunId && l.Memory != null
                orderby l.Sort
                select new { p.EmployeeId, l.Code, l.Memory }).ToListAsync(ct))
        .GroupBy(x => (x.EmployeeId, x.Code)).ToDictionary(g => g.Key, g => g.SelectMany(x => PayrollCalculator.ReadMemory(x.Memory)).ToList());

    /// <summary>Grava as linhas da comparação no lugar das anteriores (sem apagar e recriar a mesma chave).</summary>
    private async Task SyncRowsAsync(PayrollParallelRun run, IReadOnlyList<PayrollParallelRow> rows, CancellationToken ct)
    {
        var existing = (await db.PayrollParallelDifferences.Where(d => d.ParallelRunId == run.Id).ToListAsync(ct)).ToDictionary(d => (d.EmployeeId, d.PayrollItemCode));
        foreach (var row in rows)
        {
            if (existing.Remove((row.EmployeeId, row.Code), out var d))
            {
                d.ItemName = Cut(row.Name, PayrollRules.NameMaxLength);
                d.Kind = row.Kind;
                d.EnteredAmount = row.Entered;
                d.CalculatedAmount = row.Calculated;
                d.Difference = row.Difference;
                d.DifferencePct = row.DifferencePct;
                if (row.Equal)
                {
                    d.Classification = null;
                    d.Justification = null;
                    d.ClassifiedBy = null;
                    d.ClassifiedAt = null;
                    d.CorrectionCase = null;
                }

                continue;
            }

            db.PayrollParallelDifferences.Add(new PayrollParallelDifference
            {
                ParallelRunId = run.Id, EmployeeId = row.EmployeeId, PayrollItemCode = row.Code, ItemName = Cut(row.Name, PayrollRules.NameMaxLength), Kind = row.Kind,
                EnteredAmount = row.Entered, CalculatedAmount = row.Calculated, Difference = row.Difference, DifferencePct = row.DifferencePct,
            });
        }

        db.PayrollParallelDifferences.RemoveRange(existing.Values);
    }

    private async Task<Dictionary<Guid, string?>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var id in ids.OfType<Guid>().Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, ct);
        }

        return names;
    }

    private static Error EnteredMissing(DateOnly period, Guid? enteredId) =>
        Error.Validation(PayrollParallelRules.EnteredMissingCode, $"Importe a folha da contadora de {PayrollRules.PeriodLabel(period)} para comparar.") with
        {
            Data = new Dictionary<string, object?> { ["link"] = enteredId is { } id ? PayrollClosingService.RunLink(id, "holerites") : "/app/rh/folha" },
        };

    private async Task<bool> IsOwnerAsync(CancellationToken ct) => currentUser.UserId is { } me && (await effective.GetAsync(me, ct)).IsOwner;

    private ValueTask<bool> CanSensitiveAsync(CancellationToken ct) => permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, ct);

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];

    private static Error NotFound() => Error.NotFound("parallel_run_not_found", "Conferência não encontrada.");

    private static Error Forbidden() => Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso aos valores da folha. Fale com o administrador.");

    private static Error OwnerOnly() => Error.Forbidden(Error.Codes.Forbidden, "Só o Proprietário aceita a competência, vira e volta a folha.");
}
