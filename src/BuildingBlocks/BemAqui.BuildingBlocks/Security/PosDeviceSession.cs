using System.Globalization;
using System.Security.Claims;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>
/// Cookie do aparelho de PDV (PDV-01, ADR 0048 §2): <c>__Host-bq_pos</c>, HttpOnly, Secure, SameSite=Strict, Path=/; só o hash do segredo no
/// banco do tenant. É separado do cookie do aparelho da fábrica (<c>__Host-bq_dev</c>): um caixa nunca vira tablet do <c>/fab</c> nem o contrário.
/// </summary>
public static class PosDeviceCookie
{
    public const string Name = "__Host-bq_pos";
}

/// <summary>
/// Cabeçalhos da assinatura do aparelho (ADR 0048 §2): o aparelho assina, com a chave ECDSA P-256 não exportável dele, o texto
/// <c>{MÉTODO}\n{caminho com a query}\n{instante em ms}\n{SHA-256 do corpo em base64}</c>; a assinatura vai em base64, no formato do WebCrypto
/// (r||s, IEEE P1363).
/// </summary>
public static class DeviceSignatureHeaders
{
    public const string Timestamp = "X-Device-Timestamp";
    public const string Signature = "X-Device-Signature";

    /// <summary>Tolerância do relógio do aparelho para o instante assinado.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>Texto assinado. O mesmo cálculo roda no aparelho (src/Web/src/pdv/device-signing.ts).</summary>
    public static string Payload(string method, string pathAndQuery, string timestamp, string bodySha256Base64) =>
        string.Join('\n', method.ToUpperInvariant(), pathAndQuery, timestamp, bodySha256Base64);
}

/// <summary>Situação do aparelho que se apresentou (ADR 0048 §3).</summary>
public enum DeviceAuthStatus
{
    /// <summary>Sem cookie, segredo desconhecido, assinatura ou instante inválidos.</summary>
    Invalid,

    Active,

    /// <summary>
    /// Revogado ou substituído, com o segredo e a assinatura dele: só os endpoints de subida (marcados <c>AllowRevoked</c>) aceitam, para nenhuma
    /// venda já gravada se perder; os eventos entram em quarentena (OFF-02).
    /// </summary>
    Revoked,
}

/// <summary>O aparelho autenticado: tudo vem do banco, nunca do corpo (CA 10).</summary>
public sealed record DeviceIdentity(Guid DeviceId, string Kind, Guid? StoreId, Guid? RegisterId, int Epoch, bool Revoked);

/// <summary>Claims do principal do aparelho: nunca <c>sub</c> (não é usuário), nunca permissão.</summary>
public static class DeviceClaims
{
    public const string Scheme = "Device";
    public const string Policy = "device";
    public const string Tenant = "tid";
    public const string DeviceId = "device_id";
    public const string Kind = "device_kind";
    public const string StoreId = "device_store";
    public const string RegisterId = "device_register";
    public const string Epoch = "device_epoch";
    public const string Revoked = "device_revoked";
}

/// <summary>Monta e lê o principal do aparelho.</summary>
public static class DevicePrincipal
{
    public static ClaimsPrincipal Create(Guid tenantId, DeviceIdentity device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var claims = new List<Claim>
        {
            new(DeviceClaims.Tenant, tenantId.ToString()),
            new(DeviceClaims.DeviceId, device.DeviceId.ToString()),
            new(DeviceClaims.Kind, device.Kind),
            new(DeviceClaims.Epoch, device.Epoch.ToString(CultureInfo.InvariantCulture)),
            new(DeviceClaims.Revoked, device.Revoked ? "true" : "false"),
        };
        if (device.StoreId is { } store)
        {
            claims.Add(new Claim(DeviceClaims.StoreId, store.ToString()));
        }

        if (device.RegisterId is { } register)
        {
            claims.Add(new Claim(DeviceClaims.RegisterId, register.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, DeviceClaims.Scheme, DeviceClaims.DeviceId, null));
    }

    /// <summary>O aparelho do principal, ou null quando o principal não é de aparelho.</summary>
    public static DeviceIdentity? From(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity?.AuthenticationType != DeviceClaims.Scheme || !Guid.TryParse(user.FindFirst(DeviceClaims.DeviceId)?.Value, out var id))
        {
            return null;
        }

        return new DeviceIdentity(
            id,
            user.FindFirst(DeviceClaims.Kind)?.Value ?? string.Empty,
            Guid.TryParse(user.FindFirst(DeviceClaims.StoreId)?.Value, out var store) ? store : null,
            Guid.TryParse(user.FindFirst(DeviceClaims.RegisterId)?.Value, out var register) ? register : null,
            int.TryParse(user.FindFirst(DeviceClaims.Epoch)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch) ? epoch : 0,
            user.FindFirst(DeviceClaims.Revoked)?.Value == "true");
    }
}
