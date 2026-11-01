using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.HR.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>Como o parâmetro é editado na tela: percentual, dia do mês, número de dias ou um terceiro (o órgão da guia).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HrParameterKind>))]
public enum HrParameterKind
{
    Percent,
    DayOfMonth,
    Days,
    Party,

    /// <summary>O n-ésimo dia útil do mês seguinte (RH-02c).</summary>
    BusinessDay,

    /// <summary>Dia do mês da competência (adiantamento).</summary>
    DayOfPeriod,

    /// <summary>Dia e mês do ano ("30/11"), gravado como <c>MMDD</c> (1130).</summary>
    MonthDay,

    /// <summary>Número de meses.</summary>
    Months,

    /// <summary>Conta do plano de contas, pelo código ("2.2").</summary>
    Account,

    /// <summary>Centro de custo, pelo código ("3").</summary>
    CostCenter,

    /// <summary>Layout do pacote para a contadora (RH-03, P-06), pelo código ("v1").</summary>
    ExportLayout,

    /// <summary>Geolocalização do ponto (RH-01a CA 9): <c>Off</c> ou <c>Ask</c>.</summary>
    Geolocation,

    /// <summary>Minutos (tolerância do ponto, RH-01c CA 1).</summary>
    Minutes,

    /// <summary>Ligado (1) ou desligado (0) (banco de horas, RH-01c CA 1).</summary>
    Toggle,

    /// <summary>Uma das opções da lista (<see cref="HrParameterItem.Options"/>), gravada como número (folha calculada, FLH-02a).</summary>
    Choice,

    /// <summary>Horas (divisor do valor da hora, FLH-02a; 0 = as horas mensais do cargo).</summary>
    Hours,

    /// <summary>Anos (idade do salário-família, FLH-02a).</summary>
    Years,

    /// <summary>Hora do dia, de 0 a 23 (início e fim do horário noturno, FLH-02b).</summary>
    HourOfDay,
    /// <summary>Quantidade (faltas da faixa das férias, partes do fracionamento, FLH-03).</summary>
    Count,
}

/// <summary>Parâmetros do espelho de ponto (RH-01c CA 1), lidos de uma vez; fora dos limites, o padrão.</summary>
public sealed record TimesheetParameters(int TolerancePerEntryMinutes, int TolerancePerDayMinutes, bool HourBankEnabled, int PeriodStartDay);

/// <summary>
/// Parâmetro do RH na tela de Configurações → Parâmetros → RH (RH-02a CA 3): o valor (número ou órgão), se é legal (chip "A confirmar
/// com a contadora" e "Marcar como conferido"), quem conferiu e quando, e o id para o histórico.
/// </summary>
public sealed record HrParameterItem(
    string Key,
    string Label,
    string Description,
    HrParameterKind Kind,
    decimal? Value,
    decimal? Min,
    decimal? Max,
    Guid? PartyId,
    string? PartyName,
    bool Legal,
    bool Confirmed,
    string? ConfirmedByName,
    DateTimeOffset? ConfirmedAt,
    Guid? ParameterId,
    string? Code = null,
    string? CodeName = null,
    IReadOnlyList<HrChoiceOption>? Options = null);

/// <summary>Opção de um parâmetro de escolha (FLH-02a): o número gravado e o rótulo.</summary>
public sealed record HrChoiceOption(decimal Value, string Label);

/// <summary>Opção de conta ou de centro de custo para os parâmetros da folha (RH-02c CA 4).</summary>
public sealed record HrCodeOption(string Code, string Name);

/// <summary>Os parâmetros, quantos valores legais estão "A confirmar com a contadora" e as contas e os centros para escolher.</summary>
public sealed record HrParametersView(
    IReadOnlyList<HrParameterItem> Items, int PendingCount, IReadOnlyList<HrCodeOption>? Accounts = null, IReadOnlyList<HrCodeOption>? CostCenters = null, IReadOnlyList<HrCodeOption>? ExportLayouts = null, IReadOnlyList<HrCodeOption>? GeolocationModes = null);

/// <summary>Uma troca: número em <c>Value</c>, o órgão em <c>PartyId</c> (nulo limpa o órgão padrão) ou o código da conta ou do centro em <c>Code</c>.</summary>
public sealed record HrParameterChange(string Key, decimal? Value, Guid? PartyId, string? Code = null);

public sealed record HrParametersInput(IReadOnlyList<HrParameterChange> Changes);

public sealed record HrParameterConfirmInput(string Key);

/// <summary>Terceiro para escolher como órgão da guia.</summary>
public sealed record PayrollPayeeOption(Guid Id, string Name, string? Document);

/// <summary>Padrão de uma guia na folha (CA 7): o vencimento proposto (já antecipado do dia não útil) e o órgão.</summary>
public sealed record PayrollChargeDefault(PayrollChargeKind Kind, string Label, DateOnly? DueDate, bool DueDateConfirmed, Guid? PayeePartyId, string? PayeeName);

/// <summary>
/// Parâmetros do RH (<c>hr.parameters</c>, dono HR, ADR 0016 M6; RH-02a CA 3, ADR 0042): o limite do alerta de variação do líquido
/// (10%), o vencimento padrão das guias do FGTS, do INSS e do IRRF (dia 20 do mês seguinte, o dia não útil antecipa pelo
/// <see cref="IBusinessCalendar"/>), o órgão favorecido padrão de cada guia (vazio até alguém escolher) e a quarentena da conta trocada
/// (30 dias, CAD-04c2). Os vencimentos são legais: "A confirmar com a contadora" até "Marcar como conferido". Editar e conferir exigem
/// <c>settings.edit</c>; toda mudança é auditada com antes e depois.
/// </summary>
public sealed partial class HrParametersService(
    IHrDb db, IParties parties, IBusinessCalendar calendar, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, BemAqui.Finance.Contracts.IChartOfAccounts chart,
    TimeProvider clock)
{
    public const string NetVariationAlertPct = "hr.payroll_net_variation_alert_pct";
    public const string BankAccountQuarantineDays = "hr.bank_account_quarantine_days";

    // RH-02c: vencimentos dos salários, folha prevista e conta e centro de custo dos títulos da folha ("A confirmar com a contadora").
    public const string SalaryDueBusinessDay = "hr.salary_due_business_day";
    public const string AdvanceDueDay = "hr.advance_due_day";
    public const string ThirteenthFirstDue = "hr.thirteenth_first_due";
    public const string ThirteenthSecondDue = "hr.thirteenth_second_due";
    public const string ForecastMonths = "hr.payroll_forecast_months";
    public const string AccountDirect = "hr.payroll_account_direct";
    public const string CostCenterDirect = "hr.payroll_cost_center_direct";
    public const string AccountAdmin = "hr.payroll_account_admin";
    public const string CostCenterAdmin = "hr.payroll_cost_center_admin";

    /// <summary>Layout do pacote para a contadora (RH-03): padrão <c>v1</c>, "A confirmar com a contadora" (ADR 0042).</summary>
    public const string AccountantExportLayout = "hr.accountant_export_layout";

    /// <summary>Geolocalização do ponto (RH-01a CA 9): <c>Off</c> (padrão) ou <c>Ask</c>; editada com <c>settings.edit</c>, auditada.</summary>
    public const string TimeClockGeolocationKey = "hr.time_clock_geolocation";

    public const decimal DefaultAlertPct = 10m;
    public const int DefaultDueDay = 20;

    /// <summary>Guias com vencimento padrão (as demais, contribuição sindical e "Outros", têm o vencimento informado na folha).</summary>
    public static readonly PayrollChargeKind[] DueDayCharges = [PayrollChargeKind.Fgts, PayrollChargeKind.Inss, PayrollChargeKind.Irrf];

    public static string DueDayKey(PayrollChargeKind kind) => "hr.charge_due_day." + kind.ToString().ToLowerInvariant();

    public static string PayeeKey(PayrollChargeKind kind) => "hr.charge_payee." + kind.ToString().ToLowerInvariant();

    /// <summary>Definição de cada chave: rótulo, descrição, tipo, padrão, limites e se depende da contadora.</summary>
    public sealed record Definition(string Key, string Label, string Description, HrParameterKind Kind, decimal? Default, decimal? Min, decimal? Max, bool Legal, string? DefaultCode = null, IReadOnlyList<HrChoiceOption>? Options = null);

    public static IReadOnlyList<Definition> Definitions { get; } =
    [
        new(NetVariationAlertPct, "Alerta de variação do líquido", "Na conferência da folha, o líquido do funcionário que variar mais do que este percentual contra o mesmo tipo de folha do mês anterior ganha o chip de alerta.",
            HrParameterKind.Percent, DefaultAlertPct, 0m, 100m, false),
        new(DueDayKey(PayrollChargeKind.Fgts), "Vencimento da guia do FGTS", "Dia do mês seguinte à competência. Se cair em dia não útil do calendário padrão, o vencimento antecipa.",
            HrParameterKind.DayOfMonth, DefaultDueDay, 1m, 31m, true),
        new(DueDayKey(PayrollChargeKind.Inss), "Vencimento da guia do INSS", "Dia do mês seguinte à competência. Se cair em dia não útil do calendário padrão, o vencimento antecipa.",
            HrParameterKind.DayOfMonth, DefaultDueDay, 1m, 31m, true),
        new(DueDayKey(PayrollChargeKind.Irrf), "Vencimento da guia do IRRF retido", "Dia do mês seguinte à competência. Se cair em dia não útil do calendário padrão, o vencimento antecipa.",
            HrParameterKind.DayOfMonth, DefaultDueDay, 1m, 31m, true),
        new(PayeeKey(PayrollChargeKind.Fgts), "Órgão da guia do FGTS", "Terceiro que recebe a guia (o título a pagar sai no nome dele). Normalmente a Caixa Econômica Federal.", HrParameterKind.Party, null, null, null, false),
        new(PayeeKey(PayrollChargeKind.Inss), "Órgão da guia do INSS", "Terceiro que recebe a guia. Normalmente a Receita Federal.", HrParameterKind.Party, null, null, null, false),
        new(PayeeKey(PayrollChargeKind.Irrf), "Órgão da guia do IRRF retido", "Terceiro que recebe a guia. Normalmente a Receita Federal.", HrParameterKind.Party, null, null, null, false),
        new(PayeeKey(PayrollChargeKind.Union), "Órgão da contribuição sindical", "Terceiro que recebe a contribuição. O vencimento é informado na folha.", HrParameterKind.Party, null, null, null, false),
        new(PayeeKey(PayrollChargeKind.Other), "Órgão das outras guias", "Terceiro padrão das guias \"Outros\". O vencimento é informado na folha.", HrParameterKind.Party, null, null, null, false),
        new(BankAccountQuarantineDays, "Quarentena da conta trocada", "Dias em que a conta para pagamento trocada fica fora do pagamento automático da folha, até a revisão expressa (CAD-04c2).",
            HrParameterKind.Days, EmployeeBankRules.QuarantineDays, 1m, 90m, false),
        new(SalaryDueBusinessDay, "Pagamento dos salários", "Dia útil do mês seguinte à competência em que a folha Mensal é paga (o vencimento dos títulos de salário).",
            HrParameterKind.BusinessDay, 5m, 1m, 20m, true),
        new(AdvanceDueDay, "Pagamento do adiantamento", "Dia da própria competência em que o adiantamento é pago. Se cair em dia não útil, antecipa.",
            HrParameterKind.DayOfPeriod, 20m, 1m, 31m, true),
        new(ThirteenthFirstDue, "13º salário, 1ª parcela", "Data limite da 1ª parcela do 13º no ano da competência. Se cair em dia não útil, antecipa.",
            HrParameterKind.MonthDay, 1130m, 101m, 1231m, true),
        new(ThirteenthSecondDue, "13º salário, 2ª parcela", "Data limite da 2ª parcela do 13º no ano da competência. Se cair em dia não útil, antecipa.",
            HrParameterKind.MonthDay, 1220m, 101m, 1231m, true),
        new(ForecastMonths, "Meses da folha prevista", "Quantos meses depois da última folha Mensal fechada a folha prevista entra no fluxo de caixa, com o 13º e as férias.",
            HrParameterKind.Months, PayrollForecastRules.DefaultMonths, 1m, PayrollForecastRules.MaxMonths, false),
        // FLH-06: quantas competências seguidas aceitas pela contadora antes da virada para a folha calculada (no mínimo 3, ADR 0039 §5).
        new(ParallelMonthsKey, "Competências da conferência paralela", "Quantas competências seguidas a folha calculada precisa bater com a da contadora (diferença zero, aceita e assinada) antes de o Proprietário poder virar para a folha calculada. O mínimo são 3.",
            HrParameterKind.Months, PayrollParallelRules.DefaultMonths, PayrollParallelRules.DefaultMonths, PayrollParallelRules.MaxMonths, false),
        new(AccountDirect, "Conta da mão de obra direta", "Conta dos salários de quem tem cargo com centro de trabalho (produção).", HrParameterKind.Account, null, null, null, true, "2.2"),
        new(CostCenterDirect, "Centro de custo da mão de obra direta", "Centro de custo dos salários de quem tem cargo com centro de trabalho.", HrParameterKind.CostCenter, null, null, null, true, "3"),
        new(AccountAdmin, "Conta do pessoal administrativo", "Conta dos salários dos demais cargos.", HrParameterKind.Account, null, null, null, true, "3.1"),
        new(CostCenterAdmin, "Centro de custo do pessoal administrativo", "Centro de custo dos salários dos demais cargos.", HrParameterKind.CostCenter, null, null, null, true, "1"),
        new(AccountantExportLayout, "Layout do pacote para a contadora", "Arquivos e colunas do pacote mensal que o RH gera para a contadora (RH → Exportar para a contadora). O padrão do sistema é o v1, descrito na ajuda; confira as colunas com a contadora.",
            HrParameterKind.ExportLayout, null, null, null, true, AccountantLayouts.Default),
        new(TimeClockGeolocationKey, "Localização no ponto", "Pedir a localização do celular ou do computador ao marcar o ponto, como indício de onde a marcação foi feita. O funcionário vê antes para que serve e pode negar; sem localização, a marcação entra do mesmo jeito. O tablet registrado da fábrica nunca pede. Só quem tem permissão de ver dados sensíveis do RH vê a localização.",
            HrParameterKind.Geolocation, null, null, null, false, nameof(TimeClockGeolocation.Off)),
        new(TolerancePerEntry, "Tolerância por marcação", "Variação de horário, para mais ou para menos, que não conta como extra nem atraso em cada marcação (entrada e saída), contra a jornada. A CLT (art. 58 §1) fala em 5 minutos; a convenção coletiva pode mudar.",
            HrParameterKind.Minutes, 5m, 0m, 30m, true),
        new(TolerancePerDay, "Tolerância por dia", "Limite da variação no dia inteiro: dentro dela, o dia conta como Normal; acima, conta a diferença inteira. A CLT fala em 10 minutos por dia.",
            HrParameterKind.Minutes, 10m, 0m, 60m, true),
        new(HourBankEnabled, "Banco de horas", "Com o banco ligado, a diferença do dia vai para o saldo de quem tem acordo de banco de horas (a partir da data do acordo), sem vencimento automático. Sem acordo, extras e faltas saem como totais do período para a folha.",
            HrParameterKind.Toggle, 1m, 0m, 1m, true),
        new(TimesheetPeriodStartDay, "Início do período de apuração", "Dia do mês em que começa o período do ponto. 1 é o mês civil; 21, por exemplo, faz o corte de 21 a 20. Mudar vale para os períodos em aberto, nunca para os fechados.",
            HrParameterKind.DayOfMonth, 1m, 1m, 28m, true),

        // FLH-02a: regras da folha calculada, pré-configuradas com o valor usual da legislação (PO 21/10/2026), "A confirmar com a contadora".
        new(SalaryProration, "Salário proporcional", "Como o salário do mês de admissão ou desligamento é proporcional: pelo mês comercial de 30 dias (o usual) ou pelos dias do mês.",
            HrParameterKind.Choice, 1m, 1m, 2m, true, Options: [new(1m, "Mês comercial de 30 dias"), new(2m, "Dias do mês")]),
        new(SickLeaveCompanyDays, "Dias de afastamento pagos pela empresa", "No afastamento por doença ou acidente, a empresa paga os primeiros dias; do dia seguinte em diante, quem paga é a previdência (o usual são 15 dias).",
            HrParameterKind.Days, 15m, 0m, 30m, true),
        new(AccidentKeepsFgts, "FGTS no afastamento por acidente", "No afastamento por acidente de trabalho, o FGTS continua sobre a remuneração que seria devida (Lei 8.036/1990, art. 15 §5º).",
            HrParameterKind.Toggle, 1m, 0m, 1m, true),
        new(InsalubrityBase, "Base da insalubridade", "Sobre o que incide o percentual da insalubridade (10, 20 ou 40%): o salário mínimo da tabela da folha (o usual) ou o salário base, se a convenção coletiva pedir.",
            HrParameterKind.Choice, 1m, 1m, 2m, true, Options: [new(1m, "Salário mínimo"), new(2m, "Salário base")]),
        new(HazardPayPct, "Percentual da periculosidade", "Percentual sobre o salário base do adicional de periculosidade (CLT art. 193: 30%).",
            HrParameterKind.Percent, 30m, 0m, 100m, true),
        new(AllowancesAccumulate, "Insalubridade e periculosidade acumulam", "Desligado (o usual, CLT art. 193 §2º): quem tem os dois recebe só o de maior valor no mês. Ligue só se a convenção ou a decisão judicial mandar acumular.",
            HrParameterKind.Toggle, 0m, 0m, 1m, true),
        new(HourValueDivisor, "Horas do valor da hora", "Divisor do valor da hora nas faltas e nos atrasos: 0 usa as horas mensais da jornada do cargo (ex.: 220); outro número vale para todos.",
            HrParameterKind.Hours, 0m, 0m, 300m, true),
        new(FamilyAllowanceMaxAge, "Idade do salário-família", "Até que idade o filho dá direito à cota do salário-família (o usual são 14 anos).",
            HrParameterKind.Years, 14m, 0m, 24m, true),
        new(IrrfReductionIncome, "Rendimento da redução do IRRF", "Que rendimento entra na faixa e na fórmula da redução adicional do IRRF: o rendimento tributável bruto do mês, como nos exemplos da Receita Federal, ou a base de cálculo depois das deduções.",
            HrParameterKind.Choice, 1m, 1m, 2m, true, Options: [new(1m, "Rendimento tributável bruto (exemplos da Receita)"), new(2m, "Base de cálculo, depois das deduções")]),

        // FLH-02b: horas extras, adicional noturno e DSR, pré-configurados com o mínimo da CLT (PO 21/10/2026); a convenção coletiva do
        // sindicato pode pedir mais: "A confirmar com a contadora".
        new(OvertimeWorkdayPct, "Horas extras em dia útil", "Adicional sobre o valor da hora nas extras de dia útil. A CLT (art. 59 §1º e Constituição, art. 7º XVI) garante no mínimo 50%; a convenção coletiva pode pedir mais.",
            HrParameterKind.Percent, 50m, 50m, 300m, true),
        new(OvertimeRestDayPct, "Horas extras em domingo ou folga", "Adicional nas extras de domingo e de dia sem jornada (folga), sem folga compensatória na semana. O usual é 100% (Lei 605/1949, art. 9º, e Súmula 146 do TST); confira a convenção coletiva.",
            HrParameterKind.Percent, 100m, 50m, 300m, true),
        new(OvertimeHolidayPct, "Horas extras em feriado", "Adicional nas extras de feriado (pelo calendário padrão), sem folga compensatória. O usual é 100% (Lei 605/1949, art. 9º, e Súmula 146 do TST); confira a convenção coletiva.",
            HrParameterKind.Percent, 100m, 50m, 300m, true),
        new(OvertimeIncludesAllowances, "Adicionais no valor da hora extra", "Ligado (o usual): o valor da hora das extras, do adicional noturno e do banco pago leva a insalubridade e a periculosidade do mês (Súmulas 132 e 139 do TST).",
            HrParameterKind.Toggle, 1m, 0m, 1m, true),
        new(NightShiftEnabled, "Adicional noturno", "Ligue se alguém trabalha no horário noturno. Desligado, nenhuma linha de adicional noturno sai (a jornada da Konebras é diurna).",
            HrParameterKind.Toggle, 0m, 0m, 1m, true),
        new(NightShiftPct, "Percentual do adicional noturno", "Adicional sobre o valor da hora no horário noturno. A CLT (art. 73) garante no mínimo 20%; a convenção coletiva pode pedir mais.",
            HrParameterKind.Percent, 20m, 20m, 100m, true),
        new(NightShiftStart, "Início do horário noturno", "Hora em que começa o horário noturno do trabalhador urbano (CLT art. 73 §2º: 22 h).",
            HrParameterKind.HourOfDay, 22m, 0m, 23m, true),
        new(NightShiftEnd, "Fim do horário noturno", "Hora em que termina o horário noturno do trabalhador urbano (CLT art. 73 §2º: 5 h).",
            HrParameterKind.HourOfDay, 5m, 0m, 23m, true),
        new(NightShiftReducedHour, "Hora noturna reduzida", "Ligado (o usual, CLT art. 73 §1º): cada 52 minutos e 30 segundos no horário noturno contam como uma hora.",
            HrParameterKind.Toggle, 1m, 0m, 1m, true),
        new(DsrFormula, "Fórmula do DSR sobre variáveis", "Como o descanso semanal remunerado sobre as horas extras e o adicional noturno é calculado (Lei 605/1949 e Súmula 172 do TST): variáveis ÷ dias úteis × (domingos + feriados).",
            HrParameterKind.Choice, 1m, 1m, 2m, true, Options: [new(1m, "Dias úteis = dias do mês − domingos − feriados (o sábado é útil)"), new(2m, "Dias úteis pelo calendário padrão da empresa")]),
        new(DsrLossOnAbsence, "Perda do DSR por falta", "Ligado (o usual, Lei 605/1949, art. 6º): a falta injustificada (sem abono aprovado) desconta o repouso da semana, um dia de salário por semana.",
            HrParameterKind.Toggle, 1m, 0m, 1m, true),
        // FLH-03: férias, pré-configuradas pela CLT (PO 21/10/2026, revisão pré-Fases 4 a 8, pontos 16 a 19), "A confirmar com a contadora".
        new(VacationAbsenceKey(30), "Férias de 30 dias: faltas até", "Faltas injustificadas no período aquisitivo que ainda dão 30 dias de férias (CLT art. 130: até 5).",
            HrParameterKind.Count, 5m, 0m, 366m, true),
        new(VacationAbsenceKey(24), "Férias de 24 dias: faltas até", "Acima da faixa anterior e até este número de faltas, 24 dias de férias (CLT: de 6 a 14).",
            HrParameterKind.Count, 14m, 0m, 366m, true),
        new(VacationAbsenceKey(18), "Férias de 18 dias: faltas até", "Acima da faixa anterior e até este número de faltas, 18 dias de férias (CLT: de 15 a 23).",
            HrParameterKind.Count, 23m, 0m, 366m, true),
        new(VacationAbsenceKey(12), "Férias de 12 dias: faltas até", "Acima da faixa anterior e até este número de faltas, 12 dias de férias (CLT: de 24 a 32). Acima, o período não dá férias.",
            HrParameterKind.Count, 32m, 0m, 366m, true),
        new(VacationInssLeaveDays, "Afastamento pela previdência que reinicia o período", "Dias de afastamento pela previdência (doença ou acidente, depois dos dias pagos pela empresa), contínuos ou não, acima dos quais o período aquisitivo se encerra e um novo começa no retorno (CLT art. 133: 6 meses).",
            HrParameterKind.Days, 180m, 1m, 366m, true),
        new(VacationPaidLeaveDays, "Licença remunerada que reinicia o período", "Dias de licença remunerada (afastamento por falta justificada) acima dos quais o período aquisitivo se encerra e um novo começa no retorno (CLT art. 133: 30 dias).",
            HrParameterKind.Days, 30m, 1m, 366m, true),
        new(VacationSplitParts, "Fracionamento: períodos", "Em quantos períodos as férias de um período aquisitivo podem ser divididas (CLT art. 134 §1: até 3).",
            HrParameterKind.Count, 3m, 1m, 3m, true),
        new(VacationSplitFirstDays, "Fracionamento: o maior período", "Dias mínimos de um dos períodos (CLT: 14 dias).", HrParameterKind.Days, 14m, 1m, 30m, true),
        new(VacationSplitOtherDays, "Fracionamento: os demais períodos", "Dias mínimos de cada um dos outros períodos (CLT: 5 dias).", HrParameterKind.Days, 5m, 1m, 30m, true),
        new(VacationNoticeDays, "Aviso de férias", "Dias de antecedência do aviso de férias ao funcionário (CLT art. 135: 30 dias).", HrParameterKind.Days, 30m, 0m, 90m, true),
        new(VacationPaymentDaysBefore, "Pagamento das férias", "Dias antes do início do gozo em que a folha de férias vence (CLT art. 145: 2 dias). Se cair em dia não útil, antecipa.",
            HrParameterKind.Days, 2m, 0m, 30m, true),
        new(VacationAverages, "Média das variáveis nas férias", "Meses do período aquisitivo cuja média dos proventos variáveis (horas extras, prêmios) entra na remuneração das férias.",
            HrParameterKind.Months, 12m, 1m, 12m, true),
        new(VacationTaxationKey, "Tributação das férias", "INSS sobre férias + 1/3 na competência do gozo (rateado quando atravessa o mês, junto com o salário), IRRF em separado no pagamento e o abono pecuniário e o seu 1/3 sem INSS, FGTS nem IRRF (pelas marcas das rubricas).",
            HrParameterKind.Choice, 1m, 1m, 2m, true, Options: [new(1m, "INSS rateado nas competências do gozo"), new(2m, "INSS inteiro na competência do início")]),
    ];

    // FLH-03: férias ("A confirmar com a contadora").
    public static string VacationAbsenceKey(int days) => "hr.vacation_absence_brackets." + days.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public const string VacationInssLeaveDays = "hr.vacation_leave_rules.inss_days";
    public const string VacationPaidLeaveDays = "hr.vacation_leave_rules.paid_leave_days";
    public const string VacationSplitParts = "hr.vacation_split.max_parts";
    public const string VacationSplitFirstDays = "hr.vacation_split.first_min_days";
    public const string VacationSplitOtherDays = "hr.vacation_split.other_min_days";
    public const string VacationNoticeDays = "hr.vacation_notice_days";
    public const string VacationPaymentDaysBefore = "hr.vacation_payment_days_before";
    public const string VacationAverages = "hr.vacation_averages";
    public const string VacationTaxationKey = "hr.vacation_taxation";

    private static readonly int[] VacationBracketDays = [30, 24, 18, 12];

    /// <summary>As chaves que as férias leem (a folha de férias guarda os ids delas).</summary>
    public static readonly string[] VacationKeys =
    [
        .. VacationBracketDays.Select(VacationAbsenceKey), VacationInssLeaveDays, VacationPaidLeaveDays, VacationSplitParts, VacationSplitFirstDays, VacationSplitOtherDays, VacationNoticeDays,
        VacationPaymentDaysBefore, VacationAverages, VacationTaxationKey,
    ];

    /// <summary>Parâmetros de férias (FLH-03) com os ids gravados; fora dos limites ou sem gravar, o pré-configurado pela CLT.</summary>
    public async Task<(VacationParameters Parameters, List<Guid> Ids)> VacationParametersAsync(CancellationToken cancellationToken = default)
    {
        var keys = VacationKeys.Append(SickLeaveCompanyDays).ToList();
        var stored = await db.HrParameters.AsNoTracking().Where(p => keys.Contains(p.Key)).ToListAsync(cancellationToken);
        int Number(string key)
        {
            var d = Definitions.First(x => x.Key == key);
            return stored.FirstOrDefault(p => p.Key == key)?.Value is { } v && v >= d.Min && v <= d.Max && decimal.Truncate(v) == v ? (int)v : (int)d.Default!.Value;
        }

        var parameters = new VacationParameters(
            VacationBracketDays.Select(days => new VacationAbsenceBracket(Number(VacationAbsenceKey(days)), days)).ToList(),
            Number(VacationInssLeaveDays), Number(VacationPaidLeaveDays), Number(SickLeaveCompanyDays), Number(VacationSplitParts), Number(VacationSplitFirstDays), Number(VacationSplitOtherDays),
            Number(VacationNoticeDays), Number(VacationPaymentDaysBefore), Number(VacationAverages), Number(VacationTaxationKey) == 2 ? VacationTaxation.StartMonth : VacationTaxation.SplitByMonth);
        return (parameters, stored.Where(p => VacationKeys.Contains(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Id).ToList());
    }


    // FLH-02b: horas extras, adicional noturno e DSR ("A confirmar com a contadora").
    public const string OvertimeWorkdayPct = "hr.overtime_rates.weekday";
    public const string OvertimeRestDayPct = "hr.overtime_rates.rest_day";
    public const string OvertimeHolidayPct = "hr.overtime_rates.holiday";
    public const string OvertimeIncludesAllowances = "hr.overtime_base_includes_allowances";
    public const string NightShiftEnabled = "hr.night_shift.enabled";
    public const string NightShiftPct = "hr.night_shift.pct";
    public const string NightShiftStart = "hr.night_shift.start";
    public const string NightShiftEnd = "hr.night_shift.end";
    public const string NightShiftReducedHour = "hr.night_shift.reduced_hour";
    public const string DsrFormula = "hr.dsr_formula";
    public const string DsrLossOnAbsence = "hr.dsr_loss_on_absence";

    /// <summary>Os percentuais das extras (sem eles, o cálculo da folha não roda).</summary>
    public static readonly string[] OvertimeRateKeys = [OvertimeWorkdayPct, OvertimeRestDayPct, OvertimeHolidayPct];

    private static readonly string[] HourKeys = [.. OvertimeRateKeys, OvertimeIncludesAllowances, NightShiftEnabled, NightShiftPct, NightShiftStart, NightShiftEnd, NightShiftReducedHour, DsrFormula, DsrLossOnAbsence];

    // FLH-02a: folha calculada ("A confirmar com a contadora").
    public const string SalaryProration = "hr.salary_proration";
    public const string SickLeaveCompanyDays = "hr.sick_leave_company_days";
    public const string AccidentKeepsFgts = "hr.accident_keeps_fgts";
    public const string InsalubrityBase = "hr.insalubrity_base";
    public const string HazardPayPct = "hr.hazard_pay_pct";
    public const string AllowancesAccumulate = "hr.allowances_accumulate";
    public const string HourValueDivisor = "hr.hour_value_divisor";
    public const string FamilyAllowanceMaxAge = "hr.family_allowance_max_age";
    public const string IrrfReductionIncome = "hr.irrf_reduction_income";

    /// <summary>As chaves que o motor da folha calculada lê (o holerite guarda os ids delas).</summary>
    public static readonly string[] CalculationKeys = [SalaryProration, SickLeaveCompanyDays, AccidentKeepsFgts, InsalubrityBase, HazardPayPct, AllowancesAccumulate, HourValueDivisor, FamilyAllowanceMaxAge, IrrfReductionIncome, .. HourKeys];

    /// <summary>
    /// Virada da folha calculada (FLH-06, ADR 0039 §5): enquanto não for 1, a calculada é só simulação. Não aparece em Configurações; quem
    /// grava é a virada da FLH-06 (auditada).
    /// </summary>
    public const string CalculatedOfficialKey = "hr.payroll_calculated_official";

    /// <summary>Parâmetros da folha calculada (FLH-02a), com os ids gravados; fora dos limites ou sem gravar, o pré-configurado.</summary>
    public async Task<(PayrollCalcParameters Parameters, List<Guid> Ids)> CalculationParametersAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.HrParameters.AsNoTracking().Where(p => CalculationKeys.Contains(p.Key)).ToListAsync(cancellationToken);
        decimal Number(string key)
        {
            var d = Definitions.First(x => x.Key == key);
            return stored.FirstOrDefault(p => p.Key == key)?.Value is { } v && v >= d.Min && v <= d.Max ? v : d.Default!.Value;
        }

        var parameters = new PayrollCalcParameters(
            Number(SalaryProration) == 2m ? Domain.SalaryProration.CalendarDays : Domain.SalaryProration.CommercialMonth,
            (int)Number(SickLeaveCompanyDays),
            Number(AccidentKeepsFgts) == 1m,
            Number(InsalubrityBase) == 2m ? Domain.InsalubrityBase.BaseSalary : Domain.InsalubrityBase.MinimumWage,
            Number(HazardPayPct),
            Number(AllowancesAccumulate) == 1m,
            Number(HourValueDivisor),
            (int)Number(FamilyAllowanceMaxAge),
            Number(IrrfReductionIncome) == 2m ? Domain.IrrfReductionIncome.CalculationBase : Domain.IrrfReductionIncome.GrossTaxable,
            new PayrollHourParameters(Number(OvertimeWorkdayPct), Number(OvertimeRestDayPct), Number(OvertimeHolidayPct), Number(OvertimeIncludesAllowances) == 1m,
                Number(NightShiftEnabled) == 1m, Number(NightShiftPct), (int)Number(NightShiftStart), (int)Number(NightShiftEnd), Number(NightShiftReducedHour) == 1m,
                Number(DsrFormula) == 2m ? Domain.DsrFormula.CompanyCalendar : Domain.DsrFormula.SundaysAndHolidays, Number(DsrLossOnAbsence) == 1m));
        return (parameters, stored.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Id).ToList());
    }

    /// <summary>
    /// FLH-02b (Estados): os percentuais das horas extras apagados (sem a linha gravada ou sem valor) bloqueiam o cálculo, em vez de cair
    /// num padrão que ninguém conferiu.
    /// </summary>
    public async Task<bool> OvertimeRatesMissingAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.HrParameters.AsNoTracking().Where(p => OvertimeRateKeys.Contains(p.Key) && p.Value != null).Select(p => p.Key).ToListAsync(cancellationToken);
        return OvertimeRateKeys.Any(k => !stored.Contains(k));
    }

    /// <summary>A virada foi feita (FLH-06): a folha calculada deixa de ser simulação.</summary>
    public async Task<bool> CalculatedOfficialAsync(CancellationToken cancellationToken = default) =>
        await NumberAsync(CalculatedOfficialKey, cancellationToken) == 1m;

    // RH-01c: espelho de ponto e banco de horas ("A confirmar com a contadora").
    public const string TolerancePerEntry = "hr.time_tolerance_per_entry_minutes";
    public const string TolerancePerDay = "hr.time_tolerance_per_day_minutes";
    public const string HourBankEnabled = "hr.hour_bank_enabled";
    public const string TimesheetPeriodStartDay = "hr.timesheet_period_start_day";

    /// <summary>Parâmetros do espelho (RH-01c CA 1): 5 e 10 minutos, banco ligado e o mês civil, salvo o que o tenant mudou.</summary>
    public async Task<TimesheetParameters> TimesheetParametersAsync(CancellationToken cancellationToken = default)
    {
        string[] keys = [TolerancePerEntry, TolerancePerDay, HourBankEnabled, TimesheetPeriodStartDay];
        var stored = await db.HrParameters.AsNoTracking().Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, p => p.Value, cancellationToken);
        int Number(string key, int fallback, int min, int max) => stored.GetValueOrDefault(key) is { } v && v >= min && v <= max && decimal.Truncate(v) == v ? (int)v : fallback;
        return new TimesheetParameters(Number(TolerancePerEntry, 5, 0, 30), Number(TolerancePerDay, 10, 0, 60), Number(HourBankEnabled, 1, 0, 1) == 1, Number(TimesheetPeriodStartDay, 1, 1, 28));
    }

    private static readonly HrParameterKind[] CodeKinds = [HrParameterKind.Account, HrParameterKind.CostCenter, HrParameterKind.ExportLayout, HrParameterKind.Geolocation];

    /// <summary>Vencimentos da folha (RH-02c CA 1); fora do intervalo, o padrão.</summary>
    public async Task<PayrollDueParameters> DueParametersAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.HrParameters.AsNoTracking().ToDictionaryAsync(p => p.Key, p => p.Value, cancellationToken);
        var d = PayrollDueParameters.Default;
        int Number(string key, int fallback, int min, int max) => stored.GetValueOrDefault(key) is { } v && v >= min && v <= max && decimal.Truncate(v) == v ? (int)v : fallback;
        var first = Number(ThirteenthFirstDue, 1130, 101, 1231);
        var second = Number(ThirteenthSecondDue, 1220, 101, 1231);
        return new PayrollDueParameters(Number(SalaryDueBusinessDay, d.SalaryBusinessDay, 1, 20), Number(AdvanceDueDay, d.AdvanceDay, 1, 31),
            ValidMonthDay(first) ? first / 100 : 11, ValidMonthDay(first) ? first % 100 : 30, ValidMonthDay(second) ? second / 100 : 12, ValidMonthDay(second) ? second % 100 : 20);
    }

    /// <summary>Meses da folha prevista (CA 14): de 1 a 12, padrão 3.</summary>
    public async Task<int> ForecastMonthsAsync(CancellationToken cancellationToken = default) =>
        await NumberAsync(ForecastMonths, cancellationToken) is { } months and >= 1m and <= PayrollForecastRules.MaxMonths ? (int)months : PayrollForecastRules.DefaultMonths;

    /// <summary>Código da conta e do centro de custo do grupo (CA 4), pelos parâmetros ou o padrão do seed.</summary>
    public async Task<(string Account, string CostCenter)> CostCodesAsync(PayrollCostGroup group, CancellationToken cancellationToken = default)
    {
        var (accountKey, centerKey) = group == PayrollCostGroup.Direct ? (AccountDirect, CostCenterDirect) : (AccountAdmin, CostCenterAdmin);
        var stored = await db.HrParameters.AsNoTracking().Where(p => p.Key == accountKey || p.Key == centerKey).ToDictionaryAsync(p => p.Key, p => p.Text, cancellationToken);
        return (stored.GetValueOrDefault(accountKey) ?? Definitions.First(d => d.Key == accountKey).DefaultCode!, stored.GetValueOrDefault(centerKey) ?? Definitions.First(d => d.Key == centerKey).DefaultCode!);
    }

    private static List<HrCodeOption> ExportLayoutOptions() => AccountantLayouts.Available.Select(l => new HrCodeOption(l.Code, l.Name)).ToList();

    private static List<HrCodeOption> GeolocationOptions() => [new(nameof(TimeClockGeolocation.Off), "Desligada"), new(nameof(TimeClockGeolocation.Ask), "Pedir a localização ao marcar")];

    /// <summary>Geolocalização do ponto (RH-01a CA 9); valor desconhecido vale como desligada.</summary>
    public async Task<TimeClockGeolocation> TimeClockGeolocationAsync(CancellationToken cancellationToken = default) =>
        await db.HrParameters.AsNoTracking().Where(p => p.Key == TimeClockGeolocationKey).Select(p => p.Text).FirstOrDefaultAsync(cancellationToken) == nameof(TimeClockGeolocation.Ask)
            ? TimeClockGeolocation.Ask : TimeClockGeolocation.Off;

    private static bool ValidMonthDay(decimal value)
    {
        var v = (int)value;
        return value == decimal.Truncate(value) && v / 100 is >= 1 and <= 12 && v % 100 >= 1 && v % 100 <= DateTime.DaysInMonth(2026, v / 100);
    }

    /// <summary>Limite do alerta de variação do líquido em % (CA 11); fora do intervalo, o padrão.</summary>
    public async Task<decimal> NetVariationAlertPctAsync(CancellationToken cancellationToken = default) =>
        await NumberAsync(NetVariationAlertPct, cancellationToken) is { } pct and >= 0m and <= 100m ? pct : DefaultAlertPct;

    /// <summary>Dias da quarentena da conta trocada (CAD-04c2 CA 7, ADR 0036 §5); fora de 1 a 90, os 30 do padrão.</summary>
    public async Task<int> QuarantineDaysAsync(CancellationToken cancellationToken = default) =>
        await NumberAsync(BankAccountQuarantineDays, cancellationToken) is { } days and >= 1m and <= 90m ? (int)days : EmployeeBankRules.QuarantineDays;

    /// <summary>Padrões das cinco guias para a competência (CA 7): vencimento (já antecipado do dia não útil) e órgão.</summary>
    public async Task<IReadOnlyList<PayrollChargeDefault>> ChargeDefaultsAsync(DateOnly period, CancellationToken cancellationToken = default)
    {
        var stored = await db.HrParameters.AsNoTracking().ToDictionaryAsync(p => p.Key, cancellationToken);
        var payeeIds = Enum.GetValues<PayrollChargeKind>().Select(k => stored.GetValueOrDefault(PayeeKey(k))?.Text).Select(t => Guid.TryParse(t, out var id) ? id : (Guid?)null).OfType<Guid>().ToList();
        var names = (await parties.FindManyAsync(payeeIds, cancellationToken)).Where(p => p.IsActive && !p.IsDeleted).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var result = new List<PayrollChargeDefault>();
        foreach (var kind in Enum.GetValues<PayrollChargeKind>())
        {
            DateOnly? due = null;
            var confirmed = false;
            if (DueDayCharges.Contains(kind))
            {
                var p = stored.GetValueOrDefault(DueDayKey(kind));
                var day = p?.Value is { } v and >= 1m and <= 31m ? (int)v : DefaultDueDay;
                due = await calendar.PreviousBusinessDayAsync(PayrollRules.DueDateNextMonth(period, day), cancellationToken);
                confirmed = p?.ConfirmedAt is not null;
            }

            Guid? payee = Guid.TryParse(stored.GetValueOrDefault(PayeeKey(kind))?.Text, out var id) && names.ContainsKey(id) ? id : null;
            result.Add(new PayrollChargeDefault(kind, PayrollRules.ChargeLabel(kind), due, confirmed, payee, payee is { } pid ? names[pid] : null));
        }

        return result;
    }

    public async Task<HrParametersView> GetAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.HrParameters.AsNoTracking().ToDictionaryAsync(p => p.Key, cancellationToken);
        var partyIds = stored.Values.Select(p => Guid.TryParse(p.Text, out var id) ? id : (Guid?)null).OfType<Guid>().ToList();
        var names = (await parties.FindManyAsync(partyIds, cancellationToken)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var reviewers = new Dictionary<Guid, string?>();
        foreach (var by in stored.Values.Select(p => p.ConfirmedBy).OfType<Guid>().Distinct())
        {
            reviewers[by] = await users.DisplayNameAsync(by, cancellationToken);
        }

        var accounts = (await chart.PostableAsync(BemAqui.Finance.Contracts.AccountType.Expense, cancellationToken)).Select(a => new HrCodeOption(a.Code, a.Name)).ToList();
        var centers = (await chart.CostCentersAsync(cancellationToken)).Select(c => new HrCodeOption(c.Code, c.Name)).ToList();
        var exportLayouts = ExportLayoutOptions();
        var geolocation = GeolocationOptions();
        var items = Definitions.Select(d =>
        {
            var p = stored.GetValueOrDefault(d.Key);
            Guid? party = d.Kind == HrParameterKind.Party && Guid.TryParse(p?.Text, out var id) ? id : null;
            var code = CodeKinds.Contains(d.Kind) ? p?.Text ?? d.DefaultCode : null;
            var codeName = code is null ? null : (d.Kind switch { HrParameterKind.Account => accounts, HrParameterKind.CostCenter => centers, HrParameterKind.Geolocation => geolocation, _ => exportLayouts }).FirstOrDefault(o => o.Code == code)?.Name;
            return new HrParameterItem(d.Key, d.Label, d.Description, d.Kind, d.Kind == HrParameterKind.Party || CodeKinds.Contains(d.Kind) ? null : p?.Value ?? d.Default, d.Min, d.Max, party,
                party is { } pid ? names.GetValueOrDefault(pid) : null, d.Legal, d.Legal && p?.ConfirmedAt is not null,
                p?.ConfirmedBy is { } by ? reviewers.GetValueOrDefault(by) : null, d.Legal ? p?.ConfirmedAt : null, p?.Id, code, codeName, d.Options);
        }).ToList();
        return new HrParametersView(items, items.Count(i => i.Legal && !i.Confirmed), accounts, centers, exportLayouts, geolocation);
    }

    /// <summary>Só o que veio muda; o valor legal trocado volta para "A confirmar com a contadora" (ADR 0042 §1).</summary>
    public async Task<Result<HrParametersView>> UpdateAsync(HrParametersInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        var changes = input.Changes ?? [];
        foreach (var change in changes)
        {
            var definition = Definitions.FirstOrDefault(d => d.Key == change.Key);
            if (definition is null)
            {
                return Error.NotFound("parameter_not_found", "Parâmetro não encontrado.");
            }

            if (definition.Kind == HrParameterKind.Party)
            {
                if (change.PartyId is { } partyId && await parties.FindAsync(partyId, cancellationToken) is not { IsActive: true, IsDeleted: false })
                {
                    return PayrollRules.Field(change.Key, "Escolha um terceiro ativo como órgão da guia.");
                }
            }
            else if (definition.Kind == HrParameterKind.Account)
            {
                if ((await chart.PostableAsync(BemAqui.Finance.Contracts.AccountType.Expense, cancellationToken)).All(a => a.Code != change.Code))
                {
                    return PayrollRules.Field(change.Key, "Escolha uma conta de despesa analítica ativa.");
                }
            }
            else if (definition.Kind == HrParameterKind.Geolocation)
            {
                if (GeolocationOptions().All(o => o.Code != change.Code))
                {
                    return PayrollRules.Field(change.Key, "Escolha desligada ou pedir a localização ao marcar.");
                }
            }
            else if (definition.Kind == HrParameterKind.ExportLayout)
            {
                if (ExportLayoutOptions().All(l => l.Code != change.Code))
                {
                    return PayrollRules.Field(change.Key, "Escolha um dos layouts disponíveis.");
                }
            }
            else if (definition.Kind == HrParameterKind.CostCenter)
            {
                if ((await chart.CostCentersAsync(cancellationToken)).All(c => c.Code != change.Code))
                {
                    return PayrollRules.Field(change.Key, "Escolha um centro de custo ativo.");
                }
            }
            else if (change.Value is not { } value || value < definition.Min || value > definition.Max
                || (definition.Kind == HrParameterKind.Percent ? decimal.Round(value, 2) != value : decimal.Truncate(value) != value)
                || (definition.Kind == HrParameterKind.MonthDay && !ValidMonthDay(value))
                || (definition.Options is { } options && options.All(o => o.Value != value)))
            {
                return PayrollRules.Field(change.Key, definition.Kind switch
                {
                    HrParameterKind.Percent => $"Informe um percentual de {definition.Min:0} a {definition.Max:0}, com até 2 casas.",
                    HrParameterKind.HourOfDay => "Informe uma hora de 0 a 23.",
                    HrParameterKind.DayOfMonth or HrParameterKind.DayOfPeriod => $"Informe um dia de {definition.Min:0} a {definition.Max:0}.",
                    HrParameterKind.Minutes => $"Informe de {definition.Min:0} a {definition.Max:0} minutos.",
                    HrParameterKind.Toggle => "Escolha ligado ou desligado.",
                    HrParameterKind.Choice => "Escolha uma das opções.",
                    HrParameterKind.Hours => $"Informe de {definition.Min:0} a {definition.Max:0} horas (0 usa as horas mensais do cargo).",
                    HrParameterKind.Years => $"Informe de {definition.Min:0} a {definition.Max:0} anos.",
                    HrParameterKind.Count => $"Informe um número inteiro de {definition.Min:0} a {definition.Max:0}.",
                    HrParameterKind.BusinessDay => $"Informe um dia útil de {definition.Min:0} a {definition.Max:0}.",
                    HrParameterKind.MonthDay => "Informe uma data válida no formato dia/mês (ex.: 30/11).",
                    HrParameterKind.Months => $"Informe de {definition.Min:0} a {definition.Max:0} meses.",
                    _ => $"Informe de {definition.Min:0} a {definition.Max:0} dias.",
                });
            }
        }

        var stored = await db.HrParameters.ToDictionaryAsync(p => p.Key, cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var change in changes)
        {
            var definition = Definitions.First(d => d.Key == change.Key);
            var text = definition.Kind == HrParameterKind.Party ? change.PartyId?.ToString() : CodeKinds.Contains(definition.Kind) ? change.Code : null;
            var value = definition.Kind == HrParameterKind.Party || CodeKinds.Contains(definition.Kind) ? null : change.Value;
            if (!stored.TryGetValue(change.Key, out var p))
            {
                db.HrParameters.Add(new HrParameter { Key = change.Key, Value = value, Text = text, UpdatedAt = now });
            }
            else if (p.Value != value || p.Text != text)
            {
                p.Value = value;
                p.Text = text;
                p.UpdatedAt = now;
                p.ResetConfirmation();
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>"Marcar como conferido" (ADR 0042 §1): só nos valores legais; grava quem e quando, auditado.</summary>
    public async Task<Result<HrParametersView>> ConfirmAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        var definition = Definitions.FirstOrDefault(d => d.Key == key && d.Legal);
        if (definition is null)
        {
            return Error.NotFound("parameter_not_found", "Parâmetro não encontrado.");
        }

        var parameter = await db.HrParameters.FirstOrDefaultAsync(p => p.Key == key, cancellationToken);
        if (parameter is null)
        {
            // Tenant sem o seed ainda: grava o valor proposto e já conferido.
            parameter = new HrParameter { Key = key, Value = definition.Default, Text = definition.DefaultCode, UpdatedAt = clock.GetUtcNow() };
            db.HrParameters.Add(parameter);
        }

        if (parameter.ConfirmedAt is null)
        {
            parameter.Confirm(currentUser.UserId, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetAsync(cancellationToken);
    }

    /// <summary>
    /// Órgãos para escolher como favorecido da guia (CA 3 e 7): terceiros ativos pela busca do <c>IParties</c>, sem exigir a permissão de
    /// terceiros de quem lança a folha.
    /// </summary>
    public async Task<IReadOnlyList<PayrollPayeeOption>> PayeesAsync(string? term, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(term, null, 20, cancellationToken)).Select(p => new PayrollPayeeOption(p.Id, p.TradeName ?? p.LegalName, p.Document)).ToList();

    /// <summary>Seed (também nos tenants existentes): grava os números que faltam com o padrão, os legais "A confirmar com a contadora".</summary>
    public static async Task SeedAsync(IHrDb db, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        // RH-01c CA 17: o layout padrão sobe para v2 (com o ponto.csv). Quem ainda está no v1 sem conferir passa para o v2; o v1 já conferido
        // com a contadora fica como está (troca só por "Configurações → Parâmetros → RH").
        var layout = await db.HrParameters.FirstOrDefaultAsync(p => p.Key == AccountantExportLayout, cancellationToken);
        if (layout is { Text: AccountantLayoutV1.Version, ConfirmedAt: null })
        {
            layout.Text = AccountantLayoutV2.Version;
            layout.UpdatedAt = clock.GetUtcNow();
        }

        var existing = (await db.HrParameters.Select(p => p.Key).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        foreach (var d in Definitions.Where(d => (d.Default is not null || d.DefaultCode is not null) && !existing.Contains(d.Key)))
        {
            db.HrParameters.Add(new HrParameter { Key = d.Key, Value = d.Default, Text = d.DefaultCode, UpdatedAt = clock.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<decimal?> NumberAsync(string key, CancellationToken ct) =>
        await db.HrParameters.AsNoTracking().Where(p => p.Key == key).Select(p => p.Value).FirstOrDefaultAsync(ct);
}
