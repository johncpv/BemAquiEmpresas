using System.Security.Cryptography;
using System.Text;
using BemAqui.SharedKernel;

namespace BemAqui.Parties.Domain;

/// <summary>Canal do código de identidade (INT-04 CA 12).</summary>
public enum IdentityChallengeChannel
{
    Email,
    Phone,
}

/// <summary>Situação do desafio: <c>Pending</c> → <c>Verified</c> | <c>Dead</c> (5 tentativas erradas). Vencido é calculado por <see cref="IdentityChallenge.ExpiresAt"/>.</summary>
public enum IdentityChallengeStatus
{
    Pending,
    Verified,
    Dead,
}

/// <summary>
/// Código de identidade de 6 dígitos (INT-04 CA 12 e 13, decisão do PO 1): o aplicativo pede, o ERP manda ao e-mail ou telefone que já está no cadastro e
/// não veio de aplicativo, e o cliente diz o código ao atendente. Guarda só o HMAC do código (com sal próprio), nunca o código nem o destino; vale 10 min,
/// aceita no máximo 5 tentativas e é comparado em tempo constante.
/// </summary>
public sealed class IdentityChallenge : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxAttempts = 5;
    public const int CodeLength = 6;

    public Guid PartyId { get; init; }

    public IdentityChallengeChannel Channel { get; init; }

    /// <summary>Aplicativo (<c>connected_apps.id</c>) que pediu: só ele confere o código.</summary>
    public Guid AppId { get; init; }

    /// <summary>Conexão que pediu (limite de 20 por hora por conexão).</summary>
    public Guid ConnectionId { get; init; }

    public byte[] Salt { get; init; } = [];

    public byte[] CodeHash { get; init; } = [];

    public IdentityChallengeStatus Status { get; set; } = IdentityChallengeStatus.Pending;

    public int Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>Código novo de 6 dígitos (gerador criptográfico).</summary>
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    public static IdentityChallenge Create(Guid partyId, IdentityChallengeChannel channel, Guid appId, Guid connectionId, string code, DateTimeOffset now)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return new IdentityChallenge
        {
            PartyId = partyId, Channel = channel, AppId = appId, ConnectionId = connectionId, Salt = salt, CodeHash = Hash(salt, code), CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
    }

    /// <summary>Confere em tempo constante (o código informado é normalizado: só dígitos).</summary>
    public bool Matches(string? code)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        var candidate = Hash(Salt, digits.Length == CodeLength ? digits : "x");
        return CryptographicOperations.FixedTimeEquals(candidate, CodeHash);
    }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    private static byte[] Hash(byte[] salt, string code) => HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(code));
}
