#!/bin/sh
# Agendado no cron do root da VPS (FND-10 CA 11 a 14):
#   30 3 * * *  /opt/projetos/bemaqui-empresas-homolog/backup-cron.sh daily   >> /var/log/bqe-homolog-backup.log 2>&1
#   45 3 * * 0  /opt/projetos/bemaqui-empresas-homolog/backup-cron.sh keyring >> /var/log/bqe-homolog-backup.log 2>&1
# Falhou: dispara o alerta backup_failed pela CLI da API (mesmo caminho dos outros alertas, FND-12).
set -u
DIR=/opt/projetos/bemaqui-empresas-homolog
MODE="${1:-daily}"
cd "$DIR" || exit 1
echo "== $(date -u +%FT%TZ) backup $MODE"
if docker compose --profile backup run --rm backup "$MODE"; then
  exit 0
fi
docker compose run --rm --no-deps migrator alert raise --kind backup_failed \
  --summary "Backup $MODE da homologação falhou" --details "Veja /var/log/bqe-homolog-backup.log na VPS." || true
exit 1
