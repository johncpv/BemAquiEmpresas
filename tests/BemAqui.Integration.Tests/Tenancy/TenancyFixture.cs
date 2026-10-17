using BemAqui.BuildingBlocks.Security;
using BemAqui.Host.Persistence;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>Catálogo + tenants A e B (bancos e roles próprios) + o Host real apontando para esse catálogo.</summary>
public sealed class TenancyFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public Tenant TenantA { get; private set; } = null!;

    public Tenant TenantB { get; private set; } = null!;

    public string CatalogConnection { get; private set; } = string.Empty;

    public string RoleAConnectionToB { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        CatalogConnection = await _postgres.CreateDatabaseAsync("catalog");
        var dbA = await CreateTenantDatabaseAsync("tenant_a", "role_a", "senha_a");
        var dbB = await CreateTenantDatabaseAsync("tenant_b", "role_b", "senha_b");
        RoleAConnectionToB = new NpgsqlConnectionStringBuilder(dbB) { Username = "role_a", Password = "senha_a" }.ConnectionString;

        Factory = HostSmokeTests.Factory(CatalogConnection).WithWebHostBuilder(b =>
        {
            b.UseSetting("Catalog:AutoMigrate", "true");
            b.UseSetting("Jobs:ServerEnabled", "false");
        });
        _ = Factory.Server; // sobe o Host: migra catálogo e key ring

        var protector = Factory.Services.GetRequiredService<ISecretProtector>();
        await using var scope = Factory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        TenantA = NewTenant("empresa-a", "Empresa A Ltda", "a.localhost", new NpgsqlConnectionStringBuilder(dbA) { Username = "role_a", Password = "senha_a" }.ConnectionString, protector);
        TenantB = NewTenant("empresa-b", "Empresa B Ltda", "b.localhost", new NpgsqlConnectionStringBuilder(dbB) { Username = "role_b", Password = "senha_b" }.ConnectionString, protector);
        catalog.Tenants.AddRange(TenantA, TenantB);
        await catalog.SaveChangesAsync();
    }

    public HttpClient ClientFor(string host)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        return client;
    }

    public async Task SetStatusAsync(Guid tenantId, bool suspended)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var tenant = await catalog.Tenants.SingleAsync(t => t.Id == tenantId);
        if (suspended)
        {
            tenant.Suspend();
        }
        else
        {
            tenant.Activate();
        }

        await catalog.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    private static Tenant NewTenant(string key, string name, string host, string connection, ISecretProtector protector)
    {
        var id = Guid.CreateVersion7();
        var tenant = new Tenant
        {
            Key = key,
            LegalName = name,
            DbName = key,
            CreatedAt = DateTimeOffset.UtcNow,
            ConnectionSecret = protector.Protect(BemAqui.BuildingBlocks.Security.SecretScope.ForTenant(id), SecretKind.TenantConnection, new Secret(connection)),
        };
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(tenant, id);
        tenant.Activate();
        tenant.Domains.Add(new TenantDomain { TenantId = id, Host = host, IsPrimary = true });
        return tenant;
    }

    /// <summary>Banco do tenant migrado, com role próprio e REVOKE CONNECT FROM PUBLIC (simula a FND-03a).</summary>
    private async Task<string> CreateTenantDatabaseAsync(string prefix, string role, string password)
    {
        var connection = await _postgres.CreateDatabaseAsync(prefix);
        var database = new NpgsqlConnectionStringBuilder(connection).Database!;
        await using (var db = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options))
        {
            await db.Database.MigrateAsync();
        }

        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        var sql = $"""
            CREATE ROLE {role} LOGIN PASSWORD '{password}';
            REVOKE CONNECT ON DATABASE "{database}" FROM PUBLIC;
            GRANT CONNECT ON DATABASE "{database}" TO {role};
            GRANT USAGE ON SCHEMA shared, audit TO {role};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA shared TO {role};
            GRANT SELECT, INSERT ON ALL TABLES IN SCHEMA audit TO {role};
            """;
        await using var command = new NpgsqlCommand(sql, admin);
        await command.ExecuteNonQueryAsync();
        return connection;
    }
}
