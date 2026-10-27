using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>Um valor conferível de Configurações → Cobrança (ADR 0042): o selo "A confirmar com a contadora" e quem conferiu e quando.</summary>
public sealed record BillingReviewItem(string Key, string Label, ParameterReviewState ReviewState, string? ReviewedByName, DateTimeOffset? ReviewedAt, Guid? ParameterId = null);

/// <summary>Configurações → Cobrança (BNK-01b CA 1 e 2): os valores, a conferência por chave e quantos faltam conferir.</summary>
public sealed record BillingSettingsView(
    decimal FinePct,
    decimal InterestMonthPct,
    decimal DefaultDiscountPct,
    bool AutoEmail,
    int DailyEmailLimit,
    OverdueFineFrom OverdueFineFrom,
    string NonBusinessDayRule,
    IReadOnlyList<BillingReviewItem> Reviews,
    int PendingCount,
    bool CanEdit);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BillingSettingsInput(decimal FinePct, decimal InterestMonthPct, decimal DefaultDiscountPct, bool AutoEmail, int DailyEmailLimit, OverdueFineFrom OverdueFineFrom);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BillingSettingConfirmInput(string Key);

/// <summary>
/// Parâmetros de cobrança em <c>finance.parameters</c> (BNK-01b, ADR 0042): multa 2,00%, juros 1,00% a.m., desconto padrão 0,00%,
/// envio automático do boleto, limite diário de 200 e-mails e a regra do vencido (multa e juros do vencimento original). Seed nos
/// tenants existentes sem sobrescrever; multa, juros e a regra do vencido nascem "A confirmar com a contadora". Editar e conferir com
/// <c>settings.edit</c>, auditado (antes e depois).
/// </summary>
public sealed class BillingSettingsService(IFinanceDb db, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, TimeProvider clock)
{
    public const string FinePct = "finance.late_fine_pct";
    public const string InterestMonthPct = "finance.late_interest_month_pct";
    public const string DefaultDiscountPct = "finance.default_discount_pct";
    public const string AutoEmail = "finance.billing_slip_auto_email";
    public const string DailyEmailLimit = "finance.daily_email_limit";
    public const string OverdueFineFromKey = "finance.overdue_fine_from";

    /// <summary>Regra fixa (sem edição, CA 1): o banco decide o dia não útil; o título não muda.</summary>
    public const string NonBusinessDayText = "Vencimento em dia não útil segue a regra do banco (o pagamento vale no dia útil seguinte). O título fica sem ajuste.";

    /// <summary>Valores dependentes da contadora (ADR 0042 §1): levam o selo até alguém conferir.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Reviewable =
        [(FinePct, "Multa"), (InterestMonthPct, "Juros de mora"), (OverdueFineFromKey, "Multa e juros do título vencido")];

    public static readonly IReadOnlyDictionary<string, decimal> Defaults = new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        [FinePct] = 2m,
        [InterestMonthPct] = 1m,
        [DefaultDiscountPct] = 0m,
        [AutoEmail] = 1m,
        [DailyEmailLimit] = 200m,
        [OverdueFineFromKey] = 0m,
    };

    private static string[] Keys() => [.. Defaults.Keys];

    /// <summary>Padrões em uso (seed ausente: os valores sugeridos).</summary>
    public async Task<BillingDefaults> DefaultsAsync(CancellationToken cancellationToken = default)
    {
        var keys = Keys();
        var stored = await db.FinanceParameters.AsNoTracking().Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, cancellationToken);
        decimal Get(string key) => stored.TryGetValue(key, out var p) ? p.Value : Defaults[key];
        var from = stored.TryGetValue(OverdueFineFromKey, out var f) && Enum.TryParse<OverdueFineFrom>(f.Text, out var parsed) ? parsed : OverdueFineFrom.OriginalDueDate;
        return new BillingDefaults(Get(FinePct), Get(InterestMonthPct), Get(DefaultDiscountPct), Get(AutoEmail) != 0, (int)Get(DailyEmailLimit), from);
    }

    public async Task<BillingSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        var values = await DefaultsAsync(cancellationToken);
        var keys = Reviewable.Select(r => r.Key).ToArray();
        var stored = await db.FinanceParameters.AsNoTracking().Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, cancellationToken);
        var reviews = new List<BillingReviewItem>();
        foreach (var (key, label) in Reviewable)
        {
            var p = stored.GetValueOrDefault(key);
            var name = p?.ReviewedBy is { } by ? await users.DisplayNameAsync(by, cancellationToken) : null;
            reviews.Add(new BillingReviewItem(key, label, p?.ReviewState ?? ParameterReviewState.ToConfirm, name, p?.ReviewedAt, p?.Id));
        }

        var canEdit = await permissions.HasPermissionAsync("settings", "edit", cancellationToken);
        return new BillingSettingsView(values.FinePct, values.InterestMonthPct, values.DefaultDiscountPct, values.AutoEmail, values.DailyEmailLimit, values.OverdueFineFrom,
            NonBusinessDayText, reviews, reviews.Count(r => r.ReviewState != ParameterReviewState.Confirmed), canEdit);
    }

    /// <summary>Validação do "Salvar cobrança" (CA 3), com mensagens em PT-BR.</summary>
    public static Error? Validate(BillingSettingsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.FinePct < 0)
        {
            return ReceivableService.Invalid("finePct", "A multa não pode ser negativa.");
        }

        if (input.FinePct > BillingSlipRules.MaxFinePct)
        {
            return ReceivableService.Invalid("finePct", "A multa não pode passar de 2,00%.");
        }

        if (input.InterestMonthPct < 0)
        {
            return ReceivableService.Invalid("interestMonthPct", "Os juros de mora não podem ser negativos.");
        }

        if (input.InterestMonthPct > BillingSlipRules.MaxInterestMonthPct)
        {
            return ReceivableService.Invalid("interestMonthPct", "Os juros de mora não podem passar de 1,00% ao mês.");
        }

        if (input.DefaultDiscountPct is < 0 or > 100)
        {
            return ReceivableService.Invalid("defaultDiscountPct", "O desconto padrão deve ficar entre 0% e 100%.");
        }

        if (input.DailyEmailLimit is < BillingSlipRules.MinDailyEmailLimit or > BillingSlipRules.MaxDailyEmailLimit)
        {
            return ReceivableService.Invalid("dailyEmailLimit", "O limite diário de e-mails deve ficar entre 1 e 2.000.");
        }

        return Enum.IsDefined(input.OverdueFineFrom) ? null : ReceivableService.Invalid("overdueFineFrom", "Escolha de onde contam a multa e os juros do título vencido.");
    }

    public async Task<Result<BillingSettingsView>> UpdateAsync(BillingSettingsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (Validate(input) is { } invalid)
        {
            return invalid;
        }

        var values = new Dictionary<string, (decimal Value, string? Text)>(StringComparer.Ordinal)
        {
            [FinePct] = (Math.Round(input.FinePct, 2, MidpointRounding.AwayFromZero), null),
            [InterestMonthPct] = (Math.Round(input.InterestMonthPct, 2, MidpointRounding.AwayFromZero), null),
            [DefaultDiscountPct] = (Math.Round(input.DefaultDiscountPct, 2, MidpointRounding.AwayFromZero), null),
            [AutoEmail] = (input.AutoEmail ? 1m : 0m, null),
            [DailyEmailLimit] = (input.DailyEmailLimit, null),
            [OverdueFineFromKey] = (0m, input.OverdueFineFrom.ToString()),
        };
        var keys = Keys();
        var stored = await db.FinanceParameters.Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, cancellationToken);
        var now = clock.GetUtcNow();
        var reviewable = Reviewable.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, (value, text)) in values)
        {
            if (stored.TryGetValue(key, out var p))
            {
                if (p.Value != value || (text is not null && p.Text != text))
                {
                    p.Value = value;
                    p.Text = text ?? p.Text;
                    p.UpdatedAt = now;
                    if (reviewable.Contains(key))
                    {
                        // A conferência vale para o valor (ADR 0042 §1): mudou, volta para "A confirmar com a contadora".
                        p.ResetReview();
                    }
                }
            }
            else
            {
                var created = new FinanceParameter { Key = key, Value = value, Text = text, UpdatedAt = now };
                if (reviewable.Contains(key))
                {
                    created.ResetReview();
                }

                db.FinanceParameters.Add(created);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>"Marcar como conferido" (CA 2): grava quem e quando, auditado; vale para o valor de agora.</summary>
    public async Task<Result<BillingSettingsView>> ConfirmAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (!Reviewable.Any(r => r.Key == key))
        {
            return Error.NotFound("parameter_not_found", "Parâmetro não encontrado.");
        }

        var parameter = await db.FinanceParameters.FirstOrDefaultAsync(p => p.Key == key, cancellationToken);
        if (parameter is null)
        {
            parameter = new FinanceParameter { Key = key, Value = Defaults[key], Text = key == OverdueFineFromKey ? nameof(OverdueFineFrom.OriginalDueDate) : null, UpdatedAt = clock.GetUtcNow() };
            db.FinanceParameters.Add(parameter);
        }

        if (parameter.ReviewState != ParameterReviewState.Confirmed)
        {
            parameter.Confirm(currentUser.UserId, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetAsync(cancellationToken);
    }

    /// <summary>Seed (também nos tenants existentes, ADR 0016): grava o que falta, sem sobrescrever; os conferíveis nascem "A confirmar".</summary>
    public static async Task SeedAsync(IFinanceDb db, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var keys = Keys();
        var existing = (await db.FinanceParameters.Where(p => keys.Contains(p.Key)).Select(p => p.Key).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var reviewable = Reviewable.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var now = clock.GetUtcNow();
        foreach (var (key, value) in Defaults.Where(d => !existing.Contains(d.Key)))
        {
            var parameter = new FinanceParameter { Key = key, Value = value, Text = key == OverdueFineFromKey ? nameof(OverdueFineFrom.OriginalDueDate) : null, UpdatedAt = now };
            if (reviewable.Contains(key))
            {
                parameter.ResetReview();
            }

            db.FinanceParameters.Add(parameter);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
