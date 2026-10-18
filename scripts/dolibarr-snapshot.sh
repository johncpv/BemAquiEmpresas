#!/usr/bin/env sh
# Snapshot dos cadastros do Dolibarr (MIG-01a CA 3, ADR 0017). Roda na VPS, com o usuário somente leitura.
#
# Uso: sh scripts/dolibarr-snapshot.sh <container-do-mariadb-do-dolibarr> <banco> <arquivo-cnf-do-usuario-ro> <destinatario-age> <saida.sql.age>
#
# - O arquivo .cnf é o secret do usuário bqe_migracao_ro ([client] user=... password=...), com permissão 0600. A senha nunca
#   vai em argumento nem em variável de ambiente (CA 5): o arquivo é copiado para o /tmp do container e apagado no fim.
# - O SELECT de cada tabela tem colunas explícitas (scripts/dolibarr/snapshot.sql, gerado da lista fixa) e roda dentro de
#   START TRANSACTION WITH CONSISTENT SNAPSHOT, READ ONLY. A primeira linha grava a data e hora da extração.
# - A saída vai direto para o age: nada fica em claro no disco.
set -eu
umask 077

if [ "$#" -ne 5 ]; then
  sed -n '4p' "$0"
  exit 1
fi

CONTAINER=$1
DATABASE=$2
CNF=$3
RECIPIENT=$4
OUT=$5
DIR=$(cd "$(dirname "$0")" && pwd)
REMOTE_CNF=/tmp/bqe-migracao-ro.$$.cnf

[ -r "$CNF" ] || { echo "Arquivo de secret não encontrado: $CNF" >&2; exit 1; }
[ -e "$OUT" ] && { echo "Saída já existe: $OUT (não sobrescrevo)" >&2; exit 1; }
command -v age >/dev/null || { echo "age não instalado" >&2; exit 1; }

cleanup() { docker exec "$CONTAINER" rm -f "$REMOTE_CNF" >/dev/null 2>&1 || true; }
trap cleanup EXIT INT TERM

docker cp "$CNF" "$CONTAINER:$REMOTE_CNF"
docker exec "$CONTAINER" chmod 600 "$REMOTE_CNF"

docker exec -i "$CONTAINER" mariadb --defaults-extra-file="$REMOTE_CNF" --default-character-set=utf8mb4 \
    --batch --raw --skip-column-names "$DATABASE" < "$DIR/dolibarr/snapshot.sql" \
  | age -r "$RECIPIENT" -o "$OUT"

echo "Snapshot cifrado em $OUT ($(wc -c < "$OUT") bytes). Restaure com scripts/dolibarr/restore-snapshot.sh."
