using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

public partial class AdjustedCashFlowTests
{
    private const string Parameters = "/api/v1/finance/parameters";

    [Fact]
    public async Task Resumo_do_painel_e_igual_a_tela_para_o_mesmo_horizonte()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        foreach (var days in new[] { 30, 60, 90 })
        {
            var summary = await JsonAsync(await owner.GetAsync($"{Base}/summary?days={days}"));
            var flow = await JsonAsync(await owner.GetAsync($"{Base}?view=daily&days={days}&from={Iso(Today)}&scenario=both"));
            var adjusted = flow.GetProperty("adjusted");
            summary.GetProperty("currentBalance").GetDecimal().Should().Be(flow.GetProperty("currentBalance").GetDecimal());
            summary.GetProperty("base").GetProperty("endBalance").GetDecimal().Should().Be(flow.GetProperty("periods").EnumerateArray().Last().GetProperty("balance").GetDecimal());
            summary.GetProperty("adjusted").GetProperty("endBalance").GetDecimal().Should().Be(adjusted.GetProperty("periods").EnumerateArray().Last().GetProperty("balance").GetDecimal());
            summary.GetProperty("base").GetProperty("lowestBalance").GetDecimal().Should().Be(flow.GetProperty("lowest").GetProperty("balance").GetDecimal());
            summary.GetProperty("base").GetProperty("lowestDate").GetString().Should().Be(flow.GetProperty("lowest").GetProperty("date").GetString());
            summary.GetProperty("adjusted").GetProperty("lowestBalance").GetDecimal().Should().Be(adjusted.GetProperty("lowest").GetProperty("balance").GetDecimal());
            summary.GetProperty("adjusted").GetProperty("lowestDate").GetString().Should().Be(adjusted.GetProperty("lowest").GetProperty("date").GetString());
            summary.GetProperty("lowestDifference").GetDecimal().Should().Be(adjusted.GetProperty("lowestDifference").GetDecimal());
        }

        // O contrato em processo devolve o mesmo que o endpoint (DSH-01a usa o contrato).
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        var contract = await scope.ServiceProvider.GetRequiredService<ICashForecast>().SummaryAsync(60);
        var endpoint = await JsonAsync(await owner.GetAsync($"{Base}/summary?days=60"));
        contract.Adjusted.EndBalance.Should().Be(endpoint.GetProperty("adjusted").GetProperty("endBalance").GetDecimal());
        contract.LowestDifference.Should().Be(endpoint.GetProperty("lowestDifference").GetDecimal());
        (await owner.GetAsync($"{Base}/summary?days=45")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Limite_de_inadimplencia_grava_com_auditoria_e_exige_settings_edit()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var finance = await ActorAsync("finance");
        var limits = await JsonAsync(await owner.GetAsync(Parameters));
        limits.GetProperty("adjustedDefaultThresholdPct").GetDecimal().Should().Be(20m, "seed 20,00");
        object Body(decimal threshold) => new
        {
            discountApprovalPct = limits.GetProperty("discountApprovalPct").GetDecimal(), discountApprovalAmount = limits.GetProperty("discountApprovalAmount").GetDecimal(),
            cancelApprovalAmount = limits.GetProperty("cancelApprovalAmount").GetDecimal(), backdateDays = limits.GetProperty("backdateDays").GetInt32(),
            ownerCanApproveOwn = limits.GetProperty("ownerCanApproveOwn").GetBoolean(), paymentTolerance = limits.GetProperty("paymentTolerance").GetDecimal(),
            transferNotifyAmount = limits.GetProperty("transferNotifyAmount").GetDecimal(), adjustedDefaultThresholdPct = threshold,
        };

        var denied = await finance.PutAsJsonAsync(Parameters, Body(35m));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem settings.edit");
        (await owner.PutAsJsonAsync(Parameters, Body(100.01m))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        try
        {
            var saved = await owner.PutAsJsonAsync(Parameters, Body(35.555m));
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            (await JsonAsync(saved)).GetProperty("adjustedDefaultThresholdPct").GetDecimal().Should().Be(35.56m, "2 casas");
            (await JsonAsync(await owner.GetAsync($"{Base}?days=30"))).GetProperty("adjusted").GetProperty("thresholdPct").GetDecimal().Should().Be(35.56m, "vale no próximo cálculo");
            var audit = await WithDbAsync(async db =>
            {
                var id = await db.FinanceParameters.Where(p => p.Key == "finance.adjusted_default_threshold_pct").Select(p => p.Id).SingleAsync();
                return await db.AuditEntries.AsNoTracking().Where(a => a.EntityId == id.ToString()).Select(a => a.Changes).ToListAsync();
            });
            audit.Should().Contain(c => c.Contains("35.56", StringComparison.Ordinal), "antes e depois na auditoria");

            // Tela antiga sem o campo: o limite fica como está.
            var old = await owner.PutAsJsonAsync(Parameters, new
            {
                discountApprovalPct = limits.GetProperty("discountApprovalPct").GetDecimal(), discountApprovalAmount = limits.GetProperty("discountApprovalAmount").GetDecimal(),
                cancelApprovalAmount = limits.GetProperty("cancelApprovalAmount").GetDecimal(), backdateDays = limits.GetProperty("backdateDays").GetInt32(),
                ownerCanApproveOwn = limits.GetProperty("ownerCanApproveOwn").GetBoolean(), paymentTolerance = limits.GetProperty("paymentTolerance").GetDecimal(),
                transferNotifyAmount = limits.GetProperty("transferNotifyAmount").GetDecimal(),
            });
            (await JsonAsync(old)).GetProperty("adjustedDefaultThresholdPct").GetDecimal().Should().Be(35.56m);
        }
        finally
        {
            (await owner.PutAsJsonAsync(Parameters, Body(20m))).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Tres_camadas_no_fluxo_no_resumo_e_no_parametro_e_allowlist()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        (await owner.GetAsync($"{Base}?scenario=pessimista")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.GetAsync($"{Base}/day?date={Iso(Today)}&scenario=both")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.GetAsync($"{Base}/day?date={Iso(Today)}&scenario=adjusted&sort=amount;drop")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var nobody = await ActorAsync(null);
        var commercial = await ActorAsync("commercial");
        foreach (var client in new[] { nobody, commercial })
        {
            foreach (var url in new[] { $"{Base}?scenario=adjusted", $"{Base}/summary?days=30", $"{Base}/day?date={Iso(Today)}&scenario=adjusted", Parameters })
            {
                var denied = await client.GetAsync(url);
                denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
                (await JsonAsync(denied)).GetProperty("code").GetString().Should().Be("forbidden", url);
            }
        }

        await SetFinanceReleasedAsync(false);
        try
        {
            foreach (var url in new[] { $"{Base}?scenario=adjusted", $"{Base}/summary?days=30", Parameters })
            {
                var blocked = await owner.GetAsync(url);
                blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
                (await JsonAsync(blocked)).GetProperty("code").GetString().Should().Be("module_not_released", url);
            }
        }
        finally
        {
            await SetFinanceReleasedAsync(true);
        }
    }

    private async Task SetFinanceReleasedAsync(bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "finance")
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }
}
