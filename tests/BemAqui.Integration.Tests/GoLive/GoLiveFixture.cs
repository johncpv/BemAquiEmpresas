using BemAqui.BuildingBlocks.Security;
using BemAqui.Host.Cli;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.GoLive;

/// <summary>
/// Dois ambientes no mesmo PostgreSQL (GOL-01a): o "antigo" (homologação) é o catálogo do <see cref="TenancyFixture"/>, com o certificado
/// <see cref="TestCertificates.Primary"/>; o "novo" (produção) é outro catálogo, migrado pela CLI, com key ring e certificado próprios
/// (<see cref="TestCertificates.Other"/>).
/// </summary>
public sealed class GoLiveFixture : IAsyncLifetime
{
    private readonly List<string> _files = [];

    public TenancyFixture Tenancy { get; } = new();

    public string NewCatalog { get; private set; } = string.Empty;

    /// <summary>Arquivo de secret com a conexão do catálogo antigo (o <c>--from-catalog-file</c>).</summary>
    public string OldCatalogFile { get; private set; } = string.Empty;

    public ISecretProtector OldProtector => Tenancy.Factory.Services.GetRequiredService<ISecretProtector>();

    public async ValueTask InitializeAsync()
    {
        await Tenancy.InitializeAsync();
        var name = "catalog_prod_" + Guid.NewGuid().ToString("N")[..8];
        await using (var admin = await Tenancy.AdminAsync())
        await using (var create = new NpgsqlCommand("CREATE DATABASE " + name, admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        NewCatalog = new NpgsqlConnectionStringBuilder(Tenancy.CatalogConnection) { Database = name }.ConnectionString;
        var output = new StringWriter();
        if (await RunNewAsync(output, allowKeyRingCreation: true, "migrate") != 0)
        {
            throw new InvalidOperationException("Catálogo novo não migrou: " + output);
        }

        OldCatalogFile = TempFile(Tenancy.CatalogConnection);
    }

    /// <summary>CLI no ambiente novo (catálogo e certificado da produção).</summary>
    public Task<int> RunNewAsync(TextWriter output, params string[] args) => RunNewAsync(output, false, args);

    public Task<int> RunOldAsync(TextWriter output, params string[] args) =>
        CliCommand.RunAsync(args, c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = Tenancy.CatalogConnection,
            ["DataProtection:CertificatePath"] = TestCertificates.Primary,
            ["DataProtection:CertificatePassword"] = TestCertificates.Password,
            ["DataProtection:AllowKeyRingCreation"] = "false",
        }), output);

    /// <summary>Protetor do ambiente novo, como a API de produção o monta.</summary>
    public ServiceProvider NewSecrets() => SecretsHarness.Build(NewCatalog, TestCertificates.Other, allowCreation: false);

    /// <summary>Export do key ring antigo como o runbook manda: <c>psql -At -c "SELECT xml FROM security.data_protection_keys"</c>.</summary>
    public async Task<string> ExportOldKeyRingAsync()
    {
        var lines = new List<string>();
        await using var connection = new NpgsqlConnection(Tenancy.CatalogConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT xml FROM security.data_protection_keys ORDER BY id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return TempFile(string.Join('\n', lines) + "\n");
    }

    /// <summary>Tenant próprio do teste no ambiente antigo.</summary>
    public Task<Tenant> ProvisionAsync(string key) => Tenancy.ProvisionAsync(key, "Empresa " + key, key + ".homolog.localhost");

    public async Task<int> PromoteAsync(string key, TextWriter? output = null) =>
        await RunNewAsync(output ?? new StringWriter(), "tenant", "promote", "--tenant", key, "--from-catalog-file", OldCatalogFile,
            "--primary-host", "app." + key + ".localhost", "--confirm-tenant", key);

    public string TempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "bemaqui-golive-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, content);
        _files.Add(path);
        return path;
    }

    public string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "bemaqui-golive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _files.Add(path);
        return path;
    }

    /// <summary>Resultado de um SELECT do script: colunas e linhas.</summary>
    public sealed record ScriptResult(IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows);

    /// <summary>
    /// Roda um script de <c>scripts/golive/</c> como o psql (<c>-c "SET ..." -f script</c>): as variáveis de sessão primeiro, depois o arquivo
    /// inteiro na mesma conexão. Devolve cada conjunto de resultados.
    /// </summary>
    public static async Task<List<ScriptResult>> RunScriptAsync(string connectionString, string script, IReadOnlyDictionary<string, string>? settings = null)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(OpenApiDocumentTests.RepositoryRoot(), "scripts", "golive", script));
        await using var connection = await OpenAsync(connectionString);
        foreach (var (name, value) in settings ?? new Dictionary<string, string>())
        {
            await using var set = new NpgsqlCommand("SELECT set_config($1, $2, false)", connection) { Parameters = { new() { Value = name }, new() { Value = value } } };
            await set.ExecuteNonQueryAsync();
        }

        var results = new List<ScriptResult>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        do
        {
            if (reader.FieldCount == 0)
            {
                continue;
            }

            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var rows = new List<object?[]>();
            while (await reader.ReadAsync())
            {
                var row = new object[reader.FieldCount];
                reader.GetValues(row);
                rows.Add(row.Select(v => v is DBNull ? null : (object?)v).ToArray());
            }

            results.Add(new ScriptResult(columns, rows));
        }
        while (await reader.NextResultAsync());

        return results;
    }

    public static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var path in _files)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }

        await Tenancy.DisposeAsync();
    }

    private Task<int> RunNewAsync(TextWriter output, bool allowKeyRingCreation, params string[] args) =>
        CliCommand.RunAsync(args, c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = NewCatalog,
            ["DataProtection:CertificatePath"] = TestCertificates.Other,
            ["DataProtection:CertificatePassword"] = TestCertificates.Password,
            ["DataProtection:AllowKeyRingCreation"] = allowKeyRingCreation.ToString(),
        }), output);
}

[CollectionDefinition(Name)]
public sealed class GoLiveGroup : ICollectionFixture<GoLiveFixture>
{
    public const string Name = "golive";
}
