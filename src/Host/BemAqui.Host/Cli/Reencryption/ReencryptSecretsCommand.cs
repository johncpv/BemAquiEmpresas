using System.Security.Cryptography;
using System.Xml.Linq;
using BemAqui.BuildingBlocks.Security;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BemAqui.Host.Cli.Reencryption;

/// <summary>
/// <c>reencrypt-secrets</c> (GOL-01a CA 4, ADR 0034 §2): decifra cada segredo do tenant com o key ring antigo, aberto só neste comando, e
/// cifra de novo com o key ring configurado (o do ambiente novo). Uma transação no banco do tenant e outra no catálogo; antes do commit,
/// confere que tudo decifra com o novo e nada decifra com o antigo. Nenhum valor vai para a saída: só a contagem por coluna e por tipo.
/// Idempotente: o que já decifra com o novo fica como está.
/// </summary>
internal static class ReencryptSecretsCommand
{
    public const string Usage =
        "Uso: reencrypt-secrets --tenant <chave> --old-keys <export-do-key-ring> --old-cert <certificado-antigo.pfx> --old-cert-password-file <arquivo>\n"
        + "       (--dry-run | --confirm-tenant <chave>) [--tenant-connection-file <arquivo-com-a-conexão-nova>]";

    public static async Task<int> RunAsync(IServiceProvider services, CliArguments args, TextWriter output)
    {
        var key = args.Get("tenant");
        var dryRun = args.Has("dry-run");
        if (string.IsNullOrWhiteSpace(key) || args.Get("old-keys") is not { } keys || args.Get("old-cert") is not { } cert || args.Get("old-cert-password-file") is not { } password)
        {
            await output.WriteLineAsync("Informe --tenant, --old-keys, --old-cert e --old-cert-password-file. Nada foi gravado.\n" + Usage);
            return 1;
        }

        if (!dryRun && args.Get("confirm-tenant") != key)
        {
            await output.WriteLineAsync("Para gravar, repita a chave em --confirm-tenant " + key + " (ou rode antes com --dry-run). Nada foi gravado.");
            return 1;
        }

        string? newConnection = null;
        if (args.Get("tenant-connection-file") is { } connectionFile)
        {
            if (!File.Exists(connectionFile))
            {
                await output.WriteLineAsync("Arquivo da conexão nova do tenant não encontrado: " + connectionFile + ". Nada foi gravado.");
                return 1;
            }

            newConnection = (await File.ReadAllTextAsync(connectionFile)).Trim();
        }

        OldKeyRing old;
        try
        {
            old = OldKeyRing.Open(keys, cert, password);
        }
        catch (InvalidOperationException ex)
        {
            await output.WriteLineAsync(ex.Message + " Nada foi gravado.");
            return 1;
        }

        using (old)
        {
            var run = new Run(services, old, key, dryRun, newConnection, output);
            return await run.ExecuteAsync();
        }
    }

    private sealed class Run(IServiceProvider services, OldKeyRing old, string tenantKey, bool dryRun, string? newConnection, TextWriter output)
    {
        private readonly ISecretProtector _new = services.GetRequiredService<ISecretProtector>();
        private readonly string _catalog = services.GetRequiredService<IConfiguration>().GetConnectionString("Catalog")!;
        private readonly List<ColumnReport> _reports = [];

        public async Task<int> ExecuteAsync()
        {
            var refusal = await GuardAsync();
            if (refusal is not null)
            {
                await output.WriteLineAsync(refusal + " Nada foi gravado.");
                return 1;
            }

            await using var catalog = await OpenAsync(_catalog);
            var tenant = await FindTenantAsync(catalog);
            if (tenant is null)
            {
                await output.WriteLineAsync("Tenant não encontrado no catálogo: " + tenantKey + ". Nada foi gravado.");
                return 1;
            }

            var (tenantId, connectionCipher) = tenant.Value;
            var connection = newConnection ?? Decrypt(SecretColumns.TenantConnection, tenantId, tenantId, connectionCipher);
            if (connection is null)
            {
                await output.WriteLineAsync("A conexão do tenant não decifra nem com o key ring novo nem com o antigo. Nada foi gravado.");
                return 2;
            }

            await output.WriteLineAsync("Recifração dos segredos do tenant " + tenantKey + (dryRun ? " (simulação: nada é gravado)." : "."));
            await using (var tenantDb = await OpenAsync(connection))
            {
                var unknown = SecretColumns.Unknown(SecretDatabase.Tenant, await CipherColumnsAsync(tenantDb));
                if (unknown.Count > 0)
                {
                    await output.WriteLineAsync("Coluna cifrada fora da lista do comando: " + string.Join(", ", unknown) + ". Atualize SecretColumns antes de recifrar. Nada foi gravado.");
                    return 1;
                }

                if (!await ProcessAsync(tenantDb, SecretDatabase.Tenant, tenantId, null))
                {
                    return 2;
                }
            }

            if (!await ProcessAsync(catalog, SecretDatabase.Catalog, tenantId, newConnection))
            {
                return 2;
            }

            await WriteSummaryAsync();
            return 0;
        }

        /// <summary>Recusa key ring ou certificado em comum entre o antigo e o novo, e coluna cifrada desconhecida no catálogo.</summary>
        private async Task<string?> GuardAsync()
        {
            if (string.Equals(old.CertificateThumbprint, services.GetRequiredService<ProtectionCertificateInfo>().Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return "O certificado antigo é o mesmo do ambiente novo: a produção precisa de certificado novo (ADR 0034).";
            }

            var keys = services.GetRequiredService<DataProtectionKeysDbContext>();
            var xml = await keys.DataProtectionKeys.AsNoTracking().Select(k => k.Xml).ToListAsync();
            var current = OldKeyRing.KeyIdsOf(xml.Where(x => x is not null).Select(x => XElement.Parse(x!)));
            if (current.Overlaps(old.KeyIds))
            {
                return "O key ring antigo e o novo têm chaves em comum: a produção precisa de key ring novo (ADR 0034).";
            }

            await using var catalog = await OpenAsync(_catalog);
            var unknown = SecretColumns.Unknown(SecretDatabase.Catalog, await CipherColumnsAsync(catalog));
            return unknown.Count > 0 ? "Coluna cifrada fora da lista do comando no catálogo: " + string.Join(", ", unknown) + "." : null;
        }

        private async Task<(Guid Id, string Cipher)?> FindTenantAsync(NpgsqlConnection catalog)
        {
            await using var command = new NpgsqlCommand("SELECT id, connection_secret FROM catalog.tenants WHERE key = $1", catalog) { Parameters = { new() { Value = tenantKey } } };
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? (reader.GetGuid(0), reader.GetString(1)) : null;
        }

        /// <summary>Uma transação por banco: recifra, confere (novo decifra, antigo não) e só então grava; na simulação, desfaz.</summary>
        private async Task<bool> ProcessAsync(NpgsqlConnection db, SecretDatabase database, Guid tenantId, string? replacementConnection)
        {
            await using var transaction = await db.BeginTransactionAsync();
            foreach (var column in SecretColumns.In(database))
            {
                var report = new ColumnReport(column);
                _reports.Add(report);
                foreach (var (rowId, cipher) in await RowsAsync(db, column, tenantId))
                {
                    if (column.Purpose == SecretPurpose.TenantRow && replacementConnection is not null)
                    {
                        await UpdateAsync(db, column, rowId, Protect(column, tenantId, rowId, new Secret(replacementConnection)));
                        report.Replaced++;
                        continue;
                    }

                    if (Decrypt(_new, column, tenantId, rowId, cipher) is not null)
                    {
                        report.AlreadyNew++;
                        continue;
                    }

                    if (Decrypt(old.Protector, column, tenantId, rowId, cipher) is not { } plain)
                    {
                        report.Unreadable++;
                        continue;
                    }

                    await UpdateAsync(db, column, rowId, Protect(column, tenantId, rowId, new Secret(plain)));
                    report.Reencrypted++;
                }
            }

            var verification = await VerifyAsync(db, database, tenantId);
            var reports = _reports.Where(r => r.Column.Database == database).ToList();
            await output.WriteLineAsync(database == SecretDatabase.Tenant ? "Banco do tenant:" : "Catálogo:");
            foreach (var r in reports)
            {
                await output.WriteLineAsync($"  {r.Column.Name,-52} {SecretColumns.KindName(r.Column.Kind),-18} recifrados: {r.Reencrypted + r.Replaced,3}   já no key ring novo: {r.AlreadyNew,3}   ilegíveis: {r.Unreadable,3}");
            }

            var unreadable = reports.Sum(r => r.Unreadable);
            if (unreadable > 0 || verification.OldReadable > 0 || verification.NewUnreadable > 0)
            {
                await transaction.RollbackAsync();
                await output.WriteLineAsync($"Falhou: {unreadable} segredo(s) não decifram com nenhum dos key rings; na conferência, {verification.NewUnreadable} não decifram com o novo e {verification.OldReadable} ainda decifram com o antigo. Tudo deste banco foi desfeito.");
                return false;
            }

            await output.WriteLineAsync($"  Conferência: {verification.Total} segredo(s) decifram com o key ring novo; 0 decifram com o antigo.");
            if (dryRun)
            {
                await transaction.RollbackAsync();
            }
            else
            {
                await transaction.CommitAsync();
            }

            return true;
        }

        private async Task<(int Total, int NewUnreadable, int OldReadable)> VerifyAsync(NpgsqlConnection db, SecretDatabase database, Guid tenantId)
        {
            var total = 0;
            var newUnreadable = 0;
            var oldReadable = 0;
            foreach (var column in SecretColumns.In(database))
            {
                foreach (var (rowId, cipher) in await RowsAsync(db, column, tenantId))
                {
                    total++;
                    newUnreadable += Decrypt(_new, column, tenantId, rowId, cipher) is null ? 1 : 0;
                    oldReadable += Decrypt(old.Protector, column, tenantId, rowId, cipher) is null ? 0 : 1;
                }
            }

            return (total, newUnreadable, oldReadable);
        }

        private async Task WriteSummaryAsync()
        {
            await output.WriteLineAsync("Por tipo:");
            foreach (var kind in _reports.GroupBy(r => SecretColumns.KindName(r.Column.Kind)).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                await output.WriteLineAsync($"  {kind.Key,-18} recifrados: {kind.Sum(r => r.Reencrypted + r.Replaced),3}   já no key ring novo: {kind.Sum(r => r.AlreadyNew),3}");
            }

            await output.WriteLineAsync(dryRun
                ? "Simulação concluída: nada foi gravado. Para gravar, rode de novo sem --dry-run e com --confirm-tenant " + tenantKey + "."
                : "Recifração concluída. Nenhum segredo do tenant decifra mais com o key ring antigo; guarde o export e o certificado antigos só até o fim do ensaio ou da virada e apague-os.");
        }

        private string? Decrypt(SecretColumn column, Guid tenantId, Guid rowId, string cipher) =>
            Decrypt(_new, column, tenantId, rowId, cipher) ?? Decrypt(old.Protector, column, tenantId, rowId, cipher);

        private string Protect(SecretColumn column, Guid tenantId, Guid rowId, Secret secret) => column.Purpose switch
        {
            SecretPurpose.Platform => _new.Protect(SecretScope.Platform, column.Kind, secret),
            SecretPurpose.TenantRow => _new.Protect(SecretScope.ForTenant(rowId), column.Kind, secret),
            SecretPurpose.TenantItem => _new.Protect(SecretScope.ForTenant(tenantId), column.Kind, rowId, secret),
            _ => _new.Protect(SecretScope.ForTenant(tenantId), column.Kind, secret),
        };

        private static string? Decrypt(ISecretProtector protector, SecretColumn column, Guid tenantId, Guid rowId, string cipher)
        {
            try
            {
                return (column.Purpose switch
                {
                    SecretPurpose.Platform => protector.Unprotect(SecretScope.Platform, column.Kind, cipher),
                    SecretPurpose.TenantRow => protector.Unprotect(SecretScope.ForTenant(rowId), column.Kind, cipher),
                    SecretPurpose.TenantItem => protector.Unprotect(SecretScope.ForTenant(tenantId), column.Kind, rowId, cipher),
                    _ => protector.Unprotect(SecretScope.ForTenant(tenantId), column.Kind, cipher),
                }).Reveal();
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private static async Task<List<(Guid Id, string Cipher)>> RowsAsync(NpgsqlConnection db, SecretColumn column, Guid tenantId)
        {
            // Identificadores vêm da lista fixa (SecretColumns), nunca de entrada do usuário.
            var sql = $"SELECT id, {column.Column} FROM {column.Schema}.{column.Table} WHERE {column.Column} IS NOT NULL AND {column.Column} <> ''"
                + (column.Purpose == SecretPurpose.TenantRow ? " AND id = $1" : string.Empty) + " ORDER BY id FOR UPDATE";
            await using var command = new NpgsqlCommand(sql, db);
            if (column.Purpose == SecretPurpose.TenantRow)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = tenantId });
            }

            var rows = new List<(Guid, string)>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetGuid(0), reader.GetString(1)));
            }

            return rows;
        }

        private static async Task UpdateAsync(NpgsqlConnection db, SecretColumn column, Guid rowId, string cipher)
        {
            await using var command = new NpgsqlCommand($"UPDATE {column.Schema}.{column.Table} SET {column.Column} = $1 WHERE id = $2", db)
            {
                Parameters = { new() { Value = cipher }, new() { Value = rowId } },
            };
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Colunas cifradas do schema real: sufixo <c>_cipher</c> e a conexão do tenant no catálogo.</summary>
        private static async Task<List<string>> CipherColumnsAsync(NpgsqlConnection db)
        {
            const string sql = "SELECT table_schema || '.' || table_name || '.' || column_name FROM information_schema.columns "
                + "WHERE table_schema NOT IN ('pg_catalog', 'information_schema') "
                + "AND (column_name LIKE '%\\_cipher' OR (table_schema = 'catalog' AND table_name = 'tenants' AND column_name = 'connection_secret'))";
            await using var command = new NpgsqlCommand(sql, db);
            var columns = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }

            return columns;
        }

        private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }
    }

    private sealed class ColumnReport(SecretColumn column)
    {
        public SecretColumn Column { get; } = column;

        public int Reencrypted { get; set; }

        public int Replaced { get; set; }

        public int AlreadyNew { get; set; }

        public int Unreadable { get; set; }
    }
}
