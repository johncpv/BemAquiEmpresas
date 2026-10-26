using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using BemAqui.Finance.Infrastructure.Inter;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BemAqui.Integration.Tests.Finance.BankIntegrationFixture;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// BNK-01a, integração (Testcontainers + fake do Inter com o certificado do mTLS de cada conta): salvar com o 2FA de novo, ler sem
/// segredo e o canário (CA 3 a 5, 18), três camadas e IDOR (CA 6, 17), escopos (CA 7, 8), produção (CA 12), troca (CA 9, 14),
/// desligar (CA 16), vencimento (CA 15) e log sem segredo (CA 11).
/// </summary>
public partial class BankIntegrationTests(BankIntegrationFixture fixture) : IClassFixture<BankIntegrationFixture>
{
    private const string Canary = "CANARIO-SECRET-7f3a91";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    /// <summary>Proprietário com 2FA recente, uma conta 077 nova e um client id próprio no fake.</summary>
    private async Task<(HttpClient Owner, Guid Account, string ClientId)> SetupAsync(string? secret = null)
    {
        var (_, owner, _) = await fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: true, recentTwoFactor: true);
        var account = await BankAccountAsync(owner);
        var clientId = "cli-" + Guid.NewGuid().ToString("N")[..12];
        fixture.Inter.Account(clientId).ClientSecret = secret ?? Canary;
        return (owner, account, clientId);
    }

    private static async Task<JsonElement> SaveAsync(HttpClient client, Guid account, string clientId, string secret, TestInterCertificate certificate, string environment = "Sandbox", bool confirm = false, bool pfx = false)
    {
        var response = await client.PutAsync($"{Base}/{account}", Form(clientId, secret, certificate, environment, pfx, confirm));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    [Fact]
    public async Task Salvar_com_2FA_de_novo_le_sem_segredo_e_o_canario_nao_aparece_em_lugar_nenhum()
    {
        var (owner, account, clientId) = await SetupAsync();
        var (ownerId, _, stamp) = await fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: true, recentTwoFactor: false);
        var withoutProof = fixture.Client(fixture.Tenancy.TenantA, HostA, ownerId, stamp, otpAt: null);
        var certificate = TestInterCertificate.Create();

        // CA 1 e 3: sem a prova recente, 403 two_factor_required e nada gravado.
        var refused = await withoutProof.PutAsync($"{Base}/{account}", Form(clientId, Canary, certificate));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(refused)).Should().Be("two_factor_required");
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.BankIntegrations.AnyAsync(i => i.BankAccountId == account))).Should().BeFalse();

        // Primeira configuração: Sandbox e a conta corrente sugeridos (propositivo).
        var empty = await JsonAsync(await owner.GetAsync($"{Base}/{account}"));
        empty.GetProperty("configured").GetBoolean().Should().BeFalse();
        empty.GetProperty("environment").GetString().Should().Be("Sandbox");
        empty.GetProperty("accountNumber").GetString().Should().Be("12345678");

        var saved = await SaveAsync(owner, account, clientId, Canary, certificate);
        saved.GetProperty("configured").GetBoolean().Should().BeTrue();
        saved.GetProperty("situation").GetString().Should().Be("Sandbox");
        saved.GetProperty("clientIdMasked").GetString().Should().Be("•••• " + clientId[^4..]);
        saved.GetProperty("certificate").GetProperty("thumbprint").GetString().Should().Be(certificate.Thumbprint);
        saved.GetProperty("configuredByName").ValueKind.Should().NotBe(JsonValueKind.Undefined);
        saved.GetProperty("missingScopes").GetArrayLength().Should().Be(0);

        // CA 5: o segredo nunca volta (detalhe e lista), nem o PEM, a chave ou o client id inteiro.
        var detail = await owner.GetStringAsync($"{Base}/{account}");
        var list = await owner.GetStringAsync(Base);
        foreach (var text in new[] { detail, list, saved.GetRawText() })
        {
            text.Should().NotContain(Canary).And.NotContain("PRIVATE KEY").And.NotContain("BEGIN CERTIFICATE").And.NotContain(clientId).And.NotContain("Cipher");
        }

        // Canário no banco: só ciphertext; nunca na auditoria nem no log.
        var row = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.BankIntegrations.AsNoTracking().SingleAsync(i => i.BankAccountId == account));
        new[] { row.ClientIdCipher, row.ClientSecretCipher, row.CertificateCipher, row.PrivateKeyCipher }.Should().OnlyContain(c => c != null && !c.Contains(Canary) && !c.Contains("PRIVATE") && !c.Contains(clientId));
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.BankAccounts.Where(a => a.Id == account).Select(a => a.Provider).SingleAsync())).Should().Be(BankProvider.Inter);
        var audit = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.AuditEntries.AsNoTracking().Where(e => e.Entity == nameof(BankIntegration) && e.EntityId == row.Id.ToString()).Select(e => e.Changes).ToListAsync());
        audit.Should().NotBeEmpty();
        audit.Should().OnlyContain(c => !c.Contains(Canary) && !c.Contains(row.ClientSecretCipher!) && !c.Contains("PRIVATE KEY") && !c.Contains(clientId));
        fixture.RenderedLogs().Should().NotContain(Canary).And.NotContain("PRIVATE KEY").And.NotContain("tok-");

        // CA 18: o histórico mostra o que mudou, sem os valores.
        var history = await owner.GetAsync($"/api/v1/audit/BankIntegration/{row.Id}");
        history.StatusCode.Should().Be(HttpStatusCode.OK, await history.Content.ReadAsStringAsync());
        (await history.Content.ReadAsStringAsync()).ToLowerInvariant().Should().NotContain(Canary.ToLowerInvariant()).And.Contain("environment");

        // CA 3: o Proprietário é avisado; salvar de novo pede "Trocar credenciais".
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.Notifications.AnyAsync(n => n.Link == BankIntegrationService.Link(account) && n.Title.Contains("ligada por")))).Should().BeTrue();
        var again = await owner.PutAsync($"{Base}/{account}", Form(clientId, Canary, certificate));
        Code(await JsonAsync(again)).Should().Be("bank_integration_configured");

        // O .pfx com senha também vale (CA 4), noutra conta.
        var other = await BankAccountAsync(owner);
        var pfx = await SaveAsync(owner, other, clientId, Canary, TestInterCertificate.Create(), pfx: true);
        pfx.GetProperty("certificate").GetProperty("format").GetString().Should().Be("Pfx");
    }

    [Fact]
    public async Task Certificado_invalido_e_arquivo_fora_do_formato_recusam_com_422_sem_gravar()
    {
        var (owner, account, clientId) = await SetupAsync();
        var certificate = TestInterCertificate.Create();
        var wrongKey = Form(clientId, Canary, null);
        wrongKey.Add(new ByteArrayContent(certificate.CertificateBytes), "certificate", "a.crt");
        wrongKey.Add(new ByteArrayContent(TestInterCertificate.Create().KeyBytes), "privateKey", "a.key");
        var mismatch = await owner.PutAsync($"{Base}/{account}", wrongKey);
        mismatch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(mismatch)).GetProperty("detail").GetString().Should().Be("A chave privada não corresponde a este certificado.");

        var exe = Form(clientId, Canary, null);
        exe.Add(new ByteArrayContent(certificate.CertificateBytes), "certificate", "virus.exe");
        exe.Add(new ByteArrayContent(certificate.KeyBytes), "privateKey", "a.key");
        (await JsonAsync(await owner.PutAsync($"{Base}/{account}", exe))).GetProperty("detail").GetString().Should().Be("Envie só .crt, .pem, .key ou .pfx, com até 20 KB cada.");

        var big = Form(clientId, Canary, null);
        big.Add(new ByteArrayContent(new byte[21 * 1024]), "certificate", "a.crt");
        big.Add(new ByteArrayContent(certificate.KeyBytes), "privateKey", "a.key");
        (await owner.PutAsync($"{Base}/{account}", big)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var refusedByBank = await owner.PutAsync($"{Base}/{account}", Form(clientId, "segredo-errado", certificate));
        refusedByBank.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(refusedByBank)).Should().Be("bank_connection_refused");
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.BankIntegrations.AnyAsync(i => i.BankAccountId == account))).Should().BeFalse("nada é gravado na recusa");
    }

    [Fact]
    public async Task Credencial_com_escopo_de_pagamento_e_recusada_ao_salvar_e_ao_testar()
    {
        var (owner, account, clientId) = await SetupAsync();
        fixture.Inter.Account(clientId).Granted += " pagamento-pix.write";

        var refused = await owner.PutAsync($"{Base}/{account}", Form(clientId, Canary, TestInterCertificate.Create()));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be("bank_credential_can_pay");
        problem.GetProperty("detail").GetString().Should().Be("Esta credencial pode fazer pagamentos. Gere no Inter uma credencial só com cobrança, extrato e webhook.");
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.BankIntegrations.AnyAsync(i => i.BankAccountId == account))).Should().BeFalse("nada é gravado (D13)");
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.BankAccounts.Where(a => a.Id == account).Select(a => a.Provider).SingleAsync())).Should().Be(BankProvider.None);

        // Gravada limpa e o Inter passa a conceder pagamento: o teste recusa com 422.
        fixture.Inter.Account(clientId).Granted = "boleto-cobranca.read boleto-cobranca.write extrato.read webhook.read webhook.write";
        await SaveAsync(owner, account, clientId, Canary, TestInterCertificate.Create());
        fixture.Inter.Account(clientId).Granted += " pagamento-boleto.write";
        var test = await owner.PostAsync($"{Base}/{account}/test", null);
        test.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(test)).Should().Be("bank_credential_can_pay");
    }

    [Fact]
    public async Task Testar_conexao_le_o_saldo_e_mostra_os_escopos_que_faltam()
    {
        var (owner, account, clientId) = await SetupAsync();
        var certificate = TestInterCertificate.Create();
        await SaveAsync(owner, account, clientId, Canary, certificate);

        var ok = await JsonAsync(await owner.PostAsync($"{Base}/{account}/test", null));
        ok.GetProperty("ok").GetBoolean().Should().BeTrue();
        ok.GetProperty("message").GetString().Should().Be("Conexão com o Inter funcionando");
        ok.GetProperty("grantedScopes").EnumerateArray().Select(s => s.GetString()).Should().Contain("extrato.read");
        var balance = fixture.Inter.Calls.Last(c => c.Path == "/" + InterEnvironments.BalancePath);
        balance.CertificateThumbprint.Should().Be(certificate.Thumbprint, "mTLS com o certificado da conta");
        balance.Host.Should().Be(InterEnvironments.Sandbox.Host, "URL base fixa do ambiente");
        balance.AccountHeader.Should().Be("12345678");

        fixture.Inter.Account(clientId).Granted = "boleto-cobranca.read boleto-cobranca.write extrato.read";
        var missing = await JsonAsync(await owner.PostAsync($"{Base}/{account}/test", null));
        missing.GetProperty("ok").GetBoolean().Should().BeFalse();
        missing.GetProperty("outcome").GetString().Should().Be("MissingScopes");
        missing.GetProperty("missingScopes").EnumerateArray().Select(s => s.GetString()).Should().Equal("webhook.read", "webhook.write");
        missing.GetProperty("action").GetString().Should().Contain("Internet Banking do Inter");
        var detail = await JsonAsync(await owner.GetAsync($"{Base}/{account}"));
        detail.GetProperty("lastTest").GetProperty("outcome").GetString().Should().Be("MissingScopes");

        fixture.Inter.Account(clientId).BalanceStatus = HttpStatusCode.ServiceUnavailable;
        fixture.Inter.Account(clientId).Granted = "boleto-cobranca.read boleto-cobranca.write extrato.read webhook.read webhook.write";
        var down = await JsonAsync(await owner.PostAsync($"{Base}/{account}/test", null));
        down.GetProperty("outcome").GetString().Should().Be("Refused");
        down.GetProperty("message").GetString().Should().Be("O Inter está com instabilidade.");
    }

    [Fact]
    public async Task Tres_camadas_modo_leitura_e_IDOR()
    {
        var (owner, account, clientId) = await SetupAsync();
        await SaveAsync(owner, account, clientId, Canary, TestInterCertificate.Create());
        var (_, financeEditor, _) = await fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: false, recentTwoFactor: true, "finance.view", "finance.edit");
        var (_, settingsViewer, _) = await fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: false, recentTwoFactor: true, "settings.view");
        var (_, nobody, _) = await fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: false, recentTwoFactor: true, "parties.view");

        // CA 6: Financeiro (finance.edit) só lê; os botões de escrita respondem 403 forbidden.
        (await financeEditor.GetAsync($"{Base}/{account}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await settingsViewer.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await settingsViewer.GetStringAsync($"{Base}/{account}")).Should().NotContain(Canary);
        foreach (var response in new[]
        {
            await financeEditor.PutAsync($"{Base}/{account}", Form(clientId, Canary, TestInterCertificate.Create())),
            await financeEditor.PostAsync($"{Base}/{account}/rotate", Form(clientId, Canary, TestInterCertificate.Create())),
            await financeEditor.PostAsync($"{Base}/{account}/test", null),
            await financeEditor.DeleteAsync($"{Base}/{account}"),
        })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(response)).Should().Be("forbidden");
        }

        Code(await JsonAsync(await nobody.GetAsync(Base))).Should().Be("forbidden");

        // CA 17: IDOR (conta que não existe neste tenant, conta do tenant B, conta de outro banco): 404.
        (await owner.GetAsync($"{Base}/{Guid.CreateVersion7()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var (_, ownerB, _) = await fixture.ActorAsync(fixture.Tenancy.TenantB, HostB, owner: true, recentTwoFactor: true);
        var accountB = await BankAccountAsync(ownerB);
        (await owner.GetAsync($"{Base}/{accountB}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PutAsync($"{Base}/{accountB}", Form(clientId, Canary, TestInterCertificate.Create()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ownerB.GetAsync($"{Base}/{account}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var itau = await BankAccountAsync(owner, "341");
        (await owner.GetAsync($"{Base}/{itau}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JsonAsync(await owner.GetAsync(Base))).GetProperty("items").EnumerateArray().Should().NotContain(r => r.GetProperty("bankAccountId").GetGuid() == itau);

        // Módulo não liberado (settings ou finance): 403 module_not_released.
        foreach (var module in new[] { "settings", "finance" })
        {
            await fixture.SetModuleAsync(fixture.Tenancy.TenantA, module, false);
            try
            {
                Code(await JsonAsync(await owner.GetAsync(Base))).Should().Be("module_not_released", module);
            }
            finally
            {
                await fixture.SetModuleAsync(fixture.Tenancy.TenantA, module, true);
            }
        }
    }
}
