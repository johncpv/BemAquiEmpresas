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

    public string Protect(SecretScope scope, SecretKind kind, Guid itemId, Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Protector(Purposes(scope, kind, itemId)).Protect(secret.Reveal());
    }

    public Secret Unprotect(SecretScope scope, SecretKind kind, Guid itemId, string protectedValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedValue);
        return new Secret(Protector(Purposes(scope, kind, itemId)).Unprotect(protectedValue));
    }

    /// <summary>
    /// Cadeia com o item depois do tipo: <c>bemaqui → tenant:{id} → inter-credentials → {integrationId} → v1</c> (ADR 0007 adendo de
    /// 21/10/2026). Pública para o teste golden.
    /// </summary>
    public static string[] Purposes(SecretScope scope, SecretKind kind, Guid itemId)
    {
        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("Item inválido para o propósito do segredo.", nameof(itemId));
        }

        var basic = Purposes(scope, kind);
        return [basic[0], basic[1], basic[2], itemId.ToString("N"), Version];
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
        SecretKind.InterCredentials => "inter-credentials",
        SecretKind.InterCertificate => "inter-certificate",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de segredo sem propósito definido."),
    };

    private IDataProtector Protector(SecretScope scope, SecretKind kind) => Protector(Purposes(scope, kind));

    private IDataProtector Protector(string[] purposes) => provider.CreateProtector(purposes[0], purposes[1..]);
}
