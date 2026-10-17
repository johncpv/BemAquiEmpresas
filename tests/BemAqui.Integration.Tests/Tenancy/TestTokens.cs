using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>Emite JWTs de teste com a chave configurada no Host de teste (a emissão real é a FND-04).</summary>
public static class TestTokens
{
    public static readonly byte[] SigningKey = Enumerable.Range(1, 32).Select(i => (byte)(i * 7)).ToArray();

    public static string SigningKeyBase64 { get; } = Convert.ToBase64String(SigningKey);

    public static string Create(Guid? tenantId, string audience, DateTime? expires = null, string issuer = "bemaqui-empresas", byte[]? key = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = Guid.CreateVersion7().ToString() };
        if (tenantId is { } tid)
        {
            claims["tid"] = tid.ToString();
        }

        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            NotBefore = (expires ?? now.AddMinutes(15)) < now ? now.AddMinutes(-30) : now.AddMinutes(-1),
            IssuedAt = (expires ?? now.AddMinutes(15)) < now ? now.AddMinutes(-30) : now.AddMinutes(-1),
            Expires = expires ?? now.AddMinutes(15),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key ?? SigningKey), SecurityAlgorithms.HmacSha256),
        });
    }

    public static ClaimsIdentity Empty => new();
}
