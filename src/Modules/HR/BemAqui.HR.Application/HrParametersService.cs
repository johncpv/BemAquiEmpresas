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
    Guid? ParameterId);

/// <summary>Os parâmetros e quantos valores legais estão "A confirmar com a contadora".</summary>
public sealed record HrParametersView(IReadOnlyList<HrParameterItem> Items, int PendingCount);

/// <summary>Uma troca: número em <c>Value</c> ou o órgão em <c>PartyId</c> (nulo limpa o órgão padrão).</summary>
public sealed record HrParameterChange(string Key, decimal? Value, Guid? PartyId);

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
public sealed class HrParametersService(IHrDb db, IParties parties, IBusinessCalendar calendar, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, TimeProvider clock)
{
    public const string NetVariationAlertPct = "hr.payroll_net_variation_alert_pct";
    public const string BankAccountQuarantineDays = "hr.bank_account_quarantine_days";

    public const decimal DefaultAlertPct = 10m;
    public const int DefaultDueDay = 20;

    /// <summary>Guias com vencimento padrão (as demais, contribuição sindical e "Outros", têm o vencimento informado na folha).</summary>
    public static readonly PayrollChargeKind[] DueDayCharges = [PayrollChargeKind.Fgts, PayrollChargeKind.Inss, PayrollChargeKind.Irrf];

    public static string DueDayKey(PayrollChargeKind kind) => "hr.charge_due_day." + kind.ToString().ToLowerInvariant();

    public static string PayeeKey(PayrollChargeKind kind) => "hr.charge_payee." + kind.ToString().ToLowerInvariant();

    /// <summary>Definição de cada chave: rótulo, descrição, tipo, padrão, limites e se depende da contadora.</summary>
    public sealed record Definition(string Key, string Label, string Description, HrParameterKind Kind, decimal? Default, decimal? Min, decimal? Max, bool Legal);

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
    ];

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

        var items = Definitions.Select(d =>
        {
            var p = stored.GetValueOrDefault(d.Key);
            Guid? party = d.Kind == HrParameterKind.Party && Guid.TryParse(p?.Text, out var id) ? id : null;
            return new HrParameterItem(d.Key, d.Label, d.Description, d.Kind, d.Kind == HrParameterKind.Party ? null : p?.Value ?? d.Default, d.Min, d.Max, party,
                party is { } pid ? names.GetValueOrDefault(pid) : null, d.Legal, d.Legal && p?.ConfirmedAt is not null,
                p?.ConfirmedBy is { } by ? reviewers.GetValueOrDefault(by) : null, d.Legal ? p?.ConfirmedAt : null, p?.Id);
        }).ToList();
        return new HrParametersView(items, items.Count(i => i.Legal && !i.Confirmed));
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
            else if (change.Value is not { } value || value < definition.Min || value > definition.Max
                || (definition.Kind == HrParameterKind.Percent ? decimal.Round(value, 2) != value : decimal.Truncate(value) != value))
            {
                return PayrollRules.Field(change.Key, definition.Kind switch
                {
                    HrParameterKind.Percent => "Informe um percentual de 0 a 100, com até 2 casas.",
                    HrParameterKind.DayOfMonth => "Informe um dia de 1 a 31.",
                    _ => $"Informe de {definition.Min:0} a {definition.Max:0} dias.",
                });
            }
        }

        var stored = await db.HrParameters.ToDictionaryAsync(p => p.Key, cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var change in changes)
        {
            var definition = Definitions.First(d => d.Key == change.Key);
            var text = definition.Kind == HrParameterKind.Party ? change.PartyId?.ToString() : null;
            var value = definition.Kind == HrParameterKind.Party ? null : change.Value;
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
            parameter = new HrParameter { Key = key, Value = definition.Default, UpdatedAt = clock.GetUtcNow() };
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
        foreach (var d in Definitions.Where(d => d.Default is not null && !existing.Contains(d.Key)))
        {
            db.HrParameters.Add(new HrParameter { Key = d.Key, Value = d.Default, UpdatedAt = clock.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<decimal?> NumberAsync(string key, CancellationToken ct) =>
        await db.HrParameters.AsNoTracking().Where(p => p.Key == key).Select(p => p.Value).FirstOrDefaultAsync(ct);
}
