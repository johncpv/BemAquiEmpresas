using BemAqui.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BemAqui.BuildingBlocks.Web;

/// <summary>
/// Marca o endpoint de sincronização do aparelho de PDV (PDV-01 CA 9, ADR 0048 §2): aceita só o principal do aparelho (esquema
/// <c>Device</c>, policy <c>device</c>), nunca um usuário. Só em <c>/api/v1/pos/sync/*</c>: o teste de arquitetura confere cada uso e o
/// <c>EndpointAuthorizationTests</c> reprova o esquema em qualquer outra rota. <see cref="AllowRevoked"/> é só para a subida de eventos já
/// gravados no aparelho (CA 8): o revogado continua subindo, nunca descendo.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class DeviceEndpointAttribute(bool allowRevoked = false) : Attribute
{
    public bool AllowRevoked { get; } = allowRevoked;
}

public static class DeviceEndpoints
{
    public const string PathPrefix = "/api/v1/pos/sync";
    public const string RevokedCode = "device_revoked";
    public const string RevokedMessage = "Este aparelho foi bloqueado ou trocado por outro. Registre o aparelho de novo.";
    public const string InvalidCode = "device_not_registered";
    public const string InvalidMessage = "Este aparelho ainda não é um caixa. Registre o aparelho.";

    /// <summary>
    /// Exige o principal do aparelho (esquema <c>Device</c>) e marca o endpoint. Aparelho revogado ou substituído responde 401
    /// <c>device_revoked</c>, a não ser na subida (<paramref name="allowRevoked"/>).
    /// </summary>
    public static TBuilder RequireDevice<TBuilder>(this TBuilder builder, bool allowRevoked = false)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new DeviceEndpointAttribute(allowRevoked));
        builder.RequireAuthorization(DeviceClaims.Policy);
        if (!allowRevoked)
        {
            builder.AddEndpointFilter(async (context, next) =>
                DevicePrincipal.From(context.HttpContext.User) is { Revoked: false } ? await next(context) : Revoked());
        }

        return builder;
    }

    /// <summary>Cookie do aparelho de caixa (ADR 0048 §2): HttpOnly, Secure, SameSite=Strict, Path=/, com a validade do aparelho.</summary>
    public static void SetCookie(HttpContext http, string secret, DateTimeOffset expires)
    {
        ArgumentNullException.ThrowIfNull(http);
        http.Response.Cookies.Append(PosDeviceCookie.Name, secret, new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", Expires = expires, IsEssential = true,
        });
        http.Response.Headers.CacheControl = "no-store";
    }

    public static IResult Revoked() =>
        Results.Problem(detail: RevokedMessage, statusCode: StatusCodes.Status401Unauthorized, title: "Aparelho bloqueado",
            extensions: new Dictionary<string, object?> { ["code"] = RevokedCode });
}
