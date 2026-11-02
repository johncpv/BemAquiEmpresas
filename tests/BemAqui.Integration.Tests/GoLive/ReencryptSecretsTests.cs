using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Fiscal.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.GoLive;

/// <summary>
/// GOL-01a CA 4, GOL-01b CA 5, ADR 0034 §2: <c>reencrypt-secrets</c> recifra um segredo de cada tipo do key ring antigo (homologação) para o
/// novo (produção); depois, todos decifram com o novo e nenhum com o antigo; a simulação não muda nada; rodar de novo não recifra nada.
/// </summary>
[Collection(GoLiveGroup.Name)]
public class ReencryptSecretsTests(GoLiveFixture golive)
{
    private sealed record Stored(string Schema, string Table, string Column, Guid RowId, SecretScope Scope, SecretKind Kind, Guid? Item, string Plain, bool Catalog = false);

    [Fact]
    public async Task Recifra_um_segredo_de_cada_tipo_e_o_antigo_nao_decifra_mais_nada()
    {
        var tenant = await golive.ProvisionAsync("recifra");
        var connection = golive.Tenancy.Decrypt(tenant);
        var stored = await SeedSecretsAsync(tenant.Id);
        (await golive.PromoteAsync("recifra")).Should().Be(0);
        stored.Add(await SeedPlatformAdminAsync());
        stored.Add(new Stored("catalog", "tenants", "connection_secret", tenant.Id, SecretScope.ForTenant(tenant.Id), SecretKind.TenantConnection, null, connection, Catalog: true));
        stored.Select(s => s.Kind).Distinct().Should().HaveCount(7, "um segredo de cada tipo gravado no banco");
        var keys = await golive.ExportOldKeyRingAsync();
        string[] common = ["reencrypt-secrets", "--tenant", "recifra", "--old-keys", keys, "--old-cert", TestCertificates.Primary, "--old-cert-password-file", TestCertificates.PasswordFile];
        var before = await RawAsync(stored, connection);

        var dry = new StringWriter();
        (await golive.RunNewAsync(dry, [.. common, "--dry-run"])).Should().Be(0, dry.ToString());
        (await RawAsync(stored, connection)).Should().Equal(before, "a simulação desfaz tudo");
        (await golive.RunNewAsync(new StringWriter(), common)).Should().Be(1, "gravar exige --confirm-tenant");

        var output = new StringWriter();
        (await golive.RunNewAsync(output, [.. common, "--confirm-tenant", "recifra"])).Should().Be(0, output.ToString());

        using var fresh = golive.NewSecrets();
        var current = fresh.GetRequiredService<ISecretProtector>();
        var after = await RawAsync(stored, connection);
        foreach (var (secret, cipher) in stored.Zip(after))
        {
            Reveal(current, secret, cipher).Should().Be(secret.Plain, secret.Column + " decifra com o key ring novo");
            var withOld = () => Reveal(golive.OldProtector, secret, cipher);
            withOld.Should().Throw<CryptographicException>(secret.Column + " não decifra mais com o key ring antigo");
        }

        var report = output.ToString();
        report.Should().Contain("0 decifram com o antigo");
        foreach (var secret in stored)
        {
            report.Should().NotContain(secret.Plain, "nenhum valor vai para a saída");
        }

        var again = new StringWriter();
        (await golive.RunNewAsync(again, [.. common, "--confirm-tenant", "recifra"])).Should().Be(0, again.ToString());
        (await RawAsync(stored, connection)).Should().Equal(after, "rodar de novo não recifra o que já está no key ring novo");
    }

    [Fact]
    public async Task Recusa_quando_o_key_ring_antigo_e_o_mesmo_do_ambiente()
    {
        var keys = await golive.ExportOldKeyRingAsync();
        var output = new StringWriter();

        var exit = await golive.RunOldAsync(output, "reencrypt-secrets", "--tenant", "empresa-a", "--old-keys", keys, "--old-cert", TestCertificates.Primary,
            "--old-cert-password-file", TestCertificates.PasswordFile, "--dry-run");

        exit.Should().Be(1, output.ToString());
        output.ToString().Should().Contain("Nada foi gravado");
    }

    private static string Reveal(ISecretProtector protector, Stored s, string cipher) =>
        (s.Item is { } item ? protector.Unprotect(s.Scope, s.Kind, item, cipher) : protector.Unprotect(s.Scope, s.Kind, cipher)).Reveal();

    private async Task<List<string>> RawAsync(IEnumerable<Stored> stored, string tenantConnection)
    {
        var values = new List<string>();
        foreach (var s in stored)
        {
            await using var db = await GoLiveFixture.OpenAsync(s.Catalog ? golive.NewCatalog : tenantConnection);
            await using var command = new NpgsqlCommand($"SELECT {s.Column} FROM {s.Schema}.{s.Table} WHERE id = $1", db) { Parameters = { new() { Value = s.RowId } } };
            values.Add((string)(await command.ExecuteScalarAsync())!);
        }

        return values;
    }

    private async Task<Stored> SeedPlatformAdminAsync()
    {
        var id = Guid.CreateVersion7();
        const string plain = "PLATAFORMATOTP234";
        await using var db = await GoLiveFixture.OpenAsync(golive.NewCatalog);
        await using var command = new NpgsqlCommand(
            "INSERT INTO catalog.platform_admins (id, access_failed_count, created_at, email, normalized_email, security_stamp, status, totp_enabled, totp_secret_cipher) "
            + "VALUES ($1, 0, now(), 'admin-recifra@teste.com', 'admin-recifra@teste.com', 'stamp', 'Active', true, $2)", db)
        {
            Parameters = { new() { Value = id }, new() { Value = golive.OldProtector.Protect(SecretScope.Platform, SecretKind.TotpSecret, new Secret(plain)) } },
        };
        await command.ExecuteNonQueryAsync();
        return new Stored("catalog", "platform_admins", "totp_secret_cipher", id, SecretScope.Platform, SecretKind.TotpSecret, null, plain, Catalog: true);
    }

    /// <summary>Um segredo de cada coluna cifrada do tenant, cifrado com o key ring antigo, gravado pelo EF como a aplicação grava.</summary>
    private async Task<List<Stored>> SeedSecretsAsync(Guid tenantId)
    {
        var old = golive.OldProtector;
        var scope = SecretScope.ForTenant(tenantId);
        string Protect(SecretKind kind, string plain) => old.Protect(scope, kind, new Secret(plain));
        string ProtectItem(SecretKind kind, Guid item, string plain) => old.Protect(scope, kind, item, new Secret(plain));
        var now = DateTimeOffset.UtcNow;

        var info = (await golive.Tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenantId))!;
        await using var tenantScope = await golive.Tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        var db = tenantScope.ServiceProvider.GetRequiredService<TenantDbContext>();

        var user = User.Invite("totp-recifra@konebras.com.br");
        user.BeginTotpSetup(Protect(SecretKind.TotpSecret, "TOTPSEGREDO234567"));
        db.Users.Add(user);

        var ai = new AiSettings { Provider = "anthropic", Model = "modelo", ApiKeyCipher = Protect(SecretKind.AiApiKey, "chave-ia-recifra"), KeySetAt = now, UpdatedAt = now };
        db.Set<AiSettings>().Add(ai);

        var fiscal = await db.Set<FiscalIntegrationSettings>().FirstOrDefaultAsync() ?? db.Set<FiscalIntegrationSettings>().Add(new FiscalIntegrationSettings { BaseUrl = "https://emissor.teste/" }).Entity;
        fiscal.SetApiKey(Protect(SecretKind.FiscalApiKey, "chave-emissor-recifra"), now);

        var webhook = new WebhookCredential { Provider = "golive-teste", ScopeId = Guid.Empty, Kind = WebhookCredentialKind.HmacSecret, CreatedAt = now };
        webhook.Rotate(null, Protect(SecretKind.WebhookSecret, "webhook-anterior"), null, now);
        webhook.Rotate(null, Protect(SecretKind.WebhookSecret, "webhook-atual"), null, now);
        db.Set<WebhookCredential>().Add(webhook);

        var account = new BankAccount { Name = "Inter recifra", Kind = BankAccountKind.Bank, CreatedAt = now };
        db.Set<BankAccount>().Add(account);
        var bank = BankIntegration.For(account.Id);
        bank.Configure(
            new BankIntegrationSecrets(ProtectItem(SecretKind.InterCredentials, bank.Id, "client-id"), "t-id", ProtectItem(SecretKind.InterCredentials, bank.Id, "client-secret"),
                ProtectItem(SecretKind.InterCertificate, bank.Id, "certificado-pem"), ProtectItem(SecretKind.InterCertificate, bank.Id, "chave-privada"),
                ProtectItem(SecretKind.InterCertificate, bank.Id, "senha-pfx")),
            new BankCertificateInfo(BankCertificateFormat.Pfx, now.AddYears(1), "ab", "CN=teste"), null, null, now);
        db.Set<BankIntegration>().Add(bank);
        await db.SaveChangesAsync();

        return
        [
            new("identity", "users", "totp_secret_cipher", user.Id, scope, SecretKind.TotpSecret, null, "TOTPSEGREDO234567"),
            new("identity", "ai_settings", "api_key_cipher", ai.Id, scope, SecretKind.AiApiKey, null, "chave-ia-recifra"),
            new("fiscal", "integration_settings", "api_key_cipher", fiscal.Id, scope, SecretKind.FiscalApiKey, null, "chave-emissor-recifra"),
            new("shared", "webhook_tokens", "secret_cipher", webhook.Id, scope, SecretKind.WebhookSecret, null, "webhook-atual"),
            new("shared", "webhook_tokens", "previous_secret_cipher", webhook.Id, scope, SecretKind.WebhookSecret, null, "webhook-anterior"),
            new("finance", "bank_integrations", "client_id_cipher", bank.Id, scope, SecretKind.InterCredentials, bank.Id, "client-id"),
            new("finance", "bank_integrations", "client_secret_cipher", bank.Id, scope, SecretKind.InterCredentials, bank.Id, "client-secret"),
            new("finance", "bank_integrations", "certificate_cipher", bank.Id, scope, SecretKind.InterCertificate, bank.Id, "certificado-pem"),
            new("finance", "bank_integrations", "private_key_cipher", bank.Id, scope, SecretKind.InterCertificate, bank.Id, "chave-privada"),
            new("finance", "bank_integrations", "certificate_password_cipher", bank.Id, scope, SecretKind.InterCertificate, bank.Id, "senha-pfx"),
        ];
    }
}
