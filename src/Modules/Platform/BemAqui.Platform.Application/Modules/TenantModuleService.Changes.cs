using BemAqui.BuildingBlocks.Modules;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Platform.Application.Modules;

public sealed partial class TenantModuleService
{
    /// <summary>"Salvar liberação": diferente do plano vira exceção; igual ao plano deixa de ser exceção. Auditado (CA 8).</summary>
    public async Task<Result> SaveAsync(Guid tenantId, IReadOnlyCollection<ModuleReleaseChange> changes, Guid? adminId, string? ip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var unknown = changes.Where(c => ModuleCatalog.Find(c.Code) is null).Select(c => c.Code).ToList();
        if (unknown.Count > 0)
        {
            return Error.Validation(Error.Codes.Validation, "Módulo desconhecido: " + string.Join(", ", unknown) + ".");
        }

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Empresa não encontrada.");
        }

        await ApplyPlanAsync(tenantId, tenant.PlanId, cancellationToken);
        var plan = await PlanModulesAsync(tenant.PlanId, cancellationToken);
        var rows = await db.TenantModules.Where(m => m.TenantId == tenantId).ToDictionaryAsync(m => m.ModuleCode, StringComparer.Ordinal, cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var change in changes)
        {
            var row = rows[change.Code];
            if (row.Enabled == change.Released)
            {
                continue;
            }

            Audit(tenantId + ":" + change.Code, change.Released ? "ModuleReleased" : "ModuleBlocked", adminId, ip, new { before = row.Enabled, after = change.Released });
            row.Enabled = change.Released;
            row.IsException = change.Released != plan.Contains(change.Code);
            row.ReleasedAt = change.Released ? now : row.ReleasedAt;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        resolver.Invalidate();
        return Result.Success();
    }

    /// <summary>Troca de plano: as linhas que não são exceção passam a seguir o plano novo (CA 7).</summary>
    public async Task<Result> ChangePlanAsync(Guid tenantId, Guid planId, Guid? adminId, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null || !await db.Plans.AnyAsync(p => p.Id == planId, cancellationToken))
        {
            return Error.NotFound(Error.Codes.NotFound, "Empresa ou plano não encontrado.");
        }

        var before = tenant.PlanId;
        tenant.PlanId = planId;
        Audit(tenantId.ToString(), "PlanChanged", adminId, null, new { before, after = planId });
        await ApplyPlanAsync(tenantId, planId, cancellationToken);
        return Result.Success();
    }
}
