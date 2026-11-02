-- Acessos da virada (GOL-01a CA 10, GOL-01b CA 4, ADR 0034 §3). Roda no BANCO DO TENANT (ex.: tenant_konebras), com a API parada.
--
-- O que faz, numa transação só:
--   1. apaga os usuários de teste (critério abaixo) e o que é só deles no Identity (grupos, preferências, PINs, sessões, convites,
--      redefinições, códigos de recuperação e desafios de 2FA);
--   2. revoga TODAS as famílias de refresh (o refresh não fica preso ao host: sem isso, uma sessão da homologação valeria em produção);
--   3. sobe o security stamp de todos os usuários (o JWT antigo cai: o sstamp não confere) e a versão das permissões (perm_version, que
--      entra na chave do cache de permissões, ADR 0008 adendo FND-06c);
--   4. expira convites pendentes, redefinições de senha e desafios de 2FA abertos;
--   5. apaga todos os PINs e revoga os aparelhos do chão de fábrica, as telas de TV (quiosque) e as sessões delas.
--   Os super admins ficam no catálogo: liste-os com acessos-virada-catalogo.sql.
--
-- Parâmetros (variáveis de sessão; nada é digitado no script):
--   bqe.confirmar        nome do banco (current_database()). Sem ele, ou com outro valor, é SIMULAÇÃO: faz tudo, mostra as
--                        contagens e desfaz. Com ele, grava.
--   bqe.dominios_teste   domínios de e-mail de teste, separados por vírgula (ex.: teste.com,example.com,localhost).
--   bqe.emails_teste     e-mails de teste avulsos, separados por vírgula (lista do PO).
-- Uso (docs/operacao/go-live-scripts.md):
--   psql "$TENANT_DB" -v ON_ERROR_STOP=1 -c "SET bqe.dominios_teste = 'teste.com'" -f scripts/golive/acessos-virada.sql
--   psql "$TENANT_DB" -v ON_ERROR_STOP=1 -c "SET bqe.dominios_teste = 'teste.com'" -c "SET bqe.confirmar = 'tenant_konebras'" -f ...
--
-- Idempotente: rodar de novo não encontra nada pendente (só gira o stamp de novo, o que não muda nada para quem já caiu).
-- Recusa (erro, nada gravado) se o grupo Proprietário ficar sem nenhum membro ativo.
-- Depois de aplicar, suba a API de novo: o cache do security stamp é em memória (30 s) e some com o processo.

BEGIN;

CREATE TEMP TABLE golive_acessos (ordem int, item text, quantidade bigint, situacao text) ON COMMIT DROP;

DO $golive$
DECLARE
    aplicar boolean := coalesce(current_setting('bqe.confirmar', true), '') = current_database();
    dominios text[] := ARRAY(SELECT trim(d) FROM unnest(string_to_array(lower(coalesce(current_setting('bqe.dominios_teste', true), '')), ',')) d WHERE trim(d) <> '');
    emails text[] := ARRAY(SELECT trim(e) FROM unnest(string_to_array(lower(coalesce(current_setting('bqe.emails_teste', true), '')), ',')) e WHERE trim(e) <> '');
    agora timestamptz := now();
    teste uuid[];
    n_teste bigint := 0;
    n_convites_teste bigint := 0;
    n_familias bigint := 0;
    n_refresh bigint := 0;
    n_stamps bigint := 0;
    n_convites bigint := 0;
    n_resets bigint := 0;
    n_desafios bigint := 0;
    n_pins bigint := 0;
    n_aparelhos bigint := 0;
    n_telas bigint := 0;
    n_sessoes_tv bigint := 0;
    n_donos bigint := 0;
    situacao text;
BEGIN
    BEGIN
        -- 1. Usuários de teste: por domínio do e-mail ou pela lista do PO.
        teste := ARRAY(SELECT id FROM identity.users
                       WHERE split_part(normalized_email, '@', 2) = ANY (dominios) OR normalized_email = ANY (emails));
        n_teste := cardinality(teste);

        SELECT count(DISTINCT ug.user_id) INTO n_donos
        FROM identity.user_groups ug
        JOIN identity.groups g ON g.id = ug.group_id AND g.is_system AND g.seed_key = 'owner'
        JOIN identity.users u ON u.id = ug.user_id AND u.status = 'Active'
        WHERE NOT (ug.user_id = ANY (teste));
        IF n_donos = 0 THEN
            RAISE EXCEPTION 'O grupo Proprietário ficaria sem nenhum membro ativo: revise bqe.dominios_teste e bqe.emails_teste. Nada foi gravado.'
                USING ERRCODE = 'BQOWN';
        END IF;

        DELETE FROM identity.user_invites WHERE user_id = ANY (teste) OR lower(email) = ANY (emails) OR split_part(lower(email), '@', 2) = ANY (dominios);
        GET DIAGNOSTICS n_convites_teste = ROW_COUNT;
        DELETE FROM identity.user_groups WHERE user_id = ANY (teste);
        DELETE FROM identity.user_preferences WHERE user_id = ANY (teste);
        DELETE FROM identity.user_pins WHERE user_id = ANY (teste);
        DELETE FROM identity.refresh_tokens WHERE user_id = ANY (teste);
        DELETE FROM identity.password_resets WHERE user_id = ANY (teste);
        DELETE FROM identity.recovery_codes WHERE user_id = ANY (teste);
        DELETE FROM identity.two_factor_challenges WHERE user_id = ANY (teste);
        DELETE FROM identity.users WHERE id = ANY (teste);

        -- 2. Todas as famílias de refresh.
        SELECT count(DISTINCT family_id) INTO n_familias FROM identity.refresh_tokens WHERE revoked_at IS NULL;
        UPDATE identity.refresh_tokens SET revoked_at = agora WHERE revoked_at IS NULL;
        GET DIAGNOSTICS n_refresh = ROW_COUNT;

        -- 3. Security stamp e versão das permissões de todos os usuários.
        UPDATE identity.users SET security_stamp = replace(gen_random_uuid()::text, '-', ''), perm_version = perm_version + 1;
        GET DIAGNOSTICS n_stamps = ROW_COUNT;

        -- 4. Convites, redefinições de senha e desafios de 2FA.
        UPDATE identity.user_invites SET canceled_at = agora, expires_at = least(expires_at, agora)
        WHERE accepted_at IS NULL AND canceled_at IS NULL;
        GET DIAGNOSTICS n_convites = ROW_COUNT;
        UPDATE identity.password_resets SET expires_at = agora WHERE used_at IS NULL AND expires_at > agora;
        GET DIAGNOSTICS n_resets = ROW_COUNT;
        UPDATE identity.two_factor_challenges SET invalidated_at = agora
        WHERE completed_at IS NULL AND invalidated_at IS NULL AND expires_at > agora;
        GET DIAGNOSTICS n_desafios = ROW_COUNT;

        -- 5. PINs, aparelhos do chão de fábrica e telas de TV.
        DELETE FROM identity.user_pins;
        GET DIAGNOSTICS n_pins = ROW_COUNT;
        UPDATE identity.shopfloor_devices SET revoked_at = agora, revoked_reason = 'go_live' WHERE revoked_at IS NULL;
        GET DIAGNOSTICS n_aparelhos = ROW_COUNT;
        UPDATE identity.kiosk_sessions SET revoked_at = agora WHERE revoked_at IS NULL;
        GET DIAGNOSTICS n_sessoes_tv = ROW_COUNT;
        UPDATE identity.kiosk_tokens SET revoked_at = agora WHERE revoked_at IS NULL;
        GET DIAGNOSTICS n_telas = ROW_COUNT;

        IF NOT aplicar THEN
            RAISE EXCEPTION 'simulação' USING ERRCODE = 'BQSIM';
        END IF;
    EXCEPTION WHEN SQLSTATE 'BQSIM' THEN
        NULL; -- simulação: tudo acima foi desfeito, as contagens ficam
    END;

    situacao := CASE WHEN aplicar THEN 'aplicado' ELSE 'simulação (nada gravado; para gravar, SET bqe.confirmar = ''' || current_database() || ''')' END;
    INSERT INTO golive_acessos VALUES
        (1, 'Usuários de teste apagados', n_teste, situacao),
        (2, 'Convites de teste apagados', n_convites_teste, situacao),
        (3, 'Famílias de refresh revogadas', n_familias, situacao),
        (4, 'Refresh tokens revogados', n_refresh, situacao),
        (5, 'Usuários com security stamp e versão de permissões novos', n_stamps, situacao),
        (6, 'Convites pendentes expirados', n_convites, situacao),
        (7, 'Redefinições de senha expiradas', n_resets, situacao),
        (8, 'Desafios de 2FA invalidados', n_desafios, situacao),
        (9, 'PINs apagados', n_pins, situacao),
        (10, 'Aparelhos do chão de fábrica revogados', n_aparelhos, situacao),
        (11, 'Sessões de TV revogadas', n_sessoes_tv, situacao),
        (12, 'Telas de TV revogadas', n_telas, situacao),
        (13, 'Membros ativos que ficam no Proprietário', n_donos, situacao);
END
$golive$;

SELECT item, quantidade, situacao FROM golive_acessos ORDER BY ordem;

COMMIT;
