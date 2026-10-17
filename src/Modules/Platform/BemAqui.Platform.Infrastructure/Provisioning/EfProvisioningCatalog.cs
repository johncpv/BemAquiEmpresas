using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Platform.Infrastructure.Provisioning;

public sealed class EfProvisioningCatalog(CatalogDbContext db, TimeProvider clock) : IProvisioningCatalog
{
    public const string DefaultPlan = "Piloto";

    public Task<Tenant?> FindByKeyAsync(string key, CancellationToken cancellationToken) =>
        db.Tenants.Include(t => t.Domains).FirstOrDefaultAsync(t => t.Key == key, cancellationToken);

    public Task<bool> HostInUseAsync(string host, CancellationToken cancellationToken) =>
        db.TenantDomains.AnyAsync(d => d.Host == host, cancellationToken);

    public async Task<Guid?> EnsurePlanAsync(string? name, CancellationToken cancellationToken)
    {
        var planName = string.IsNullOrWhiteSpace(name) ? DefaultPlan : name.Trim();
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Name == planName, cancellationToken);
        if (plan is null)
        {
            plan = new Plan { Name = planName };
            db.Plans.Add(plan);
            await db.SaveChangesAsync(cancellationToken);
        }

        return plan.Id;
    }

    public async Task AddTenantAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlySet<string>> CompletedStepsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        (await db.ProvisioningLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.Status == ProvisioningStatus.Succeeded)
            .Select(l => l.Step)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

    public async Task LogAsync(Guid tenantId, string step, ProvisioningStatus status, string? message, CancellationToken cancellationToken)
    {
        db.ProvisioningLogs.Add(new ProvisioningLog { TenantId = tenantId, Step = step, Status = status, Message = message, At = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }
}
