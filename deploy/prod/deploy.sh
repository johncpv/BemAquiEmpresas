#!/bin/sh
# Deploy da PRODUÇÃO (GOL-01a CA 3, ADR 0034 e 0015). Fica em /opt/projetos/bemaqui-empresas/deploy.sh e é o único comando que a chave
# de deploy de produção do GitHub Actions pode rodar (authorized_keys: command="/opt/projetos/bemaqui-empresas/deploy.sh",restrict).
# Este arquivo NÃO se atualiza pelo pipeline: a cópia da VPS é instalada à mão (docs/operacao/deploy.md, seção "Produção").
# Entrada: SSH_ORIGINAL_COMMAND="deploy <sha de 40 caracteres>" e, no stdin, um tar.gz com a pasta deploy/ do commit.
# Ordem:
#   1. compose do commit conferido (docker compose config) antes de instalar;
#   2. pull das imagens da versão nova (falhou: nada mudou);
#   3. pg_dump do catálogo e de cada tenant_* (tenant_konebras), chmod 0600, conferido com pg_restore -l (falhou: nada mudou);
#   4. migrator (falhou: volta compose e tag; a versão anterior continua no ar e o migrator manda o alerta);
#   5. fiscal-pdf-reader, bqe-prod-api e bqe-prod-web com --wait (falhou: volta para a tag e o compose anteriores);
#   6. fumaça local pelo stack-nginx (/health/ready e tenant-context); falhou: volta como no passo 5.
# As migrations seguem expand/contract: a versão anterior aceita o schema novo. Voltar o BANCO é manual, pelo dump do passo 3
# (docs/operacao/restauracao.md), e só com o PO.
set -eu
DIR=/opt/projetos/bemaqui-empresas
DUMPS="$DIR/predeploy-dumps"
KEEP_DUMPS=5
SERVICES="fiscal-pdf-reader bqe-prod-api bqe-prod-web"
cd "$DIR"
umask 077
# Login do GHCR só da produção (token próprio, só leitura), separado do /root/.docker da homologação.
export DOCKER_CONFIG="$DIR/.docker"

set -- ${SSH_ORIGINAL_COMMAND:-"$@"}
[ "${1:-}" = "deploy" ] || { echo "Comando não permitido." >&2; exit 64; }
TAG="${2:-}"
echo "$TAG" | grep -Eq '^[0-9a-f]{40}$' || { echo "Versão inválida: use o SHA completo do commit." >&2; exit 64; }
docker compose version >/dev/null 2>&1 || { echo "docker compose não encontrado com DOCKER_CONFIG=$DOCKER_CONFIG." >&2; exit 69; }

exec 9>"$DIR/.deploy.lock"
flock -n 9 || { echo "Já existe um deploy em andamento." >&2; exit 75; }

T=$(date -u +%Y%m%dT%H%M%SZ)
staging=$(mktemp -d)
trap 'rm -rf "$staging"' EXIT
head -c 5000000 | tar -xz -C "$staging" deploy/prod/docker-compose.yml deploy/prod/backup-cron.sh deploy/postgres/init-roles.sh

# 1. Compose do commit válido com o .env e os secrets daqui, antes de trocar qualquer coisa.
cp -p .env "$staging/.env"
ln -s "$DIR/secrets" "$staging/deploy/prod/secrets"
ln -s "$DIR/postgres" "$staging/deploy/prod/postgres"
if ! docker compose -f "$staging/deploy/prod/docker-compose.yml" --env-file "$staging/.env" --profile backup config -q; then
  echo "O compose de $TAG não é válido com o .env e os secrets da produção. Nada mudou." >&2
  exit 1
fi

previous=$(sed -n 's/^IMAGE_TAG=//p' .env)
set_tag() { sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=$1/" .env; }
cp -p docker-compose.yml "docker-compose.yml.bak-$T" 2>/dev/null || true
restore_compose() { [ -f "docker-compose.yml.bak-$T" ] && cp -p "docker-compose.yml.bak-$T" docker-compose.yml || true; }

install -m 0644 "$staging/deploy/prod/docker-compose.yml" "$DIR/docker-compose.yml"
install -m 0755 "$staging/deploy/prod/backup-cron.sh" "$DIR/backup-cron.sh"
install -d -m 0755 "$DIR/postgres"
install -m 0755 "$staging/deploy/postgres/init-roles.sh" "$DIR/postgres/init-roles.sh"

rollback() { # $1 = mensagem; volta tag e compose e sobe a versão anterior
  echo "$1 Voltando para $previous." >&2
  set_tag "$previous"
  restore_compose
  docker compose up -d --no-deps --wait --wait-timeout 180 $SERVICES || echo "ATENÇÃO: a versão anterior também não ficou saudável." >&2
}

# 2. Imagens.
set_tag "$TAG"
if ! docker compose pull --quiet migrator $SERVICES backup; then
  set_tag "$previous"; restore_compose
  echo "Não foi possível baixar as imagens de $TAG. Nada mudou." >&2
  exit 1
fi

# 3. Dump antes de migrar: catálogo e cada banco de tenant, conferidos.
install -d -m 0700 "$DUMPS"
dbs="catalog $(docker exec bqe-prod-postgres psql -U postgres -d postgres -Atc "select datname from pg_database where datname like 'tenant\_%' order by 1")" \
  || { set_tag "$previous"; restore_compose; echo "Não foi possível listar os bancos. Nada mudou." >&2; exit 1; }
for db in $dbs; do
  f="$DUMPS/$db-$T.dump"
  if ! docker exec bqe-prod-postgres pg_dump -U postgres -Fc "$db" > "$f" || ! chmod 0600 "$f" \
     || ! docker exec -i bqe-prod-postgres pg_restore -l < "$f" > /dev/null; then
    rm -f "$f"
    set_tag "$previous"; restore_compose
    echo "Dump de $db falhou ou não confere (pg_restore -l). Nada mudou." >&2
    exit 1
  fi
  echo "dump ok  $db ($(wc -c < "$f") bytes)"
  # Guarda só os últimos $KEEP_DUMPS dumps de cada banco (dado real em claro no disco da VPS, 0600 em diretório 0700).
  ls -1t "$DUMPS/$db"-*.dump 2>/dev/null | tail -n +$((KEEP_DUMPS + 1)) | xargs -r rm -f
done
echo "$dbs" | grep -qw tenant_konebras || echo "AVISO: tenant_konebras não existe ainda (primeira subida?)." >&2

# 4. Migrator.
if ! docker compose run --rm migrator; then
  set_tag "$previous"; restore_compose
  echo "Migração falhou. A versão anterior ($previous) continua no ar; o alerta foi enviado pelo migrator. Dumps em $DUMPS/*-$T.dump." >&2
  exit 2
fi

# 5. Subida esperando healthy.
if ! docker compose up -d --no-deps --wait --wait-timeout 180 $SERVICES; then
  rollback "A versão $TAG não ficou saudável."
  exit 3
fi

# 6. Fumaça local, pelo stack-nginx (mesmo caminho do usuário, sem sair da VPS).
TENANT_HOST=$(sed -n 's/^TENANT_HOST=//p' .env)
smoke() {
  [ -n "$TENANT_HOST" ] || return 0
  i=0
  until [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 --resolve "$TENANT_HOST:443:127.0.0.1" "https://$TENANT_HOST/health/ready")" = "200" ]; do
    i=$((i + 1)); [ "$i" -lt 12 ] || return 1; sleep 5
  done
  [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 --resolve "$TENANT_HOST:443:127.0.0.1" "https://$TENANT_HOST/api/v1/public/tenant-context")" = "200" ]
}
if ! smoke; then
  rollback "A fumaça falhou em https://$TENANT_HOST (/health/ready ou tenant-context)."
  exit 4
fi

ls -1t docker-compose.yml.bak-* 2>/dev/null | tail -n +6 | xargs -r rm -f
docker image prune -f --filter "until=168h" >/dev/null 2>&1 || true
echo "Produção na versão $TAG (anterior: $previous)."
