using System.Reflection;
using BemAqui.Platform.Application.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BemAqui.Host.Persistence;

/// <summary>Versão de schema que este build espera nos bancos de tenant: a última migration do TenantDbContext.</summary>
public static class TenantSchema
{
    public static string ExpectedVersion { get; } = typeof(TenantDbContext).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(TenantDbContext))
        .Select(t => t.GetCustomAttribute<MigrationAttribute>()?.Id)
        .OfType<string>()
        .Order(StringComparer.Ordinal)
        .Last();
}

/// <summary>
/// Migra um banco de tenant com advisory lock na mesma conexão (dois migrators nunca migram o mesmo banco em
/// paralelo; FND-03a CA 11). Devolve a versão anterior e a nova.
/// </summary>
public sealed class TenantDatabaseMigrator : ITenantDatabaseMigrator
{
    // Chave fixa do lock de migração (64 bits), igual em todos os bancos de tenant.
    public const long LockKey = 0x42_65_6D_41_71_75_69_4D;

    public async Task<(string? From, string? To)> MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var db = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock({0})", [LockKey], cancellationToken);
            var from = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
            await db.Database.MigrateAsync(cancellationToken);
            var to = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", [LockKey], cancellationToken);
            return (from, to);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

/// <summary>A versão esperada para quem está fora do middleware de tenant por host (webhooks, FND-16).</summary>
public sealed class TenantSchemaVersion : BemAqui.BuildingBlocks.Webhooks.ITenantSchemaVersion
{
    public string Expected => TenantSchema.ExpectedVersion;
}
