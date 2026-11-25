#!/usr/bin/env sh
# Deploy do ambiente SaaS multi-tenant (ADR 0051), na VPS compartilhada, em /opt/projetos/bemaqui-saas (projeto compose bqe-saas).
# Mesmo padrão do scripts/deploy-homolog.sh (que continua sendo o deploy da Konebras), com destino próprio. Ordem do PO: o deploy vai
# PRIMEIRO ao SaaS e, com a fumaça verde, depois à Konebras (deploy.md, "Ambiente SaaS"). Mesma versão (SHA) nos dois.
# Só da origin/main, depois do merge de um PR com o CI verde, e com o dump antes (passo 3).
#
# Passos: (1) worktree de deploy no commit da origin/main; (2) build local das imagens api, web e backup com a tag do SHA e envio à
# VPS por "docker save | ssh docker load" (sem GHCR); (3) na VPS: pg_dump -Fc do catalog e de TODO banco tenant_* em predeploy-dumps/
# (0600), conferidos com pg_restore -l; (4) compose e deploy.sh do commit instalados, com cópia .bak-<carimbo>, e conferidos com o
# .env e os secrets daqui; (5) IMAGE_TAG novo no .env e migrator (o TenantMigrator em todos os tenants); falhou, volta a tag e o compose;
# (6) up --wait de fiscal-pdf-reader, api e web; sem saúde, volta e sobe a anterior; (7) fumaça de fora no host do ERP do tenant bemaqui
# e, se informado, no da plataforma.
#
# Uso (Git Bash ou Linux, com Docker local):
#   BQE_SSH_KEY=~/.ssh/<chave da VPS> BQE_SAAS_APP_HOST=<host do ERP> sh scripts/deploy-saas.sh
# Variáveis:
#   BQE_SSH_KEY         caminho da chave SSH do root da VPS (obrigatória; nunca vai para o repositório)
#   BQE_SSH_TARGET      destino do SSH (padrão root@2.25.82.127)
#   BQE_SAAS_APP_HOST   host do ERP do tenant bemaqui, para a fumaça de fora (vazio: pula a fumaça de fora, com aviso)
#   BQE_SAAS_PLATFORM_HOST  host da plataforma (opcional; SMOKE_PLATFORM_HOST do smoke.sh, só de um IP fora da allowlist)
#   BQE_WORKTREE        worktree só de deploy, criada se não existir (padrão: ../bq-deploy ao lado do repositório)
#   BQE_REGISTRY        prefixo das imagens; TEM de ser o mesmo REGISTRY do .env da VPS do SaaS (padrão ghcr.io/johncpv, o mesmo da Konebras: a mesma imagem por SHA serve as duas stacks)
#   BQE_LOG_DIR         onde ficam os logs do build (padrão: diretório temporário)
#   BQE_YES=1           não pergunta antes de publicar
# Voltar o BANCO é manual, pelo dump do passo 3 (restauracao.md), só com o PO.
set -eu
ROOT=$(cd "$(dirname "$0")/.." && pwd)
KEY="${BQE_SSH_KEY:?defina BQE_SSH_KEY com o caminho da chave SSH da VPS}"
TARGET="${BQE_SSH_TARGET:-root@2.25.82.127}"
W="${BQE_WORKTREE:-$ROOT/../bq-deploy}"
REG="${BQE_REGISTRY:-ghcr.io/johncpv}"
R="$REG/bemaqui-empresas"
L="${BQE_LOG_DIR:-$(mktemp -d)}"
APP_HOST="${BQE_SAAS_APP_HOST:-}"
[ -r "$KEY" ] || { echo "Chave SSH não encontrada: $KEY" >&2; exit 64; }
SSH="ssh -i $KEY -o IdentitiesOnly=yes -o BatchMode=yes $TARGET"
SCP="scp -q -i $KEY -o IdentitiesOnly=yes -o BatchMode=yes"

# 1. Worktree de deploy no commit da origin/main (o repositório de trabalho não é tocado).
git -C "$ROOT" fetch -q origin main
[ -d "$W" ] || git -C "$ROOT" worktree add -q --detach "$W" origin/main
git -C "$W" checkout -q --detach origin/main
SHA=$(git -C "$W" rev-parse HEAD)
echo "Commit: $(git -C "$W" log -1 --format='%h %s')"
if [ "${BQE_YES:-}" != "1" ]; then
  printf 'Deploy do AMBIENTE SaaS (bqe-saas, %s) do commit acima, já com o PR verde e mergeado? [s/N] ' "${APP_HOST:-host ainda não definido}"
  read -r answer
  [ "$answer" = "s" ] || { echo "Cancelado."; exit 0; }
fi

# 2. Build local e envio das imagens.
cd "$W"
for i in api web backup; do
  docker build -q -f "deploy/$i.Dockerfile" -t "$R-$i:$SHA" . > "$L/build-$i.log" 2>&1 \
    || { echo "FALHOU build $i (log em $L/build-$i.log)" >&2; tail -15 "$L/build-$i.log" >&2; exit 1; }
done
echo "build ok $SHA"
docker save "$R-api:$SHA" "$R-web:$SHA" "$R-backup:$SHA" | gzip -1 | $SSH 'gunzip | docker load' > /dev/null
echo "imagens enviadas"
$SCP deploy/saas/docker-compose.yml "$TARGET:/tmp/bqe-saas-compose.yml"
$SCP deploy/saas/deploy.sh "$TARGET:/tmp/bqe-saas-deploy.sh"
$SCP deploy/saas/backup-cron.sh "$TARGET:/tmp/bqe-saas-backup-cron.sh"
$SCP deploy/postgres/init-roles.sh "$TARGET:/tmp/bqe-saas-init-roles.sh"

# 3 a 6. Na VPS.
$SSH sh -s -- "$SHA" "$REG" <<'REMOTE'
set -eu
SHA=$1
REG=$2
D=/opt/projetos/bemaqui-saas
cd "$D"
grep -qx "REGISTRY=$REG" .env || { echo "REGISTRY do .env da VPS difere de $REG: ajuste BQE_REGISTRY ou o .env" >&2; exit 4; }
for i in api web backup; do
  docker image inspect "$REG/bemaqui-empresas-$i:$SHA" > /dev/null || { echo "imagem $i:$SHA não carregada na VPS" >&2; exit 4; }
done
T=$(date -u +%Y%m%dT%H%M%SZ)
DUMPS=predeploy-dumps
KEEP=5
install -d -m 0700 "$DUMPS"
# Dump do catalog e de TODO banco tenant_* (todos os clientes), conferidos com pg_restore -l; falhou, nada muda.
dbs="catalog $(docker exec bqe-saas-postgres psql -U postgres -d postgres -Atc "select datname from pg_database where datname like 'tenant\_%' order by 1")"
for db in $dbs; do
  f="$DUMPS/$db-$T.dump"
  if ! docker exec bqe-saas-postgres pg_dump -U postgres -Fc "$db" > "$f" || ! chmod 0600 "$f" \
     || ! docker exec -i bqe-saas-postgres pg_restore -l < "$f" > /dev/null; then
    rm -f "$f"; echo "Dump de $db falhou ou não confere. Nada mudou." >&2; exit 1
  fi
  echo "dump ok  $db ($(wc -c < "$f") bytes)"
  ls -1t "$DUMPS/$db"-*.dump 2>/dev/null | tail -n +$((KEEP + 1)) | xargs -r rm -f
done

prev=$(sed -n 's/^IMAGE_TAG=//p' .env)
cp -p docker-compose.yml "docker-compose.yml.bak-$T"
cp -p deploy.sh "deploy.sh.bak-$T"
restore() {
  sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=$prev/" .env
  cp -p "docker-compose.yml.bak-$T" docker-compose.yml
  cp -p "deploy.sh.bak-$T" deploy.sh
}
install -m 0644 /tmp/bqe-saas-compose.yml docker-compose.yml
install -m 0755 /tmp/bqe-saas-deploy.sh deploy.sh
install -m 0755 /tmp/bqe-saas-backup-cron.sh backup-cron.sh
install -d -m 0755 postgres
install -m 0755 /tmp/bqe-saas-init-roles.sh postgres/init-roles.sh
rm -f /tmp/bqe-saas-compose.yml /tmp/bqe-saas-deploy.sh /tmp/bqe-saas-backup-cron.sh /tmp/bqe-saas-init-roles.sh
sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=$SHA/" .env
docker compose --profile backup config -q || { restore; echo "COMPOSE INVÁLIDO com o .env e os secrets do SaaS: nada mudou" >&2; exit 4; }
echo "compose válido"

log=$(mktemp)
if ! docker compose run --rm --pull never migrator > "$log" 2>&1; then
  grep -vE '^\{' "$log" | tail -20 >&2; rm -f "$log"
  restore
  echo "MIGRATOR FALHOU: a versão $prev continua no ar" >&2
  exit 2
fi
grep -vE '^\{' "$log" | tail -4; rm -f "$log"

S="fiscal-pdf-reader bqe-saas-api bqe-saas-web"
if ! docker compose up -d --pull never --no-deps --wait --wait-timeout 180 $S; then
  echo "SUBIDA FALHOU: voltando para $prev" >&2
  restore
  docker compose up -d --pull never --no-deps --wait --wait-timeout 180 $S || true
  exit 3
fi
ls -1t docker-compose.yml.bak-* 2>/dev/null | tail -n +6 | xargs -r rm -f
ls -1t deploy.sh.bak-* 2>/dev/null | tail -n +6 | xargs -r rm -f
echo "no ar: $SHA (dumps $T em $DUMPS)"
REMOTE

# 7. Fumaça de fora (smoke.sh do commit publicado).
if [ -n "$APP_HOST" ]; then
  SMOKE_PLATFORM_HOST="${BQE_SAAS_PLATFORM_HOST:-}" sh scripts/smoke.sh "$APP_HOST"
else
  echo "AVISO: BQE_SAAS_APP_HOST vazio; fumaça de fora pulada (o tenant bemaqui ainda não existe?). Confira /health/ready à mão." >&2
fi
echo "deploy SaaS $SHA ok. Próximo: o mesmo SHA na Konebras (scripts/deploy-homolog.sh)."
