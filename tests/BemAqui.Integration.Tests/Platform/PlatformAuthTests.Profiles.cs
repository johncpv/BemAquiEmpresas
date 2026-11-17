using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Application.Modules;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Platform;

/// <summary>PERF-01 (ADR 0046): perfis de negócio, liberação efetiva por plano e perfil, estado oculto e a regressão da Konebras.</summary>
public partial class PlatformAuthTests
{
    private async Task<HttpClient> ActorAsync(Tenant tenant, string host, string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenant, "p" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
            await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        }

        var client = tenancy.ClientFor(host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp));
        return client;
    }

    private async Task<IReadOnlyList<string>> SeedGroupsAsync(Tenant tenant)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await scope.ServiceProvider.GetRequiredService<IIdentityDb>().Groups.Where(g => g.SeedKey != null).OrderBy(g => g.SeedKey).Select(g => g.SeedKey!).ToListAsync();
    }

    private async Task<T> CatalogScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private static async Task<Dictionary<string, JsonElement>> ModulesOfAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/me/permissions")).GetProperty("modules").EnumerateArray()
            .ToDictionary(m => m.GetProperty("code").GetString()!, m => m);

    [Fact]
    public async Task Regressao_da_Konebras_backfill_factory_deixa_permissoes_e_menu_identicos()
    {
        const string host = "konebras-regressao.localhost";
        var tenant = await tenancy.ProvisionAsync("konebras-regressao", "Fábrica de Elevadores Ltda", host);
        await CatalogScopeAsync(async sp =>
        {
            // Retrato realista: uma exceção manual (módulo bloqueado à mão, com cadeado) além do plano completo.
            (await sp.GetRequiredService<TenantModuleService>().SaveAsync(tenant.Id, [new ModuleReleaseChange("assets", false)], null, null)).IsSuccess.Should().BeTrue();

            // Estado de antes da migration: o tenant ainda não tem perfil.
            await sp.GetRequiredService<CatalogDbContext>().TenantProfiles.Where(p => p.TenantId == tenant.Id).ExecuteDeleteAsync();
            sp.GetRequiredService<ITenantResolver>().Invalidate();
            return 0;
        });

        var actors = new Dictionary<string, HttpClient>();
        foreach (var group in await SeedGroupsAsync(tenant))
        {
            actors[group] = await ActorAsync(tenant, host, group);
        }

        actors.Should().ContainKey(Group.OwnerSeedKey);
        var before = new Dictionary<string, string>();
        foreach (var (group, client) in actors)
        {
            before[group] = await client.GetStringAsync("/api/v1/me/permissions");
        }

        var modulesBefore = await CatalogScopeAsync(sp => sp.GetRequiredService<CatalogDbContext>().TenantModules.AsNoTracking()
            .Where(m => m.TenantId == tenant.Id).OrderBy(m => m.ModuleCode).Select(m => new { m.ModuleCode, m.Enabled, m.IsException, m.ReleasedAt }).ToListAsync());

        // A migration (CA 4) e depois um recálculo, como o de uma troca de plano.
        await CatalogScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CatalogDbContext>();
            // As chaves do JSON da auditoria não são parâmetros: o ExecuteSqlRaw as leria como {0}.
            await db.Database.ExecuteSqlRawAsync(CatalogNotifySql.BackfillFactoryProfile.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal));
            await sp.GetRequiredService<TenantModuleService>().ApplyPlanAsync(tenant.Id, (await db.Tenants.SingleAsync(t => t.Id == tenant.Id)).PlanId);
            return 0;
        });

        var profile = await CatalogScopeAsync(sp => sp.GetRequiredService<CatalogDbContext>().TenantProfiles.AsNoTracking().SingleAsync(p => p.TenantId == tenant.Id));
        profile.ProfileCode.Should().Be(BusinessProfileCatalog.Factory);
        profile.AppliedBy.Should().BeNull("o backfill é do sistema");
        (await CatalogScopeAsync(sp => sp.GetRequiredService<CatalogDbContext>().AuditEntries.CountAsync(a => a.Entity == "TenantProfile" && a.EntityId == tenant.Id + ":factory" && a.Origin!.Contains("PERF-01"))))
            .Should().Be(1);

        (await CatalogScopeAsync(sp => sp.GetRequiredService<CatalogDbContext>().TenantModules.AsNoTracking()
            .Where(m => m.TenantId == tenant.Id).OrderBy(m => m.ModuleCode).Select(m => new { m.ModuleCode, m.Enabled, m.IsException, m.ReleasedAt }).ToListAsync()))
            .Should().Equal(modulesBefore, "o perfil Fábrica lista todos os módulos de hoje: tenant_modules não muda");
        foreach (var (group, client) in actors)
        {
            (await client.GetStringAsync("/api/v1/me/permissions")).Should().Be(before[group], $"o grupo {group} vê o mesmo menu e as mesmas permissões (CA 11)");
        }

        (await ModulesOfAsync(actors[Group.OwnerSeedKey])).Values.Should().NotContain(m => m.GetProperty("hidden").GetBoolean());
    }

    [Fact]
    public async Task Perfil_une_modulos_oculta_o_que_esta_fora_e_a_excecao_manual_prevalece()
    {
        const string host = "loja-perfil.localhost";
        var tenant = await tenancy.ProvisionAsync("loja-perfil", "Loja Perfil Ltda", host);
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TenantModuleService>();
        var db = scope.ServiceProvider.GetRequiredService<ITenantModulesDb>();

        // Comércio + E-commerce no lugar de Fábrica (CA 6), e um plano sem Patrimônio (cadeado: no perfil, fora do plano, CA 8).
        (await service.ApplyProfileAsync(tenant.Id, BusinessProfileCatalog.Retail, "bakery", null)).IsSuccess.Should().BeTrue();
        (await service.ApplyProfileAsync(tenant.Id, BusinessProfileCatalog.Retail, "bakery", null)).IsSuccess.Should().BeTrue("aplicar é idempotente");
        (await service.ApplyProfileAsync(tenant.Id, BusinessProfileCatalog.Ecommerce, null, null)).IsSuccess.Should().BeTrue();
        (await service.ApplyProfileAsync(tenant.Id, BusinessProfileCatalog.Retail, "workshop", null)).IsSuccess.Should().BeFalse("oficina é segmento de Serviços");
        (await service.RemoveProfileAsync(tenant.Id, BusinessProfileCatalog.Factory, null, null)).IsSuccess.Should().BeTrue();
        var plan = new Plan { Name = "Loja " + Guid.NewGuid().ToString("N")[..6] };
        db.Plans.Add(plan);
        db.PlanModules.AddRange(ModuleCatalog.All.Where(m => m.Code != "assets").Select(m => new PlanModule { PlanId = plan.Id, ModuleCode = m.Code }));
        await db.SaveChangesAsync();
        (await service.ChangePlanAsync(tenant.Id, plan.Id, null)).IsSuccess.Should().BeTrue();

        (await service.GetProfilesAsync(tenant.Id)).Select(p => p.Code).Should().BeEquivalentTo([BusinessProfileCatalog.Retail, BusinessProfileCatalog.Ecommerce]);
        var released = (await service.GetAsync(tenant.Id)).Value.ToDictionary(m => m.Code);
        released["production"].Should().Match<ModuleRelease>(m => !m.Released && m.InPlan && !m.InProfile && !m.IsException);
        released["kiosk"].Released.Should().BeTrue("o painel da TV está no Comércio");

        var owner = await ActorAsync(tenant, host, Group.OwnerSeedKey);
        var modules = await ModulesOfAsync(owner);
        modules["production"].GetProperty("hidden").GetBoolean().Should().BeTrue("fora de todo perfil (CA 7)");
        modules["production"].GetProperty("released").GetBoolean().Should().BeFalse();
        modules["assets"].GetProperty("hidden").GetBoolean().Should().BeFalse("no perfil e fora do plano fica com cadeado (CA 8)");
        modules["assets"].GetProperty("released").GetBoolean().Should().BeFalse();
        modules["finance"].GetProperty("released").GetBoolean().Should().BeTrue();

        // CA 9: oculto é só apresentação; a rota direta continua barrada pela camada 1.
        var direct = await owner.GetAsync("/api/v1/production/work-centers");
        direct.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(direct)).GetProperty("code").GetString().Should().Be("module_not_released");
        (await owner.GetFromJsonAsync<JsonElement>("/api/v1/ajuda")).EnumerateArray()
            .Should().NotContain(d => d.GetProperty("module").GetString() == "production", "a ajuda e o Pergunte ao BemAqui não citam módulo oculto");

        // CA 10: a exceção manual libera um módulo fora do perfil, e ele aparece; reaplicar o plano não a desfaz (CA 5).
        (await service.SaveAsync(tenant.Id, [new ModuleReleaseChange("production", true)], null, null)).IsSuccess.Should().BeTrue();
        await service.ApplyPlanAsync(tenant.Id, plan.Id);
        (await service.GetAsync(tenant.Id)).Value.Single(m => m.Code == "production").Should().Match<ModuleRelease>(m => m.Released && m.IsException);
        var after = await ModulesOfAsync(owner);
        after["production"].GetProperty("hidden").GetBoolean().Should().BeFalse();
        after["production"].GetProperty("released").GetBoolean().Should().BeTrue();

        (await service.RemoveProfileAsync(tenant.Id, BusinessProfileCatalog.Ecommerce, null, null)).IsSuccess.Should().BeTrue();
        (await service.RemoveProfileAsync(tenant.Id, BusinessProfileCatalog.Retail, "bakery", null)).IsSuccess.Should().BeFalse("o último perfil não sai");
        (await db.AuditEntries.Where(a => a.Entity == "TenantProfile" && a.EntityId.StartsWith(tenant.Id.ToString())).Select(a => a.Action).ToListAsync())
            .Should().Contain(["ProfileApplied", "ProfileRemoved"]);
    }
}
