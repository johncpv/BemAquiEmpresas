#!/usr/bin/env sh
# Restaura o snapshot cifrado num MariaDB temporário (MIG-01a CA 4, ADR 0017), na rede interna do projeto, com o DDL fixo.
# Nenhum container do BemAqui entra na rede do Dolibarr: o importador lê só deste container temporário.
#
# Uso: sh scripts/dolibarr/restore-snapshot.sh <ambiente> <rede-do-projeto> <chave-age> <snapshot.sql.age> <arquivo-senha-root>
#      sh scripts/dolibarr/restore-snapshot.sh --remove <ambiente>
#
# - A senha do root do temporário vem de um arquivo de secret (0600), montado no container como MARIADB_ROOT_PASSWORD_FILE.
# - O volume é anônimo e o container é removido com --remove (com o volume). Confira com: docker ps -a | grep dolibarr-snapshot
set -eu
umask 077

if [ "${1:-}" = "--remove" ]; then
  NAME="bqe-${2:?ambiente}-dolibarr-snapshot"
  docker rm -f -v "$NAME" >/dev/null 2>&1 || true
  docker ps -a --format '{{.Names}}' | grep -qx "$NAME" && { echo "Container $NAME ainda existe" >&2; exit 1; }
  echo "Snapshot removido: container $NAME e volume apagados."
  exit 0
fi

if [ "$#" -ne 5 ]; then
  sed -n '5,6p' "$0"
  exit 1
fi

NAME="bqe-$1-dolibarr-snapshot"
NETWORK=$2
KEY=$3
PACKAGE=$4
ROOT_SECRET=$5
DIR=$(cd "$(dirname "$0")" && pwd)

[ -r "$PACKAGE" ] || { echo "Pacote não encontrado: $PACKAGE" >&2; exit 1; }
[ -r "$ROOT_SECRET" ] || { echo "Arquivo de senha não encontrado: $ROOT_SECRET" >&2; exit 1; }

docker run -d --name "$NAME" --network "$NETWORK" \
  -v "$ROOT_SECRET:/run/secrets/root:ro" -e MARIADB_ROOT_PASSWORD_FILE=/run/secrets/root \
  -e MARIADB_DATABASE=snapshot mariadb:10.11 >/dev/null

echo "Aguardando o MariaDB temporário..."
i=0
until docker exec "$NAME" healthcheck.sh --connect --innodb_initialized >/dev/null 2>&1; do
  i=$((i + 1)); [ "$i" -gt 60 ] && { echo "MariaDB temporário não subiu" >&2; exit 1; }
  sleep 2
done

# Senha lida dentro do container, do arquivo montado: não passa por argumento do host.
docker exec -i "$NAME" sh -c 'mariadb -uroot -p"$(cat /run/secrets/root)" --default-character-set=utf8mb4 snapshot' < "$DIR/snapshot-ddl.sql"
{ echo "SET NAMES utf8mb4; SET SESSION sql_mode = '';"; age -d -i "$KEY" "$PACKAGE"; } \
  | docker exec -i "$NAME" sh -c 'mariadb -uroot -p"$(cat /run/secrets/root)" --default-character-set=utf8mb4 snapshot'

echo "Snapshot restaurado em $NAME (rede $NETWORK, banco snapshot). Depois da importação: --remove $1 e apague o pacote."
