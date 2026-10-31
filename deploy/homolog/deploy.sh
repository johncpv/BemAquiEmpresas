#!/bin/sh
# Deploy da homologação (FND-10 CA 1 e 3). Fica em /opt/projetos/bemaqui-empresas-homolog/deploy.sh e é o único comando
# que a chave de deploy do GitHub Actions pode rodar (authorized_keys: command="...deploy.sh",restrict).
# Entrada: SSH_ORIGINAL_COMMAND="deploy <sha de 40 caracteres>" e, no stdin, um tar.gz com a pasta deploy/ do commit.
# Ordem: instala compose e scripts do commit -> pull das imagens -> migrator -> bqe-homolog-api e -web (esperando healthy).
# Se o migrator falhar, a API antiga continua no ar (o migrator manda o alerta). Se a API nova não ficar saudável,
# volta para a versão anterior.
set -eu
DIR=/opt/projetos/bemaqui-empresas-homolog
cd "$DIR"

set -- ${SSH_ORIGINAL_COMMAND:-"$@"}
[ "${1:-}" = "deploy" ] || { echo "Comando não permitido." >&2; exit 64; }
TAG="${2:-}"
echo "$TAG" | grep -Eq '^[0-9a-f]{40}$' || { echo "Versão inválida: use o SHA completo do commit." >&2; exit 64; }

exec 9>"$DIR/.deploy.lock"
flock -n 9 || { echo "Já existe um deploy em andamento." >&2; exit 75; }

staging=$(mktemp -d)
trap 'rm -rf "$staging"' EXIT
head -c 5000000 | tar -xz -C "$staging" deploy/homolog/docker-compose.yml deploy/homolog/backup-cron.sh deploy/postgres/init-roles.sh
install -m 0644 "$staging/deploy/homolog/docker-compose.yml" "$DIR/docker-compose.yml"
install -m 0755 "$staging/deploy/homolog/backup-cron.sh" "$DIR/backup-cron.sh"
install -d -m 0755 "$DIR/postgres"
install -m 0755 "$staging/deploy/postgres/init-roles.sh" "$DIR/postgres/init-roles.sh"

previous=$(sed -n 's/^IMAGE_TAG=//p' .env)
set_tag() { sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=$1/" .env; }

set_tag "$TAG"
if ! docker compose pull --quiet migrator bqe-homolog-api bqe-homolog-web backup; then
  set_tag "$previous"
  echo "Não foi possível baixar as imagens de $TAG. Nada mudou." >&2
  exit 1
fi

if ! docker compose run --rm migrator; then
  set_tag "$previous"
  echo "Migração falhou. A versão anterior ($previous) continua no ar; o alerta foi enviado pelo migrator." >&2
  exit 2
fi

if ! docker compose up -d --no-deps --wait --wait-timeout 180 bqe-homolog-api bqe-homolog-web; then
  echo "A versão $TAG não ficou saudável. Voltando para $previous." >&2
  set_tag "$previous"
  docker compose up -d --no-deps --wait --wait-timeout 180 bqe-homolog-api bqe-homolog-web || true
  exit 3
fi

docker image prune -f --filter "until=168h" >/dev/null 2>&1 || true
echo "Homologação na versão $TAG."
