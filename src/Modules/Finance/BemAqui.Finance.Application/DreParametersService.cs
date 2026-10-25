using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>Conta do plano que um parâmetro do DRE aceita.</summary>
public sealed record DreAccountOption(Guid Id, string Code, string Name);

/// <summary>
/// Parâmetro do DRE em Configurações → Parâmetros → Financeiro (CA 16, ADR 0042): a conta (ou o sim/não do sinal), o selo
/// "A confirmar com a contadora" (<see cref="ReviewState"/>) e quem conferiu e quando.
/// </summary>
public sealed record DreParameterItem(
    string Key,
    string Label,
    string Description,
    Guid? AccountId,
    string? AccountCode,
    string? AccountName,
    bool? Enabled,
    ParameterReviewState ReviewState,
    string? ReviewedByName,
    DateTimeOffset? ReviewedAt,
    Guid? ParameterId,
    IReadOnlyList<DreAccountOption> Options);

public sealed record DreParametersView(IReadOnlyList<DreParameterItem> Items, int PendingCount);

/// <summary>Só o que veio é trocado; trocar o valor volta o selo para "A confirmar com a contadora".</summary>
public sealed record DreParametersInput(Guid? InterestIncomeAccountId, Guid? InterestExpenseAccountId, Guid? DiscountGrantedAccountId, Guid? DiscountReceivedAccountId, bool? DownPaymentAsAdvance);

/// <summary>
/// Parâmetros do DRE (FIN-08 CA 8 e 16), em <c>finance.parameters</c>: as quatro contas das baixas (juros e multa recebidos e pagos,
/// descontos concedidos e obtidos) e a regra do sinal da D4 ("sinal como adiantamento até a nota"). Tudo o que depende da
/// contadora nasce pré-configurado pelo seed, editável com <c>settings.edit</c>, com auditoria, e com o selo "A confirmar com a
/// contadora" até alguém marcar como conferido (ADR 0042); nada disso fica fixo no código.
/// </summary>
public sealed class DreParametersService(IFinanceDb db, IChartOfAccounts chart, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, TimeProvider clock)
{
    public const string InterestIncomeAccount = "finance.dre_interest_income_account_id";
    public const string InterestExpenseAccount = "finance.dre_interest_expense_account_id";
    public const string DiscountGrantedAccount = "finance.dre_discount_granted_account_id";
    public const string DiscountReceivedAccount = "finance.dre_discount_received_account_id";

    /// <summary>D4 (proposta levada à contadora): o sinal fica em "Adiantamentos de clientes" até a nota (no provisório, até a expedição).</summary>
    public const string DownPaymentAsAdvance = "finance.dre_down_payment_as_advance";

    /// <summary>Parâmetro de conta: a conta proposta (código do seed) e os grupos e tipos da DRE aceitos.</summary>
    public sealed record AccountRule(string Key, string Field, string Label, string Description, string DefaultCode, AccountType Type, DreGroup[] Groups);

    public static readonly IReadOnlyList<AccountRule> AccountRules =
    [
        new(InterestIncomeAccount, "interestIncomeAccountId", "Juros e multa recebidos",
            "Juros e multa das baixas a receber entram nesta conta, no mês da baixa.", "1.3", AccountType.Revenue, [DreGroup.FinancialResult, DreGroup.OtherResults]),
        new(InterestExpenseAccount, "interestExpenseAccountId", "Juros e multa pagos",
            "Juros e multa dos pagamentos das ordens entram nesta conta, no mês do pagamento.", "3.5", AccountType.Expense, [DreGroup.FinancialResult, DreGroup.OtherResults]),
        new(DiscountGrantedAccount, "discountGrantedAccountId", "Descontos concedidos",
            "Desconto dado ao cliente na baixa, inclusive \"Quitar o saldo com desconto\", no mês da baixa.", "3.5", AccountType.Expense, [DreGroup.FinancialResult, DreGroup.Deductions, DreGroup.OtherResults]),
        new(DiscountReceivedAccount, "discountReceivedAccountId", "Descontos obtidos",
            "Desconto que o fornecedor deu no pagamento da ordem, no mês do pagamento.", "1.3", AccountType.Revenue, [DreGroup.FinancialResult, DreGroup.OtherResults]),
    ];

    public const string DownPaymentLabel = "Sinal como adiantamento até a nota";
    public const string DownPaymentDescription =
        "Ligado, o sinal do contrato não entra na receita na assinatura: fica em \"Adiantamentos de clientes\" e entra na expedição do projeto (ou com o marco seguinte). Desligado, entra na receita na emissão do título, como as outras parcelas.";

    public async Task<DreSettlementAccounts> SettlementAccountsAsync(CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(cancellationToken);
        return new DreSettlementAccounts(resolved[InterestIncomeAccount] ?? Guid.Empty, resolved[InterestExpenseAccount] ?? Guid.Empty,
            resolved[DiscountGrantedAccount] ?? Guid.Empty, resolved[DiscountReceivedAccount] ?? Guid.Empty);
    }

    public async Task<bool> DownPaymentAsAdvanceAsync(CancellationToken cancellationToken = default) =>
        await db.FinanceParameters.AsNoTracking().Where(p => p.Key == DownPaymentAsAdvance).Select(p => (decimal?)p.Value).FirstOrDefaultAsync(cancellationToken) is not 0m;

    /// <summary>Quantos valores do DRE ainda estão "A confirmar com a contadora" (aviso na tela do DRE).</summary>
    public async Task<int> PendingCountAsync(CancellationToken cancellationToken = default)
    {
        var keys = Keys();
        var confirmed = await db.FinanceParameters.AsNoTracking().CountAsync(p => keys.Contains(p.Key) && p.ReviewState == ParameterReviewState.Confirmed, cancellationToken);
        return keys.Length - confirmed;
    }

    public async Task<DreParametersView> GetAsync(CancellationToken cancellationToken = default)
    {
        var keys = Keys();
        var stored = await db.FinanceParameters.AsNoTracking().Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, cancellationToken);
        var resolved = await ResolveAsync(cancellationToken);
        var postable = await chart.PostableAsync(null, cancellationToken);
        var ids = resolved.Values.OfType<Guid>().ToList();
        var accounts = (await chart.FindAsync(ids, cancellationToken)).ToDictionary(a => a.Id);
        var reviewers = new Dictionary<Guid, string?>();
        foreach (var by in stored.Values.Select(p => p.ReviewedBy).OfType<Guid>().Distinct())
        {
            reviewers[by] = await users.DisplayNameAsync(by, cancellationToken);
        }

        string? Reviewer(FinanceParameter? p) => p?.ReviewedBy is { } by ? reviewers.GetValueOrDefault(by) : null;
        var items = new List<DreParameterItem>();
        foreach (var rule in AccountRules)
        {
            var p = stored.GetValueOrDefault(rule.Key);
            var account = resolved[rule.Key] is { } id ? accounts.GetValueOrDefault(id) : null;
            var options = postable.Where(a => Accepts(rule, a)).OrderBy(a => TreeCode.SortKey(a.Code), StringComparer.Ordinal).Select(a => new DreAccountOption(a.Id, a.Code, a.Name)).ToList();
            items.Add(new DreParameterItem(rule.Key, rule.Label, rule.Description, account?.Id, account?.Code, account?.Name, null, p?.ReviewState ?? ParameterReviewState.ToConfirm,
                Reviewer(p), p?.ReviewedAt, p?.Id, options));
        }

        var down = stored.GetValueOrDefault(DownPaymentAsAdvance);
        items.Add(new DreParameterItem(DownPaymentAsAdvance, DownPaymentLabel, DownPaymentDescription, null, null, null, down is null || down.Value != 0, down?.ReviewState ?? ParameterReviewState.ToConfirm,
            Reviewer(down), down?.ReviewedAt, down?.Id, []));
        return new DreParametersView(items, items.Count(i => i.ReviewState != ParameterReviewState.Confirmed));
    }

    public async Task<Result<DreParametersView>> UpdateAsync(DreParametersInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        var postable = await chart.PostableAsync(null, cancellationToken);
        var wanted = new Dictionary<string, Guid?>
        {
            [InterestIncomeAccount] = input.InterestIncomeAccountId,
            [InterestExpenseAccount] = input.InterestExpenseAccountId,
            [DiscountGrantedAccount] = input.DiscountGrantedAccountId,
            [DiscountReceivedAccount] = input.DiscountReceivedAccountId,
        };
        foreach (var rule in AccountRules)
        {
            if (wanted[rule.Key] is { } id && !postable.Any(a => a.Id == id && Accepts(rule, a)))
            {
                var groups = string.Join(" ou ", rule.Groups.Select(GroupName));
                return ReceivableService.Invalid(rule.Field, $"Escolha uma conta analítica e ativa de {(rule.Type == AccountType.Revenue ? "receita" : "despesa")} do grupo {groups}.");
            }
        }

        var keys = Keys();
        var stored = await db.FinanceParameters.Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var rule in AccountRules)
        {
            if (wanted[rule.Key] is { } id)
            {
                var text = id.ToString();
                if (stored.TryGetValue(rule.Key, out var p))
                {
                    if (p.Text != text)
                    {
                        p.Text = text;
                        p.UpdatedAt = now;
                        p.ResetReview();
                    }
                }
                else
                {
                    var created = new FinanceParameter { Key = rule.Key, Text = text, UpdatedAt = now };
                    created.ResetReview();
                    db.FinanceParameters.Add(created);
                }
            }
        }

        if (input.DownPaymentAsAdvance is { } advance)
        {
            var value = advance ? 1m : 0m;
            if (stored.TryGetValue(DownPaymentAsAdvance, out var p))
            {
                if (p.Value != value)
                {
                    p.Value = value;
                    p.UpdatedAt = now;
                    p.ResetReview();
                }
            }
            else
            {
                var created = new FinanceParameter { Key = DownPaymentAsAdvance, Value = value, UpdatedAt = now };
                created.ResetReview();
                db.FinanceParameters.Add(created);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>"Marcar como conferido" (ADR 0042 §1): grava quem e quando, com auditoria; vale para o valor de agora.</summary>
    public async Task<Result<DreParametersView>> ConfirmAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (!Keys().Contains(key, StringComparer.Ordinal))
        {
            return Error.NotFound("parameter_not_found", "Parâmetro não encontrado.");
        }

        var parameter = await db.FinanceParameters.FirstOrDefaultAsync(p => p.Key == key, cancellationToken);
        if (parameter is null)
        {
            // Tenant sem o seed ainda: grava o valor proposto e já conferido.
            var resolved = await ResolveAsync(cancellationToken);
            parameter = new FinanceParameter { Key = key, Value = key == DownPaymentAsAdvance ? 1m : 0m, Text = resolved.GetValueOrDefault(key)?.ToString(), UpdatedAt = clock.GetUtcNow() };
            db.FinanceParameters.Add(parameter);
        }

        if (parameter.ReviewState != ParameterReviewState.Confirmed)
        {
            parameter.Confirm(currentUser.UserId, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetAsync(cancellationToken);
    }

    public static string[] Keys() => [.. AccountRules.Select(r => r.Key), DownPaymentAsAdvance];

    private static bool Accepts(AccountRule rule, AccountOption account) => account.AcceptsEntries && account.IsActive && account.Type == rule.Type && rule.Groups.Contains(account.DreGroup);

    private static string GroupName(DreGroup group) => DreBuilder.GroupLabel(group).TrimStart('(', '−', '±', ')', ' ');

    /// <summary>
    /// Conta de cada parâmetro: a gravada, se ainda existe no plano; senão a do código proposto (1.3 ou 3.5), para o DRE não perder
    /// juros e descontos num tenant em que o seed ainda não rodou ou a conta foi excluída.
    /// </summary>
    private async Task<Dictionary<string, Guid?>> ResolveAsync(CancellationToken ct)
    {
        var keys = AccountRules.Select(r => r.Key).ToArray();
        var texts = await db.FinanceParameters.AsNoTracking().Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, p => p.Text, ct);
        var accounts = await db.Accounts.AsNoTracking().Select(a => new { a.Id, a.Code, a.SeedKey }).ToListAsync(ct);
        var result = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        foreach (var rule in AccountRules)
        {
            Guid? id = Guid.TryParse(texts.GetValueOrDefault(rule.Key), out var parsed) && accounts.Any(a => a.Id == parsed) ? parsed : null;
            id ??= accounts.FirstOrDefault(a => a.SeedKey == rule.DefaultCode)?.Id ?? accounts.FirstOrDefault(a => a.Code == rule.DefaultCode)?.Id;
            result[rule.Key] = id;
        }

        return result;
    }

    /// <summary>Seed (também nos tenants existentes): grava o que falta com o valor proposto e o selo "A confirmar com a contadora".</summary>
    public static async Task SeedAsync(IFinanceDb db, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var keys = Keys();
        var existing = (await db.FinanceParameters.Where(p => keys.Contains(p.Key)).Select(p => p.Key).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var accounts = await db.Accounts.AsNoTracking().Select(a => new { a.Id, a.Code, a.SeedKey }).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var rule in AccountRules.Where(r => !existing.Contains(r.Key)))
        {
            var id = accounts.FirstOrDefault(a => a.SeedKey == rule.DefaultCode)?.Id ?? accounts.FirstOrDefault(a => a.Code == rule.DefaultCode)?.Id;
            var parameter = new FinanceParameter { Key = rule.Key, Text = id?.ToString(), UpdatedAt = now };
            parameter.ResetReview();
            db.FinanceParameters.Add(parameter);
        }

        if (!existing.Contains(DownPaymentAsAdvance))
        {
            var parameter = new FinanceParameter { Key = DownPaymentAsAdvance, Value = 1m, UpdatedAt = now };
            parameter.ResetReview();
            db.FinanceParameters.Add(parameter);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>"Marcar como conferido" de um parâmetro do DRE, pela chave.</summary>
public sealed record DreParameterConfirmInput(string Key);
