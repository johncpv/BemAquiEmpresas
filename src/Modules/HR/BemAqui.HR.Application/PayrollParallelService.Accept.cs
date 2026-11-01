using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Classificação, caso de correção, termo, aceite, virada e volta (FLH-06 CA 2 a 7). Aceitar, virar e voltar só o Proprietário; a virada pede a
/// confirmação explícita do diálogo com o impacto e fica registrada em <c>hr.payroll_mode_changes</c>, nos parâmetros do modo (auditados) e no
/// <c>PayrollModeChanged</c> (sem valor), com aviso aos membros do Proprietário.
/// </summary>
public sealed partial class PayrollParallelService
{
    public const string PdfContentType = "application/pdf";
    public const string JsonContentType = "application/json";

    private static readonly JsonSerializerOptions CasesJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Classificar e justificar (CA 2 e 4), com <c>hr.view_sensitive</c>: só a linha com diferença de uma competência não aceita. "Erro do
    /// sistema" grava o caso de correção anonimizado; "Regra diferente" e "Arredondamento" levam ao parâmetro ou à tabela; "Erro da contadora"
    /// leva à folha lançada para importar a corrigida. A justificativa nunca aceita a competência: só a comparação refeita com diferença zero.
    /// </summary>
    public async Task<Result<PayrollDifferenceResult>> ClassifyAsync(Guid id, Guid differenceId, PayrollDifferenceInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var run = await db.PayrollParallelRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        var difference = run is null ? null : await db.PayrollParallelDifferences.FirstOrDefaultAsync(d => d.Id == differenceId && d.ParallelRunId == id, cancellationToken);
        if (run is null || difference is null)
        {
            return Error.NotFound("parallel_difference_not_found", "Diferença não encontrada.");
        }

        if (run.Status == PayrollParallelStatus.Accepted)
        {
            return Error.Conflict(PayrollParallelRules.AcceptedCode, $"A competência {PayrollRules.PeriodLabel(run.Period)} já foi aceita.");
        }

        if (difference.Difference == 0m)
        {
            return PayrollRules.Field("classification", "Só a linha com diferença é classificada.");
        }

        if (input.Classification is not { } classification || !Enum.IsDefined(classification))
        {
            return PayrollRules.Field("classification", "Escolha a classificação da diferença.");
        }

        var justification = (input.Justification ?? string.Empty).Trim();
        if (justification.Length is < PayrollParallelRules.JustificationMin or > PayrollParallelRules.JustificationMax)
        {
            return PayrollRules.Field("justification", "Escreva a justificativa, de 5 a 500 caracteres.");
        }

        difference.Classification = classification;
        difference.Justification = justification;
        difference.ClassifiedBy = currentUser.UserId;
        difference.ClassifiedAt = clock.GetUtcNow();
        difference.CorrectionCase = classification == PayrollDifferenceClassification.SystemError ? await CorrectionCaseAsync(run, difference.EmployeeId, cancellationToken) : null;
        await db.SaveChangesAsync(cancellationToken);

        var memory = await MemoryAsync(run.CalculatedRunId, cancellationToken);
        var names = await NamesAsync([difference.ClassifiedBy], cancellationToken);
        PayrollNextStep? next = classification switch
        {
            PayrollDifferenceClassification.RuleDifference => new PayrollNextStep("Ajustar parâmetro", ParametersLink),
            PayrollDifferenceClassification.Rounding => new PayrollNextStep("Conferir o arredondamento da tabela", PayrollTaxTableService.Link),
            PayrollDifferenceClassification.AccountantError => new PayrollNextStep("Importar a folha corrigida", PayrollClosingService.RunLink(run.EnteredRunId, "holerites")),
            _ => null,
        };
        return new PayrollDifferenceResult(Cell(difference, names, memory), next);
    }

    /// <summary>
    /// "Baixar casos de correção" (CA 2): os casos "Erro do sistema" da competência em JSON, sem nome, CPF, matrícula nem data de nascimento,
    /// para virar caso no dataset sintético do CI. Gerado na hora, nunca guardado; a leitura vai para a auditoria.
    /// </summary>
    public async Task<Result<PayrollParallelFile>> CorrectionCasesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollParallelRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        // Um caso por funcionário (as linhas do mesmo funcionário levam o mesmo caso).
        var cases = (await db.PayrollParallelDifferences.AsNoTracking().Where(d => d.ParallelRunId == id && d.CorrectionCase != null)
                .OrderBy(d => d.ClassifiedAt).Select(d => new { d.EmployeeId, d.CorrectionCase }).ToListAsync(cancellationToken))
            .GroupBy(d => d.EmployeeId).Select(g => PayrollParallelRules.ReadCase(g.Last().CorrectionCase)).OfType<PayrollCorrectionCase>().ToList();
        if (cases.Count == 0)
        {
            return Error.Validation("parallel_no_correction_cases", "Nenhuma diferença classificada como \"Erro do sistema\" nesta competência.");
        }

        await audit.RecordReadAsync(nameof(PayrollParallelRun) + ".CorrectionCases", id.ToString(), cancellationToken);
        var json = JsonSerializer.Serialize(cases, CasesJson);
        return new PayrollParallelFile(Encoding.UTF8.GetBytes(json), $"casos-correcao-{PayrollParallelRules.Month(run.Period)}.json", JsonContentType);
    }

    /// <summary>
    /// "Termo de conferência da competência {mês}" (CA 3): o PDF pronto para imprimir e assinar, com os totais das duas folhas e os campos da
    /// contadora. Só para a competência sem diferença; gerado na hora, nunca guardado.
    /// </summary>
    public async Task<Result<PayrollParallelFile>> TermAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollParallelRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        if (run.Differences > 0 || run.Status == PayrollParallelStatus.Rejected)
        {
            return Error.Conflict(PayrollParallelRules.DifferencesCode, "O termo sai só para a competência sem diferença: corrija e compare de novo.");
        }

        var entered = await TotalsAsync(run.EnteredRunId, cancellationToken);
        var calculated = await TotalsAsync(run.CalculatedRunId, cancellationToken);
        var info = await company.GetAsync(cancellationToken);
        var version = await db.PayrollRuns.AsNoTracking().Where(r => r.Id == run.CalculatedRunId).Select(r => r.CalculatorVersion).FirstOrDefaultAsync(cancellationToken);
        var model = new PayrollParallelTermModel(info.LegalName, info.Document, run.Period, PayrollParallelRules.TypeLabel(run.RunType), run.Employees, entered, calculated, version,
            BusinessTime.ToBusiness(clock.GetUtcNow()).DateTime, run.AccountantName, run.AccountantCrc);
        var pdf = await renderer.RenderAsync(new PayrollParallelTermDocument(model), cancellationToken);
        await audit.RecordReadAsync(nameof(PayrollParallelRun) + ".Term", id.ToString(), cancellationToken);
        return new PayrollParallelFile(pdf, $"termo-conferencia-{PayrollParallelRules.Month(run.Period)}.pdf", PdfContentType);
    }

    /// <summary>Anexa o termo assinado (CA 3): só o Proprietário, na competência ainda não aceita; o arquivo passa pelo antivírus antes de valer.</summary>
    public async Task<Result<AttachmentDto>> AttachTermAsync(Guid id, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        if (!await IsOwnerAsync(cancellationToken))
        {
            return OwnerOnly();
        }

        var run = await db.PayrollParallelRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        if (run.Status == PayrollParallelStatus.Accepted)
        {
            return Error.Conflict(PayrollParallelRules.AcceptedCode, $"A competência {PayrollRules.PeriodLabel(run.Period)} já foi aceita.");
        }

        return await attachments.UploadCheckedAsync(TermOwner, id, fileName, content, (tenant.Tenant?.Id ?? Guid.Empty).ToString("N"), cancellationToken);
    }

    /// <summary>
    /// "Aceitar competência {mês}" (CA 3, 4 e 6): só o Proprietário; a competência em conferência com diferença zero no líquido e em cada
    /// rubrica, conferida de novo contra as folhas de agora (mudou: 409, compare de novo); a contadora responsável (nome e CRC) e o termo
    /// assinado anexado e limpo. Aceito o caso de férias, 13º ou rescisão com a Mensal já virada, o tipo vira junto (CA 6).
    /// </summary>
    public async Task<Result<PayrollParallelDetail>> AcceptAsync(Guid id, PayrollParallelAcceptInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await IsOwnerAsync(cancellationToken))
        {
            return OwnerOnly();
        }

        var run = await db.PayrollParallelRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        if (run.Status == PayrollParallelStatus.Accepted)
        {
            return Error.Conflict(PayrollParallelRules.AcceptedCode, $"A competência {PayrollRules.PeriodLabel(run.Period)} já foi aceita.");
        }

        if (run.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "A conferência mudou desde que você abriu. Recarregue.");
        }

        if (run.Status != PayrollParallelStatus.Open || run.Differences > 0)
        {
            return Error.Conflict(PayrollParallelRules.DifferencesCode, "Há diferenças na competência: a justificativa não basta. Importe a folha corrigida ou ajuste o parâmetro, recalcule e compare de novo.");
        }

        var name = (input.AccountantName ?? string.Empty).Trim();
        if (name.Length is < 3 or > PayrollParallelRules.AccountantNameMax)
        {
            return PayrollRules.Field("accountantName", "Informe o nome da contadora responsável.");
        }

        var crc = (input.AccountantCrc ?? string.Empty).Trim().ToUpperInvariant();
        if (crc.Length is < 4 or > PayrollParallelRules.CrcMax || crc.Count(char.IsAsciiDigit) < 3)
        {
            return PayrollRules.Field("accountantCrc", "Informe o CRC da contadora (ex.: SP-123456/O).");
        }

        var term = input.SignedTermAttachmentId is { } termId ? (await attachments.ListCheckedAsync(TermOwner, run.Id, cancellationToken)).FirstOrDefault(a => a.Id == termId) : null;
        if (term is null)
        {
            return PayrollRules.Field("signedTermAttachmentId", "Anexe o termo de conferência assinado pela contadora.") with { Code = PayrollParallelRules.TermMissingCode };
        }

        if (term.ScanStatus != ScanStatus.Clean)
        {
            return Error.Conflict(PayrollParallelRules.TermScanningCode, "O termo ainda está sendo verificado pelo antivírus. Tente de novo em alguns minutos.");
        }

        // A folha mudou depois da comparação (reimportação, recálculo): o aceite vale para o que foi comparado.
        var rows = PayrollParallelRules.Compare(await LinesAsync(run.EnteredRunId, cancellationToken), await LinesAsync(run.CalculatedRunId, cancellationToken), await MappingsAsync(cancellationToken));
        var stillCurrent = await db.PayrollRuns.CountAsync(r => (r.Id == run.EnteredRunId || r.Id == run.CalculatedRunId) && r.RectifiesRunId == null, cancellationToken) == 2;
        if (!stillCurrent || !PayrollParallelRules.CanAccept(rows) || PayrollParallelRules.Net(rows, false) != run.EnteredNet)
        {
            return Error.Conflict(PayrollParallelRules.ChangedCode, "A folha mudou depois da comparação: compare de novo antes de aceitar.");
        }

        var now = clock.GetUtcNow();
        run.Status = PayrollParallelStatus.Accepted;
        run.AcceptedAt = now;
        run.AcceptedBy = currentUser.UserId;
        run.AccountantName = name;
        run.AccountantCrc = crc;
        run.SignedTermAttachmentId = term.Id;
        db.Entry(run).Property(r => r.Version).OriginalValue = input.Version;

        // CA 6: o caso de férias, 13º ou rescisão aceito com a Mensal já virada faz o tipo virar (a partir da competência seguinte ao caso).
        var modes = await parameters.ModesAsync(cancellationToken);
        if (run.RunType != PayrollParallelType.Monthly && modes[PayrollParallelType.Monthly].Calculated && !modes[run.RunType].Calculated)
        {
            var from = Max(run.Period.AddMonths(1), modes[PayrollParallelType.Monthly].From);
            await ChangeModeAsync(run.RunType, true, from, null, [run.Id], name, crc, cancellationToken);
            await NotifyOwnersAsync($"Folha calculada é a oficial para {PayrollParallelRules.TypeLabel(run.RunType)} a partir de {PayrollRules.PeriodLabel(from)}.", cancellationToken);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("concurrency_conflict", "A conferência mudou desde que você abriu. Recarregue.");
        }

        return await DetailAsync(run.Id, cancellationToken);
    }

    /// <summary>
    /// "Virar para a folha calculada" (CA 5 e 6): só o Proprietário, com a confirmação do diálogo; exige <c>hr.parallel_months</c> competências
    /// Mensais seguidas aceitas (sem exigir férias, 13º ou rescisão). Dali (a competência seguinte à mais recente aceita) a calculada Mensal fecha
    /// como oficial (FLH-02a CA 15) e a lançada continua disponível; o tipo que já tem um caso conferido vira junto. Registrada e avisada.
    /// </summary>
    public async Task<Result<PayrollModeResult>> SwitchAsync(PayrollModeSwitchInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await IsOwnerAsync(cancellationToken))
        {
            return OwnerOnly();
        }

        if (!input.Confirmed)
        {
            return PayrollRules.Field("confirmed", "Confirme a virada no diálogo, depois de ler o impacto.");
        }

        var state = await StateAsync(cancellationToken);
        if (state.Modes[PayrollParallelType.Monthly].Calculated)
        {
            return Error.Conflict(PayrollParallelRules.AlreadyCalculatedCode, "A folha calculada já é a oficial.");
        }

        if (state.Consecutive.Count < state.Required)
        {
            var missing = state.Required - state.Consecutive.Count;
            return Error.Conflict(PayrollParallelRules.NotEnoughMonthsCode,
                $"Faltam {missing} {(missing == 1 ? "competência seguida aceita" : "competências seguidas aceitas")} pela contadora para virar ({state.Consecutive.Count} de {state.Required}).");
        }

        var from = PayrollParallelRules.FromPeriod(state.Consecutive);
        var accepted = state.Runs.Where(r => r.RunType == PayrollParallelType.Monthly && state.Consecutive.Contains(r.Period)).OrderBy(r => r.Period).ToList();
        var latest = accepted[^1];
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ChangeModeAsync(PayrollParallelType.Monthly, true, from, null, accepted.Select(r => r.Id).ToList(), latest.AccountantName, latest.AccountantCrc, cancellationToken);
        var switched = new List<string> { "Mensal" };
        foreach (var type in new[] { PayrollParallelType.Vacation, PayrollParallelType.Thirteenth, PayrollParallelType.Termination })
        {
            if (!state.Modes[type].Calculated && AcceptedCase(state, type) is { } found)
            {
                await ChangeModeAsync(type, true, Max(from, found.Period.AddMonths(1)), null, [found.Id], found.AccountantName, found.AccountantCrc, cancellationToken);
                switched.Add(PayrollParallelRules.TypeLabel(type));
            }
        }

        var message = $"Folha calculada é a oficial a partir de {PayrollRules.PeriodLabel(from)} ({string.Join(", ", switched)}).";
        await NotifyOwnersAsync(message, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PayrollModeResult(message, await OverviewAsync(cancellationToken));
    }

    /// <summary>
    /// "Voltar para a folha lançada" (CA 7): só o Proprietário, com o motivo; por tipo ou para todos (a Mensal leva todos, porque os outros tipos
    /// dependem dela). A calculada volta a ser simulação, a lançada é a oficial e a contagem das seguidas recomeça.
    /// </summary>
    public async Task<Result<PayrollModeResult>> RevertAsync(PayrollModeRevertInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await IsOwnerAsync(cancellationToken))
        {
            return OwnerOnly();
        }

        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is < PayrollParallelRules.ReasonMin or > PayrollParallelRules.ReasonMax)
        {
            return PayrollRules.Field("reason", "Informe o motivo da volta, de 10 a 500 caracteres (ex.: a divergência apontada pela contadora).");
        }

        if (input.Type is { } t && !Enum.IsDefined(t))
        {
            return PayrollRules.Field("type", "Escolha o tipo da folha.");
        }

        var modes = await parameters.ModesAsync(cancellationToken);
        var types = (input.Type is null or PayrollParallelType.Monthly ? Enum.GetValues<PayrollParallelType>() : [input.Type.Value]).Where(x => modes[x].Calculated).ToList();
        if (types.Count == 0)
        {
            return Error.Conflict(PayrollParallelRules.NotCalculatedCode, input.Type is { } only && only != PayrollParallelType.Monthly
                ? $"A folha de {PayrollParallelRules.TypeLabel(only)} já é a lançada."
                : "A folha lançada já é a oficial.");
        }

        var today = BusinessTime.Today(clock);
        var from = new DateOnly(today.Year, today.Month, 1);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var type in types)
        {
            await ChangeModeAsync(type, false, from, reason, [], null, null, cancellationToken);
        }

        var message = types.Contains(PayrollParallelType.Monthly)
            ? "Folha lançada é a oficial."
            : $"Folha lançada é a oficial para {string.Join(", ", types.Select(PayrollParallelRules.TypeLabel))}.";
        await NotifyOwnersAsync($"{message} Motivo: {(reason.Length > 120 ? reason[..119] + "…" : reason)}", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PayrollModeResult(message, await OverviewAsync(cancellationToken));
    }

    /// <summary>
    /// Registra a mudança do modo (CA 5 a 7): a linha em <c>payroll_mode_changes</c>, o parâmetro do modo e o evento; as folhas calculadas em
    /// rascunho do tipo, dali em diante, deixam (ou voltam a ser) simulação.
    /// </summary>
    private async Task ChangeModeAsync(PayrollParallelType type, bool calculated, DateOnly from, string? reason, List<Guid> runIds, string? accountant, string? crc, CancellationToken ct)
    {
        var mode = calculated ? PayrollRunMode.Calculated : PayrollRunMode.Entered;
        db.PayrollModeChanges.Add(new PayrollModeChange
        {
            RunType = type, Mode = mode, FromPeriod = from, ChangedAt = clock.GetUtcNow(), ChangedBy = currentUser.UserId, Reason = reason, ParallelRunIds = runIds,
            AccountantName = accountant, AccountantCrc = crc,
        });
        await parameters.SetModeAsync(type, calculated, from, ct);
        var drafts = await db.PayrollRuns.Where(r => r.Mode == PayrollRunMode.Calculated && r.Status == PayrollRunStatus.Draft && (!calculated || r.Period >= from)).ToListAsync(ct);
        foreach (var draft in drafts.Where(d => (PayrollParallelRules.TypeOf(d.Type) ?? PayrollParallelType.Monthly) == type))
        {
            draft.IsSimulation = !calculated;
        }

        events.Publish(new PayrollModeChanged(type.ToString(), mode.ToString(), from));
    }

    private async Task NotifyOwnersAsync(string message, CancellationToken ct)
    {
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, NotificationType, message, Route);
        }
    }

    /// <summary>
    /// Caso de correção anonimizado do funcionário (CA 2 e 9): o retrato da calculada sem o que identifica, a versão do motor, as tabelas e as
    /// rubricas com diferença (com a memória). Nome, CPF e matrícula nunca entram; o nascimento do dependente vira a idade.
    /// </summary>
    private async Task<string> CorrectionCaseAsync(PayrollParallelRun run, Guid employeeId, CancellationToken ct)
    {
        var payslip = await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == run.CalculatedRunId && p.EmployeeId == employeeId)
            .Select(p => new { p.InputsSnapshot, p.CalculatorVersion, p.TaxTableIds }).FirstOrDefaultAsync(ct);
        var snapshot = PayrollCalculator.ReadSnapshot(payslip?.InputsSnapshot);
        var memory = await MemoryAsync(run.CalculatedRunId, ct);
        var lines = (await db.PayrollParallelDifferences.AsNoTracking().Where(d => d.ParallelRunId == run.Id && d.EmployeeId == employeeId && d.Difference != 0m).ToListAsync(ct))
            .OrderBy(d => d.PayrollItemCode, StringComparer.Ordinal)
            .Select(d => new PayrollCorrectionLine(d.PayrollItemCode, d.ItemName, d.Kind, d.EnteredAmount, d.CalculatedAmount, d.Difference, memory.GetValueOrDefault((employeeId, d.PayrollItemCode)) ?? []))
            .ToList();
        return PayrollParallelRules.WriteCase(new PayrollCorrectionCase(1, PayrollParallelRules.Month(run.Period), payslip?.CalculatorVersion, payslip?.TaxTableIds ?? [],
            snapshot is null ? null : PayrollParallelRules.Anonymize(snapshot, run.Period), lines));
    }

    private async Task<PayrollParallelTermTotals> TotalsAsync(Guid runId, CancellationToken ct)
    {
        var slips = await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == runId).Select(p => new { p.Gross, p.Deductions, p.Net, p.FgtsAmount }).ToListAsync(ct);
        return new PayrollParallelTermTotals(slips.Sum(s => s.Gross), slips.Sum(s => s.Deductions), slips.Sum(s => s.Net), slips.Sum(s => s.FgtsAmount));
    }

    private static DateOnly Max(DateOnly a, DateOnly? b) => b is { } x && x > a ? x : a;
}
