using System.Security.Claims;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;

namespace BemAqui.Host.Tenancy;

/// <summary>
/// Usuário do request: <c>sub</c> do token (FND-04) e IP real (forwarded headers confiáveis, FND-02b). Para aplicativo conectado (INT-01 CA 18),
/// o usuário é quem autorizou e a conexão vai em <see cref="ConnectionId"/>.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        AppClaims.GrantorOf(accessor.HttpContext?.User) is { } grantor
            ? grantor
            : Guid.TryParse(accessor.HttpContext?.User.FindFirstValue("sub") ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public Guid? ConnectionId => AppClaims.ConnectionOf(accessor.HttpContext?.User);
}
