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

    public const string Uninstall = """
        DROP TRIGGER IF EXISTS tenant_domains_changed ON catalog.tenant_domains;
        DROP TRIGGER IF EXISTS tenants_changed ON catalog.tenants;
        DROP FUNCTION IF EXISTS catalog.notify_tenant_changed();
        """;
}
