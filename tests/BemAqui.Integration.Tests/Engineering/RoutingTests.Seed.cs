using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Engineering.Application;
using BemAqui.Engineering.Domain;
using BemAqui.Identity.Application;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Engineering;

public partial class RoutingTests
{
    private static async Task RunSeedersAsync(IServiceProvider scope)
    {
        foreach (var seeder in scope.GetServices<ITenantSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(scope, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Seed_patch_engineering_permissions_uma_vez_com_delete_no_catalogo()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        (await db.CatalogModuleActions.AnyAsync(a => a.ModuleCode == "engineering" && a.Action == "delete")).Should().BeTrue("o catálogo do engineering declara delete (D13)");
        (await db.SeedPatches.CountAsync(p => p.Key == "engineering-permissions")).Should().Be(1);
        var engineering = await db.Groups.SingleAsync(g => g.SeedKey == "engineering");
        (await db.GroupModules.AnyAsync(m => m.GroupId == engineering.Id && m.ModuleCode == "engineering" && m.Enabled)).Should().BeTrue();
        (await db.GroupPermissions.Where(p => p.GroupId == engineering.Id && p.ModuleCode == "engineering").Select(p => p.Action).ToListAsync())
            .Should().BeEquivalentTo(["view", "create", "edit", "delete"], "engineering.approve (liberar a BOM) é da ENG-02");
        var supervision = await db.Groups.SingleAsync(g => g.SeedKey == "production_supervision");
        (await db.GroupPermissions.Where(p => p.GroupId == supervision.Id && p.ModuleCode == "engineering").Select(p => p.Action).ToListAsync()).Should().Equal("view");

        // O tenant tira "delete" da Engenharia; um novo deploy (seeds de novo) não devolve.
        await db.GroupPermissions.Where(p => p.GroupId == engineering.Id && p.ModuleCode == "engineering" && p.Action == "delete").ExecuteDeleteAsync();
        await RunSeedersAsync(scope.ServiceProvider);
        (await db.GroupPermissions.CountAsync(p => p.GroupId == engineering.Id && p.ModuleCode == "engineering" && p.Action == "delete")).Should().Be(0);
        (await db.SeedPatches.CountAsync(p => p.Key == "engineering-permissions")).Should().Be(1);
        await db.GroupPermissions.AddAsync(new BemAqui.Identity.Domain.GroupGrant { GroupId = engineering.Id, ModuleCode = "engineering", Action = "delete" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Roteiro_de_exemplo_sem_centro_nasce_inativo_e_nao_volta_depois_de_excluido()
    {
        // Tenant B: provisionado sem centros e sem uso nos outros testes.
        await using var scope = await ScopeAsync(tenancy.TenantB);
        var db = scope.ServiceProvider.GetRequiredService<IEngineeringDb>();
        var example = await db.RoutingTemplates.SingleAsync(t => t.SeedKey == RoutingTemplate.ExampleSeedKey);
        example.Name.Should().Be("Roteiro padrão (exemplo)");
        example.IsActive.Should().BeFalse("sem centro único, as etapas ficam sem centro e o roteiro nasce inativo (CA 8)");
        var steps = await db.RoutingTemplateSteps.Where(s => s.TemplateId == example.Id).OrderBy(s => s.Sequence).ToListAsync();
        steps.Select(s => (s.Code, s.Name)).Should().Equal(("P", "Projeto"), ("C", "Corte"), ("M", "Montagem"), ("Q", "Qualidade"), ("E", "Expedição"));
        steps.Should().OnlyContain(s => s.StandardHours == 0 && s.WorkCenterId == null);
        steps.Single(s => s.IsQualityStep).Code.Should().Be("Q");
        steps[^1].Checklist.Select(c => c.Text).Should().Equal("Preparar e embalar para o romaneio");

        await RunSeedersAsync(scope.ServiceProvider);
        (await db.RoutingTemplates.CountAsync(t => t.SeedKey == RoutingTemplate.ExampleSeedKey)).Should().Be(1, "idempotente");

        db.RoutingTemplates.Remove(example);
        await db.SaveChangesAsync();
        await RunSeedersAsync(scope.ServiceProvider);
        (await db.RoutingTemplates.CountAsync(t => t.SeedKey == RoutingTemplate.ExampleSeedKey)).Should().Be(0, "não recria o roteiro que o tenant excluiu");

        // Com um centro ativo só, o exemplo nasce ativo com esse centro (simulado sem a seed_key gravada).
        var only = await CenterAsync("Único", tenant: tenancy.TenantB);
        var tenantDb = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        await tenantDb.RoutingTemplates.IgnoreQueryFilters().Where(t => t.SeedKey == RoutingTemplate.ExampleSeedKey).ExecuteUpdateAsync(s => s.SetProperty(t => t.SeedKey, (string?)null));
        await scope.ServiceProvider.GetRequiredService<RoutingTemplateService>().SeedExampleAsync();
        var active = await db.RoutingTemplates.AsNoTracking().SingleAsync(t => t.SeedKey == RoutingTemplate.ExampleSeedKey);
        active.IsActive.Should().BeTrue();
        (await db.RoutingTemplateSteps.Where(s => s.TemplateId == active.Id).Select(s => s.WorkCenterId).Distinct().ToListAsync()).Should().Equal(only);
    }
}