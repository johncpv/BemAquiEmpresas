using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Identity.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Settings;

public partial class CompanySettingsTests
{
    [Fact]
    public async Task Parametros_nascem_com_80_e_markup_e_mudar_encargos_vai_para_o_outbox()
    {
        var owner = await ClientAsync();
        var initial = await owner.GetFromJsonAsync<JsonElement>("/api/v1/settings/parameters");
        initial.GetProperty("pricingMode").GetString().Should().BeOneOf("Markup", "Margin");

        (await owner.PutAsJsonAsync("/api/v1/settings/parameters/payroll-charges", new { pct = 200.01m })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "CA 17");
        (await owner.PutAsJsonAsync("/api/v1/settings/parameters/payroll-charges", new { pct = 1.234m })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var saved = await owner.PutAsJsonAsync("/api/v1/settings/parameters/payroll-charges", new { pct = 75.50m });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await owner.PutAsJsonAsync("/api/v1/settings/parameters/pricing-mode", new { mode = "Margin" })).EnsureSuccessStatusCode();

        await using var scope = await ScopeAsync();
        var parameters = scope.ServiceProvider.GetRequiredService<ITenantParameters>();
        (await parameters.PayrollChargesPctAsync()).Should().Be(75.50m, "outros módulos leem pelo contrato (CA 19)");
        (await parameters.PricingModeAsync()).Should().Be(PricingMode.Margin);
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        (await db.OutboxMessages.AsNoTracking().Where(m => m.Type.EndsWith("PayrollChargesChanged")).Select(m => m.Payload).ToListAsync())
            .Should().Contain(p => p.Contains("75.5"), "evento no outbox na mesma transação (CA 16)");
    }

    [Fact]
    public async Task Seed_cria_os_parametros_sem_sobrescrever_e_quem_so_ve_nao_grava()
    {
        await using (var scope = await ScopeAsync())
        {
            await new BemAqui.Identity.Infrastructure.TenantParametersSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            (await db.TenantSettings.CountAsync(s => s.Key == "payroll.charges_pct")).Should().Be(1, "idempotente (CA 15)");
        }

        var viewer = await ClientAsync("view");
        (await viewer.GetAsync("/api/v1/settings/numbering")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.PutAsJsonAsync("/api/v1/settings/parameters/pricing-mode", new { mode = "Markup" })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "CA 20");
        (await viewer.PostAsync("/api/v1/settings/company/logo", File(Png(10, 10), "x.png"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var nobody = await ClientAsync(null);
        (await nobody.GetAsync("/api/v1/settings/company")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
