using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>Catálogo + tenants A e B criados pelo provisionamento real (role bemaqui_provisioner, sem superuser).</summary>
public sealed class TenancyFixture : IAsyncLifetime
{
    public const string ProvisionerPassword = "provisionador-teste";

    private readonly PostgresFixture _postgres = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public Tenant TenantA { get; private set; } = null!;

    public Tenant TenantB { get; private set; } = null!;

    public string CatalogConnection { get; private set; } = string.Empty;

    public string ProvisionerConnection { get; private set; } = string.Empty;

    public string RoleAConnectionToB { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        CatalogConnection = await _postgres.CreateDatabaseAsync("catalog");
        await using (var admin = new NpgsqlConnection(_postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE ROLE bemaqui_provisioner LOGIN CREATEDB CREATEROLE NOSUPERUSER PASSWORD '{ProvisionerPassword}'", admin);
            await command.ExecuteNonQueryAsync();
        }

        ProvisionerConnection = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString) { Database = "postgres", Username = "bemaqui_provisioner", Password = ProvisionerPassword }.ConnectionString;
        Factory = HostSmokeTests.Factory(CatalogConnection).WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Provisioner", ProvisionerConnection));
        _ = Factory.Server;

        TenantA = await ProvisionAsync("empresa-a", "Empresa A Ltda", "a.localhost");
        TenantB = await ProvisionAsync("empresa-b", "Empresa B Ltda", "b.localhost");
        var connectionA = Decrypt(TenantA);
        RoleAConnectionToB = new NpgsqlConnectionStringBuilder(connectionA) { Database = TenantB.DbName }.ConnectionString;
    }

    public async Task<Tenant> ProvisionAsync(string key, string name, string host, IServiceProvider? services = null)
    {
        await using var scope = (services ?? Factory.Services).CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ProvisioningService>()
            .ProvisionAsync(new ProvisionRequest(key, name, "47.225.005/0001-86", host, "dono@" + host, null));
        if (!result.Success)
        {
            throw new InvalidOperationException("Provisionamento de teste falhou: " + result.Error);
        }

        return await FindAsync(key);
    }

    public async Task<Tenant> FindAsync(string key)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants.AsNoTracking().Include(t => t.Domains).SingleAsync(t => t.Key == key);
    }

    public string Decrypt(Tenant tenant) =>
        Factory.Services.GetRequiredService<ISecretProtector>().Unprotect(SecretScope.ForTenant(tenant.Id), SecretKind.TenantConnection, tenant.ConnectionSecret).Reveal();

    public const string DefaultPassword = "uma frase de teste bem longa";

    /// <summary>Cria um usuário ativo no banco do tenant e devolve id e security stamp.</summary>
    public async Task<(Guid Id, string Stamp)> SeedUserAsync(Tenant tenant, string email, string password = DefaultPassword)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        await using var scope = await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        var user = User.Invite(email, "Usuário de teste");
        user.SetPassword(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, password));
        user.Activate();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, user.SecurityStamp);
    }

    /// <summary>Token de teste de um usuário real (com sstamp válido) do tenant, para o host informado.</summary>
    public async Task<string> TokenAsync(Tenant tenant, string host)
    {
        var (id, stamp) = await SeedUserAsync(tenant, "u" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        return TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp);
    }

    public HttpClient ClientFor(string host)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        return client;
    }

    public async Task UpdateTenantAsync(Guid tenantId, Action<Tenant> change)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        change(await catalog.Tenants.SingleAsync(t => t.Id == tenantId));
        await catalog.SaveChangesAsync();
    }

    public Task SetStatusAsync(Guid tenantId, bool suspended) =>
        UpdateTenantAsync(tenantId, t =>
        {
            if (suspended)
            {
                t.Suspend();
            }
            else
            {
                t.Activate();
            }
        });

    public Task<NpgsqlConnection> AdminAsync() => OpenAsync(_postgres.ConnectionString);

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
