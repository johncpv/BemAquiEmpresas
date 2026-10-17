using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Security;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Secret = BemAqui.SharedKernel.Secret;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>FND-11 CA 1: o propósito separa tenant e tipo de segredo.</summary>
public class SecretProtectorTests
{
    private static readonly SecretScope TenantA = SecretScope.ForTenant(Guid.CreateVersion7());
    private static readonly SecretScope TenantB = SecretScope.ForTenant(Guid.CreateVersion7());

    private readonly SecretProtector _protector = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Mesmo_escopo_e_tipo_decifra()
    {
        var cipher = _protector.Protect(TenantA, SecretKind.SmtpPassword, new Secret("smtp-pass"));

        cipher.Should().NotContain("smtp-pass");
        _protector.Unprotect(TenantA, SecretKind.SmtpPassword, cipher).Reveal().Should().Be("smtp-pass");
    }

    [Fact]
    public void Outro_tenant_nao_decifra()
    {
        var cipher = _protector.Protect(TenantA, SecretKind.SmtpPassword, new Secret("smtp-pass"));

        var decifrar = () => _protector.Unprotect(TenantB, SecretKind.SmtpPassword, cipher);

        decifrar.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Outro_tipo_nao_decifra()
    {
        var cipher = _protector.Protect(TenantA, SecretKind.SmtpPassword, new Secret("smtp-pass"));

        var decifrar = () => _protector.Unprotect(TenantA, SecretKind.AiApiKey, cipher);

        decifrar.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Plataforma_e_tenant_sao_escopos_distintos()
    {
        var cipher = _protector.Protect(SecretScope.Platform, SecretKind.TenantConnection, new Secret("Host=x"));

        var decifrar = () => _protector.Unprotect(TenantA, SecretKind.TenantConnection, cipher);

        decifrar.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Tenant_vazio_e_recusado()
    {
        var criar = () => SecretScope.ForTenant(Guid.Empty);

        criar.Should().Throw<ArgumentException>();
    }
}
