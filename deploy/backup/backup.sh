#!/bin/sh
# Backup cifrado dos bancos (FND-10, ADR 0015). Uso: backup.sh daily | keyring
#   daily   -> um pg_dump por banco (catálogo e cada tenant) + papéis (globals), para o bucket dos dumps
#   keyring -> só security.data_protection_keys, para a trilha separada (outro bucket e outra credencial)
# O dump nunca toca o disco em claro: pg_dump | age | rclone rcat. A credencial do bucket não tem permissão de apagar;
# a retenção de 30 dias é regra de ciclo de vida do bucket. Saída diferente de 0 = falha (o cron dispara o alerta).
set -eu
set -o pipefail

MODE="${1:-daily}"
SECRETS="${SECRETS_DIR:-/run/secrets}"
: "${BACKUP_ENV:?defina BACKUP_ENV (ex.: homolog)}"
export PGHOST="${PGHOST:-postgres}" PGUSER="${PGUSER:-postgres}" PGDATABASE=postgres
PGPASSWORD="$(cat "$SECRETS/backup-pg-password")"
export PGPASSWORD
RECIPIENTS="$SECRETS/backup-age-recipients"
CATALOG_DB="${CATALOG_DB:-catalog}"
DAY="$(date -u +%F)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"

remote() { # $1 = prefixo dos secrets do destino: dumps | keyring
  export RCLONE_CONFIG_DEST_TYPE=s3
  export RCLONE_CONFIG_DEST_PROVIDER="${S3_PROVIDER:-Other}"
  RCLONE_CONFIG_DEST_ENDPOINT="$(cat "$SECRETS/$1-s3-endpoint")"
  RCLONE_CONFIG_DEST_ACCESS_KEY_ID="$(cat "$SECRETS/$1-s3-access-key")"
  RCLONE_CONFIG_DEST_SECRET_ACCESS_KEY="$(cat "$SECRETS/$1-s3-secret-key")"
  export RCLONE_CONFIG_DEST_ENDPOINT RCLONE_CONFIG_DEST_ACCESS_KEY_ID RCLONE_CONFIG_DEST_SECRET_ACCESS_KEY
  BUCKET="$(cat "$SECRETS/$1-s3-bucket")"
}

send() { # $1 = nome do objeto; lê do stdin o conteúdo já cifrado
  rclone rcat --s3-no-check-bucket --s3-no-head "dest:$BUCKET/$BACKUP_ENV/$DAY/$1"
}

failed=""
dump() { # $1 = banco
  if pg_dump -Fc -d "$1" | age -R "$RECIPIENTS" | send "$1-$STAMP.dump.age"; then
    echo "OK     $1"
  else
    echo "FALHOU $1" >&2
    failed="$failed $1"
  fi
}

case "$MODE" in
  daily)
    remote dumps
    if pg_dumpall --globals-only | age -R "$RECIPIENTS" | send "globals-$STAMP.sql.age"; then echo "OK     globals"; else failed="$failed globals"; fi
    dbs="$(psql -At -c "select datname from pg_database where not datistemplate and datname <> 'postgres' order by 1")" || { echo "Não foi possível listar os bancos." >&2; exit 1; }
    for db in $dbs; do
      dump "$db"
    done
    ;;
  keyring)
    remote keyring
    if pg_dump -Fc -d "$CATALOG_DB" -t security.data_protection_keys --data-only | age -R "$RECIPIENTS" | send "keyring-$STAMP.dump.age"; then
      echo "OK     keyring"
    else
      failed="keyring"
    fi
    ;;
  *)
    echo "Uso: backup.sh daily | keyring" >&2
    exit 64
    ;;
esac

if [ -n "$failed" ]; then
  echo "Backup $MODE com falha em:$failed" >&2
  exit 1
fi
echo "Backup $MODE concluído em $DAY."
