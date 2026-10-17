using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>Seção <c>Auth</c>: emissor e chave HMAC dos access tokens (secret; mínimo 32 bytes em base64).</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Issuer { get; set; } = "bemaqui-empresas";

    public string? SigningKey { get; set; }

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Refresh já substituído ainda aceito por este tempo (duas abas renovando juntas; ADR 0010).</summary>
    public TimeSpan RefreshReuseTolerance { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Chave única de assinatura, compartilhada entre a emissão (Identity) e a validação (Host).</summary>
public sealed class TokenSigning
{
    public TokenSigning(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        Options = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        Key = CreateKey(Options, environment);
    }

    public AuthOptions Options { get; }

    public SymmetricSecurityKey Key { get; }

    private static SymmetricSecurityKey CreateKey(AuthOptions options, IHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(options.SigningKey))
        {
            var bytes = Convert.FromBase64String(options.SigningKey);
            return bytes.Length >= 32
                ? new SymmetricSecurityKey(bytes)
                : throw new InvalidOperationException("Auth:SigningKey precisa ter pelo menos 32 bytes (base64).");
        }

        if (environment.IsDevelopment())
        {
            return new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)); // só desenvolvimento: muda a cada subida
        }

        throw new InvalidOperationException("Auth:SigningKey não configurada (secret obrigatório fora de Development).");
    }
}
