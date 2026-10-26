using Microsoft.AspNetCore.DataProtection;
using Secret = BemAqui.SharedKernel.Secret;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>
/// Propósito <c>bemaqui → {escopo} → {tipo} → v1</c> (ADR 0007). As cadeias são fixas: mudar qualquer uma
/// deixa ilegíveis todos os segredos já gravados (há teste golden travando o formato).
/// </summary>
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

    /// <summary>Cadeia de propósitos de um segredo. Pública para o teste golden.</summary>
    public static string[] Purposes(SecretScope scope, SecretKind kind)
    {
        if (string.IsNullOrEmpty(scope.Value))
        {
            throw new ArgumentException("Escopo de segredo não inicializado (use SecretScope.Platform ou ForTenant).", nameof(scope));
        }

        return [RootPurpose, scope.Value, KindPurpose(kind), Version];
    }

    // Nome estável por tipo, independente do nome do enum (renomear o enum não pode quebrar segredos).
    private static string KindPurpose(SecretKind kind) => kind switch
    {
        SecretKind.TenantConnection => "connection",
        SecretKind.SmtpPassword => "smtp",
        SecretKind.TotpSecret => "totp",
        SecretKind.AiApiKey => "ai-key",
        SecretKind.WebhookSecret => "webhook-secret",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de segredo sem propósito definido."),
    };

    private IDataProtector Protector(SecretScope scope, SecretKind kind)
    {
        var purposes = Purposes(scope, kind);
        return provider.CreateProtector(purposes[0], purposes[1..]);
    }
}
