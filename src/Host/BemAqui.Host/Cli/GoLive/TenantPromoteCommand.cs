using BemAqui.Platform.Domain;
using Npgsql;

namespace BemAqui.Host.Cli.GoLive;

/// <summary>
/// <c>tenant promote</c> (GOL-01a CA 3 e 6, ADR 0034 §2): copia um tenant do catálogo antigo (homologação) para o catálogo configurado (o
/// novo, da produção), com o MESMO id, porque o id entra no propósito de todos os segredos do tenant. Leva a linha do tenant, o plano (com
/// os módulos do plano, se o novo catálogo ainda não tem um de mesmo nome), os módulos liberados e os hosts informados; não leva nenhum
/// outro tenant, nem os hosts da homologação, nem a chave dos webhooks (a produção cadastra os provedores de novo). Cobrança real e
/// ambiente fiscal chegam desligados (ligados só na GOL-01b). A conexão vem como estava (cifrada com o key ring antigo): o
/// <c>reencrypt-secrets</c> a recifra, ou troca pela conexão nova com <c>--tenant-connection-file</c>. Idempotente: rodar de novo não
/// muda o que já está no catálogo novo.
/// </summary>
internal static class TenantPromoteCommand
{
    public const string Usage =
        "Uso: tenant promote --tenant <chave> --from-catalog-file <arquivo-com-a-conexão-do-catálogo-antigo> --primary-host <host>\n"
        + "       [--extra-hosts <h1,h2>] (--dry-run | --confirm-tenant <chave>)";

    private sealed record OldTenant(Guid Id, string Key, string LegalName, string? TradeName, string? Document, string Status, Guid? PlanId,
        string DbName, string ConnectionSecret, DateTimeOffset CreatedAt, string? SchemaVersion, DateTimeOffset? MigratedAt);

    private sealed record OldPlan(Guid Id, string Name, string Limits, decimal Price, List<string> Modules);

    private sealed record OldModule(Guid Id, string Code, bool Enabled, bool IsException, DateTimeOffset? ReleasedAt, DateTimeOffset? UpdatedAt);

    public static async Task<int> RunAsync(IServiceProvider services, CliArguments args, TextWriter output)
    {
        var key = args.Get("tenant");
        var dryRun = args.Has("dry-run");
        var primary = args.Get("primary-host") is { Length: > 0 } p ? TenantDomain.Normalize(p) : null;
        if (string.IsNullOrWhiteSpace(key) || args.Get("from-catalog-file") is not { } file || primary is null)
        {
            await output.WriteLineAsync("Informe --tenant, --from-catalog-file e --primary-host. Nada foi gravado.\n" + Usage);
            return 1;
        }

        if (!dryRun && args.Get("confirm-tenant") != key)
        {
            await output.WriteLineAsync("Para gravar, repita a chave em --confirm-tenant " + key + " (ou rode antes com --dry-run). Nada foi gravado.");
            return 1;
        }

        if (!File.Exists(file))
        {
            await output.WriteLineAsync("Arquivo da conexão do catálogo antigo não encontrado: " + file + ". Nada foi gravado.");
            return 1;
        }

        var extra = (args.Get("extra-hosts") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TenantDomain.Normalize).Where(h => h != primary).Distinct(StringComparer.Ordinal).ToList();
        var newCatalog = services.GetRequiredService<IConfiguration>().GetConnectionString("Catalog")!;
        var oldCatalog = (await File.ReadAllTextAsync(file)).Trim();
        if (string.Equals(new NpgsqlConnectionStringBuilder(oldCatalog).Database, new NpgsqlConnectionStringBuilder(newCatalog).Database, StringComparison.Ordinal)
            && string.Equals(new NpgsqlConnectionStringBuilder(oldCatalog).Host, new NpgsqlConnectionStringBuilder(newCatalog).Host, StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync("O catálogo antigo e o configurado são o mesmo banco: rode com o ConnectionStrings__Catalog do ambiente novo. Nada foi gravado.");
            return 1;
        }

        await using var old = await OpenAsync(oldCatalog);
        var tenant = await ReadTenantAsync(old, key);
        if (tenant is null || tenant.Status != nameof(TenantLifecycle.Active))
        {
            await output.WriteLineAsync((tenant is null ? "Tenant não encontrado no catálogo antigo: " : "O tenant não está ativo no catálogo antigo: ") + key + ". Nada foi gravado.");
            return 1;
        }

        var plan = tenant.PlanId is { } planId ? await ReadPlanAsync(old, planId) : null;
        var modules = await ReadModulesAsync(old, tenant.Id);
        var oldHosts = (await ReadHostsAsync(old, tenant.Id)).Where(h => h != primary && !extra.Contains(h)).ToList();

        await using var db = await OpenAsync(newCatalog);
        await using var transaction = await db.BeginTransactionAsync();
        var conflict = await ConflictAsync(db, tenant, [primary, .. extra]);
        if (conflict is not null)
        {
            await output.WriteLineAsync(conflict + " Nada foi gravado.");
            return 1;
        }

        (Guid Id, bool Created)? planTarget = plan is null ? null : await EnsurePlanAsync(db, plan);
        var tenantInserted = await InsertTenantAsync(db, tenant, planTarget);
        var modulesInserted = 0;
        foreach (var m in modules)
        {
            modulesInserted += await ExecuteAsync(db,
                "INSERT INTO catalog.tenant_modules (id, tenant_id, module_code, enabled, is_exception, released_at, updated_at) VALUES ($1, $2, $3, $4, $5, $6, $7) ON CONFLICT DO NOTHING",
                m.Id, tenant.Id, m.Code, m.Enabled, m.IsException, (object?)m.ReleasedAt ?? DBNull.Value, (object?)m.UpdatedAt ?? DBNull.Value);
        }

        var hostsInserted = await EnsureHostsAsync(db, tenant.Id, primary, extra);
        await output.WriteLineAsync("Promoção do tenant " + key + " para o catálogo novo" + (dryRun ? " (simulação: nada é gravado)." : "."));
        await output.WriteLineAsync("  Tenant (mesmo id " + tenant.Id + "): " + (tenantInserted ? "criado, cobrança real e ambiente fiscal desligados" : "já existia, mantido como está"));
        await output.WriteLineAsync("  Plano: " + (plan is null ? "sem plano" : plan.Name + (planTarget!.Value.Created ? " (criado, " + plan.Modules.Count + " módulo(s))" : " (já existia)")));
        await output.WriteLineAsync($"  Módulos liberados: {modulesInserted} copiado(s), {modules.Count - modulesInserted} já existiam");
        await output.WriteLineAsync($"  Hosts: principal {primary}; {hostsInserted} host(s) novo(s). Hosts do catálogo antigo não copiados: {(oldHosts.Count == 0 ? "nenhum" : string.Join(", ", oldHosts))}");
        if (dryRun)
        {
            await transaction.RollbackAsync();
            await output.WriteLineAsync("Simulação concluída: nada foi gravado. Para gravar, rode sem --dry-run e com --confirm-tenant " + key + ".");
            return 0;
        }

        await transaction.CommitAsync();
        await output.WriteLineAsync("Promoção concluída. Próximo passo: reencrypt-secrets --tenant " + key + " (a conexão ainda está cifrada com o key ring antigo).");
        return 0;
    }

    private static async Task<string?> ConflictAsync(NpgsqlConnection db, OldTenant tenant, List<string> hosts)
    {
        await using (var byKey = new NpgsqlCommand("SELECT id FROM catalog.tenants WHERE key = $1 OR db_name = $2", db) { Parameters = { new() { Value = tenant.Key }, new() { Value = tenant.DbName } } })
        {
            if (await byKey.ExecuteScalarAsync() is Guid existing && existing != tenant.Id)
            {
                return "O catálogo novo já tem outro tenant com a chave " + tenant.Key + " ou o banco " + tenant.DbName + ", com outro id.";
            }
        }

        await using var byHost = new NpgsqlCommand("SELECT host FROM catalog.tenant_domains WHERE host = ANY ($1) AND tenant_id <> $2 LIMIT 1", db)
        {
            Parameters = { new() { Value = hosts.ToArray() }, new() { Value = tenant.Id } },
        };
        return await byHost.ExecuteScalarAsync() is string host ? "O host " + host + " já é de outro tenant no catálogo novo." : null;
    }

    private static async Task<(Guid Id, bool Created)> EnsurePlanAsync(NpgsqlConnection db, OldPlan plan)
    {
        await using (var find = new NpgsqlCommand("SELECT id FROM catalog.plans WHERE id = $1 OR name = $2 ORDER BY (id = $1) DESC LIMIT 1", db)
        {
            Parameters = { new() { Value = plan.Id }, new() { Value = plan.Name } },
        })
        {
            if (await find.ExecuteScalarAsync() is Guid existing)
            {
                return (existing, false);
            }
        }

        await ExecuteAsync(db, "INSERT INTO catalog.plans (id, name, limits, price) VALUES ($1, $2, $3::jsonb, $4)", plan.Id, plan.Name, plan.Limits, plan.Price);
        foreach (var module in plan.Modules)
        {
            await ExecuteAsync(db, "INSERT INTO catalog.plan_modules (id, plan_id, module_code) VALUES ($1, $2, $3) ON CONFLICT DO NOTHING", Guid.CreateVersion7(), plan.Id, module);
        }

        return (plan.Id, true);
    }

    private static async Task<bool> InsertTenantAsync(NpgsqlConnection db, OldTenant t, (Guid Id, bool Created)? plan) =>
        await ExecuteAsync(db,
            "INSERT INTO catalog.tenants (id, key, legal_name, trade_name, document, status, plan_id, db_name, connection_secret, created_at, schema_version, migrated_at, "
            + "bank_production_allowed, fiscal_environment_allowed, webhook_key) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, false, 'None', NULL) ON CONFLICT (id) DO NOTHING",
            t.Id, t.Key, t.LegalName, (object?)t.TradeName ?? DBNull.Value, (object?)t.Document ?? DBNull.Value, t.Status, (object?)plan?.Id ?? DBNull.Value, t.DbName,
            t.ConnectionSecret, t.CreatedAt, (object?)t.SchemaVersion ?? DBNull.Value, (object?)t.MigratedAt ?? DBNull.Value) == 1;

    /// <summary>O host principal do parâmetro (os outros do tenant deixam de ser principais) e os extras; nenhum host da homologação.</summary>
    private static async Task<int> EnsureHostsAsync(NpgsqlConnection db, Guid tenantId, string primary, List<string> extra)
    {
        var inserted = 0;
        foreach (var (host, isPrimary) in extra.Select(h => (h, false)).Prepend((primary, true)))
        {
            inserted += await ExecuteAsync(db, "INSERT INTO catalog.tenant_domains (id, tenant_id, host, is_primary) VALUES ($1, $2, $3, $4) ON CONFLICT (host) DO NOTHING",
                Guid.CreateVersion7(), tenantId, host, isPrimary);
        }

        await ExecuteAsync(db, "UPDATE catalog.tenant_domains SET is_primary = (host = $2) WHERE tenant_id = $1 AND is_primary <> (host = $2)", tenantId, primary);
        return inserted;
    }

    private static async Task<OldTenant?> ReadTenantAsync(NpgsqlConnection db, string key)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id, key, legal_name, trade_name, document, status, plan_id, db_name, connection_secret, created_at, schema_version, migrated_at FROM catalog.tenants WHERE key = $1", db)
        {
            Parameters = { new() { Value = key } },
        };
        await using var r = await command.ExecuteReaderAsync();
        return await r.ReadAsync()
            ? new OldTenant(r.GetGuid(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5),
                r.IsDBNull(6) ? null : r.GetGuid(6), r.GetString(7), r.GetString(8), r.GetFieldValue<DateTimeOffset>(9), r.IsDBNull(10) ? null : r.GetString(10),
                r.IsDBNull(11) ? null : r.GetFieldValue<DateTimeOffset>(11))
            : null;
    }

    private static async Task<OldPlan?> ReadPlanAsync(NpgsqlConnection db, Guid planId)
    {
        OldPlan? plan = null;
        await using (var command = new NpgsqlCommand("SELECT id, name, limits::text, price FROM catalog.plans WHERE id = $1", db) { Parameters = { new() { Value = planId } } })
        await using (var r = await command.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                plan = new OldPlan(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetDecimal(3), []);
            }
        }

        if (plan is not null)
        {
            await using var modules = new NpgsqlCommand("SELECT module_code FROM catalog.plan_modules WHERE plan_id = $1 ORDER BY module_code", db) { Parameters = { new() { Value = planId } } };
            await using var r = await modules.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                plan.Modules.Add(r.GetString(0));
            }
        }

        return plan;
    }

    private static async Task<List<OldModule>> ReadModulesAsync(NpgsqlConnection db, Guid tenantId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id, module_code, enabled, is_exception, released_at, updated_at FROM catalog.tenant_modules WHERE tenant_id = $1 ORDER BY module_code", db)
        {
            Parameters = { new() { Value = tenantId } },
        };
        var list = new List<OldModule>();
        await using var r = await command.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new OldModule(r.GetGuid(0), r.GetString(1), r.GetBoolean(2), r.GetBoolean(3),
                r.IsDBNull(4) ? null : r.GetFieldValue<DateTimeOffset>(4), r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5)));
        }

        return list;
    }

    private static async Task<List<string>> ReadHostsAsync(NpgsqlConnection db, Guid tenantId)
    {
        await using var command = new NpgsqlCommand("SELECT host FROM catalog.tenant_domains WHERE tenant_id = $1 ORDER BY host", db) { Parameters = { new() { Value = tenantId } } };
        var list = new List<string>();
        await using var r = await command.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(r.GetString(0));
        }

        return list;
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection db, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, db);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
