using System.Security.Claims;
using BemAqui.BuildingBlocks.Identity;

namespace BemAqui.Host.Tenancy;

/// <summary>Usuário do request: <c>sub</c> do token (FND-04) e IP real (forwarded headers confiáveis, FND-02b).</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue("sub") ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
