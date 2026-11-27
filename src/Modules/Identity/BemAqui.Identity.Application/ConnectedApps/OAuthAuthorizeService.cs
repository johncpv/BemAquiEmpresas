using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BemAqui.Identity.Application.ConnectedApps;

/// <summary>
/// Começo da conexão e consentimento (INT-01 CA 1 a 10). A conexão começa no ERP (intenção de 10 min, uso único, da mesma pessoa); o pedido do
/// aplicativo é conferido antes de qualquer redirecionamento (cliente, URI exata e PKCE S256); só então os erros OAuth voltam ao aplicativo com o
/// <c>state</c>. O consentimento exige 2FA de novo e consome a intenção na mesma transação do código.
/// </summary>
public sealed class OAuthAuthorizeService(
    IIdentityDb db,
    IOptions<ConnectedAppOptions> options,
    IPermissionService permissions,
    IStepUpTwoFactor stepUp,
    ITenantBranding branding,
    AppConnectionLifecycle lifecycle,
    TimeProvider clock)
{
    public const string IntentInvalidCode = "connect_intent_invalid";
    public const string IntentInvalidMessage = "Este pedido de conexão não foi iniciado por você neste ERP.";
    public const string IntentUsedCode = "connect_intent_used";
    public const string IntentUsedMessage = "Este pedido de conexão já foi usado. Comece de novo em Integrações → Conecta.";
    public const string InvalidCodeMessage = "Código incorreto. Confira no aplicativo autenticador e tente de novo.";
    public const string EnrollMessage = "Para conectar, ative a verificação em duas etapas.";
    public const string NotConfiguredMessage = "A conexão com o Conecta não está configurada neste ambiente. Fale com o suporte do BemAqui.";

    private ConnectedAppOptions App => options.Value;

    /// <summary>"Conectar" (CA 1): cria a intenção e devolve o endereço do Conecta com o host do tenant e o valor opaco.</summary>
    public async Task<Result<ConnectStart>> StartAsync(Guid userId, string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(App.StartUrl) || !Uri.TryCreate(App.StartUrl, UriKind.Absolute, out _))
        {
            return Error.Unavailable("conecta_not_configured", NotConfiguredMessage);
        }

        var app = await EnsureAppAsync(cancellationToken);
        var intent = OpaqueToken.New();
        var now = clock.GetUtcNow();
        db.AppConnectIntents.Add(new AppConnectIntent
        {
            IntentHash = OpaqueToken.Hash(intent), UserId = userId, AppId = app.Id, CreatedAt = now, ExpiresAt = now + AppConnectIntent.Lifetime,
        });
        await db.SaveChangesAsync(cancellationToken);
        return new ConnectStart(OAuthRules.AppendQuery(App.StartUrl, [new("erp", host), new("intent", intent)]));
    }

    /// <summary>Confere o pedido e monta a tela (CA 3 a 6). No primeiro pedido válido, amarra a intenção a ele.</summary>
    public async Task<AuthorizeInspection> InspectAsync(AuthorizeRequest request, Guid userId, string host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var check = await CheckAsync(request, userId, host, consent: false, cancellationToken);
        if (check.Inspection is { } done)
        {
            return done;
        }

        var notHeld = check.Grant!.NotHeld.Select(View).ToList();
        var tenant = (await branding.GetAsync(cancellationToken)).Name;
        return new AuthorizeInspection(AuthorizeInspection.Consent, null, null, null, App.Name, Label(request.AccountLabel), tenant,
            check.Grant.Granted.Select(View).ToList(), notHeld);
    }

    /// <summary>"Autorizar" (CA 7 e 9): 2FA de novo, intenção usada na mesma transação do código, e o retorno ao aplicativo com <c>code</c>, <c>state</c> e <c>iss</c>.</summary>
    public async Task<Result<AuthorizeRedirect>> ConsentAsync(AuthorizeConsentInput input, Guid userId, string host, string? ip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var request = input.Request;
        var check = await CheckAsync(request, userId, host, consent: true, cancellationToken);
        if (check.Inspection is { } done)
        {
            return done.Outcome == AuthorizeInspection.Redirect
                ? new AuthorizeRedirect(done.RedirectTo!)
                : new Error(done.ErrorCode ?? IntentInvalidCode, done.Message ?? IntentInvalidMessage, done.ErrorCode == IntentUsedCode ? ErrorType.Conflict : ErrorType.Validation);
        }

        var enrolled = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.TotpEnabled, cancellationToken);
        if (!enrolled)
        {
            return Error.Forbidden(RecentTwoFactorEndpoints.Code, EnrollMessage) with { Data = new Dictionary<string, object?> { ["enrolled"] = false } };
        }

        if (!await stepUp.VerifyAsync(userId, input.Code, cancellationToken))
        {
            return Error.Validation("auth.2fa_invalid_code", InvalidCodeMessage, new Dictionary<string, string[]> { ["code"] = [InvalidCodeMessage] });
        }

        var now = clock.GetUtcNow();
        var code = OpaqueToken.New();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var intentId = check.Intent!.Id;
        var used = await db.AppConnectIntents.Where(i => i.Id == intentId && i.UsedAt == null).ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedAt, now), cancellationToken);
        if (used == 0)
        {
            return Error.Conflict(IntentUsedCode, IntentUsedMessage);
        }

        db.AppAuthorizationCodes.Add(new AppAuthorizationCode
        {
            CodeHash = OpaqueToken.Hash(code), AppId = check.App!.Id, UserId = userId, RedirectUri = request.RedirectUri!, CodeChallenge = request.CodeChallenge!,
            Scopes = [.. check.Grant!.Granted], AccountLabel = Label(request.AccountLabel), CreatedAt = now, ExpiresAt = now + AppAuthorizationCode.Lifetime,
        });
        // Prova do consentimento (LGPD): quem, quando, IP e escopos; nunca o código.
        lifecycle.Audit(check.App.Id, "AppConsentGranted", userId, new { app = App.ClientId, scopes = check.Grant.Granted, notHeld = check.Grant.NotHeld, account = Label(request.AccountLabel) }, ip, nameof(ConnectedApp));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AuthorizeRedirect(OAuthRules.AppendQuery(request.RedirectUri!, [new("code", code), new("state", request.State), new("iss", Issuer(host))]));
    }

    /// <summary>"Cancelar" (CA 10): consome a intenção, audita a recusa e volta ao aplicativo com <c>access_denied</c>.</summary>
    public async Task<Result<AuthorizeRedirect>> DenyAsync(AuthorizeRequest request, Guid userId, string host, string? ip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var check = await CheckAsync(request, userId, host, consent: true, cancellationToken);
        if (check.Inspection is { Outcome: AuthorizeInspection.Error } done)
        {
            return new Error(done.ErrorCode ?? IntentInvalidCode, done.Message ?? IntentInvalidMessage, ErrorType.Validation);
        }

        if (check.Intent is { } intent)
        {
            intent.MarkUsed(clock.GetUtcNow());
        }

        lifecycle.Audit(check.App!.Id, "AppConsentDenied", userId, new { app = App.ClientId }, ip, nameof(ConnectedApp));
        await db.SaveChangesAsync(cancellationToken);
        return new AuthorizeRedirect(ErrorRedirect(request, host, "access_denied"));
    }

    private sealed record Check(AuthorizeInspection? Inspection, ConnectedApp? App, AppConnectIntent? Intent, AppScopeGrant? Grant);

    private async Task<Check> CheckAsync(AuthorizeRequest request, Guid userId, string host, bool consent, CancellationToken cancellationToken)
    {
        // CA 3: cliente, URI exata e PKCE errados ficam no ERP; nunca redirecionam para um endereço não conferido.
        if (!string.Equals(request.ClientId, App.ClientId, StringComparison.Ordinal))
        {
            return Local("invalid_client", "O aplicativo que pediu a conexão não é conhecido por este ERP.");
        }

        if (!OAuthRules.RedirectMatches(request.RedirectUri, App.RedirectUris.Where(u => u.StartsWith("https://", StringComparison.Ordinal))))
        {
            return Local("invalid_redirect_uri", "O endereço de retorno do aplicativo não confere com o cadastrado. Nada foi enviado ao aplicativo.");
        }

        if (!string.Equals(request.CodeChallengeMethod, "S256", StringComparison.Ordinal) || !OAuthRules.IsValidChallenge(request.CodeChallenge))
        {
            return Local("invalid_request", "O pedido do aplicativo veio incompleto ou fora do padrão de segurança. Comece de novo pelo ERP.");
        }

        // CA 4 e 5: a intenção é desta pessoa, deste aplicativo, válida, não usada e amarrada ao mesmo pedido.
        var app = await EnsureAppAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var intentHash = string.IsNullOrEmpty(request.Intent) ? null : OpaqueToken.Hash(request.Intent);
        var intent = intentHash is null
            ? null
            : await db.AppConnectIntents.FirstOrDefaultAsync(i => i.IntentHash == intentHash, cancellationToken);
        if (intent is null || intent.UserId != userId || intent.AppId != app.Id || intent.ExpiresAt <= now)
        {
            return Local(IntentInvalidCode, IntentInvalidMessage);
        }

        if (intent.UsedAt is not null)
        {
            return Local(consent ? IntentUsedCode : IntentInvalidCode, consent ? IntentUsedMessage : IntentInvalidMessage);
        }

        var binding = OAuthRules.BindingHash(request.State, request.CodeChallenge, request.RedirectUri);
        if (consent ? !string.Equals(intent.RequestBindingHash, binding, StringComparison.Ordinal) : !intent.Bind(binding))
        {
            return Local(IntentInvalidCode, IntentInvalidMessage);
        }

        if (!consent)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!string.Equals(request.ResponseType, "code", StringComparison.Ordinal))
        {
            return new Check(Redirected(request, host, "unsupported_response_type"), app, intent, null);
        }

        var effective = await permissions.GetAsync(userId, cancellationToken);
        var grant = ConnectedAppScopes.Compute(ConnectedAppScopes.Parse(request.Scope), App.EffectiveScopes.ToList(), effective.Has);
        if (grant.Unknown.Count > 0 || grant.Granted.Count + grant.NotHeld.Count == 0)
        {
            return new Check(Redirected(request, host, "invalid_scope"), app, intent, grant);
        }

        // CA 9: concessão vazia (a pessoa não tem nada do que foi pedido) volta como access_denied.
        return grant.Granted.Count == 0 ? new Check(Redirected(request, host, "access_denied"), app, intent, grant) : new Check(null, app, intent, grant);
    }

    private static Check Local(string code, string message) =>
        new(new AuthorizeInspection(AuthorizeInspection.Error, code, message, null, null, null, null, [], []), null, null, null);

    private static AuthorizeInspection Redirected(AuthorizeRequest request, string host, string error) =>
        new(AuthorizeInspection.Redirect, error, null, ErrorRedirect(request, host, error), null, null, null, [], []);

    private static string ErrorRedirect(AuthorizeRequest request, string host, string error) =>
        OAuthRules.AppendQuery(request.RedirectUri!, [new("error", error), new("state", request.State), new("iss", Issuer(host))]);

    public static string Issuer(string host) => "https://" + host;

    private static AppScopeView View(string scope) => new(scope, ConnectedAppScopes.Text(scope));

    private static string? Label(string? accountLabel)
    {
        var text = accountLabel?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > AppConnection.MaxAccountLabelLength ? text[..AppConnection.MaxAccountLabelLength] : text;
    }

    /// <summary>A linha do aplicativo nasce pelo seeder; se faltar (tenant antigo antes do deploy), nasce aqui.</summary>
    public async Task<ConnectedApp> EnsureAppAsync(CancellationToken cancellationToken)
    {
        var app = await db.ConnectedApps.FirstOrDefaultAsync(a => a.ClientId == App.ClientId, cancellationToken);
        if (app is null)
        {
            app = new ConnectedApp { ClientId = App.ClientId, Name = App.Name, RequiredModule = App.RequiredModule, CreatedAt = clock.GetUtcNow() };
            db.ConnectedApps.Add(app);
            await db.SaveChangesAsync(cancellationToken);
        }

        return app;
    }
}
