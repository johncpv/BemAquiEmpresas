using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using BemAqui.Platform.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-01 CA 7 a 10, 12 e 18: credencial por tenant só de escrita com 2FA de novo, URL só da lista, ambiente pela marca do catálogo,
/// "Testar conexão" com escopos, CNPJ e ambiente, rotação testando a chave nova, alerta da chave recusada e as três camadas.
/// </summary>
public partial class FiscalTests(FiscalFixture fixture) : IClassFixture<FiscalFixture>
{
    private const string Integration = "/api/v1/fiscal/integration";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static object Body(string? code, string? apiKey = FiscalFixture.ApiKey, string? secret = FiscalFixture.WebhookSecret, string baseUrl = "http://emissor-fiscal:8080") =>
        new { baseUrl, apiKey, webhookSecret = secret, code };

    private Task<(Guid Id, HttpClient Client, Func<Task<string>> Code)> OwnerWithTotpAsync() => fixture.OwnerWithTotpAsync(fixture.Tenancy.TenantA, FiscalFixture.HostA);

    [Fact]
    public async Task Salvar_exige_2FA_de_novo_e_grava_a_chave_e_o_segredo_cifrados_sem_devolver_nem_auditar_o_valor()
    {
        await fixture.RemoveIntegrationAsync(fixture.Tenancy.TenantA);
        var (_, owner, code) = await OwnerWithTotpAsync();

        var noCode = await owner.PutAsJsonAsync(Integration, Body(null));
        noCode.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(noCode)).GetProperty("code").GetString().Should().Be("two_factor_required");
        (await fixture.FiscalAsync(fixture.Tenancy.TenantA, db => db.FiscalIntegrationSettings.AnyAsync())).Should().BeFalse("sem o código nada é gravado");

        var saved = await owner.PutAsJsonAsync(Integration, Body(await code()));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        saved.Headers.CacheControl!.NoStore.Should().BeTrue();
        var view = await saved.Content.ReadAsStringAsync();
        view.Should().NotContain(FiscalFixture.ApiKey).And.NotContain(FiscalFixture.WebhookSecret);
        var json = JsonDocument.Parse(view).RootElement;
        json.GetProperty("configured").GetBoolean().Should().BeTrue();
        json.GetProperty("active").GetBoolean().Should().BeFalse("só liga depois de um teste aprovado");
        json.GetProperty("apiKeySetAt").ValueKind.Should().Be(JsonValueKind.String);
        json.GetProperty("webhookSecretSetAt").ValueKind.Should().Be(JsonValueKind.String);
        json.GetProperty("webhookUrl").GetString().Should().Contain("/webhooks/emissor-fiscal/");

        var get = await (await owner.GetAsync(Integration)).Content.ReadAsStringAsync();
        get.Should().NotContain(FiscalFixture.ApiKey).And.NotContain(FiscalFixture.WebhookSecret);

        var cipher = await fixture.FiscalAsync(fixture.Tenancy.TenantA, db => db.FiscalIntegrationSettings.Select(s => s.ApiKeyCipher).SingleAsync());
        cipher.Should().NotContain(FiscalFixture.ApiKey);
        fixture.Factory.Services.GetRequiredService<ISecretProtector>().Unprotect(SecretScope.ForTenant(fixture.Tenancy.TenantA.Id), SecretKind.FiscalApiKey, cipher!).Reveal()
            .Should().Be(FiscalFixture.ApiKey);
        var audit = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.AuditEntries.AsNoTracking()
            .Where(a => a.Entity == nameof(FiscalIntegrationSettings) || a.Entity == "WebhookCredential").Select(a => a.Changes).ToListAsync());
        audit.Should().NotBeEmpty();
        string.Join("\n", audit).Should().NotContain(FiscalFixture.ApiKey).And.NotContain(FiscalFixture.WebhookSecret).And.NotContain(cipher!);
        fixture.AllLogs().Should().NotContain(FiscalFixture.ApiKey).And.NotContain(FiscalFixture.WebhookSecret);
    }

    [Theory]
    [InlineData("http://169.254.169.254")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://bqe-homolog-postgres:5432")]
    [InlineData("http://10.0.0.5:8080")]
    public async Task URL_fora_da_lista_do_ambiente_e_recusada(string url)
    {
        var (_, owner, code) = await OwnerWithTotpAsync();

        var response = await owner.PutAsJsonAsync(Integration, Body(await code(), baseUrl: url));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("errors").GetProperty("baseUrl")[0].GetString().Should().Be(FiscalBaseUrl.Message);
    }

    [Fact]
    public async Task Tenant_sem_ambiente_fiscal_permitido_nao_salva_nem_testa()
    {
        await fixture.RemoveIntegrationAsync(fixture.Tenancy.TenantB);
        var (_, owner, code) = await fixture.OwnerWithTotpAsync(fixture.Tenancy.TenantB, FiscalFixture.HostB);

        var view = await JsonAsync(await owner.GetAsync(Integration));
        view.GetProperty("allowed").GetBoolean().Should().BeFalse();
        view.GetProperty("allowedEnvironment").GetString().Should().Be("None");

        var saved = await owner.PutAsJsonAsync(Integration, Body(await code()));
        saved.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await JsonAsync(saved);
        problem.GetProperty("code").GetString().Should().Be(FiscalErrorCodes.EnvironmentNotAllowed);
        problem.GetProperty("detail").GetString().Should().Be("A emissão desta empresa só liga na virada para a produção.");
        (await fixture.FiscalAsync(fixture.Tenancy.TenantB, db => db.FiscalIntegrationSettings.AnyAsync())).Should().BeFalse();
    }

    [Fact]
    public async Task Testar_conexao_recusa_admin_escopo_faltando_outro_CNPJ_e_producao_e_aprova_a_chave_certa()
    {
        var tenant = fixture.Tenancy.TenantA;
        await fixture.ConfigureAsync(tenant, active: false);
        var fake = fixture.Fake(tenant);
        var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);

        async Task<JsonElement> TestAsync()
        {
            var response = await owner.PostAsync(Integration + "/test", null);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return await JsonAsync(response);
        }

        try
        {
            fake.Scopes = ["admin", "nfe:emitir", "nfe:eventos", "consultar"];
            var admin = await TestAsync();
            admin.GetProperty("ok").GetBoolean().Should().BeFalse();
            admin.GetProperty("message").GetString().Should().Be("Use uma chave sem o escopo admin.");
            admin.GetProperty("integration").GetProperty("active").GetBoolean().Should().BeFalse();

            fake.Scopes = ["nfe:emitir"];
            (await TestAsync()).GetProperty("message").GetString().Should().Be("Faltam os escopos nfe:eventos, consultar nesta chave.");

            fake.Scopes = ["consultar", "nfe:emitir", "nfe:eventos"];
            fake.IssuerDocument = "11222333000181";
            (await TestAsync()).GetProperty("message").GetString().Should().Be("O emitente do Emissor Fiscal não é o CNPJ desta empresa.");

            fake.IssuerDocument = "47225005000186";
            fake.Environment = FiscalEnvironment.Production;
            (await TestAsync()).GetProperty("message").GetString().Should().Be("A emissão desta empresa só liga na virada para a produção.");

            fake.Environment = FiscalEnvironment.Homologation;
            var ok = await TestAsync();
            ok.GetProperty("ok").GetBoolean().Should().BeTrue();
            ok.GetProperty("message").GetString().Should().Be("Conexão testada");
            var integration = ok.GetProperty("integration");
            integration.GetProperty("active").GetBoolean().Should().BeTrue();
            integration.GetProperty("issuerDocument").GetString().Should().Be("47225005000186");
            integration.GetProperty("environment").GetString().Should().Be("Homologation");
            integration.GetProperty("emitterTenantId").GetGuid().Should().Be(fake.TenantId);
            integration.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()).Should().BeEquivalentTo(["consultar", "nfe:emitir", "nfe:eventos"]);
            fake.Calls.Should().Contain(c => c.Operation == "me" && c.ApiKey == FiscalFixture.ApiKey);
        }
        finally
        {
            await fixture.ConfigureAsync(tenant);
            fake.IssuerDocument = "47225005000186";
        }
    }

    [Fact]
    public async Task Chave_recusada_no_teste_alerta_e_avisa_quem_edita_o_fiscal()
    {
        var tenant = fixture.Tenancy.TenantA;
        await fixture.ConfigureAsync(tenant, apiKey: "recusada-chave-antiga-0001");
        var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
        var (editorId, _) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, grants: ["fiscal.view", "fiscal.edit"]);
        try
        {
            var test = await JsonAsync(await owner.PostAsync(Integration + "/test", null));

            test.GetProperty("ok").GetBoolean().Should().BeFalse();
            test.GetProperty("result").GetString().Should().Be(FiscalTestResults.KeyRejected);
            test.GetProperty("integration").GetProperty("active").GetBoolean().Should().BeFalse();
            fixture.Alerts.Raised.Should().Contain(a => a.Kind == FiscalAlerts.Kind && a.Tenant == tenant.Key && a.Link == FiscalLinks.Integration);
            (await fixture.QueryAsync(tenant, db => db.Notifications.AnyAsync(n => n.UserId == editorId && n.Type == FiscalAlerts.NotificationType && n.Link == FiscalLinks.Integration)))
                .Should().BeTrue();
            fixture.AllLogs().Should().NotContain("recusada-chave-antiga-0001");
        }
        finally
        {
            await fixture.ConfigureAsync(tenant);
        }
    }

    [Fact]
    public async Task Rotacao_testa_a_chave_nova_antes_de_trocar_e_avisa_o_Proprietario()
    {
        var tenant = fixture.Tenancy.TenantA;
        await fixture.ConfigureAsync(tenant);
        var fake = fixture.Fake(tenant);
        var (ownerId, owner, code) = await OwnerWithTotpAsync();
        string CurrentKey() => fixture.Factory.Services.GetRequiredService<ISecretProtector>().Unprotect(SecretScope.ForTenant(tenant.Id), SecretKind.FiscalApiKey,
            fixture.FiscalAsync(tenant, db => db.FiscalIntegrationSettings.Select(s => s.ApiKeyCipher!).SingleAsync()).GetAwaiter().GetResult()).Reveal();

        var adminKey = "ef_chave-nova-com-admin-000000001";
        fake.Accounts[adminKey] = fake.DefaultAccount() with { Scopes = ["admin", "consultar", "nfe:emitir", "nfe:eventos"] };
        var refused = await owner.PutAsJsonAsync(Integration, Body(await code(), apiKey: adminKey, secret: null));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(refused)).GetProperty("errors").GetProperty("apiKey")[0].GetString().Should().Be("Use uma chave sem o escopo admin.");
        CurrentKey().Should().Be(FiscalFixture.ApiKey, "a chave nova recusada não troca nada");

        var newKey = "ef_chave-nova-sem-admin-000000002";
        var code2 = await code();
        var rotated = await owner.PutAsJsonAsync(Integration, Body(code2, apiKey: newKey, secret: null));
        rotated.StatusCode.Should().Be(HttpStatusCode.OK, await rotated.Content.ReadAsStringAsync());
        CurrentKey().Should().Be(newKey);
        (await JsonAsync(rotated)).GetProperty("active").GetBoolean().Should().BeTrue("a chave nova já foi testada");
        fake.Calls.Should().Contain(c => c.Operation == "me" && c.ApiKey == newKey);
        (await fixture.QueryAsync(tenant, db => db.Notifications.AnyAsync(n => n.UserId == ownerId && n.Type == FiscalIntegrationService.RotatedNotification))).Should().BeTrue();

        fake.Calls.Clear();
        await fixture.WithAsync<Result<IFiscalEmitter>, FiscalEmitterAccess>(tenant, async access =>
        {
            var emitter = await access.CurrentAsync();
            await emitter.Value.SefazStatusAsync(null);
            return emitter;
        });
        fake.Calls.Should().OnlyContain(c => c.ApiKey == newKey, "a antiga deixa de ser usada na hora");
        await fixture.ConfigureAsync(tenant);
    }

    [Fact]
    public async Task Rotacao_do_segredo_do_webhook_mantem_o_anterior_por_24_horas()
    {
        var tenant = fixture.Tenancy.TenantA;
        await fixture.ConfigureAsync(tenant);
        var (_, owner, code) = await OwnerWithTotpAsync();
        const string newSecret = "segredo-novo-do-webhook-0123456789";

        var saved = await owner.PutAsJsonAsync(Integration, Body(await code(), apiKey: null, secret: newSecret));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await JsonAsync(saved)).GetProperty("webhookPreviousValidUntil").ValueKind.Should().Be(JsonValueKind.String);

        var body = "{\"tipo\":\"nfe.autorizada\",\"tenantId\":\"" + Guid.CreateVersion7() + "\",\"documentoId\":\"" + Guid.CreateVersion7() + "\"}";
        (await fixture.PostWebhookAsync(tenant, body, "nfe.autorizada", newSecret)).Response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body2 = body.Replace("autorizada", "cancelada", StringComparison.Ordinal);
        (await fixture.PostWebhookAsync(tenant, body2, "nfe.cancelada", FiscalFixture.WebhookSecret)).Response.StatusCode.Should().Be(HttpStatusCode.Accepted, "o anterior vale por 24 h");
        await fixture.ConfigureAsync(tenant);
    }

    [Fact]
    public async Task Tres_camadas_modo_leitura_e_rate_limit_do_teste()
    {
        var tenant = fixture.Tenancy.TenantA;
        await fixture.ConfigureAsync(tenant);
        var (_, viewer) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, grants: ["settings.view", "fiscal.view"]);
        var (_, nobody) = await fixture.ActorAsync(tenant, FiscalFixture.HostA);

        (await viewer.GetAsync(Integration)).StatusCode.Should().Be(HttpStatusCode.OK, "com settings.view a tela abre em modo leitura");
        var put = await viewer.PutAsJsonAsync(Integration, Body("123456"));
        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(put)).GetProperty("code").GetString().Should().Be("forbidden");
        (await viewer.PostAsync(Integration + "/test", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var none = await nobody.GetAsync(Integration);
        none.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(none)).GetProperty("code").GetString().Should().Be("forbidden");

        var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
        for (var i = 0; i < 5; i++)
        {
            (await owner.PostAsync(Integration + "/test", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var limited = await owner.PostAsync(Integration + "/test", null);
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();

        await fixture.SetModuleAsync(tenant, "fiscal", released: false);
        try
        {
            var blocked = await owner.GetAsync(Integration);
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await JsonAsync(blocked)).GetProperty("code").GetString().Should().Be("module_not_released");
        }
        finally
        {
            await fixture.SetModuleAsync(tenant, "fiscal", released: true);
        }
    }

    [Fact]
    public async Task Ambiente_permitido_vem_do_catalogo_na_hora()
    {
        var tenant = fixture.Tenancy.TenantA;
        await fixture.ConfigureAsync(tenant);
        await fixture.SetAllowanceAsync(tenant, FiscalEnvironmentAllowance.None);
        try
        {
            var access = await fixture.WithAsync<Result<IFiscalEmitter>, FiscalEmitterAccess>(tenant, a => a.CurrentAsync());
            access.Error!.Code.Should().Be(FiscalErrorCodes.EnvironmentNotAllowed, "nada fiscal sai sem a marca do catálogo");
        }
        finally
        {
            await fixture.SetAllowanceAsync(tenant, FiscalEnvironmentAllowance.Homologation);
        }
    }
}
