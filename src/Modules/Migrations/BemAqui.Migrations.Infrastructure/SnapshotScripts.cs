using System.Text;
using BemAqui.Migrations.Application.Snapshot;

namespace BemAqui.Migrations.Infrastructure;

/// <summary>
/// Os três scripts de <c>scripts/dolibarr/</c>, gerados da lista fixa (<see cref="SnapshotTables"/>), para o GRANT, o snapshot
/// e o DDL nunca divergirem. O teste de consistência compara os arquivos com esta saída; com <c>BQ_UPDATE_SNAPSHOT_SQL=1</c>
/// ele os regrava.
/// </summary>
public static class SnapshotScripts
{
    public const string User = "'bqe_migracao_ro'@'localhost'";

    private const string Header = "-- Gerado de SnapshotTables (MIG-01a, ADR 0017). Não edite à mão: rode o SnapshotScriptsTests com BQ_UPDATE_SNAPSHOT_SQL=1.\n";

    private static string Q(string identifier) => "`" + identifier + "`";

    private static string Columns(SnapshotTable table) => string.Join(", ", table.Columns.Select(c => Q(c.Name)));

    /// <summary>SELECT de colunas explícitas, na ordem da chave (o importador lê assim do MariaDB temporário).</summary>
    public static string Select(SnapshotTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return "SELECT " + Columns(table) + " FROM " + Q(table.Name)
            + (table.Filter is null ? string.Empty : " WHERE " + table.Filter)
            + " ORDER BY " + string.Join(", ", table.Key.Select(Q));
    }

    /// <summary>GRANT SELECT por coluna, só nas colunas mapeadas (CA 2). Roda no banco do Dolibarr, depois de criar o usuário.</summary>
    public static string Grant()
    {
        var sql = new StringBuilder(Header);
        sql.Append("-- Uso (runbook docs/operacao/migracao-dolibarr.md): mariadb <banco-do-dolibarr> < grant-readonly.sql\n");
        sql.Append("-- O usuário ").Append(User).Append(" é criado antes, com a senha do arquivo de secret; aqui só entram as permissões.\n");
        foreach (var table in SnapshotTables.All)
        {
            sql.Append("GRANT SELECT (").Append(Columns(table)).Append(") ON ").Append(Q(table.Name)).Append(" TO ").Append(User).Append(";\n");
        }

        return sql.ToString();
    }

    /// <summary>
    /// Extração (CA 3): numa transação consistente e só de leitura, cada linha sai como um INSERT com colunas explícitas
    /// (<c>QUOTE</c> escapa o texto e escreve NULL). A primeira linha grava a data e hora da extração em <c>bqe_snapshot</c>.
    /// </summary>
    public static string Snapshot()
    {
        var sql = new StringBuilder(Header);
        sql.Append("-- Uso: scripts/dolibarr-snapshot.sh (mariadb --batch --raw --skip-column-names, saída direto para o age).\n");
        sql.Append("SET SESSION TRANSACTION ISOLATION LEVEL REPEATABLE READ;\n");
        sql.Append("START TRANSACTION WITH CONSISTENT SNAPSHOT, READ ONLY;\n");
        sql.Append("SELECT CONCAT('INSERT INTO `bqe_snapshot` (`taken_at`) VALUES (', QUOTE(UTC_TIMESTAMP(6)), ');');\n");
        foreach (var table in SnapshotTables.All)
        {
            var values = string.Join(", ", table.Columns.Select(c => "QUOTE(CONVERT(" + Q(c.Name) + " USING utf8mb4))"));
            sql.Append("SELECT CONCAT('INSERT INTO ").Append(Q(table.Name)).Append(" (").Append(Columns(table)).Append(") VALUES (', CONCAT_WS(',', ")
                .Append(values).Append("), ');') FROM ").Append(Q(table.Name));
            if (table.Filter is not null)
            {
                sql.Append(" WHERE ").Append(table.Filter);
            }

            sql.Append(";\n");
        }

        sql.Append("COMMIT;\n");
        return sql.ToString();
    }

    /// <summary>DDL fixo do MariaDB temporário (CA 4): só as colunas mapeadas, em utf8mb4, sem modo estrito (datas zeradas do Dolibarr).</summary>
    public static string Ddl()
    {
        var sql = new StringBuilder(Header);
        sql.Append("SET NAMES utf8mb4;\nSET SESSION sql_mode = '';\n");
        sql.Append("CREATE TABLE `bqe_snapshot` (`taken_at` DATETIME(6) NOT NULL) DEFAULT CHARSET=utf8mb4;\n");
        foreach (var table in SnapshotTables.All)
        {
            var columns = table.Columns.Select(c => Q(c.Name) + " " + c.Type + (table.Key.Contains(c.Name) ? " NOT NULL" : " NULL"));
            sql.Append("CREATE TABLE ").Append(Q(table.Name)).Append(" (").Append(string.Join(", ", columns))
                .Append(", PRIMARY KEY (").Append(string.Join(", ", table.Key.Select(Q))).Append(")) DEFAULT CHARSET=utf8mb4;\n");
        }

        return sql.ToString();
    }
}
