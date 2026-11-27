using BemAqui.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BemAqui.BuildingBlocks.Web;

/// <summary>
/// Marca a rota de aplicativo conectado (INT-01 CA 15, ADR 0053 §4): aceita só o principal do esquema <c>App</c>, nunca uma pessoa. Só em
/// <c>/api/v1/integration/*</c>: o <c>EndpointAuthorizationTests</c> reprova o esquema em qualquer outra rota. <see cref="Self"/> marca as rotas
/// da própria conexão, que não pedem escopo de negócio (só token válido e o módulo <c>conecta</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class ConnectedAppEndpointAttribute(bool self = false) : Attribute
{
    public bool Self { get; } = self;
}

public static class ConnectedAppEndpoints
{
    public const string PathPrefix = "/api/v1/integration";
    public const string ModuleCode = "conecta";

    public const string InvalidTokenCode = "invalid_token";
    public const string InvalidTokenMessage = "Token inválido, vencido ou revogado. Renove o token ou conecte de novo.";
    public const string GrantorInactiveCode = "app_grantor_inactive";
    public const string GrantorInactiveMessage = "Quem autorizou o aplicativo perdeu o acesso. Um Proprietário precisa autorizar de novo.";
    public const string InsufficientScopeCode = "insufficient_scope";
    public const string InsufficientScopeMessage = "O aplicativo não tem permissão para esta ação.";

    /// <summary>Política de rate limit das rotas de aplicativo, por conexão (ADR 0053 §8; compartilhada com a INT-03).</summary>
    public const string RateLimitPolicy = "app-api";

    /// <summary><c>/oauth/token</c> e <c>/oauth/revoke</c>: 30 por minuto por IP e cliente (INT-01 CA 19).</summary>
    public const string TokenRateLimitPolicy = "oauth-token";

    /// <summary>Conferir, autorizar e cancelar o consentimento: 10 por minuto por usuário (INT-01 CA 19).</summary>
    public const string ConsentRateLimitPolicy = "oauth-consent";

    public const string OAuthPathPrefix = "/api/v1/oauth";

    /// <summary>Exige o principal de aplicativo (policy <c>connected-app</c>). Use no grupo <c>/api/v1/integration</c>, com <c>RequirePermission</c> por rota.</summary>
    public static TBuilder RequireConnectedApp<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => Mark(builder, self: false);

    /// <summary>Rota da própria conexão: token válido e o módulo <c>conecta</c>, sem escopo de negócio.</summary>
    public static TBuilder RequireConnectedAppSelf<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => Mark(builder, self: true);

    private static TBuilder Mark<TBuilder>(TBuilder builder, bool self)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new ConnectedAppEndpointAttribute(self));
        builder.WithMetadata(new RequiresModuleMetadata(ModuleCode));
        builder.RequireAuthorization(AppClaims.Policy);
        return builder;
    }

    public static IResult InvalidToken() => Problem(StatusCodes.Status401Unauthorized, InvalidTokenCode, InvalidTokenMessage, "Token inválido");

    public static IResult GrantorInactive() => Problem(StatusCodes.Status401Unauthorized, GrantorInactiveCode, GrantorInactiveMessage, "Autorização suspensa");

    public static IResult InsufficientScope(string scope) =>
        Results.Problem(detail: InsufficientScopeMessage, statusCode: StatusCodes.Status403Forbidden, title: "Acesso negado",
            extensions: new Dictionary<string, object?> { ["code"] = InsufficientScopeCode, ["scope"] = scope });

    private static IResult Problem(int status, string code, string message, string title) =>
        Results.Problem(detail: message, statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
