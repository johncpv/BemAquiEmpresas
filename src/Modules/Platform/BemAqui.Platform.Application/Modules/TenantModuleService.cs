using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Platform.Application.Modules;

public interface ITenantModulesDb
{
    DbSet<Tenant> Tenants { get; }

    DbSet<Plan> Plans { get; }

    DbSet<PlanModule> PlanModules { get; }

    DbSet<TenantModule> TenantModules { get; }

    DbSet<AuditEntry> AuditEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Linha da aba "Módulos" do tenant: o que o plano dá, o que vale e se é exceção.</summary>
public sealed record ModuleRelease(string Code, string Name, string Area, string Description, bool Released, bool InPlan, bool IsException);

public sealed record ModuleReleaseChange(string Code, bool Released);

/// <summary>
/// Liberação de módulos por tenant, sem deploy (FND-06 CA 5 a 8). Gravar muda <c>tenant_modules</c>; o trigger do
/// catálogo avisa por NOTIFY e o cache do tenant recarrega, então vale no próximo request.
/// </summary>
public sealed partial class TenantModuleService(ITenantModulesDb db, BemAqui.BuildingBlocks.Tenancy.ITenantResolver resolver, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<ModuleRelease>>> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Empresa não encontrada.");
        }

        var plan = await PlanModulesAsync(tenant.PlanId, cancellationToken);
        var rows = await db.TenantModules.AsNoTracking().Where(m => m.TenantId == tenantId).ToDictionaryAsync(m => m.ModuleCode, StringComparer.Ordinal, cancellationToken);
        return ModuleCatalog.All
            .OrderBy(m => Array.IndexOf(ModuleAreas.All, m.Area)).ThenBy(m => m.Order)
            .Select(m =>
            {
                var row = rows.GetValueOrDefault(m.Code);
                return new ModuleRelease(m.Code, m.Name, m.Area, m.Description, row?.Enabled ?? plan.Contains(m.Code), plan.Contains(m.Code), row?.IsException ?? false);
            })
            .ToList();
    }

    /// <summary>Herda do plano: cria as linhas que faltam e atualiza as que não são exceção (provisionamento e troca de plano).</summary>
    public async Task ApplyPlanAsync(Guid tenantId, Guid? planId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var plan = await PlanModulesAsync(planId, cancellationToken);
        var rows = await db.TenantModules.Where(m => m.TenantId == tenantId).ToDictionaryAsync(m => m.ModuleCode, StringComparer.Ordinal, cancellationToken);
        foreach (var module in ModuleCatalog.All)
        {
            var enabled = plan.Contains(module.Code);
            if (!rows.TryGetValue(module.Code, out var row))
            {
                db.TenantModules.Add(new TenantModule { TenantId = tenantId, ModuleCode = module.Code, Enabled = enabled, ReleasedAt = enabled ? now : null, UpdatedAt = now });
            }
            else if (!row.IsException && row.Enabled != enabled)
            {
                row.Enabled = enabled;
                row.ReleasedAt = enabled ? now : row.ReleasedAt;
                row.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        resolver.Invalidate(); // nesta instância já no próximo request; nas outras, pelo NOTIFY do catálogo
    }

    private async Task<HashSet<string>> PlanModulesAsync(Guid? planId, CancellationToken cancellationToken) =>
        planId is null
            ? []
            : (await db.PlanModules.AsNoTracking().Where(p => p.PlanId == planId).Select(p => p.ModuleCode).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

    private void Audit(string entityId, string action, Guid? adminId, string? ip, object changes) =>
        db.AuditEntries.Add(new AuditEntry { Entity = "TenantModule", EntityId = entityId, Action = action, UserId = adminId, At = clock.GetUtcNow(), Ip = ip, Changes = JsonSerializer.Serialize(changes) });
}
