using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.Platform.Infrastructure.Provisioning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Platform;

/// <summary>FND-03b: tenants no painel da plataforma (lista, provisionar em job, suspender, reativar, auditoria).</summary>
public partial class PlatformAuthTests
{
    [Fact]
    public async Task Lista_e_detalhe_dos_tenants_com_o_log_de_etapas()
    {
        var admin = await AdminWithSessionAsync();
        var platform = WithCookie(Client(), admin.Cookie);

        var list = await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/tenants?q=empresa a");
        list.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("key").GetString()).Should().Contain("empresa-a");
        var detail = await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/tenants/empresa-a");
        detail.GetProperty("tenant").GetProperty("status").GetString().Should().Be("Active");
        detail.GetProperty("domains").EnumerateArray().Select(d => d.GetString()).Should().Contain("a.localhost");
        detail.GetProperty("log").EnumerateArray().Select(l => l.GetProperty("step").GetString()).Should().Contain(["Register", "Activate"]);
        (await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/plans")).EnumerateArray().Should().NotBeEmpty();
        (await Client("a.localhost").GetAsync("/api/v1/platform/tenants")).StatusCode.Should().Be(HttpStatusCode.NotFound, "API da plataforma não existe no host do tenant (CA 7)");
    }

    [Fact]
    public async Task Provisionar_valida_por_campo_e_roda_em_job_ate_o_tenant_ficar_ativo()
    {
        var admin = await AdminWithSessionAsync();
        var platform = WithCookie(Client(), admin.Cookie);

        var invalid = await platform.PostAsJsonAsync("/api/v1/platform/tenants", new { key = "empresa-a", legalName = "", document = "11.111.111/1111-11", host = "a.localhost", ownerEmail = "x", plan = (string?)null });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errors = (await JsonAsync(invalid)).GetProperty("errors");
        errors.EnumerateObject().Select(e => e.Name).Should().BeEquivalentTo(["key", "host", "legalName", "document", "ownerEmail"]);

        var key = "tela-" + Guid.NewGuid().ToString("N")[..8];
        var accepted = await platform.PostAsJsonAsync("/api/v1/platform/tenants", new { key, legalName = "Empresa Tela Ltda", tradeName = "Tela", document = "47.225.005/0001-86", host = key + ".localhost", ownerEmail = "dono@" + key + ".localhost", plan = (string?)null });
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // O servidor de jobs está desligado nos testes: executa o job que a tela enfileirou.
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProvisionTenantJob>().ExecuteAsync(
                new ProvisionRequest(key, "Empresa Tela Ltda", "47.225.005/0001-86", key + ".localhost", "dono@" + key + ".localhost", null, TradeName: "Tela"), CancellationToken.None);
        }

        var detail = await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/tenants/" + key);
        detail.GetProperty("tenant").GetProperty("status").GetString().Should().Be("Active");
        detail.GetProperty("tenant").GetProperty("tradeName").GetString().Should().Be("Tela");
    }

    [Fact]
    public async Task Suspender_exige_motivo_vale_no_host_do_tenant_e_reativar_volta()
    {
        var admin = await AdminWithSessionAsync();
        var platform = WithCookie(Client(), admin.Cookie);
        var tenant = await tenancy.ProvisionAsync("suspensa-" + Guid.NewGuid().ToString("N")[..6], "Suspensa Ltda", "suspensa-" + Guid.NewGuid().ToString("N")[..6] + ".localhost");
        var host = tenant.Domains.Single(d => d.IsPrimary).Host;

        (await platform.PostAsJsonAsync($"/api/v1/platform/tenants/{tenant.Key}/suspend", new { reason = "" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await platform.PostAsJsonAsync($"/api/v1/platform/tenants/{tenant.Key}/suspend", new { reason = "Inadimplência" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var blocked = await Client(host).GetAsync("/api/v1/public/tenant-context");
        (await Client(host).GetAsync("/api/v1/modules")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(blocked)).GetProperty("status").GetString().Should().Be("Suspended");

        (await platform.PostAsync($"/api/v1/platform/tenants/{tenant.Key}/reactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await JsonAsync(await Client(host).GetAsync("/api/v1/public/tenant-context"))).GetProperty("status").GetString().Should().Be("Active");

        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().AuditEntries
            .Where(a => a.Entity == "Tenant" && a.EntityId == tenant.Key).OrderBy(a => a.At).Select(a => new { a.Action, a.Changes, a.UserId }).ToListAsync();
        audit.Select(a => a.Action).Should().ContainInOrder("TenantSuspended", "TenantReactivated");
        audit.First().Changes.Should().Contain("Inadimplência");
        audit.Should().OnlyContain(a => a.UserId != null);
    }
}
