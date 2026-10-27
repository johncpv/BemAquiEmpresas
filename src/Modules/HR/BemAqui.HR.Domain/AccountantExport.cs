using System.Globalization;

namespace BemAqui.HR.Domain;

/// <summary>Evento da movimentação do mês (RH-03 CA 2).</summary>
public enum AccountantEventKind
{
    Admission,
    Termination,
    SalaryChange,
    PositionChange,
    Leave,
}

/// <summary>
/// Evento da competência já resolvido (sem banco): quem, o quê e quando. Só os campos do layout (minimização, CA 4): nada de endereço,
/// contato, conta, dependentes nem o motivo do afastamento.
/// </summary>
public sealed record AccountantEvent(
    AccountantEventKind Kind,
    string? Registration,
    string Name,
    string Cpf,
    DateOnly Date,
    DateOnly? BirthDate = null,
    string? Position = null,
    string? Cbo = null,
    decimal? Salary = null,
    string? Schedule = null,
    EmployeeLeaveType? LeaveType = null,
    DateOnly? LeaveEnd = null);

/// <summary>Linha do holerite da folha fechada (CA 3): a chave do funcionário, o tipo da folha e a linha como está na folha.</summary>
public sealed record AccountantPayslipLine(PayrollRunType RunType, string EmployeeKey, string Code, string Description, string? Reference, decimal Amount);

/// <summary>Guia da folha fechada (CA 3).</summary>
public sealed record AccountantCharge(PayrollRunType RunType, PayrollChargeKind Kind, decimal Amount, DateOnly DueDate);

/// <summary>
/// Layout padrão do sistema <c>v1</c> do pacote para a contadora (RH-03, P-06): os cabeçalhos e as linhas de <c>movimentacao.csv</c> e
/// <c>folha.csv</c>. Datas <c>dd/mm/aaaa</c>, decimal com vírgula e duas casas, sem separador de milhar; o separador <c>;</c>, o UTF-8 com BOM
/// e a proteção contra fórmula ficam no <c>SafeCsv</c>. Mudou coluna, sobe a versão (CA 7): o teste golden trava os cabeçalhos.
/// As colunas são "A confirmar com a contadora" (ADR 0042): se ela pedir outra coisa, entra uma versão nova ou um layout adicional.
/// </summary>
public static class AccountantLayoutV1
{
    public const string Version = "v1";
    public const string MovementsFile = "movimentacao.csv";
    public const string PayrollFile = "folha.csv";
    public const string ReadmeFile = "leia-me.txt";

    public static IReadOnlyList<string> MovementsHeader { get; } =
        ["evento", "matricula", "nome", "cpf", "nascimento", "cargo", "cbo", "salario", "jornada", "data", "tipo_afastamento", "fim_previsto"];

    public static IReadOnlyList<string> PayrollHeader { get; } =
        ["registro", "tipo_folha", "matricula_ou_cpf", "codigo_rubrica", "descricao_rubrica", "referencia", "valor", "tipo_guia", "vencimento"];

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Datas <c>dd/mm/aaaa</c>.</summary>
    public static string Date(DateOnly date) => date.ToString("dd'/'MM'/'yyyy", Invariant);

    /// <summary>Decimal com vírgula e duas casas, sem milhar ("3500,00").</summary>
    public static string Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", Invariant).Replace('.', ',');

    /// <summary>CPF com máscara (a planilha não come o zero da frente).</summary>
    public static string Cpf(string cpf)
    {
        var d = new string((cpf ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return d.Length == 11 ? $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}" : d;
    }

    /// <summary>CBO "9999-99".</summary>
    public static string? Cbo(string? cbo)
    {
        var d = new string((cbo ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return d.Length == 0 ? null : d.Length == 6 ? $"{d[..4]}-{d[4..]}" : d;
    }

    public static string EventCode(AccountantEventKind kind) => kind switch
    {
        AccountantEventKind.Admission => "admissao",
        AccountantEventKind.Termination => "desligamento",
        AccountantEventKind.SalaryChange => "reajuste",
        AccountantEventKind.PositionChange => "troca_cargo",
        _ => "afastamento",
    };

    public static string LeaveLabel(EmployeeLeaveType type) => type switch
    {
        EmployeeLeaveType.Illness => "Doença",
        EmployeeLeaveType.WorkAccident => "Acidente de trabalho",
        EmployeeLeaveType.Maternity => "Maternidade",
        EmployeeLeaveType.Paternity => "Paternidade",
        EmployeeLeaveType.Military => "Serviço militar",
        _ => "Outros",
    };

    /// <summary>
    /// Linhas de <c>movimentacao.csv</c> (CA 2), em ordem de data, evento e nome. Cada evento leva só as colunas dele: nascimento, cargo,
    /// CBO, salário e jornada na admissão; salário no reajuste; cargo e CBO na troca; tipo e fim previsto no afastamento. O tipo só sai
    /// quando veio (quem gera sem acesso ao dado de saúde recebe a coluna vazia).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string?>> MovementRows(IEnumerable<AccountantEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events.OrderBy(e => e.Date).ThenBy(e => e.Kind).ThenBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.Cpf, StringComparer.Ordinal)
            .Select(e => (IReadOnlyList<string?>)
            [
                EventCode(e.Kind),
                e.Registration,
                e.Name,
                Cpf(e.Cpf),
                e.Kind == AccountantEventKind.Admission && e.BirthDate is { } birth ? Date(birth) : null,
                e.Kind is AccountantEventKind.Admission or AccountantEventKind.PositionChange ? e.Position : null,
                e.Kind is AccountantEventKind.Admission or AccountantEventKind.PositionChange ? Cbo(e.Cbo) : null,
                e.Kind is AccountantEventKind.Admission or AccountantEventKind.SalaryChange && e.Salary is { } salary ? Money(salary) : null,
                e.Kind == AccountantEventKind.Admission ? e.Schedule : null,
                Date(e.Date),
                e.Kind == AccountantEventKind.Leave && e.LeaveType is { } type ? LeaveLabel(type) : null,
                e.Kind == AccountantEventKind.Leave && e.LeaveEnd is { } end ? Date(end) : null,
            ]).ToList();
    }

    /// <summary>
    /// Linhas de <c>folha.csv</c> (CA 3): uma por linha de holerite (<c>holerite</c>), na ordem em que vieram, e uma por guia (<c>guia</c>).
    /// A chave é a matrícula ou, sem ela, o CPF, como no layout de importação.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string?>> PayrollRows(IEnumerable<AccountantPayslipLine> lines, IEnumerable<AccountantCharge> charges)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(charges);
        var rows = new List<IReadOnlyList<string?>>();
        rows.AddRange(lines.Select(l => (IReadOnlyList<string?>)["holerite", PayrollRules.TypeLabel(l.RunType), l.EmployeeKey, l.Code, l.Description, l.Reference, Money(l.Amount), null, null]));
        rows.AddRange(charges.Select(c => (IReadOnlyList<string?>)["guia", PayrollRules.TypeLabel(c.RunType), null, null, null, null, Money(c.Amount), PayrollRules.ChargeLabel(c.Kind), Date(c.DueDate)]));
        return rows;
    }

    /// <summary>Chave do funcionário na folha: a matrícula, ou o CPF com máscara.</summary>
    public static string EmployeeKey(string? registration, string cpf) => string.IsNullOrWhiteSpace(registration) ? Cpf(cpf) : registration.Trim();

    /// <summary>Competência que pode ser exportada (CA 1): o mês atual ou um anterior.</summary>
    public static bool Exportable(DateOnly period, DateOnly today) => period <= new DateOnly(today.Year, today.Month, 1);

    /// <summary>Competência sugerida (propositivo): o mês anterior.</summary>
    public static DateOnly SuggestedPeriod(DateOnly today) => new DateOnly(today.Year, today.Month, 1).AddMonths(-1);

    /// <summary>Afastamento que cobre algum dia da competência (inclusive o que começou antes ou termina depois).</summary>
    public static bool LeaveInPeriod(DateOnly start, DateOnly? end, DateOnly period) => start <= PayrollRules.PeriodEnd(period) && (end ?? start) >= period;

    /// <summary>"dd/mm/aaaa hh:mm" (horário de Brasília, já convertido por quem chama).</summary>
    public static string Timestamp(DateTime local) => local.ToString("dd'/'MM'/'yyyy HH':'mm", Invariant);

    /// <summary>
    /// <c>leia-me.txt</c> (CA 1 e 6): a versão do layout (e se a contadora já conferiu), a competência, a data de geração, o que substitui e a
    /// lista do que entrou e do que não entrou. Nenhum valor nem nome de funcionário.
    /// </summary>
    public static string Readme(string layout, bool layoutConfirmed, DateOnly period, DateTime generatedLocal, DateTime? replacesLocal, IEnumerable<string> included, IEnumerable<string> notIncluded)
    {
        ArgumentNullException.ThrowIfNull(included);
        ArgumentNullException.ThrowIfNull(notIncluded);
        var text = new System.Text.StringBuilder();
        text.Append("Pacote do RH para a contadora\r\n\r\n");
        text.Append("Layout: padrão do sistema ").Append(layout).Append(layoutConfirmed ? " (conferido com a contadora)" : " (A confirmar com a contadora)").Append("\r\n");
        text.Append("Competência: ").Append(PayrollRules.PeriodLabel(period)).Append(" (").Append(period.ToString("MM'/'yyyy", Invariant)).Append(")\r\n");
        text.Append("Gerado em: ").Append(Timestamp(generatedLocal)).Append(" (horário de Brasília)\r\n");
        if (replacesLocal is { } replaces)
        {
            text.Append("Substitui a exportação de ").Append(Timestamp(replaces)).Append(".\r\n");
        }

        text.Append("\r\nEntrou no pacote:\r\n");
        foreach (var line in included)
        {
            text.Append("- ").Append(line).Append("\r\n");
        }

        text.Append("\r\nNão entrou no pacote:\r\n");
        var none = true;
        foreach (var line in notIncluded)
        {
            none = false;
            text.Append("- ").Append(line).Append("\r\n");
        }

        if (none)
        {
            text.Append("- Nada.\r\n");
        }

        text.Append("\r\nFormato dos arquivos: separador ponto e vírgula (;), UTF-8 com BOM, datas dd/mm/aaaa e decimal com vírgula (3500,00).\r\n");
        text.Append("Célula que começaria com =, +, - ou @ vem com um apóstrofo na frente, para a planilha não executar fórmula.\r\n");
        text.Append("As colunas de cada arquivo estão descritas na ajuda do sistema (RH > Exportar para a contadora).\r\n");
        return text.ToString();
    }
}

/// <summary>Layouts do pacote para a contadora disponíveis (P-06): o padrão <c>v1</c>; os adicionais entram aqui como plug-ins.</summary>
public static class AccountantLayouts
{
    public const string Default = AccountantLayoutV1.Version;

    public static IReadOnlyList<(string Code, string Name)> Available { get; } = [(AccountantLayoutV1.Version, "Layout padrão do sistema v1")];
}
