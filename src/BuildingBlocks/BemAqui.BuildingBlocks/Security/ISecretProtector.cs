using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>
/// Cifra e decifra segredos com Data Protection, separando por escopo (plataforma/tenant) e tipo.
/// Decifrar com escopo ou tipo diferentes falha com <see cref="System.Security.Cryptography.CryptographicException"/>.
/// </summary>
public interface ISecretProtector
{
    string Protect(SecretScope scope, SecretKind kind, Secret secret);

    Secret Unprotect(SecretScope scope, SecretKind kind, string protectedValue);
}
