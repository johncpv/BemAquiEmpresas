#!/usr/bin/env sh
# Fechamento do Dolibarr na virada (GOL-01a CA 11, GOL-01b CA 3 e 9, ADR 0034 §4, ADR 0017 adendo 2).
# Roda NA VPS, contra o MariaDB do Dolibarr (container), com o root do próprio container (variável de ambiente dele).
#
# Uso:
#   sh scripts/golive/dolibarr-fechamento.sh <acao> --container <nome> --database <banco> [opções]
# Ações:
#   fechar     regenera a api_key (DOLAPIKEY) dos usuários da API e os desativa; tira a escrita dos perfis (direitos de usuário e de
#              grupo que não são de leitura) e o admin de todos os usuários, guardando o que saiu nas tabelas bqe_golive_*; faz o
#              DROP USER IF EXISTS do bqe_migracao_ro (em qualquer host). Por padrão é SIMULAÇÃO (só conta e lista).
#   restaurar  rollback do "só leitura": devolve os direitos e o admin guardados. NÃO reativa os usuários da API nem a chave antiga
#              (GOL-01a CA 9: a DOLAPIKEY antiga não volta). Por padrão é SIMULAÇÃO.
#   conferir   só leitura: usuários com api_key e situação, direitos de escrita restantes, admins, bqe_migracao_ro.
# Opções:
#   --container <nome>          container do MariaDB do Dolibarr (ex.: dolibarr-db)
#   --database <banco>          banco do Dolibarr
#   --prefix <prefixo>          prefixo das tabelas (padrão llx_)
#   --api-logins <l1,l2>        logins dos usuários da API (o do Konebras UI e o da importação). Obrigatório para aplicar o "fechar";
#                               a simulação lista quem tem api_key para você escolher.
#   --client <binário>          cliente dentro do container (padrão mariadb; em imagens antigas, mysql)
#   --root-password-env <var>   variável do container com a senha de root (padrão MARIADB_ROOT_PASSWORD)
#   --aplicar --confirmar <banco>  grava de verdade; --confirmar precisa repetir o --database
#
# Nada de senha em argumento: a senha de root é lida dentro do container. A chave nova não é mostrada nem guardada (ninguém a conhece:
# a API do Dolibarr deixa de funcionar com qualquer chave para esses usuários). A rede e o vhost são manuais: o script lembra no fim.
set -eu

die() { echo "$*" >&2; exit 1; }

ACTION=${1:-}
[ -n "$ACTION" ] || { sed -n '4,27p' "$0"; exit 1; }
shift

CONTAINER=""
DATABASE=""
PREFIX="llx_"
API_LOGINS=""
CLIENT="mariadb"
PASSWORD_ENV="MARIADB_ROOT_PASSWORD"
APPLY=0
CONFIRM=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    --container) CONTAINER=${2:-}; shift 2 ;;
    --database) DATABASE=${2:-}; shift 2 ;;
    --prefix) PREFIX=${2:-}; shift 2 ;;
    --api-logins) API_LOGINS=${2:-}; shift 2 ;;
    --client) CLIENT=${2:-}; shift 2 ;;
    --root-password-env) PASSWORD_ENV=${2:-}; shift 2 ;;
    --aplicar) APPLY=1; shift ;;
    --confirmar) CONFIRM=${2:-}; shift 2 ;;
    *) die "Opção desconhecida: $1" ;;
  esac
done

valid() { printf '%s' "$2" | grep -Eq "$1"; }
[ -n "$CONTAINER" ] && [ -n "$DATABASE" ] || die "Informe --container e --database."
valid '^[A-Za-z0-9_.-]+$' "$CONTAINER" || die "Container inválido."
valid '^[A-Za-z0-9_]+$' "$DATABASE" || die "Banco inválido."
valid '^[A-Za-z0-9_]*$' "$PREFIX" || die "Prefixo inválido."
valid '^[A-Za-z0-9_]+$' "$PASSWORD_ENV" || die "Variável de senha inválida."
valid '^(mariadb|mysql)$' "$CLIENT" || die "Cliente inválido (mariadb ou mysql)."
if [ -n "$API_LOGINS" ]; then
  valid '^[A-Za-z0-9_.@-]+(,[A-Za-z0-9_.@-]+)*$' "$API_LOGINS" || die "--api-logins inválido (logins separados por vírgula)."
fi
if [ "$APPLY" -eq 1 ] && [ "$CONFIRM" != "$DATABASE" ]; then
  die "Para gravar, repita o banco em --confirmar $DATABASE. Nada foi gravado."
fi

# Lista SQL dos logins: 'a','b'
LOGINS_SQL=$(printf '%s' "$API_LOGINS" | sed "s/[^,][^,]*/'&'/g")
U="${PREFIX}user"
UR="${PREFIX}user_rights"
GR="${PREFIX}usergroup_rights"
RD="${PREFIX}rights_def"

# A senha de root é expandida dentro do container ("$MARIADB_ROOT_PASSWORD" chega literal ao sh -c de lá).
sql() {
  docker exec -i "$CONTAINER" sh -c "$CLIENT -uroot -p\"\$$PASSWORD_ENV\" --default-character-set=utf8mb4 --table \"$DATABASE\""
}

sql_raw() {
  docker exec -i "$CONTAINER" sh -c "$CLIENT -uroot -p\"\$$PASSWORD_ENV\" --default-character-set=utf8mb4 --batch --skip-column-names \"$DATABASE\""
}

conferir_sql() {
  cat <<SQL
SELECT 'Usuários com api_key' AS item, rowid, login, statut AS ativo, admin FROM $U WHERE api_key IS NOT NULL AND api_key <> '' ORDER BY login;
SELECT 'Direitos de escrita por usuário' AS item, count(*) AS quantidade FROM $UR ur JOIN $RD rd ON rd.id = ur.fk_id WHERE rd.type <> 'r';
SELECT 'Direitos de escrita por grupo' AS item, count(*) AS quantidade FROM $GR gr JOIN $RD rd ON rd.id = gr.fk_id WHERE rd.type <> 'r';
SELECT 'Tipos de direito (r = leitura)' AS item, type, count(*) AS quantidade FROM $RD GROUP BY type ORDER BY type;
SELECT 'Admins ativos' AS item, rowid, login FROM $U WHERE admin = 1 AND statut = 1 ORDER BY login;
SELECT 'bqe_migracao_ro' AS item, user, host FROM mysql.user WHERE user = 'bqe_migracao_ro';
SQL
}

backup_tables_sql() {
  cat <<SQL
CREATE TABLE IF NOT EXISTS bqe_golive_user_rights_bkp LIKE $UR;
CREATE TABLE IF NOT EXISTS bqe_golive_usergroup_rights_bkp LIKE $GR;
CREATE TABLE IF NOT EXISTS bqe_golive_user_bkp (rowid INT PRIMARY KEY, admin SMALLINT NOT NULL, statut SMALLINT NOT NULL, saved_at DATETIME NOT NULL);
SQL
}

fechar_sql() {
  echo "START TRANSACTION;"
  backup_tables_sql
  cat <<SQL
INSERT IGNORE INTO bqe_golive_user_rights_bkp SELECT ur.* FROM $UR ur JOIN $RD rd ON rd.id = ur.fk_id WHERE rd.type <> 'r';
INSERT IGNORE INTO bqe_golive_usergroup_rights_bkp SELECT gr.* FROM $GR gr JOIN $RD rd ON rd.id = gr.fk_id WHERE rd.type <> 'r';
INSERT IGNORE INTO bqe_golive_user_bkp (rowid, admin, statut, saved_at) SELECT rowid, admin, statut, UTC_TIMESTAMP() FROM $U WHERE admin = 1;
DELETE ur FROM $UR ur JOIN $RD rd ON rd.id = ur.fk_id WHERE rd.type <> 'r';
SELECT 'Direitos de escrita removidos (usuários)' AS item, ROW_COUNT() AS quantidade;
DELETE gr FROM $GR gr JOIN $RD rd ON rd.id = gr.fk_id WHERE rd.type <> 'r';
SELECT 'Direitos de escrita removidos (grupos)' AS item, ROW_COUNT() AS quantidade;
UPDATE $U SET admin = 0 WHERE admin = 1;
SELECT 'Usuários sem admin' AS item, ROW_COUNT() AS quantidade;
UPDATE $U SET api_key = SUBSTRING(SHA2(CONCAT(UUID(), RAND(), rowid), 256), 1, 32), statut = 0 WHERE login IN ($LOGINS_SQL);
SELECT 'Usuários da API com chave regenerada e desativados' AS item, ROW_COUNT() AS quantidade;
COMMIT;
SQL
}

restaurar_sql() {
  cat <<SQL
START TRANSACTION;
INSERT IGNORE INTO $UR SELECT * FROM bqe_golive_user_rights_bkp;
SELECT 'Direitos devolvidos (usuários)' AS item, ROW_COUNT() AS quantidade;
INSERT IGNORE INTO $GR SELECT * FROM bqe_golive_usergroup_rights_bkp;
SELECT 'Direitos devolvidos (grupos)' AS item, ROW_COUNT() AS quantidade;
UPDATE $U u JOIN bqe_golive_user_bkp b ON b.rowid = u.rowid SET u.admin = b.admin;
SELECT 'Admins devolvidos' AS item, ROW_COUNT() AS quantidade;
COMMIT;
SQL
}

simular_sql() {
  cat <<SQL
SELECT 'SIMULAÇÃO: nada é gravado' AS aviso;
SELECT 'Direitos de escrita a remover (usuários)' AS item, count(*) AS quantidade FROM $UR ur JOIN $RD rd ON rd.id = ur.fk_id WHERE rd.type <> 'r';
SELECT 'Direitos de escrita a remover (grupos)' AS item, count(*) AS quantidade FROM $GR gr JOIN $RD rd ON rd.id = gr.fk_id WHERE rd.type <> 'r';
SELECT 'Admins a rebaixar' AS item, rowid, login FROM $U WHERE admin = 1 ORDER BY login;
SELECT 'Usuários com api_key (escolha os da API em --api-logins)' AS item, rowid, login, statut AS ativo FROM $U WHERE api_key IS NOT NULL AND api_key <> '' ORDER BY login;
SQL
  if [ -n "$API_LOGINS" ]; then
    echo "SELECT 'Usuários da API a desativar' AS item, rowid, login FROM $U WHERE login IN ($LOGINS_SQL) ORDER BY login;"
  fi
  echo "SELECT 'bqe_migracao_ro a apagar' AS item, user, host FROM mysql.user WHERE user = 'bqe_migracao_ro';"
}

drop_user_sql() {
  # DROP USER não é transacional: roda depois do COMMIT, um por host (o usuário pode nunca ter existido: o caminho padrão foi a API REST).
  cat <<'SQL'
SELECT CONCAT('DROP USER IF EXISTS ''bqe_migracao_ro''@''', host, ''';') AS comando FROM mysql.user WHERE user = 'bqe_migracao_ro';
SQL
}

lembretes() {
  cat <<'TXT'

Lembretes (manuais, fora do banco; registre cada um no runbook):
  1. Vhost adm.konebras.com.br: allowlist de IP (allow <ip>; deny all;) ou auth_basic no location /, nginx -t e reload.
  2. Rede: o MariaDB do Dolibarr sem porta publicada (docker port <container> vazio) e nenhum container do BemAqui na rede dele
     (docker network inspect <rede-do-dolibarr>); regra do firewall da VPS sem 3306 aberto.
  3. A DOLAPIKEY antiga não funciona mais: curl -s -o /dev/null -w '%{http_code}' -H "DOLAPIKEY: <antiga>" https://adm.konebras.com.br/api/index.php/status
     tem de responder 401 ou 403. Depois apague /opt/projetos/bemaqui-empresas*/secrets/dolibarr-api (e o dolibarr-ro.cnf, se existir).
  4. Konebras UI fora do ar (ADR 0005 D1): ele usava a mesma chave.
  5. Escrita recusada: entre com um usuário comum e confira que criar ou editar um terceiro não é permitido.
TXT
}

case "$ACTION" in
  conferir)
    conferir_sql | sql
    ;;
  fechar)
    if [ "$APPLY" -eq 0 ]; then
      simular_sql | sql
      echo "Simulação concluída. Para gravar: --api-logins <logins> --aplicar --confirmar $DATABASE"
      lembretes
      exit 0
    fi
    [ -n "$API_LOGINS" ] || die "Informe --api-logins (rode a simulação para ver quem tem api_key). Nada foi gravado."
    fechar_sql | sql
    DROPS=$(drop_user_sql | sql_raw)
    if [ -n "$DROPS" ]; then
      printf '%s\n' "$DROPS" | sql
      echo "bqe_migracao_ro apagado ($(printf '%s\n' "$DROPS" | wc -l | tr -d ' ') host(s))."
    else
      echo "bqe_migracao_ro não existe (o caminho padrão da importação foi a API REST): nada a apagar."
    fi
    echo "Fechamento aplicado. Conferência:"
    conferir_sql | sql
    lembretes
    ;;
  restaurar)
    if [ "$APPLY" -eq 0 ]; then
      cat <<SQL | sql
SELECT 'SIMULAÇÃO: nada é gravado' AS aviso;
SELECT 'Direitos a devolver (usuários)' AS item, count(*) AS quantidade FROM bqe_golive_user_rights_bkp;
SELECT 'Direitos a devolver (grupos)' AS item, count(*) AS quantidade FROM bqe_golive_usergroup_rights_bkp;
SELECT 'Admins a devolver' AS item, count(*) AS quantidade FROM bqe_golive_user_bkp;
SQL
      echo "Simulação concluída. Para gravar: --aplicar --confirmar $DATABASE (os usuários da API continuam desativados)."
      exit 0
    fi
    restaurar_sql | sql
    echo "Rollback aplicado: escrita e admins devolvidos; a DOLAPIKEY antiga continua inválida. Conferência:"
    conferir_sql | sql
    ;;
  *)
    die "Ação desconhecida: $ACTION (fechar, restaurar ou conferir)."
    ;;
esac
