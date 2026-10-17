using System.Diagnostics;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Host.Cli;

public sealed record TenantMigrationReport(string Tenant, string? From, string? To, bool Success, TimeSpan Duration, string? Error);

/// <summary>
/// Migra todos os tenants (ativos e suspensos) do catálogo. Falha em um não impede os outros; o relatório
/// marca cada um e a versão aplicada fica no catálogo (FND-03a CA 9 e 10).
/// </summary>
public sealed class TenantMigrationRunner(CatalogDbContext catalog, ITenantDatabaseMigrator migrator, ISecretProtector protector, ITenantResolver resolver, TimeProvider clock)
{
    public async Task<IReadOnlyList<TenantMigrationReport>> MigrateAllAsync(CancellationToken cancellationToken)
    {
        var tenants = await catalog.Tenants
            .Where(t => t.Status != TenantLifecycle.Provisioning)
            .OrderBy(t => t.Key)
            .ToListAsync(cancellationToken);

        var reports = new List<TenantMigrationReport>();
        foreach (var tenant in tenants)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var connection = protector.Unprotect(SecretScope.ForTenant(tenant.Id), SecretKind.TenantConnection, tenant.ConnectionSecret).Reveal();
                var (from, to) = await migrator.MigrateAsync(connection, cancellationToken);
                tenant.SchemaVersion = to;
                tenant.MigratedAt = clock.GetUtcNow();
                await catalog.SaveChangesAsync(cancellationToken);
                reports.Add(new TenantMigrationReport(tenant.Key, from, to, true, Stopwatch.GetElapsedTime(started), null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                reports.Add(new TenantMigrationReport(tenant.Key, tenant.SchemaVersion, null, false, Stopwatch.GetElapsedTime(started), ex.Message));
            }
        }

        resolver.Invalidate();
        return reports;
    }
}
