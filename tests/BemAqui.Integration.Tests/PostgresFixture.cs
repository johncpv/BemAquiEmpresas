using Npgsql;
using Testcontainers.PostgreSql;

namespace BemAqui.Integration.Tests;

/// <summary>
/// PostgreSQL 17 real (Testcontainers), compartilhado pelos testes da coleção. Os dados ficam em memória (tmpfs) e sem
/// fsync: cada <see cref="Tenancy.TenancyFixture"/> sobe o seu container, e no disco eles inflavam o disco virtual do Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithTmpfsMount("/var/lib/postgresql/data")
        .WithCommand("-c", "max_connections=500", "-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off")
        .WithDatabase("catalog")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Cria um banco vazio só para o teste e devolve a connection string dele.</summary>
    public async Task<string> CreateDatabaseAsync(string prefix)
    {
        var name = $"{prefix}_{Guid.CreateVersion7():N}";
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
