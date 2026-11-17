using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Platform.Application.Modules;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.Platform.Infrastructure.Provisioning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Platform;

/// <summary>PERF-02: perfil e segmento no provisionamento e a aba "Perfis" do tenant (adicionar, remover, último perfil, permissão).</summary>
public partial class PlatformAuthTests
{
    private static async Task<Dictionary<string, JsonElement>> ReleaseOfAsync(HttpClient platform, Guid tenantId) =>
        (await platform.GetFromJsonAsync<JsonElement>($"/api/v1/platform/tenants/{tenantId}/modules")).EnumerateArray()
            .ToDictionary(m => m.GetProperty("code").GetString()!, m => m);

    [Fact]
    public async Task Provisionar_com_Comercio_e_segmento_aplica_o_perfil_registra_no_log_e_a_retomada_nao_duplica()
    {
        var admin = await AdminWithSessionAsync();
        var platform = WithCookie(Client(), admin.Cookie);
        var key = "loja-" + Guid.NewGuid().ToString("N")[..8];
        object Body(object profiles) => new { key, legalName = "Padaria Teste Ltda", document = "47.225.005/0001-86", host = key + ".localhost", ownerEmail = "dono@" + key + ".localhost", plan = (string?)null, profiles };

        var empty = await platform.PostAsJsonAsync("/api/v1/platform/tenants", Body(Array.Empty<object>()));
        empty.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(empty)).GetProperty("errors").GetProperty("profiles")[0].GetString().Should().Be("Escolha ao menos um perfil.");
        (await platform.PostAsJsonAsync("/api/v1/platform/tenants", Body(new[] { new { code = "retail", segment = "workshop" } }))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "oficina é segmento de Serviços");
        (await platform.PostAsJsonAsync("/api/v1/platform/tenants", Body(new[] { new { code = "retail", segment = "bakery" } }))).StatusCode.Should().Be(HttpStatusCode.Accepted);

        // O servidor de jobs está desligado nos testes: executa o job com o pedido que a tela enfileirou.
        var request = new ProvisionRequest(key, "Padaria Teste Ltda", "47.225.005/0001-86", key + ".localhost", "dono@" + key + ".localhost", null,
            Profiles: [new ProfileChoice(BusinessProfileCatalog.Retail, "bakery")]);
        var watch = Stopwatch.StartNew();
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProvisionTenantJob>().ExecuteAsync(request, CancellationToken.None);
        }

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(1), "o provisionamento continua abaixo de 1 minuto (CA 4)");
        var tenant = await tenancy.FindAsync(key);
        var tab = await platform.GetFromJsonAsync<JsonElement>($"/api/v1/platform/tenants/{tenant.Id}/profiles");
        tab.GetProperty("active").EnumerateArray().Select(p => p.GetProperty("code").GetString() + "/" + p.GetProperty("segment").GetString())
            .Should().Equal("retail/bakery");
        tab.GetProperty("active")[0].GetProperty("segmentName").GetString().Should().Be("Padaria");
        var detail = await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/tenants/" + key);
        detail.GetProperty("log").EnumerateArray().Should().Contain(l => l.GetProperty("step").GetString() == ProvisioningSteps.Profile
            && l.GetProperty("status").GetString() == "Succeeded" && l.GetProperty("message").GetString()!.Contains("Perfil aplicado: Comércio · Padaria"));
        var release = await ReleaseOfAsync(platform, tenant.Id);
        release["production"].GetProperty("released").GetBoolean().Should().BeFalse("Produção não é do Comércio");
        release["finance"].GetProperty("released").GetBoolean().Should().BeTrue();

        // CA 5: retomar não aplica o perfil de novo.
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ProvisioningService>().ProvisionAsync(request with { Resume = true })).Steps
                .Should().OnlyContain(s => s.Status == "Já concluída");
        }

        (await CatalogAsync(db => db.TenantProfiles.CountAsync(p => p.TenantId == tenant.Id))).Should().Be(1);
    }

    [Fact]
    public async Task Aba_Perfis_adiciona_sem_duplicar_remove_so_ocultando_preserva_excecao_e_nao_tira_o_ultimo()
    {
        var tenant = await tenancy.ProvisionAsync("perfis-aba", "Perfis Aba Ltda", "perfis-aba.localhost");
        var admin = await AdminWithSessionAsync();
        var platform = WithCookie(Client(), admin.Cookie);
        var path = $"/api/v1/platform/tenants/{tenant.Id}/profiles";

        // Exceção manual: Painel da TV bloqueado à mão. Ele está em Fábrica e em Comércio; trocar os perfis não pode desfazer isso (CA 9).
        (await platform.PutAsJsonAsync($"/api/v1/platform/tenants/{tenant.Id}/modules", new { modules = new[] { new { code = "kiosk", released = false } } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var added = await JsonAsync(await platform.PostAsJsonAsync(path, new { profile = "retail", segment = "counter" }));
        added.GetProperty("changed").GetBoolean().Should().BeTrue();
        var again = await platform.PostAsJsonAsync(path, new { profile = "retail", segment = "counter" });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(again)).GetProperty("message").GetString().Should().Be("Esta empresa já tem o perfil Comércio · Balcão.", "adicionar é idempotente (CA 7)");
        (await platform.PostAsJsonAsync(path, new { profile = "crm", segment = (string?)null })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await platform.DeleteAsync(path + "/factory")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var release = await ReleaseOfAsync(platform, tenant.Id);
        release["production"].GetProperty("released").GetBoolean().Should().BeFalse("remover só oculta o que só a Fábrica trazia (CA 8)");
        release["production"].GetProperty("inProfile").GetBoolean().Should().BeFalse();
        release["kiosk"].Should().Match<JsonElement>(m => !m.GetProperty("released").GetBoolean() && m.GetProperty("isException").GetBoolean());
        (await CatalogAsync(db => db.Tenants.CountAsync(t => t.Id == tenant.Id))).Should().Be(1, "nenhum dado é apagado");

        var last = await platform.DeleteAsync(path + "/retail?segment=counter");
        last.StatusCode.Should().Be(HttpStatusCode.Conflict, "o último perfil não sai");

        var tab = await platform.GetFromJsonAsync<JsonElement>(path);
        tab.GetProperty("active").GetArrayLength().Should().Be(1);
        var factory = tab.GetProperty("history").EnumerateArray().Single(p => p.GetProperty("code").GetString() == "factory");
        factory.GetProperty("removedBy").GetString().Should().Be(admin.Email, "a auditoria mostra quem removeu");
        tab.GetProperty("active")[0].GetProperty("appliedBy").GetString().Should().Be(admin.Email);
        (await CatalogAsync(db => db.AuditEntries.Where(a => a.Entity == "TenantProfile" && a.EntityId.StartsWith(tenant.Id.ToString()) && a.UserId != null)
            .Select(a => a.Action).ToListAsync())).Should().BeEquivalentTo(["ProfileApplied", "ProfileRemoved"]);
    }

    [Fact]
    public async Task Endpoints_de_perfil_negam_quem_nao_e_super_admin()
    {
        var path = $"/api/v1/platform/tenants/{tenancy.TenantA.Id}/profiles";
        var tenantUser = Client();
        tenantUser.DefaultRequestHeaders.Authorization = new("Bearer", await tenancy.TokenAsync(tenancy.TenantA, "a.localhost"));

        foreach (var client in new[] { Client(), tenantUser })
        {
            (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.PostAsJsonAsync(path, new { profile = "retail", segment = "counter" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.DeleteAsync(path + "/factory")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.GetAsync("/api/v1/platform/business-profiles")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await Client("a.localhost").GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.NotFound, "a API da plataforma não existe no host do tenant");
        (await CatalogAsync(db => db.TenantProfiles.CountAsync(p => p.TenantId == tenancy.TenantA.Id && p.RemovedAt == null))).Should().Be(1);
    }
}
