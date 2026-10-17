using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Security;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests;

/// <summary>FND-11: key ring no PostgreSQL protegido por certificado (CA 2, 3, 4 e 7).</summary>
[Collection(PostgresTestGroup.Name)]
public class DataProtectionKeyRingTests(PostgresFixture postgres)
{
    private static readonly SecretScope Tenant = SecretScope.ForTenant(Guid.CreateVersion7());

    [Fact]
    public async Task Chaves_ficam_no_catalogo_cifradas_pelo_certificado()
    {
        var db = await postgres.CreateDatabaseAsync("dp_ca2");
        await using var provider = await SecretsHarness.StartedAsync(db);

        await using var scope = provider.CreateAsyncScope();
        var keys = await scope.ServiceProvider.GetRequiredService<DataProtectionKeysDbContext>().DataProtectionKeys.ToListAsync(TestContext.Current.CancellationToken);

        keys.Should().NotBeEmpty();
        keys.Should().AllSatisfy(k =>
        {
            k.Xml.Should().Contain("EncryptedData").And.Contain("X509Data");
            k.Xml.Should().NotContain("<value>", "a chave mestra nunca fica em texto claro");
        });
    }

    [Fact]
    public async Task Key_ring_vazio_sem_autorizacao_recusa_subir()
    {
        var db = await postgres.CreateDatabaseAsync("dp_ca3a");
        await using var provider = SecretsHarness.Build(db, TestCertificates.Primary, allowCreation: false);

        var subir = () => SecretsHarness.StartAsync(provider);

        (await subir.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*vazio*AllowKeyRingCreation*");
    }

    [Fact]
    public async Task Banco_inacessivel_recusa_subir()
    {
        await using var provider = SecretsHarness.Build("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2", TestCertificates.Primary, allowCreation: true);

        var subir = () => SecretsHarness.StartAsync(provider);

        (await subir.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*inacessível*");
    }

    [Fact]
    public void Sem_certificado_fora_de_desenvolvimento_recusa_configurar()
    {
        var configurar = () => SecretsHarness.Build("Host=localhost;Database=x", certificatePath: null, allowCreation: true);

        configurar.Should().Throw<InvalidOperationException>().WithMessage("*CertificatePath*");
    }

    [Fact]
    public void Certificado_configurado_mas_ausente_recusa_configurar()
    {
        var configurar = () => SecretsHarness.Build("Host=localhost;Database=x", Path.Combine(Path.GetTempPath(), "nao-existe.pfx"), allowCreation: true);

        configurar.Should().Throw<InvalidOperationException>().WithMessage("*não encontrado*");
    }

    [Fact]
    public async Task Key_ring_de_outro_certificado_recusa_subir()
    {
        var db = await postgres.CreateDatabaseAsync("dp_ca3b");
        await (await SecretsHarness.StartedAsync(db, TestCertificates.Primary)).DisposeAsync();

        await using var provider = SecretsHarness.Build(db, TestCertificates.Other, allowCreation: false);
        var subir = () => SecretsHarness.StartAsync(provider);

        (await subir.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*não decifra com o certificado*");
    }

    [Fact]
    public async Task Rotacao_mantem_segredos_antigos_e_grava_com_a_chave_nova()
    {
        var db = await postgres.CreateDatabaseAsync("dp_ca4");
        Guid oldKeyId;
        string oldCipher;
        await using (var before = await SecretsHarness.StartedAsync(db))
        {
            oldKeyId = before.GetRequiredService<IKeyManager>().GetAllKeys().Single().KeyId;
            oldCipher = before.GetRequiredService<ISecretProtector>().Protect(Tenant, SecretKind.SmtpPassword, new Secret("antiga"));

            // Simula os 90 dias: a chave antiga passa a estar expirada no key ring persistido.
            await using var scope = before.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DataProtectionKeysDbContext>().Database.ExecuteSqlRawAsync(
                """
                UPDATE security.data_protection_keys
                SET xml = regexp_replace(regexp_replace(xml,
                    '<activationDate>[^<]+</activationDate>', '<activationDate>2000-01-01T00:00:00Z</activationDate>'),
                    '<expirationDate>[^<]+</expirationDate>', '<expirationDate>2000-04-01T00:00:00Z</expirationDate>')
                """,
                TestContext.Current.CancellationToken);
        }

        // Próxima subida (ex.: deploy depois da rotação): a chave nova é criada e vira a padrão.
        await using var after = await SecretsHarness.StartedAsync(db, allowCreation: false);
        var protector = after.GetRequiredService<ISecretProtector>();
        var newCipher = protector.Protect(Tenant, SecretKind.SmtpPassword, new Secret("nova"));

        protector.Unprotect(Tenant, SecretKind.SmtpPassword, oldCipher).Reveal().Should().Be("antiga");
        protector.Unprotect(Tenant, SecretKind.SmtpPassword, newCipher).Reveal().Should().Be("nova");

        // O cabeçalho do payload traz o id da chave usada.
        KeyIdOf(oldCipher).Should().Be(oldKeyId);
        KeyIdOf(newCipher).Should().NotBe(oldKeyId);
        after.GetRequiredService<IKeyManager>().GetAllKeys().Select(k => k.KeyId).Should().Contain(KeyIdOf(newCipher));
    }

    /// <summary>Payload do Data Protection: 4 bytes de magic header + 16 bytes do id da chave.</summary>
    private static Guid KeyIdOf(string protectedValue) =>
        new(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(protectedValue).AsSpan(4, 16));

    [Fact]
    public async Task Restauracao_com_key_ring_decifra_e_sem_key_ring_nao()
    {
        var db = await postgres.CreateDatabaseAsync("dp_ca7");
        string cipher;
        await using (var original = await SecretsHarness.StartedAsync(db))
        {
            cipher = original.GetRequiredService<ISecretProtector>().Protect(Tenant, SecretKind.TenantConnection, new Secret("Host=tenant-a"));
        }

        // Outra máquina com o banco restaurado e a cópia offline do certificado: decifra.
        await using (var restored = await SecretsHarness.StartedAsync(db, allowCreation: false))
        {
            restored.GetRequiredService<ISecretProtector>().Unprotect(Tenant, SecretKind.TenantConnection, cipher).Reveal().Should().Be("Host=tenant-a");
        }

        // Backup restaurado sem o key ring: o Host recusa subir.
        await using (var cleaner = await SecretsHarness.StartedAsync(db))
        await using (var scope = cleaner.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DataProtectionKeysDbContext>().DataProtectionKeys.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        await using var withoutKeys = SecretsHarness.Build(db, TestCertificates.Primary, allowCreation: false);
        var subir = () => SecretsHarness.StartAsync(withoutKeys);
        await subir.Should().ThrowAsync<InvalidOperationException>();

        // E mesmo forçando um key ring novo, o segredo antigo não decifra.
        await using var recreated = await SecretsHarness.StartedAsync(db, allowCreation: true);
        var decifrar = () => recreated.GetRequiredService<ISecretProtector>().Unprotect(Tenant, SecretKind.TenantConnection, cipher);
        decifrar.Should().Throw<CryptographicException>();
    }
}
