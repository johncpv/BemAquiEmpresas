#!/bin/sh
# Backup da PRODUÇÃO (GOL-01a CA 1 i, ADR 0015 e 0034). Agendado no cron do root da VPS, fora do horário da homologação (03:30/03:45 UTC):
#   15 4 * * *  /opt/projetos/bemaqui-empresas/backup-cron.sh daily   >> /var/log/bqe-prod-backup.log 2>&1
#   40 4 * * 0  /opt/projetos/bemaqui-empresas/backup-cron.sh keyring >> /var/log/bqe-prod-backup.log 2>&1
# Destino: buckets B2 só da produção, chaves sem deleteFiles; retenção de 30 dias pela regra de ciclo de vida do bucket.
# Falhou: dispara o alerta backup_failed pela CLI da API (mesmo caminho dos outros alertas, FND-12).
set -u
DIR=/opt/projetos/bemaqui-empresas
MODE="${1:-daily}"
cd "$DIR" || exit 1
export DOCKER_CONFIG="$DIR/.docker"
echo "== $(date -u +%FT%TZ) backup $MODE"
if docker compose --profile backup run --rm backup "$MODE"; then
  exit 0
fi
docker compose run --rm --no-deps migrator alert raise --kind backup_failed \
  --summary "Backup $MODE da PRODUÇÃO falhou" --details "Veja /var/log/bqe-prod-backup.log na VPS." || true
exit 1
