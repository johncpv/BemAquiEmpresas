using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Host.Cli;
using BemAqui.Host.Persistence;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FIN-10a CA 1 e 2: "Só o Proprietário" só no tenant novo; retomada de tenant ativo e o TenantMigrationRunner não gravam o padrão.</summary>
public partial class ProvisioningTests
{
    private async Task<decimal> SelfApprovalModeOfAsync(Tenant tenant)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().FinanceParameters.AsNoTracking()
            .Where(p => p.Key == FinanceParametersService.OwnerCanApproveOwn).Select(p => p.Value).SingleAsync();
    }

    private async Task SetSelfApprovalModeAsync(Tenant tenant, decimal value)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        (await db.FinanceParameters.SingleAsync(p => p.Key == FinanceParametersService.OwnerCanApproveOwn)).Value = value;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Tenant_novo_nasce_em_So_o_Proprietario_e_tenant_antigo_nao_muda_na_retomada_nem_no_deploy()
    {
        // Tenant novo (CA 2): o passo grava "Só o Proprietário" e fica no log.
        var fresh = await tenancy.ProvisionAsync("empresa-nova", "Empresa Nova Ltda", "nova.localhost");
        (await SelfApprovalModeOfAsync(fresh)).Should().Be(1m);
        (await LogsAsync(fresh.Id)).Should().Contain(l => l.Step == ProvisioningSteps.NewTenantDefaults && l.Status == ProvisioningStatus.Succeeded && l.Message!.Contains("Só o Proprietário"));

        // Tenant antigo (CA 1): provisionado antes do passo existir (sem a linha no log) e em "Nunca".
        var old = await tenancy.ProvisionAsync("empresa-antiga", "Empresa Antiga Ltda", "antiga.localhost");
        await SetSelfApprovalModeAsync(old, 0m);
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catalog.ProvisioningLogs.RemoveRange(catalog.ProvisioningLogs.Where(l => l.TenantId == old.Id && l.Step == ProvisioningSteps.NewTenantDefaults));
            await catalog.SaveChangesAsync();
        }

        // A retomada de um tenant já ativo registra o passo sem gravar nada.
        var resumed = await ProvisionAsync(new ProvisionRequest("empresa-antiga", "Empresa Antiga Ltda", null, "antiga.localhost", "dono@antiga.localhost", null, Resume: true));
        resumed.Success.Should().BeTrue(resumed.Error);
        resumed.Steps.Should().ContainSingle(s => s.Step == ProvisioningSteps.NewTenantDefaults && s.Message == "Pulada: tenant já ativo.");
        (await SelfApprovalModeOfAsync(old)).Should().Be(0m);

        // O deploy (migrations e seeders em todos os tenants) não grava o padrão: o novo fica em 1, o antigo em 0.
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            var reports = await ActivatorUtilities.CreateInstance<TenantMigrationRunner>(scope.ServiceProvider).MigrateAllAsync(CancellationToken.None);
            reports.Should().OnlyContain(r => r.Success);
        }

        (await SelfApprovalModeOfAsync(old)).Should().Be(0m);
        (await SelfApprovalModeOfAsync(fresh)).Should().Be(1m);
        (await tenancy.FindAsync("empresa-antiga")).Status.Should().Be(TenantLifecycle.Active);
    }
}
