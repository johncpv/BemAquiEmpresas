using System.Text;
using BemAqui.Migrations.Infrastructure;
using MySqlConnector;
using Testcontainers.MariaDb;

namespace BemAqui.Integration.Tests.Importing;

/// <summary>
/// MariaDB 10.11 (a do Dolibarr) com um banco <c>dolibarr</c> no DDL fixo mais colunas que não podem sair (ex.: <c>birthday</c>),
/// o usuário <c>bqe_migracao_ro</c> e o GRANT do runbook aplicado como está. Os clientes rodam dentro do container, como no
/// runbook, então o usuário restrito a <c>localhost</c> vale sem trocar o script.
/// </summary>
public sealed class MariaDbFixture : IAsyncLifetime
{
    public const string RoPassword = "senha-ro-de-teste";

    private readonly MariaDbContainer _container = new MariaDbBuilder("mariadb:10.11").Build();

    public string RootPassword => new MySqlConnectionStringBuilder(_container.GetConnectionString()).Password;

    public string Connection(string database) =>
        new MySqlConnectionStringBuilder(_container.GetConnectionString()) { UserID = "root", Password = RootPassword, Database = database }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await RootAsync(null, "CREATE DATABASE dolibarr CHARACTER SET utf8mb4; CREATE DATABASE snapshot CHARACTER SET utf8mb4;");
        await RootFileAsync("dolibarr", SnapshotScripts.Ddl()
            + "ALTER TABLE `llx_socpeople` ADD `birthday` DATE NULL;\n"
            + "ALTER TABLE `llx_societe` ADD `pass_crypted` VARCHAR(128) NULL;\n"
            + "INSERT INTO `llx_const` (`name`, `value`, `entity`) VALUES ('MAIN_INFO_SIREN', '47.225.005/0001-86', 1), ('MAIN_MAIL_SMTPS_PW', 'segredo', 1);\n"
            + "INSERT INTO `llx_societe` (`rowid`, `nom`, `siren`, `email`, `client`, `entity`, `note_private`, `tms`) VALUES "
            + "(1, 'Elevadores Ação D''Ávila', '47225005000186', 'compras@acao.com.br', 1, 1, 'linha 1\nlinha 2', '2026-10-01 10:00:00'), "
            + "(2, 'Sem documento', NULL, NULL, 0, 1, NULL, '0000-00-00 00:00:00');\n"
            + "INSERT INTO `llx_socpeople` (`rowid`, `fk_soc`, `lastname`, `birthday`, `entity`) VALUES (10, 1, 'Silva', '1980-01-01', 1);\n");
        await RootAsync("dolibarr", "CREATE USER 'bqe_migracao_ro'@'localhost' IDENTIFIED BY '" + RoPassword + "';");
        await RootFileAsync("dolibarr", await File.ReadAllTextAsync(Path.Combine(OpenApiDocumentTests.RepositoryRoot(), "scripts", "dolibarr", "grant-readonly.sql")));
    }

    /// <summary>Roda SQL como o usuário somente leitura, dentro do container. Devolve código de saída, saída e erro.</summary>
    public async Task<(long ExitCode, string Stdout, string Stderr)> ReadOnlyAsync(string sql, params string[] options)
    {
        await _container.CopyAsync(Encoding.UTF8.GetBytes(sql), "/tmp/ro.sql");
        var result = await _container.ExecAsync(["sh", "-c", "mariadb -ubqe_migracao_ro -p" + RoPassword + " --default-character-set=utf8mb4 " + string.Join(' ', options) + " dolibarr < /tmp/ro.sql"]);
        return (result.ExitCode ?? -1, result.Stdout, result.Stderr);
    }

    public async Task RootFileAsync(string database, string sql)
    {
        await _container.CopyAsync(Encoding.UTF8.GetBytes(sql), "/tmp/root.sql");
        var result = await _container.ExecAsync(["sh", "-c", "mariadb -uroot -p" + RootPassword + " --default-character-set=utf8mb4 " + database + " < /tmp/root.sql"]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("SQL como root falhou: " + result.Stderr);
        }
    }

    private Task RootAsync(string? database, string sql) => RootFileAsync(database ?? string.Empty, sql);

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
