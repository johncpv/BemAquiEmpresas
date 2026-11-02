#!/usr/bin/env sh
# Deploy da stack bemaqui-empresas-homolog, que é a PRODUÇÃO desde 02/11/2026 (app.konebras.com.br; deploy.md, seção do topo).
# Todo deploy é de produção: só da origin/main, depois do merge de um PR com o CI verde, e com o dump antes (passo 3).
#
# Passos: (1) worktree de deploy no commit da origin/main; (2) build local das imagens api, web e backup com a tag do SHA e envio à
# VPS por "docker save | ssh docker load" (sem GHCR); (3) na VPS: pg_dump -Fc do catalog e do tenant_konebras em predeploy-dumps/
# (0600), conferidos com pg_restore -l; (4) compose e deploy.sh do commit instalados, com cópia .bak-<carimbo>; (5) IMAGE_TAG novo no
# .env e migrator; falhou, volta a tag e o compose; (6) up --wait de fiscal-pdf-reader, api e web; sem saúde, volta e sobe a anterior;
# (7) fumaça de fora em app.konebras.com.br.
#
# Uso (Git Bash ou Linux, com Docker local):
#   BQE_SSH_KEY=~/.ssh/<chave da VPS> sh scripts/deploy-homolog.sh
# Variáveis:
#   BQE_SSH_KEY       caminho da chave SSH do root da VPS (obrigatória; nunca vai para o repositório)
#   BQE_SSH_TARGET    destino do SSH (padrão root@2.25.82.127)
#   BQE_WORKTREE      worktree só de deploy, criada se não existir (padrão: ../bq-deploy ao lado do repositório)
#   BQE_REGISTRY      prefixo das imagens, o mesmo REGISTRY do .env da VPS (padrão ghcr.io/johncpv)
#   BQE_LOG_DIR       onde ficam os logs do build (padrão: diretório temporário)
#   BQE_YES=1         não pergunta antes de publicar
#   SMOKE_*           repassadas ao scripts/smoke.sh (ex.: SMOKE_PLATFORM_HOST, só de um IP fora da allowlist)
# Voltar o BANCO é manual, pelo dump do passo 3 (restauracao.md), só com o PO.
set -eu
ROOT=$(cd "$(dirname "$0")/.." && pwd)
KEY="${BQE_SSH_KEY:?defina BQE_SSH_KEY com o caminho da chave SSH da VPS}"
TARGET="${BQE_SSH_TARGET:-root@2.25.82.127}"
W="${BQE_WORKTREE:-$ROOT/../bq-deploy}"
R="${BQE_REGISTRY:-ghcr.io/johncpv}/bemaqui-empresas"
L="${BQE_LOG_DIR:-$(mktemp -d)}"
TENANT_HOST=app.konebras.com.br
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
  printf 'Deploy de PRODUÇÃO (%s) do commit acima, já com o PR verde e mergeado? [s/N] ' "$TENANT_HOST"
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
$SCP deploy/homolog/docker-compose.yml "$TARGET:/tmp/bqe-compose.yml"
$SCP deploy/homolog/deploy.sh "$TARGET:/tmp/bqe-deploy.sh"

# 3 a 6. Na VPS.
$SSH sh -s -- "$SHA" <<'REMOTE'
set -eu
SHA=$1
cd /opt/projetos/bemaqui-empresas-homolog
T=$(date -u +%Y%m%dT%H%M%SZ)
D=predeploy-dumps
install -d -m 0700 "$D"
for db in catalog tenant_konebras; do
  docker exec bqe-homolog-postgres pg_dump -U postgres -Fc "$db" > "$D/$db-$T.dump"
  chmod 0600 "$D/$db-$T.dump"
  docker exec -i bqe-homolog-postgres pg_restore -l < "$D/$db-$T.dump" > /dev/null
done
echo "dump ok $T"

prev=$(sed -n 's/^IMAGE_TAG=//p' .env)
cp -p docker-compose.yml "docker-compose.yml.bak-$T"
cp -p deploy.sh "deploy.sh.bak-$T"
restore() {
  sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=$prev/" .env
  cp -p "docker-compose.yml.bak-$T" docker-compose.yml
  cp -p "deploy.sh.bak-$T" deploy.sh
}
install -m 0644 /tmp/bqe-compose.yml docker-compose.yml
install -m 0755 /tmp/bqe-deploy.sh deploy.sh
rm -f /tmp/bqe-compose.yml /tmp/bqe-deploy.sh
docker compose config -q || { restore; echo "COMPOSE INVÁLIDO: nada mudou" >&2; exit 4; }
echo "compose válido"

sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=$SHA/" .env
log=$(mktemp)
if ! docker compose run --rm migrator > "$log" 2>&1; then
  grep -vE '^\{' "$log" | tail -20 >&2; rm -f "$log"
  restore
  echo "MIGRATOR FALHOU: a versão $prev continua no ar" >&2
  exit 2
fi
grep -vE '^\{' "$log" | tail -4; rm -f "$log"

if ! docker compose up -d --no-deps --wait --wait-timeout 180 fiscal-pdf-reader bqe-homolog-api bqe-homolog-web; then
  echo "SUBIDA FALHOU: voltando para $prev" >&2
  restore
  docker compose up -d --no-deps --wait --wait-timeout 180 fiscal-pdf-reader bqe-homolog-api bqe-homolog-web || true
  exit 3
fi
echo "no ar: $SHA (dumps $T em $D)"
REMOTE

# 7. Fumaça de fora (smoke.sh do commit publicado).
SMOKE_REDIRECT_HOST="${SMOKE_REDIRECT_HOST:-homolog.konebras.com.br}" sh scripts/smoke.sh "$TENANT_HOST"
echo "deploy $SHA ok"
