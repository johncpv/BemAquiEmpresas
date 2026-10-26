using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Webhooks;

/// <summary>
/// <c>tenantKey</c> opaco da URL do webhook (FND-16 CA 2, ADR 0031 §1): 128 bits aleatórios em base64url, em
/// <c>catalog.tenants.webhook_key</c>, sem relação com nome, slug ou id. Só a rota <c>/webhooks/*</c> resolve tenant por ele.
/// Implementação no Platform (catálogo).
/// </summary>
public interface IWebhookTenantKeys
{
    /// <summary>Tenant da chave, ou nulo (chave fora do formato não chega ao banco).</summary>
    Task<ResolvedTenant?> ResolveAsync(string tenantKey, CancellationToken cancellationToken = default);

    /// <summary>Chave do tenant, criada no primeiro provedor configurado.</summary>
    Task<string> EnsureAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Seção <c>Webhooks</c>.</summary>
public sealed class WebhookOptions
{
    public const string Section = "Webhooks";

    /// <summary>Corpo máximo (CA 8): acima, 413 antes de ler o resto.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>Base pública da URL mostrada para o cadastro no provedor (host da plataforma, ADR 0005 D2).</summary>
    public string PublicBaseUrl { get; set; } = "https://plataforma.bemaquiapp.com.br";

    /// <summary>Hosts internos da API (ex.: <c>bqe-homolog-api</c>), aceitos só para provedor com <c>AllowInternalHost</c> (FIS-01).</summary>
    public string[] InternalHosts { get; set; } = [];

    /// <summary>Requisições por minuto do mesmo IP para o mesmo provedor (política <c>webhooks</c>, CA 8).</summary>
    public int PerIpPerMinute { get; set; } = 60;
}

/// <summary>Credencial nova, mostrada uma única vez a quem gerou (CA 7).</summary>
public sealed record WebhookRotation(string Provider, string Url, string? Secret, DateTimeOffset? PreviousValidUntil);

/// <summary>Situação da credencial, sem o valor (ADR 0042: "configurado em dd/mm/aaaa").</summary>
public sealed record WebhookCredentialStatus(string Provider, string Name, bool Configured, DateTimeOffset? ConfiguredAt, DateTimeOffset? PreviousValidUntil, string Kind);

/// <summary>
/// Gera e troca o segredo ou o token de um provedor (CA 7): <c>settings.edit</c> (no endpoint) e 2FA de novo; a anterior vale por
/// mais 24 h; a troca é auditada sem o valor (<c>[ProtectedSecret]</c>) e avisada aos membros do Proprietário. Nesta story só o
/// provedor que declara <c>RotationInSettings</c> (o de teste); o Inter ganha a tela na BNK-01a.
/// </summary>
public sealed class WebhookCredentialService(
    BemAquiDbContext db,
    ITenantContext tenant,
    WebhookProviderRegistry providers,
    IWebhookTenantKeys tenantKeys,
    ISecretProtector secrets,
    IStepUpTwoFactor stepUp,
    ICurrentUser currentUser,
    IUserDirectory users,
    INotificationService notifications,
    IOptions<WebhookOptions> options,
    TimeProvider clock)
{
    public const string StepUpRequired = "Confirme com o código do aplicativo autenticador para gerar o segredo.";
    public const string RotatedNotification = "webhook_secret_rotated";

    public async Task<IReadOnlyList<WebhookCredentialStatus>> StatusAsync(CancellationToken cancellationToken = default)
    {
        var rotatable = providers.All.Where(p => p.RotationInSettings).ToList();
        var codes = rotatable.Select(p => p.Code).ToList();
        var credentials = await db.WebhookCredentials.AsNoTracking().Where(c => codes.Contains(c.Provider) && c.ScopeId == Guid.Empty).ToListAsync(cancellationToken);
        return rotatable.Select(p =>
        {
            var c = credentials.FirstOrDefault(x => x.Provider == p.Code);
            return new WebhookCredentialStatus(p.Code, p.Name, c is not null, c?.RotatedAt ?? c?.CreatedAt, c?.PreviousValidUntil, p.Validator.Kind.ToString());
        }).ToList();
    }

    public async Task<Result<WebhookRotation>> RotateAsync(string providerCode, string? code, CancellationToken cancellationToken = default)
    {
        if (!providers.TryGet(providerCode, out var provider) || !provider.RotationInSettings)
        {
            return Error.NotFound(Error.Codes.NotFound, "Provedor não encontrado.");
        }

        if (currentUser.UserId is not { } userId || !await stepUp.VerifyAsync(userId, code, cancellationToken))
        {
            return Error.Forbidden("two_factor_required", StepUpRequired);
        }

        var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Rotação de webhook fora do escopo de um tenant.");
        var key = await tenantKeys.EnsureAsync(tenantId, cancellationToken);
        var now = clock.GetUtcNow();
        var value = OpaqueToken.New();
        var credential = await db.WebhookCredentials.FirstOrDefaultAsync(c => c.Provider == provider.Code && c.ScopeId == Guid.Empty, cancellationToken);
        if (credential is null)
        {
            credential = new WebhookCredential { Provider = provider.Code, ScopeId = Guid.Empty, Kind = provider.Validator.Kind, CreatedAt = now };
            db.WebhookCredentials.Add(credential);
        }

        if (provider.Validator.Kind == WebhookCredentialKind.UrlToken)
        {
            credential.Rotate(Convert.ToHexStringLower(UrlTokenValidator.Hash(value)), null, userId, now);
        }
        else
        {
            credential.Rotate(null, secrets.Protect(SecretScope.ForTenant(tenantId), SecretKind.WebhookSecret, new Secret(value)), userId, now);
        }

        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, RotatedNotification, "O segredo do aviso do " + provider.Name + " foi trocado. O anterior vale por mais 24 horas.", WebhookLinks.Events);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new WebhookRotation(provider.Code, UrlFor(provider, key, value), provider.Validator.Kind == WebhookCredentialKind.HmacSecret ? value : null, credential.PreviousValidUntil);
    }

    private string UrlFor(WebhookProvider provider, string key, string value)
    {
        var path = options.Value.PublicBaseUrl.TrimEnd('/') + "/webhooks/" + provider.Code + "/" + key;
        return provider.Validator switch
        {
            UrlTokenValidator { Mode: UrlTokenMode.Query } => path + "?" + UrlTokenValidator.QueryParameter + "=" + value,
            UrlTokenValidator { Mode: UrlTokenMode.PathToken } => path + "/" + value,
            _ => path,
        };
    }
}
