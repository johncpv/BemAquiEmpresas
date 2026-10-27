using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Domain;
using BemAqui.Platform.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BemAqui.Fiscal.Application;

/// <summary>
/// Configurações → Integrações → Emissor fiscal, como a tela lê (CA 7 e 8): nunca a chave nem o segredo, só as datas. Sem permissão no
/// ambiente (<see cref="Allowed"/> falso), a tela mostra "A emissão desta empresa só liga na virada para a produção." sem o formulário.
/// </summary>
public sealed record FiscalIntegrationView(
    bool Configured,
    bool Allowed,
    string AllowedEnvironment,
    string DefaultBaseUrl,
    string? BaseUrl,
    DateTimeOffset? ApiKeySetAt,
    DateTimeOffset? WebhookSecretSetAt,
    DateTimeOffset? WebhookPreviousValidUntil,
    string? WebhookUrl,
    bool Active,
    Guid? EmitterTenantId,
    string? IssuerDocument,
    string? IssuerName,
    string? Environment,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? CertificateValidUntil,
    DateTimeOffset? LastTestAt,
    string? LastTestResult,
    string? LastTestMessage,
    DateTimeOffset? LastWebhookAt,
    int PendingInvoices,
    DateTimeOffset? OldestPendingSince,
    Guid? OldestPendingInvoiceId);

/// <summary>Salvar (CA 7 e 10): <c>ApiKey</c> e <c>WebhookSecret</c> vazios mantêm os salvos; <c>Code</c> é o código do autenticador (2FA de novo).</summary>
public sealed record SaveFiscalIntegrationRequest(string? BaseUrl, Secret? ApiKey, Secret? WebhookSecret, string? Code);

/// <summary>Resultado do "Testar conexão" (CA 8): aprovado ou o motivo, sempre com a situação atual.</summary>
public sealed record FiscalConnectionTest(bool Ok, string Result, string Message, FiscalIntegrationView Integration);

/// <summary>
/// Credencial do EmissorFiscal por tenant (FIS-01 CA 7 a 10, ADR 0027 §2, ADR 0042): <c>settings.edit</c> (no endpoint) e 2FA de novo ao
/// salvar; chave e segredo do webhook cifrados e só de escrita; URL só da lista do ambiente; ambiente pela marca do catálogo; chave nova
/// testada antes da troca; Proprietário avisado da troca. A auditoria registra "alterado" sem o valor (<c>[ProtectedSecret]</c>).
/// </summary>
public sealed class FiscalIntegrationService(
    IFiscalDb db,
    BemAquiDbContext shared,
    ITenantContext tenant,
    ITenantProfile profile,
    FiscalEmitterAccess access,
    ISecretProtector secrets,
    IWebhookTenantKeys webhookKeys,
    IStepUpTwoFactor stepUp,
    ICurrentUser currentUser,
    IUserDirectory users,
    INotificationService notifications,
    FiscalAlerts alerts,
    IOptions<FiscalIntegrationOptions> options,
    TimeProvider clock)
{
    public const string TestRateLimitPolicy = "fiscal-integration";
    public const string TwoFactorInvalid = "two_factor_invalid";
    public const string StepUpMessage = "Confirme com o código do aplicativo autenticador para salvar a integração fiscal.";
    public const string RotatedNotification = "fiscal.integration_rotated";
    public const string KeyRejectedMessage = "O Emissor Fiscal recusou a chave. Confira a chave e o endereço.";
    public const string UnavailableMessage = "O Emissor Fiscal não respondeu. Confira o endereço e se o serviço está no ar.";

    private static readonly OutgoingInvoiceStatus[] Pending = [OutgoingInvoiceStatus.Processing, OutgoingInvoiceStatus.ContingencyPending];

    public async Task<FiscalIntegrationView> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.FiscalIntegrationSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var allowance = await profile.FiscalEnvironmentAllowedAsync(TenantId(), cancellationToken);
        var credential = await shared.WebhookCredentials.AsNoTracking()
            .Where(c => c.Provider == EmissorFiscalWebhook.ProviderCode && c.ScopeId == Guid.Empty).FirstOrDefaultAsync(cancellationToken);
        var lastWebhook = await shared.WebhookEvents.AsNoTracking().Where(e => e.Provider == EmissorFiscalWebhook.ProviderCode)
            .OrderByDescending(e => e.ReceivedAt).Select(e => (DateTimeOffset?)e.ReceivedAt).FirstOrDefaultAsync(cancellationToken);
        var pendingCount = await db.OutgoingInvoices.AsNoTracking().CountAsync(i => Pending.Contains(i.Status), cancellationToken);
        var oldest = pendingCount == 0 ? null : await db.OutgoingInvoices.AsNoTracking().Where(i => Pending.Contains(i.Status))
            .OrderBy(i => i.ProcessingSince).Select(i => new { i.Id, i.ProcessingSince }).FirstOrDefaultAsync(cancellationToken);
        string? webhookUrl = null;
        if (credential is not null)
        {
            var key = await webhookKeys.EnsureAsync(TenantId(), cancellationToken);
            webhookUrl = options.Value.WebhookBaseUrl.TrimEnd('/') + "/webhooks/" + EmissorFiscalWebhook.ProviderCode + "/" + key;
        }

        return new FiscalIntegrationView(
            settings?.ApiKeyCipher is not null,
            allowance != FiscalEnvironmentAllowance.None,
            allowance.ToString(),
            options.Value.DefaultBaseUrl,
            settings?.BaseUrl,
            settings?.ApiKeySetAt,
            credential?.RotatedAt ?? credential?.CreatedAt,
            credential?.PreviousValidUntil,
            webhookUrl,
            settings?.Active ?? false,
            settings?.EmitterTenantId,
            settings?.IssuerDocument,
            settings?.IssuerName,
            settings?.Environment?.ToString(),
            settings?.ScopeList ?? [],
            settings?.CertificateValidUntil,
            settings?.LastTestAt,
            settings?.LastTestResult,
            settings?.LastTestMessage,
            lastWebhook,
            pendingCount,
            oldest?.ProcessingSince,
            oldest?.Id);
    }

    public async Task<Result<FiscalIntegrationView>> SaveAsync(SaveFiscalIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = await db.FiscalIntegrationSettings.FirstOrDefaultAsync(cancellationToken);
        var url = FiscalBaseUrl.Validate(request.BaseUrl, options.Value);
        if (Validate(request, settings, url) is { } invalid)
        {
            return invalid;
        }

        if (await profile.FiscalEnvironmentAllowedAsync(TenantId(), cancellationToken) == FiscalEnvironmentAllowance.None)
        {
            return Error.Forbidden(FiscalErrorCodes.EnvironmentNotAllowed, FiscalEmitterAccess.EnvironmentMessage);
        }

        if (currentUser.UserId is not { } userId || !await stepUp.VerifyAsync(userId, request.Code, cancellationToken))
        {
            // Sem código: two_factor_required (CA 7). Código errado ou já usado: two_factor_invalid, para o passo global "Confirme o código"
            // da BNK-01a (que reage ao two_factor_required) não abrir por cima do diálogo desta tela, que já pede o código.
            return Error.Forbidden(string.IsNullOrWhiteSpace(request.Code) ? "two_factor_required" : TwoFactorInvalid, StepUpMessage);
        }

        var now = clock.GetUtcNow();
        var rotatingKey = settings?.ApiKeyCipher is not null && request.ApiKey is not null;
        FiscalAccountVerdict? verdict = null;
        if (rotatingKey)
        {
            // CA 10: a chave nova é testada antes de trocar; recusada, nada muda e a antiga continua.
            var tested = await CheckAsync(access.Create(url.Value, request.ApiKey!), cancellationToken);
            if (tested.IsFailure)
            {
                return tested.Error!;
            }

            verdict = tested.Value;
            if (!verdict.Ok)
            {
                return Error.Validation(Error.Codes.Validation, verdict.Message, new Dictionary<string, string[]> { ["apiKey"] = [verdict.Message] });
            }
        }

        if (settings is null)
        {
            settings = new FiscalIntegrationSettings { BaseUrl = url.Value.ToString() };
            db.FiscalIntegrationSettings.Add(settings);
        }
        else if (!string.Equals(settings.BaseUrl, url.Value.ToString(), StringComparison.Ordinal))
        {
            settings.BaseUrl = url.Value.ToString();
            settings.Deactivate(); // endereço novo: só volta depois de um teste aprovado
        }

        if (request.ApiKey is { } key)
        {
            settings.SetApiKey(access.Protect(key), now);
        }

        if (verdict?.Snapshot is { } snapshot)
        {
            settings.RecordApprovedTest(snapshot, now);
        }

        settings.UpdatedAt = now;
        settings.UpdatedBy = userId;
        var rotatedSecret = request.WebhookSecret is not null && await SetWebhookSecretAsync(request.WebhookSecret, userId, now, cancellationToken);
        if (rotatingKey || rotatedSecret)
        {
            var what = rotatingKey && rotatedSecret ? "A chave e o segredo do aviso do Emissor Fiscal foram trocados."
                : rotatingKey ? "A chave do Emissor Fiscal foi trocada. A anterior deixou de ser usada."
                : "O segredo do aviso do Emissor Fiscal foi trocado. O anterior vale por mais 24 horas.";
            foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
            {
                notifications.Notify(owner, RotatedNotification, what, FiscalLinks.Integration);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>"Testar conexão" (CA 8 e 9): chama o <c>GET /me</c> com a chave salva; aprovado, liga a integração; recusado, diz o motivo.</summary>
    public async Task<Result<FiscalConnectionTest>> TestAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.FiscalIntegrationSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings?.ApiKeyCipher is null)
        {
            return Error.Conflict(FiscalErrorCodes.NotConfigured, FiscalEmitterAccess.NotConfiguredMessage);
        }

        if (await profile.FiscalEnvironmentAllowedAsync(TenantId(), cancellationToken) == FiscalEnvironmentAllowance.None)
        {
            return Error.Forbidden(FiscalErrorCodes.EnvironmentNotAllowed, FiscalEmitterAccess.EnvironmentMessage);
        }

        var emitter = await access.CurrentAsync(requireActive: false, cancellationToken);
        if (emitter.IsFailure)
        {
            return emitter.Error!;
        }

        var now = clock.GetUtcNow();
        var checkedAccount = await CheckAsync(emitter.Value, cancellationToken);
        FiscalAccountVerdict verdict;
        if (checkedAccount.IsFailure)
        {
            var rejected = checkedAccount.Error!.Code == FiscalErrorCodes.IntegrationMisconfigured;
            verdict = new FiscalAccountVerdict(rejected ? FiscalTestResults.KeyRejected : FiscalTestResults.Unavailable, rejected ? KeyRejectedMessage : UnavailableMessage, null);
            settings.RecordFailedTest(verdict.Result, verdict.Message, deactivate: rejected, now);
            await alerts.RaiseAsync(rejected ? FiscalAlertReason.KeyRejected : FiscalAlertReason.Unreachable, null, cancellationToken);
        }
        else
        {
            verdict = checkedAccount.Value;
            if (verdict.Snapshot is { } snapshot)
            {
                settings.RecordApprovedTest(snapshot, now);
            }
            else
            {
                settings.RecordFailedTest(verdict.Result, verdict.Message, deactivate: true, now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new FiscalConnectionTest(verdict.Ok, verdict.Result, verdict.Message, await GetAsync(cancellationToken));
    }

    /// <summary>Chama o <c>GET /me</c> e confere a chave; erro só quando o Emissor não respondeu ou recusou a chave.</summary>
    private async Task<Result<FiscalAccountVerdict>> CheckAsync(IFiscalEmitter emitter, CancellationToken cancellationToken)
    {
        var account = await emitter.GetAccountAsync(cancellationToken);
        if (account.IsFailure)
        {
            return account.Error!.Code == FiscalErrorCodes.IntegrationMisconfigured
                ? Error.Validation(FiscalErrorCodes.IntegrationMisconfigured, KeyRejectedMessage, new Dictionary<string, string[]> { ["apiKey"] = [KeyRejectedMessage] })
                : Error.Unavailable(FiscalErrorCodes.Unavailable, UnavailableMessage);
        }

        var document = await profile.DocumentAsync(TenantId(), cancellationToken);
        var allowance = await profile.FiscalEnvironmentAllowedAsync(TenantId(), cancellationToken);
        return FiscalAccountCheck.Evaluate(account.Value, document, allowance);
    }

    /// <summary>Segredo do webhook na credencial do provedor <c>emissor-fiscal</c> (FND-16): o anterior vale por mais 24 h.</summary>
    private async Task<bool> SetWebhookSecretAsync(Secret secret, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tenantId = TenantId();
        await webhookKeys.EnsureAsync(tenantId, cancellationToken);
        var credential = await shared.WebhookCredentials.FirstOrDefaultAsync(c => c.Provider == EmissorFiscalWebhook.ProviderCode && c.ScopeId == Guid.Empty, cancellationToken);
        var rotation = credential is not null;
        if (credential is null)
        {
            credential = new WebhookCredential { Provider = EmissorFiscalWebhook.ProviderCode, ScopeId = Guid.Empty, Kind = WebhookCredentialKind.HmacSecret, CreatedAt = now };
            shared.WebhookCredentials.Add(credential);
        }

        credential.Rotate(null, secrets.Protect(SecretScope.ForTenant(tenantId), SecretKind.WebhookSecret, secret), userId, now);
        return rotation;
    }

    private static Error? Validate(SaveFiscalIntegrationRequest request, FiscalIntegrationSettings? settings, Result<Uri> url)
    {
        var fields = new Dictionary<string, string[]>();
        if (url.IsFailure)
        {
            fields["baseUrl"] = [FiscalBaseUrl.Message];
        }

        if (request.ApiKey is null && settings?.ApiKeyCipher is null)
        {
            fields["apiKey"] = ["Informe a chave de API do Emissor Fiscal."];
        }
        else if (request.ApiKey is { } key && (key.Reveal().Length is < 16 or > 400 || key.Reveal().Any(char.IsWhiteSpace)))
        {
            fields["apiKey"] = ["A chave parece incompleta. Copie a chave inteira, sem espaços."];
        }

        if (request.WebhookSecret is null && settings is null)
        {
            fields["webhookSecret"] = ["Informe o segredo do aviso, que o Emissor Fiscal mostra ao cadastrar o webhook."];
        }
        else if (request.WebhookSecret is { } secret && (secret.Reveal().Length is < 16 or > 400 || secret.Reveal().Any(char.IsWhiteSpace)))
        {
            fields["webhookSecret"] = ["O segredo parece incompleto. Copie o segredo inteiro, sem espaços."];
        }

        return fields.Count == 0 ? null : Error.Validation(Error.Codes.Validation, fields.Values.First()[0], fields);
    }

    private Guid TenantId() => tenant.Tenant?.Id ?? throw new InvalidOperationException("Integração fiscal fora do escopo de um tenant.");
}
