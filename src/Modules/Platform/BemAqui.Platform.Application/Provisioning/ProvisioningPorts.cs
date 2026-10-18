using BemAqui.Platform.Domain;

namespace BemAqui.Platform.Application.Provisioning;

/// <summary>Pedido de provisionamento (CLI ou tela da FND-03b).</summary>
public sealed record ProvisionRequest(string Key, string LegalName, string? Document, string Host, string OwnerEmail, string? Plan, bool Resume = false, string? TradeName = null);

/// <summary>Catálogo visto pelo provisionamento.</summary>
public interface IProvisioningCatalog
{
    Task<Tenant?> FindByKeyAsync(string key, CancellationToken cancellationToken);

    Task<bool> HostInUseAsync(string host, CancellationToken cancellationToken);

    Task<Guid?> EnsurePlanAsync(string? name, CancellationToken cancellationToken);

    Task AddTenantAsync(Tenant tenant, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> CompletedStepsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task LogAsync(Guid tenantId, string step, ProvisioningStatus status, string? message, CancellationToken cancellationToken);
}

/// <summary>Operações no servidor PostgreSQL com o role de provisionamento (sem SUPERUSER).</summary>
public interface ITenantDatabaseAdmin
{
    /// <summary>Cria (ou alinha a senha de) um role LOGIN sem SUPERUSER, CREATEDB ou CREATEROLE; a senha vem da connection string do tenant.</summary>
    Task EnsureRoleAsync(string role, string tenantConnectionString, CancellationToken cancellationToken);

    /// <summary>Cria o banco com dono = role, se não existir, e aplica REVOKE CONNECT FROM PUBLIC.</summary>
    Task EnsureDatabaseAsync(string database, string ownerRole, CancellationToken cancellationToken);

    /// <summary>Connection string do banco do tenant, com o role dele.</summary>
    string TenantConnectionString(string database, string role, string password);
}

/// <summary>Aplica as migrations do TenantDbContext num banco (implementado no Host, onde estão as migrations).</summary>
public interface ITenantDatabaseMigrator
{
    Task<(string? From, string? To)> MigrateAsync(string connectionString, CancellationToken cancellationToken);
}

