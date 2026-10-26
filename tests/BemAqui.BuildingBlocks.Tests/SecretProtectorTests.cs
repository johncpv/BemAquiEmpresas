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

    /// <summary>Golden: estas cadeias nunca mudam, senão todos os segredos gravados ficam ilegíveis.</summary>
    [Theory]
    [InlineData(SecretKind.TenantConnection, "connection")]
    [InlineData(SecretKind.SmtpPassword, "smtp")]
    [InlineData(SecretKind.TotpSecret, "totp")]
    [InlineData(SecretKind.AiApiKey, "ai-key")]
    [InlineData(SecretKind.WebhookSecret, "webhook-secret")]
    public void Proposito_de_cada_tipo_e_fixo(SecretKind kind, string expected)
    {
        var tenant = Guid.Parse("0199f1a2-0000-7000-8000-000000000001");

        SecretProtector.Purposes(SecretScope.ForTenant(tenant), kind)
            .Should().Equal("bemaqui", "tenant:0199f1a2000070008000000000000001", expected, "v1");
        SecretProtector.Purposes(SecretScope.Platform, kind).Should().Equal("bemaqui", "platform", expected, "v1");
    }

    [Fact]
    public void Todo_tipo_tem_proposito_definido()
    {
        foreach (var kind in Enum.GetValues<SecretKind>())
        {
            SecretProtector.Purposes(SecretScope.Platform, kind).Should().HaveCount(4);
        }
    }

    [Fact]
    public void Escopo_nao_inicializado_e_recusado()
    {
        var usar = () => _protector.Protect(default, SecretKind.SmtpPassword, new Secret("x"));

        usar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Tenant_vazio_e_recusado()
    {
        var criar = () => SecretScope.ForTenant(Guid.Empty);

        criar.Should().Throw<ArgumentException>();
    }
}
