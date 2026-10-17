using BemAqui.SharedKernel;
using Secret = BemAqui.SharedKernel.Secret;
using Microsoft.AspNetCore.DataProtection;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>Propósito <c>bemaqui → {escopo} → {tipo} → v1</c> (ADR 0007).</summary>
public sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    public const string RootPurpose = "bemaqui";
    public const string Version = "v1";

    public string Protect(SecretScope scope, SecretKind kind, Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Protector(scope, kind).Protect(secret.Reveal());
    }

    public Secret Unprotect(SecretScope scope, SecretKind kind, string protectedValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedValue);
        return new Secret(Protector(scope, kind).Unprotect(protectedValue));
    }

    private IDataProtector Protector(SecretScope scope, SecretKind kind) =>
        provider.CreateProtector(RootPurpose, scope.Value, kind.ToString(), Version);
}
