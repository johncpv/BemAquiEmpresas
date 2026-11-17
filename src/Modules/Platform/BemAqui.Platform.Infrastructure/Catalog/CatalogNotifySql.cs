namespace BemAqui.Platform.Infrastructure.Catalog;

/// <summary>Trigger que avisa (pg_notify) toda mudança em tenants e domínios: o cache invalida em segundos (FND-02 CA 4).</summary>
public static class CatalogNotifySql
{
    public const string Install = """
        CREATE OR REPLACE FUNCTION catalog.notify_tenant_changed() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            PERFORM pg_notify('tenant_changed', TG_TABLE_NAME);
            RETURN NULL;
        END;
        $$;

        DROP TRIGGER IF EXISTS tenants_changed ON catalog.tenants;
        CREATE TRIGGER tenants_changed AFTER INSERT OR UPDATE OR DELETE ON catalog.tenants
            FOR EACH STATEMENT EXECUTE FUNCTION catalog.notify_tenant_changed();

        DROP TRIGGER IF EXISTS tenant_domains_changed ON catalog.tenant_domains;
        CREATE TRIGGER tenant_domains_changed AFTER INSERT OR UPDATE OR DELETE ON catalog.tenant_domains
            FOR EACH STATEMENT EXECUTE FUNCTION catalog.notify_tenant_changed();
        """;

    /// <summary>Liberação de módulos (FND-06): mudar <c>tenant_modules</c> ou <c>plan_modules</c> também invalida o cache.</summary>
    public const string InstallModules = """
        DROP TRIGGER IF EXISTS tenant_modules_changed ON catalog.tenant_modules;
        CREATE TRIGGER tenant_modules_changed AFTER INSERT OR UPDATE OR DELETE ON catalog.tenant_modules
            FOR EACH STATEMENT EXECUTE FUNCTION catalog.notify_tenant_changed();

        DROP TRIGGER IF EXISTS plan_modules_changed ON catalog.plan_modules;
        CREATE TRIGGER plan_modules_changed AFTER INSERT OR UPDATE OR DELETE ON catalog.plan_modules
            FOR EACH STATEMENT EXECUTE FUNCTION catalog.notify_tenant_changed();
        """;

    /// <summary>Backfill da FND-06: todo plano existente recebe todos os módulos; todo tenant herda do seu plano.</summary>
    public static string BackfillModules =>
        "INSERT INTO catalog.plan_modules (id, plan_id, module_code) SELECT gen_random_uuid(), p.id, m.code FROM catalog.plans p CROSS JOIN (VALUES "
        + string.Join(", ", BemAqui.BuildingBlocks.Modules.ModuleCatalog.All.Select(m => "('" + m.Code + "')"))
        + ") AS m(code) ON CONFLICT DO NOTHING; "
        + "INSERT INTO catalog.tenant_modules (id, tenant_id, module_code, enabled, is_exception, released_at, updated_at) "
        + "SELECT gen_random_uuid(), t.id, pm.module_code, true, false, now(), now() FROM catalog.tenants t JOIN catalog.plan_modules pm ON pm.plan_id = t.plan_id ON CONFLICT DO NOTHING;";


    /// <summary>Perfis de negócio (PERF-01, ADR 0046): aplicar ou remover perfil também invalida o cache do tenant (oculto muda).</summary>
    public const string InstallProfiles = """
        DROP TRIGGER IF EXISTS tenant_profiles_changed ON catalog.tenant_profiles;
        CREATE TRIGGER tenant_profiles_changed AFTER INSERT OR UPDATE OR DELETE ON catalog.tenant_profiles
            FOR EACH STATEMENT EXECUTE FUNCTION catalog.notify_tenant_changed();
        """;

    public const string UninstallProfiles = "DROP TRIGGER IF EXISTS tenant_profiles_changed ON catalog.tenant_profiles;";

    /// <summary>
    /// Backfill da PERF-01 (CA 4): todo tenant sem perfil ativo recebe <c>factory</c>, com <c>applied_by</c> nulo (o sistema) e a
    /// auditoria do sistema. Não toca <c>tenant_modules</c>: o perfil Fábrica lista todos os módulos de hoje, então a liberação
    /// herdada não muda (regressão da Konebras, CA 11). Idempotente.
    /// </summary>
    public static string BackfillFactoryProfile => BemAqui.BuildingBlocks.Persistence.Auditing.AuditLogSql.InsertFrom("""
        SELECT gen_random_uuid(), 'TenantProfile', t.id::text || ':factory', 'ProfileApplied', NULL, now(), 'Sistema · migration PERF-01 (ADR 0046)', '{"profile":"factory","segment":null}'::jsonb
        FROM catalog.tenants t
        WHERE NOT EXISTS (SELECT 1 FROM catalog.tenant_profiles p WHERE p.tenant_id = t.id AND p.removed_at IS NULL);
        """) + "\n" + """
        INSERT INTO catalog.tenant_profiles (id, tenant_id, profile_code, segment, applied_at, applied_by, removed_at, removed_by)
        SELECT gen_random_uuid(), t.id, 'factory', NULL, now(), NULL, NULL, NULL
        FROM catalog.tenants t
        WHERE NOT EXISTS (SELECT 1 FROM catalog.tenant_profiles p WHERE p.tenant_id = t.id AND p.removed_at IS NULL);
        """;

    public const string UninstallModules = """
        DROP TRIGGER IF EXISTS plan_modules_changed ON catalog.plan_modules;
        DROP TRIGGER IF EXISTS tenant_modules_changed ON catalog.tenant_modules;
        """;

    public const string Uninstall = """
        DROP TRIGGER IF EXISTS tenant_domains_changed ON catalog.tenant_domains;
        DROP TRIGGER IF EXISTS tenants_changed ON catalog.tenants;
        DROP FUNCTION IF EXISTS catalog.notify_tenant_changed();
        """;
}
