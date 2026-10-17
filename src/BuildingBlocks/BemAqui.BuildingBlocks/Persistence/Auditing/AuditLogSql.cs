namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary>
/// Torna <c>audit.audit_log</c> imutável no próprio banco (FND-07 CA 7): trigger que recusa UPDATE, DELETE e
/// TRUNCATE para qualquer papel da aplicação, e REVOKE dessas permissões de PUBLIC. Aplicado pela migration.
/// </summary>
public static class AuditLogSql
{
    public const string Protect = """
        CREATE OR REPLACE FUNCTION audit.reject_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'audit.audit_log é somente inserção (% recusado)', TG_OP USING ERRCODE = 'insufficient_privilege';
        END;
        $$;

        DROP TRIGGER IF EXISTS audit_log_no_update_delete ON audit.audit_log;
        CREATE TRIGGER audit_log_no_update_delete BEFORE UPDATE OR DELETE ON audit.audit_log
            FOR EACH ROW EXECUTE FUNCTION audit.reject_audit_mutation();

        DROP TRIGGER IF EXISTS audit_log_no_truncate ON audit.audit_log;
        CREATE TRIGGER audit_log_no_truncate BEFORE TRUNCATE ON audit.audit_log
            FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_audit_mutation();

        REVOKE UPDATE, DELETE, TRUNCATE ON audit.audit_log FROM PUBLIC;
        """;

    public const string Unprotect = """
        DROP TRIGGER IF EXISTS audit_log_no_truncate ON audit.audit_log;
        DROP TRIGGER IF EXISTS audit_log_no_update_delete ON audit.audit_log;
        DROP FUNCTION IF EXISTS audit.reject_audit_mutation();
        """;
}
