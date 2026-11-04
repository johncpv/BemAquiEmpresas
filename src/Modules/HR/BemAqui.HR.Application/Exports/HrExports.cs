using System.Globalization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.HR.Application.TimeClock;
using BemAqui.HR.Domain;

namespace BemAqui.HR.Application.Exports;

/// <summary>Funcionário exportado (EXP-05 CA 12): CPF e salário só saem completos com <c>hr.view_sensitive</c> (o spec mascara).</summary>
public sealed record EmployeeExportRow(
    string? Registration, string Name, string? Cpf, string Position, DateOnly Admission, string Situation, string? Schedule, string? Union, decimal? Salary);

/// <summary>Folha exportada (EXP-05 CA 13): os totais só com <c>hr.view_sensitive</c>, como a lista.</summary>
public sealed record PayrollRunExportRow(string Period, string Type, string Mode, string Situation, int Payslips, decimal? Gross, decimal? Deductions, decimal? Net);

/// <summary>Holerite da folha exportado (EXP-05 CA 13); a última linha é o total, igual ao da tela.</summary>
public sealed record PayslipExportRow(string? Registration, string Name, string? Position, decimal Gross, decimal Deductions, decimal Net, decimal Fgts, string Situation, bool IsTotal);

/// <summary>Cargo exportado (EXP-05 CA 15): salário base e custo-hora mascarados sem <c>hr.view_sensitive</c>.</summary>
public sealed record PositionExportRow(string Name, string? Cbo, string? WorkCenter, string? Schedule, decimal MonthlyHours, decimal? ReferenceSalary, decimal HourlyCost, int Employees, string Situation);

/// <summary>
/// Colunas e rótulos das exportações do RH (EXP-05, ADR 0045): os textos da tela, a máscara do CPF da RH-02b (<c>123.***.***-45</c>) e
/// <c>***</c> nos valores sem <c>hr.view_sensitive</c>. Nenhum nome de arquivo leva nome ou CPF de pessoa.
/// </summary>
public static class HrExports
{
    /// <summary>A ação que libera CPF, salário e valores por pessoa (a mesma da tela).</summary>
    public static readonly ExportSensitivity Sensitive = new("hr.view_sensitive");

    public const string Total = "Total";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string EmployeeStatusLabel(EmployeeStatus status) => status switch
    {
        EmployeeStatus.OnLeave => "Afastado",
        EmployeeStatus.Terminated => "Desligado",
        _ => "Ativo",
    };

    public static string RunTypeLabel(PayrollRunType type) => type switch
    {
        PayrollRunType.Advance => "Adiantamento",
        PayrollRunType.Vacation => "Férias",
        PayrollRunType.Thirteenth1 => "13º 1ª parcela",
        PayrollRunType.Thirteenth2 => "13º 2ª parcela",
        PayrollRunType.Termination => "Rescisão",
        _ => "Mensal",
    };

    /// <summary>Tipo no nome do arquivo, sem acento nem espaço (ex.: "13-1a-parcela").</summary>
    public static string RunTypeSlug(PayrollRunType type) => type switch
    {
        PayrollRunType.Advance => "adiantamento",
        PayrollRunType.Vacation => "ferias",
        PayrollRunType.Thirteenth1 => "13-1a-parcela",
        PayrollRunType.Thirteenth2 => "13-2a-parcela",
        PayrollRunType.Termination => "rescisao",
        _ => "mensal",
    };

    public static string RunStatusLabel(PayrollRunStatus status) => status switch
    {
        PayrollRunStatus.Closed => "Fechada",
        PayrollRunStatus.Paid => "Paga",
        _ => "Em lançamento",
    };

    /// <summary>"10/2026", como a competência na planilha.</summary>
    public static string Period(DateOnly period) => period.ToString("MM/yyyy", Invariant);

    /// <summary>"Afastado" na lista; com <c>hr.view_sensitive</c>, as datas; com <c>hr.view_health</c> também, o tipo e o motivo (CAD-04c1b).</summary>
    public static string Situation(EmployeeStatus status, (DateOnly Start, DateOnly End, AbsenceReason? Type, string? Reason)? leave, bool sensitive, bool health)
    {
        var label = EmployeeStatusLabel(status);
        if (status != EmployeeStatus.OnLeave || leave is not { } l || !sensitive)
        {
            return label;
        }

        var text = $"{label} de {l.Start.ToString("dd/MM/yyyy", Invariant)} a {l.End.ToString("dd/MM/yyyy", Invariant)}";
        if (health && EmployeeLeaveRules.ShowsDetails(health))
        {
            text += $" ({AbsenceReasons.Label(l.Type ?? AbsenceReason.JustifiedAbsence)}{(string.IsNullOrWhiteSpace(l.Reason) ? string.Empty : ": " + l.Reason)})";
        }

        return text;
    }

    public static readonly ExportSpec<EmployeeExportRow> Employees = new ExportSpec<EmployeeExportRow>("Employee", "funcionarios", "Funcionários")
        .Text("Matrícula", r => r.Registration)
        .Text("Nome", r => r.Name)
        .Text("CPF", r => r.Cpf is null ? null : BemAqui.SharedKernel.BrazilianDocuments.Format(r.Cpf), Sensitive, r => PayslipDocumentRules.MaskCpf(r.Cpf))
        .Text("Cargo", r => r.Position)
        .Date("Admissão", r => r.Admission)
        .Text("Situação", r => r.Situation)
        .Text("Jornada", r => r.Schedule)
        .Text("Sindicato", r => r.Union)
        .Money("Salário", r => r.Salary, Sensitive);

    public static readonly ExportSpec<PayrollRunExportRow> PayrollRuns = new ExportSpec<PayrollRunExportRow>("PayrollRun", "folhas", "Folhas")
        .Text("Competência", r => r.Period)
        .Text("Tipo", r => r.Type)
        .Text("Modo", r => r.Mode)
        .Text("Situação", r => r.Situation)
        .Quantity("Funcionários", r => r.Payslips)
        .Money("Proventos", r => r.Gross, Sensitive)
        .Money("Descontos", r => r.Deductions, Sensitive)
        .Money("Líquido", r => r.Net, Sensitive);

    /// <summary>Holerites de uma folha: a rota já exige <c>hr.view_sensitive</c>; o arquivo é <c>folha-{aaaa-mm}-{tipo}-{data}.xlsx</c>.</summary>
    public static ExportSpec<PayslipExportRow> Payslips(DateOnly period, PayrollRunType type) =>
        new ExportSpec<PayslipExportRow>("PayrollRun.Payslips", $"folha-{period.ToString("yyyy-MM", Invariant)}-{RunTypeSlug(type)}", $"Holerites: {RunTypeLabel(type)} {Period(period)}")
            .Text("Matrícula", r => r.Registration)
            .Text("Nome", r => r.Name)
            .Text("Cargo", r => r.Position)
            .Money("Proventos", r => r.Gross)
            .Money("Descontos", r => r.Deductions)
            .Money("Líquido", r => r.Net)
            .Money("FGTS", r => r.Fgts)
            .Text("Situação", r => r.Situation)
            .Style(r => r.IsTotal ? new ExportRowStyle(Bold: true) : null);

    /// <summary>Linhas dos holerites com o total no fim (proventos, descontos, líquido e FGTS somados, como o líquido da tela).</summary>
    public static IReadOnlyList<PayslipExportRow> PayslipRows(IReadOnlyList<PayslipExportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return [.. rows, new PayslipExportRow(null, Total, null, rows.Sum(r => r.Gross), rows.Sum(r => r.Deductions), rows.Sum(r => r.Net), rows.Sum(r => r.Fgts), string.Empty, true)];
    }

    /// <summary>Espelho do período (EXP-05 CA 14): as horas em número (decimal), como os totais da tela.</summary>
    public static ExportSpec<TimesheetOverviewRow> Timesheets(DateOnly period) =>
        new ExportSpec<TimesheetOverviewRow>("Timesheet", $"espelho-{period.ToString("yyyy-MM", Invariant)}", $"Espelho de ponto {Period(period)}")
            .Text("Matrícula", r => r.Registration)
            .Text("Nome", r => r.Name)
            .Quantity("Horas previstas", r => Hours(r.ExpectedMinutes))
            .Quantity("Horas trabalhadas", r => Hours(r.WorkedMinutes))
            .Quantity("Extras", r => Hours(r.OvertimeMinutes))
            .Quantity("Faltas", r => Hours(r.AbsenceMinutes))
            .Quantity("Atrasos", r => Hours(r.LateMinutes))
            .Quantity("Banco no mês", r => r.BankMonthMinutes is { } m ? Hours(m) : null)
            .Quantity("Banco de horas", r => r.BankBalanceMinutes is { } m ? Hours(m) : null)
            .Text("Funcionário confirmou", r => r.AckLabel).NotInPdf()
            .Text("Situação do espelho", r => r.SituationLabel);

    /// <summary>Minutos em horas com 2 casas (90 → 1,5).</summary>
    public static decimal Hours(int minutes) => Math.Round(minutes / 60m, 2);

    public static readonly ExportSpec<PositionExportRow> Positions = new ExportSpec<PositionExportRow>("JobPosition", "cargos", "Cargos")
        .Text("Nome", r => r.Name)
        .Text("CBO", r => r.Cbo is { Length: 6 } c ? $"{c[..4]}-{c[4..]}" : r.Cbo)
        .Text("Centro padrão", r => r.WorkCenter)
        .Text("Jornada", r => r.Schedule)
        .Quantity("Horas no mês", r => r.MonthlyHours)
        .Money("Salário base", r => r.ReferenceSalary, Sensitive)
        .Money("Custo-hora", r => r.HourlyCost, Sensitive)
        .Quantity("Funcionários", r => r.Employees)
        .Text("Situação", r => r.Situation);
}
