using System.Globalization;
using System.Security.Claims;
using BemAqui.BuildingBlocks.Identity;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>
/// Sessão de quiosque da TV (ADR 0030 §1 e §2, ADR 0009 adendo de 21/10/2026): cookie opaco <c>__Host-bq_kiosk</c> (HttpOnly, Secure,
/// SameSite=Strict, Path=/), só o hash no banco do tenant. O esquema de autenticação <c>Kiosk</c> monta um principal só com o tenant, a
/// tela, a sessão e o escopo (linha e visões), sem usuário.
/// </summary>
public static class KioskCookie
{
    public const string Name = "__Host-bq_kiosk";
}

/// <summary>Claims do principal de quiosque: nunca <c>sub</c> (não é usuário), nunca permissão.</summary>
public static class KioskClaims
{
    public const string Scheme = "Kiosk";
    public const string Policy = "kiosk";
    public const string Tenant = "tid";
    public const string KioskId = "kiosk_id";
    public const string SessionId = "kiosk_session";
    public const string Name = "kiosk_name";
    public const string Line = "kiosk_line";
    public const string View = "kiosk_view";
    public const string Rotation = "kiosk_rotation";
}

/// <summary>Monta e lê o principal de quiosque (o escopo vem do banco do tenant, nunca do cliente).</summary>
public static class KioskPrincipal
{
    public static ClaimsPrincipal Create(Guid tenantId, KioskSessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var claims = new List<Claim>
        {
            new(KioskClaims.Tenant, tenantId.ToString()),
            new(KioskClaims.KioskId, session.KioskId.ToString()),
            new(KioskClaims.SessionId, session.SessionId.ToString()),
            new(KioskClaims.Name, session.Name),
            new(KioskClaims.Rotation, session.RotationSeconds.ToString(CultureInfo.InvariantCulture)),
        };
        if (session.Line is not null)
        {
            claims.Add(new Claim(KioskClaims.Line, session.Line));
        }

        claims.AddRange(session.Views.Select(v => new Claim(KioskClaims.View, v)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, KioskClaims.Scheme, KioskClaims.Name, null));
    }

    public static KioskSessionInfo From(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new KioskSessionInfo(
            Guid.Parse(user.FindFirst(KioskClaims.KioskId)!.Value),
            Guid.Parse(user.FindFirst(KioskClaims.SessionId)!.Value),
            user.FindFirst(KioskClaims.Name)?.Value ?? string.Empty,
            user.FindFirst(KioskClaims.Line)?.Value,
            user.FindAll(KioskClaims.View).Select(c => c.Value).ToList(),
            int.TryParse(user.FindFirst(KioskClaims.Rotation)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rotation) ? rotation : 20);
    }
}
