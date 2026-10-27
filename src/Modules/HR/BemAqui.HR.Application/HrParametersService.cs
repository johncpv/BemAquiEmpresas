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
}

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
    string? CodeName = null);

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
public sealed class HrParametersService(
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
    public sealed record Definition(string Key, string Label, string Description, HrParameterKind Kind, decimal? Default, decimal? Min, decimal? Max, bool Legal, string? DefaultCode = null);

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
        new(AccountDirect, "Conta da mão de obra direta", "Conta dos salários de quem tem cargo com centro de trabalho (produção).", HrParameterKind.Account, null, null, null, true, "2.2"),
        new(CostCenterDirect, "Centro de custo da mão de obra direta", "Centro de custo dos salários de quem tem cargo com centro de trabalho.", HrParameterKind.CostCenter, null, null, null, true, "3"),
        new(AccountAdmin, "Conta do pessoal administrativo", "Conta dos salários dos demais cargos.", HrParameterKind.Account, null, null, null, true, "3.1"),
        new(CostCenterAdmin, "Centro de custo do pessoal administrativo", "Centro de custo dos salários dos demais cargos.", HrParameterKind.CostCenter, null, null, null, true, "1"),
        new(AccountantExportLayout, "Layout do pacote para a contadora", "Arquivos e colunas do pacote mensal que o RH gera para a contadora (RH → Exportar para a contadora). O padrão do sistema é o v1, descrito na ajuda; confira as colunas com a contadora.",
            HrParameterKind.ExportLayout, null, null, null, true, AccountantLayouts.Default),
        new(TimeClockGeolocationKey, "Localização no ponto", "Pedir a localização do celular ou do computador ao marcar o ponto, como indício de onde a marcação foi feita. O funcionário vê antes para que serve e pode negar; sem localização, a marcação entra do mesmo jeito. O tablet registrado da fábrica nunca pede. Só quem tem permissão de ver dados sensíveis do RH vê a localização.",
            HrParameterKind.Geolocation, null, null, null, false, nameof(TimeClockGeolocation.Off)),
    ];

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
                p?.ConfirmedBy is { } by ? reviewers.GetValueOrDefault(by) : null, d.Legal ? p?.ConfirmedAt : null, p?.Id, code, codeName);
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
                || (definition.Kind == HrParameterKind.MonthDay && !ValidMonthDay(value)))
            {
                return PayrollRules.Field(change.Key, definition.Kind switch
                {
                    HrParameterKind.Percent => "Informe um percentual de 0 a 100, com até 2 casas.",
                    HrParameterKind.DayOfMonth or HrParameterKind.DayOfPeriod => "Informe um dia de 1 a 31.",
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
