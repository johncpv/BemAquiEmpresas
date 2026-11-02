-- Acessos da virada no CATÁLOGO (GOL-01a CA 10, ADR 0034 §3): complemento do acessos-virada.sql (que roda no banco do tenant).
--
-- Numa transação só:
--   1. revoga as sessões abertas do painel da plataforma e invalida os links e desafios pendentes dos super admins;
--   2. lista os super admins (e-mail, situação, 2FA e último acesso) para o PO conferir: quem não deve estar lá é desativado à parte;
--   3. lista os tenants e os hosts do catálogo (a produção nasce só com o konebras, ADR 0034 §2).
--
-- Parâmetro: bqe.confirmar = nome do banco do catálogo (current_database()). Sem ele é SIMULAÇÃO: conta, lista e desfaz.
-- Uso: psql "$CATALOG_DB" -v ON_ERROR_STOP=1 [-c "SET bqe.confirmar = 'catalog'"] -f scripts/golive/acessos-virada-catalogo.sql
-- Saída com e-mail dos super admins: rode no terminal da VPS, não cole a saída em chat nem em ticket.

BEGIN;

CREATE TEMP TABLE golive_catalogo (ordem int, item text, quantidade bigint, situacao text) ON COMMIT DROP;

DO $golive$
DECLARE
    aplicar boolean := coalesce(current_setting('bqe.confirmar', true), '') = current_database();
    agora timestamptz := now();
    n_sessoes bigint := 0;
    n_links bigint := 0;
    situacao text;
BEGIN
    BEGIN
        UPDATE catalog.platform_sessions SET revoked_at = agora WHERE revoked_at IS NULL;
        GET DIAGNOSTICS n_sessoes = ROW_COUNT;
        UPDATE catalog.platform_admin_tokens SET invalidated_at = agora
        WHERE used_at IS NULL AND invalidated_at IS NULL AND expires_at > agora;
        GET DIAGNOSTICS n_links = ROW_COUNT;

        IF NOT aplicar THEN
            RAISE EXCEPTION 'simulação' USING ERRCODE = 'BQSIM';
        END IF;
    EXCEPTION WHEN SQLSTATE 'BQSIM' THEN
        NULL;
    END;

    situacao := CASE WHEN aplicar THEN 'aplicado' ELSE 'simulação (nada gravado; para gravar, SET bqe.confirmar = ''' || current_database() || ''')' END;
    INSERT INTO golive_catalogo VALUES
        (1, 'Sessões do painel da plataforma revogadas', n_sessoes, situacao),
        (2, 'Links e desafios pendentes de super admin invalidados', n_links, situacao);
END
$golive$;

SELECT item, quantidade, situacao FROM golive_catalogo ORDER BY ordem;

-- Super admins para a conferência do PO.
SELECT email, status, totp_enabled AS dois_fatores,
       to_char(last_login_at AT TIME ZONE 'America/Sao_Paulo', 'DD/MM/YYYY HH24:MI') AS ultimo_acesso
FROM catalog.platform_admins
ORDER BY email;

-- Tenants e hosts do catálogo.
SELECT t.key AS tenant, t.status, d.host, d.is_primary AS principal
FROM catalog.tenants t
LEFT JOIN catalog.tenant_domains d ON d.tenant_id = t.id
ORDER BY t.key, d.is_primary DESC, d.host;

COMMIT;
