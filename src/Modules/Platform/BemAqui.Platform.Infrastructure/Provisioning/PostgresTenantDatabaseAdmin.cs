using System.Text.RegularExpressions;
using BemAqui.Platform.Application.Provisioning;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BemAqui.Platform.Infrastructure.Provisioning;

/// <summary>
/// Cria role e banco do tenant com o role de provisionamento (<c>ConnectionStrings:Provisioner</c>: CREATEDB e
/// CREATEROLE, sem SUPERUSER; FND-03a CA 7). Identificadores são validados (sem injeção) e a senha vai como literal
/// escapado só no comando, nunca em log.
/// </summary>
public sealed partial class PostgresTenantDatabaseAdmin(IConfiguration configuration) : ITenantDatabaseAdmin
{
    [GeneratedRegex("^[a-z][a-z0-9_]{1,62}$")]
    private static partial Regex Identifier();

    private string Provisioner => configuration.GetConnectionString("Provisioner")
        ?? throw new InvalidOperationException("ConnectionStrings:Provisioner não configurada (role bemaqui_provisioner).");

    public async Task EnsureRoleAsync(string role, string tenantConnectionString, CancellationToken cancellationToken)
    {
        Check(role);
        var password = new NpgsqlConnectionStringBuilder(tenantConnectionString).Password
            ?? throw new InvalidOperationException("Connection string do tenant sem senha.");
        await using var connection = await OpenAsync(cancellationToken);
        var exists = await ScalarAsync(connection, "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = $1)", role, cancellationToken);
        var verb = exists ? "ALTER" : "CREATE";
        // PASSWORD não aceita parâmetro: vai como literal escapado pelo próprio servidor (quote_literal).
        var literal = await ScalarTextAsync(connection, "SELECT quote_literal($1)", password, cancellationToken);
        await using var command = new NpgsqlCommand($"{verb} ROLE {role} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT PASSWORD {literal}", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task EnsureDatabaseAsync(string database, string ownerRole, CancellationToken cancellationToken)
    {
        Check(database);
        Check(ownerRole);
        await using var connection = await OpenAsync(cancellationToken);
        if (!await ScalarAsync(connection, "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = $1)", database, cancellationToken))
        {
            // O provisionador precisa ser membro do role para criar o banco com esse dono (sem SUPERUSER).
            await ExecuteAsync(connection, $"GRANT {ownerRole} TO CURRENT_USER", cancellationToken);
            await ExecuteAsync(connection, $"CREATE DATABASE {database} OWNER {ownerRole} ENCODING 'UTF8' TEMPLATE template0", cancellationToken);
        }

        await ExecuteAsync(connection, $"REVOKE CONNECT, TEMPORARY ON DATABASE {database} FROM PUBLIC", cancellationToken);
        await ExecuteAsync(connection, $"GRANT CONNECT ON DATABASE {database} TO {ownerRole}", cancellationToken);
    }

    public string TenantConnectionString(string database, string role, string password)
    {
        Check(database);
        Check(role);
        var builder = new NpgsqlConnectionStringBuilder(Provisioner) { Database = database, Username = role, Password = password };
        return builder.ConnectionString;
    }

    private static void Check(string identifier)
    {
        if (!Identifier().IsMatch(identifier))
        {
            throw new ArgumentException("Identificador inválido para o PostgreSQL: " + identifier);
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(Provisioner);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> ScalarAsync(NpgsqlConnection connection, string sql, string value, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { Parameters = { new() { Value = value } } };
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<string> ScalarTextAsync(NpgsqlConnection connection, string sql, string value, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { Parameters = { new() { Value = value } } };
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
