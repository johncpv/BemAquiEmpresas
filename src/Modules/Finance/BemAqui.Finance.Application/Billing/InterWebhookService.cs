using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BemAqui.Finance.Application;

/// <summary>Relatório da conferência diária (CA 11), guardado em <c>bank_integrations.last_reconcile_report</c>, sem dado do pagador.</summary>
public sealed record ReconcileReport(DateOnly From, DateOnly To, int Consulted, int Settled, int Divergent, int Unknown, int Cancelled, int Expired, int Failed);

/// <summary>Seção "Aviso de pagamento" da conta do Inter (CA 1 a 3, 11 e 12; padrão 6: situação da integração assíncrona).</summary>
public sealed record PaymentNoticeView(
    Guid BankAccountId,
    bool Active,
    string Mode,
    DateTimeOffset? Since,
    string? SinceByName,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? PreviousValidUntil,
    string? MaskedUrl,
    DateTimeOffset? LastDeliveryAt,
    DateTimeOffset? LastReconcileAt,
    ReconcileReport? LastReconcile,
    int ReconcileFailures,
    string? LastReconcileError,
    int PendingReview);

/// <summary>Endereço completo, mostrado uma única vez a quem ativou ou trocou (CA 1), com a máscara que fica depois.</summary>
public sealed record PaymentNoticeActivation(string Url, string MaskedUrl, string Mode, DateTimeOffset RegisteredAt, DateTimeOffset? PreviousValidUntil, string Message);

/// <summary>
/// "Ativar aviso de pagamento" e "Trocar endereço do aviso" (BNK-02 CA 1 a 4): <c>settings.edit</c> e o 2FA de novo (no endpoint). Gera um
/// token de 256 bits, guarda só o hash (<c>shared.webhook_tokens</c>, escopo = a integração; o anterior vale por 24 h na troca), cadastra a
/// URL no Inter (<c>/webhooks/inter/{tenantKey}?t={token}</c> ou <c>/{token}</c> no modo <c>PathToken</c>) e só então grava; o banco
/// recusando, nada muda. A URL completa sai só nesta resposta. Produção sem <c>bank_production_allowed</c>: 409 (CA 4).
/// </summary>
public sealed class InterWebhookService(
    IFinanceDb db,
    BemAquiDbContext shared,
    BankIntegrationService integrations,
    IBankBillingProvider bank,
    IWebhookTenantKeys tenantKeys,
    WebhookProviderRegistry providers,
    TenantContextHolder tenant,
    ICurrentUser currentUser,
    IUserDirectory users,
    INotificationService notifications,
    IOptions<WebhookOptions> options,
    TimeProvider clock)
{
    public const string ActivatedMessage = "Aviso de pagamento ativo";
    public const string RotatedMessage = "Endereço trocado";
    public const string RotatedNotification = "finance.payment_notice_rotated";
    public const string AlreadyActiveCode = "payment_notice_active";
    public const string NotActiveCode = "payment_notice_inactive";
    public const string RegisterFailedCode = "payment_notice_register_failed";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public UrlTokenMode Mode => providers.TryGet(InterWebhook.ProviderCode, out var p) && p.Validator is UrlTokenValidator v ? v.Mode : UrlTokenMode.Query;

    public async Task<Result<PaymentNoticeView>> GetAsync(Guid bankAccountId, CancellationToken cancellationToken = default)
    {
        var integration = await db.BankIntegrations.AsNoTracking().FirstOrDefaultAsync(i => i.BankAccountId == bankAccountId, cancellationToken);
        if (integration is null || !await db.BankAccounts.AnyAsync(a => a.Id == bankAccountId, cancellationToken))
        {
            return Error.NotFound(Error.Codes.NotFound, BankIntegrationService.NotFoundMessage);
        }

        var id = integration.Id;
        var credential = await shared.WebhookCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == InterWebhook.ProviderCode && c.ScopeId == id, cancellationToken);
        var lastDelivery = await shared.WebhookEvents.AsNoTracking().Where(e => e.Provider == InterWebhook.ProviderCode && e.ScopeId == id)
            .OrderByDescending(e => e.ReceivedAt).Select(e => (DateTimeOffset?)e.ReceivedAt).FirstOrDefaultAsync(cancellationToken);
        var pending = await db.BillingSlips.AsNoTracking().CountAsync(s => s.BankAccountId == bankAccountId && s.Status == BillingSlipStatus.PaidPendingReview, cancellationToken);
        var active = integration.WebhookActive;
        var mode = integration.WebhookMode ?? Mode.ToString();
        return new PaymentNoticeView(
            bankAccountId, active, mode, integration.WebhookRegisteredAt,
            integration.WebhookRegisteredBy is { } by ? await users.DisplayNameAsync(by, cancellationToken) : null,
            integration.WebhookRotatedAt, credential?.PreviousValidUntil > clock.GetUtcNow() ? credential.PreviousValidUntil : null,
            active ? Masked(mode) : null, lastDelivery, integration.LastReconcileAt, Report(integration.LastReconcileReport), integration.ReconcileFailures, integration.LastReconcileError, pending);
    }

    public async Task<Result<PaymentNoticeActivation>> ActivateAsync(Guid bankAccountId, bool rotate, CancellationToken cancellationToken = default)
    {
        var allowed = await integrations.EffectsAllowedAsync(bankAccountId, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error!;
        }

        var integration = await db.BankIntegrations.FirstAsync(i => i.Id == allowed.Value.Id, cancellationToken);
        if (!rotate && integration.WebhookActive)
        {
            return Error.Conflict(AlreadyActiveCode, "O aviso de pagamento já está ativo nesta conta. Para gerar outro endereço, use Trocar endereço do aviso.");
        }

        if (rotate && !integration.WebhookActive)
        {
            return Error.Conflict(NotActiveCode, "O aviso de pagamento não está ativo nesta conta. Use Ativar aviso de pagamento.");
        }

        var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Aviso de pagamento fora do escopo de um tenant.");
        var key = await tenantKeys.EnsureAsync(tenantId, cancellationToken);
        var token = OpaqueToken.New();
        var mode = Mode;
        var url = Url(key, token, mode);
        var registered = await bank.RegisterWebhookAsync(integration, url, cancellationToken);
        if (!registered.Ok)
        {
            return Error.Conflict(RegisterFailedCode, "O Inter não aceitou o endereço do aviso: " + registered.Failure!.Message);
        }

        var now = clock.GetUtcNow();
        var credential = await shared.WebhookCredentials.FirstOrDefaultAsync(c => c.Provider == InterWebhook.ProviderCode && c.ScopeId == integration.Id, cancellationToken);
        if (credential is null)
        {
            credential = new WebhookCredential { Provider = InterWebhook.ProviderCode, ScopeId = integration.Id, Kind = WebhookCredentialKind.UrlToken, CreatedAt = now };
            shared.WebhookCredentials.Add(credential);
        }

        // Sem aviso ativo (primeira vez, ou depois de desligar a integração), o endereço antigo não fica valendo.
        var hash = Convert.ToHexStringLower(UrlTokenValidator.Hash(token));
        if (rotate)
        {
            credential.Rotate(hash, null, currentUser.UserId, now);
        }
        else
        {
            credential.Replace(hash, null, currentUser.UserId, now);
        }

        integration.RegisterWebhook(mode.ToString(), currentUser.UserId, now);
        if (rotate)
        {
            foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
            {
                notifications.Notify(owner, RotatedNotification, "O endereço do aviso de pagamento do Inter foi trocado. O anterior vale por mais 24 horas.", BankIntegrationService.Link(bankAccountId));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new PaymentNoticeActivation(url, Masked(mode.ToString()), mode.ToString(), now, rotate ? credential.PreviousValidUntil : null, rotate ? RotatedMessage : ActivatedMessage);
    }

    private string Url(string key, string token, UrlTokenMode mode)
    {
        var path = options.Value.PublicBaseUrl.TrimEnd('/') + "/webhooks/" + InterWebhook.ProviderCode + "/" + key;
        return mode == UrlTokenMode.PathToken ? path + "/" + token : path + "?" + UrlTokenValidator.QueryParameter + "=" + token;
    }

    public static string Masked(string mode) =>
        string.Equals(mode, nameof(UrlTokenMode.PathToken), StringComparison.Ordinal) ? "…/webhooks/inter/••••/••••" : "…/webhooks/inter/••••?t=••••";

    public static ReconcileReport? Report(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReconcileReport>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
