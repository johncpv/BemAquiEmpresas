using System.Text.Json.Serialization;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>Decisão do RH sobre o banco de horas na folha (FLH-02b CA 2).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollHourBankAction>))]
public enum PayrollHourBankAction
{
    /// <summary>"Pagar {n} h do banco": sai do saldo positivo, como horas extras de dia útil.</summary>
    Pay,

    /// <summary>"Descontar {n} h do banco": zera o saldo negativo, pelo valor da hora.</summary>
    Discount,
}

/// <summary>"Pagar" ou "Descontar" horas do banco (CA 2): a ação, os minutos (maior que zero, até o saldo) e o motivo (5 a 200).</summary>
public sealed record PayrollHourBankInput(PayrollHourBankAction? Action, int? Minutes, string? Note);

/// <summary>
/// Um funcionário com banco de horas na folha (CA 2, Propositivo): o saldo do espelho fechado da competência, o do período, a decisão nesta
/// folha (minutos com o sinal e o motivo), o saldo depois da folha e a sugestão "Pagar {n} h do banco" quando o saldo passa de 40 h.
/// </summary>
public sealed record PayrollHourBankRow(
    Guid PayslipId,
    Guid EmployeeId,
    string EmployeeName,
    string? Registration,
    int BalanceMinutes,
    int MonthMinutes,
    int DecisionMinutes,
    string? DecisionNote,
    int BalanceAfterMinutes,
    int? SuggestedPayMinutes);

/// <summary>A seção "Banco de horas" da folha (CA 2): só pela rota <c>/sensitive</c>; <c>Editable</c> com a folha calculada em rascunho.</summary>
public sealed record PayrollHourBank(Guid RunId, bool Editable, IReadOnlyList<PayrollHourBankRow> Rows);

/// <summary>
/// Banco de horas na folha calculada (FLH-02b CA 2): o saldo não vence sozinho; só sai pela decisão do RH ("Pagar" ou "Descontar", com
/// motivo), que vira linha no cálculo e baixa o saldo do espelho no fechamento (<c>timesheets.bank_payout_minutes</c>, com o sinal). O
/// saldo restante na rescisão é da FLH-05.
/// </summary>
public sealed partial class PayrollCalculationService
{
    /// <summary>A seção do banco de horas: quem tem banco no espelho fechado da competência. Leitura sensível auditada.</summary>
    public async Task<Result<PayrollHourBank>> HourBankAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        var rows = await (from p in db.Payslips.AsNoTracking()
                          join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                          join t in db.Timesheets.AsNoTracking() on p.EmployeeId equals t.EmployeeId
                          where p.PayrollRunId == runId && t.Period == run.Period && t.Status == TimesheetStatus.Closed && t.BankMonthMinutes != null
                          orderby e.Name
                          select new { p.Id, p.EmployeeId, e.Name, e.Registration, Balance = t.BankBalanceMinutes ?? 0, Month = t.BankMonthMinutes ?? 0, p.HourBankDecisionMinutes, p.HourBankDecisionNote })
            .ToListAsync(cancellationToken);
        await audit.RecordReadAsync(nameof(PayrollRun) + ".HourBank", runId.ToString(), cancellationToken);
        var editable = run.Mode == PayrollRunMode.Calculated && run.Status == PayrollRunStatus.Draft && run.RectifiesRunId is null && run.Type == PayrollRunType.Monthly;
        return new PayrollHourBank(run.Id, editable, rows.Select(r => new PayrollHourBankRow(r.Id, r.EmployeeId, r.Name, r.Registration, r.Balance, r.Month, r.HourBankDecisionMinutes,
            r.HourBankDecisionNote, r.Balance - r.HourBankDecisionMinutes,
            r.HourBankDecisionMinutes == 0 && r.Balance > PayrollCalculationRules.HourBankSuggestionMinutes ? r.Balance : null)).ToList());
    }

    /// <summary>
    /// "Pagar {n} h do banco" ou "Descontar {n} h do banco" (CA 2): <c>hr.edit</c> (rota) + <c>hr.view_sensitive</c>; só com banco no
    /// espelho fechado; pagar até o saldo positivo, descontar até o negativo; motivo obrigatório. O holerite é recalculado na hora.
    /// </summary>
    public async Task<Result<PayslipDetail>> SetHourBankAsync(Guid runId, Guid payslipId, PayrollHourBankInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var target = await ManualTargetAsync(runId, payslipId, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error!;
        }

        var (run, slip) = target.Value;
        if (input.Action is not { } action || !Enum.IsDefined(action))
        {
            return PayrollRules.Field("action", "Escolha pagar ou descontar horas do banco.");
        }

        var sheet = await db.Timesheets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.EmployeeId == slip.EmployeeId && t.Period == run.Period && t.Status == TimesheetStatus.Closed && t.BankMonthMinutes != null, cancellationToken);
        if (sheet is null)
        {
            return Error.Validation(PayrollCalculationRules.HourBankInvalidCode, "Sem banco de horas neste mês: o funcionário não tem acordo de banco no espelho fechado.");
        }

        var balance = sheet.BankBalanceMinutes ?? 0;
        if (input.Minutes is not { } minutes || minutes <= 0)
        {
            return PayrollRules.Field("minutes", "Informe as horas, maior que zero.");
        }

        if (action == PayrollHourBankAction.Pay && minutes > Math.Max(balance, 0))
        {
            return PayrollRules.Field("minutes", balance > 0 ? $"Pague até {TimesheetRules.Duration(balance)}, o saldo positivo do banco." : "O saldo do banco não é positivo: não há horas para pagar.");
        }

        if (action == PayrollHourBankAction.Discount && minutes > Math.Max(-balance, 0))
        {
            return PayrollRules.Field("minutes", balance < 0 ? $"Desconte até {TimesheetRules.Duration(-balance)}, o saldo negativo do banco." : "O saldo do banco não é negativo: não há horas para descontar.");
        }

        var note = (input.Note ?? string.Empty).Trim();
        if (note.Length is < PayrollCalculationRules.NoteMin or > PayrollCalculationRules.NoteMax)
        {
            return PayrollRules.Field("note", "Informe o motivo, de 5 a 200 caracteres.");
        }

        var tracked = await db.Payslips.FirstAsync(p => p.Id == slip.Id, cancellationToken);
        tracked.HourBankDecisionMinutes = action == PayrollHourBankAction.Pay ? minutes : -minutes;
        tracked.HourBankDecisionNote = note;
        await db.SaveChangesAsync(cancellationToken);
        return await RecalculateOneAsync(run, slip.Id, cancellationToken);
    }

    /// <summary>Tirar a decisão do banco (CA 2): o saldo fica como estava e o holerite é recalculado sem a linha.</summary>
    public async Task<Result<PayslipDetail>> ClearHourBankAsync(Guid runId, Guid payslipId, CancellationToken cancellationToken = default)
    {
        var target = await ManualTargetAsync(runId, payslipId, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error!;
        }

        var tracked = await db.Payslips.FirstAsync(p => p.Id == payslipId, cancellationToken);
        tracked.HourBankDecisionMinutes = 0;
        tracked.HourBankDecisionNote = null;
        await db.SaveChangesAsync(cancellationToken);
        return await RecalculateOneAsync(target.Value.Run, payslipId, cancellationToken);
    }

    /// <summary>
    /// Fechamento (CA 2): a decisão de cada holerite baixa o saldo do espelho da competência (<c>bank_payout_minutes</c>) e corrige o saldo dos
    /// espelhos seguintes já fechados. Chamado na transação do fechamento, só na folha calculada.
    /// </summary>
    public async Task ApplyHourBankAsync(PayrollRun run, CancellationToken cancellationToken = default) => await MoveHourBankAsync(run, true, cancellationToken);

    /// <summary>Reabertura da folha (RH-02c CA 13): desfaz a baixa do banco feita no fechamento.</summary>
    public async Task RevertHourBankAsync(PayrollRun run, CancellationToken cancellationToken = default) => await MoveHourBankAsync(run, false, cancellationToken);

    private async Task MoveHourBankAsync(PayrollRun run, bool apply, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Mode != PayrollRunMode.Calculated || run.RectifiesRunId is not null)
        {
            return;
        }

        var decisions = await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == run.Id && p.HourBankDecisionMinutes != 0)
            .ToDictionaryAsync(p => p.EmployeeId, p => p.HourBankDecisionMinutes, ct);
        var employees = decisions.Keys.ToList();
        var sheets = await db.Timesheets.Where(t => employees.Contains(t.EmployeeId) && t.Period >= run.Period && t.Status == TimesheetStatus.Closed).ToListAsync(ct);
        foreach (var sheet in sheets)
        {
            var current = sheets.FirstOrDefault(s => s.EmployeeId == sheet.EmployeeId && s.Period == run.Period);
            var moved = apply ? decisions[sheet.EmployeeId] : current?.BankPayoutMinutes ?? 0;
            if (moved == 0 || sheet.Period == run.Period)
            {
                continue;
            }

            // Espelho seguinte já fechado: o saldo dele partiu do saldo antes da baixa.
            if (sheet.BankBalanceMinutes is { } later)
            {
                sheet.BankBalanceMinutes = apply ? later - moved : later + moved;
            }
        }

        foreach (var sheet in sheets.Where(s => s.Period == run.Period))
        {
            sheet.BankPayoutMinutes = apply ? decisions[sheet.EmployeeId] : 0;
        }
    }
}
