#!/bin/sh
# Backup do ambiente SaaS (ADR 0051 §5). DESLIGADO no início: o ambiente começa vazio e só tem o dump antes de cada deploy. Antes do
# primeiro cliente com dado real: criar os buckets B2 e os secrets de backup (docs/operacao/deploy.md, "Ambiente SaaS") e agendar no
# cron do root, fora do horário da stack da Konebras (03:30 e 03:45 UTC):
#   50 4 * * *  /opt/projetos/bemaqui-saas/backup-cron.sh daily   >> /var/log/bqe-saas-backup.log 2>&1
#   10 5 * * 0  /opt/projetos/bemaqui-saas/backup-cron.sh keyring >> /var/log/bqe-saas-backup.log 2>&1
# Falhou: dispara o alerta backup_failed pela CLI da API (mesmo caminho dos outros alertas, FND-12).
set -u
DIR=/opt/projetos/bemaqui-saas
MODE="${1:-daily}"
cd "$DIR" || exit 1
echo "== $(date -u +%FT%TZ) backup $MODE"
if docker compose --profile backup run --rm backup "$MODE"; then
  exit 0
fi
docker compose run --rm --no-deps migrator alert raise --kind backup_failed \
  --summary "Backup $MODE do SaaS falhou" --details "Veja /var/log/bqe-saas-backup.log na VPS." || true
exit 1
