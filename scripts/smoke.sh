#!/usr/bin/env sh
# Fumaça pós-deploy (FND-10 CA 1, 2 e 5). Uso: sh scripts/smoke.sh <host do tenant> [host que deve continuar no Dolibarr]
set -eu
HOST="$1"
DOLIBARR="${2:-}"
fail() { echo "FALHOU: $1" >&2; exit 1; }
code() { curl -s -o /dev/null -w '%{http_code}' --max-time 15 "$@"; }

i=0
until [ "$(code "https://$HOST/health/ready")" = "200" ]; do
  i=$((i + 1))
  [ "$i" -ge 24 ] || { sleep 5; continue; }
  fail "/health/ready não respondeu 200 em 2 min"
done
echo "ok  /health/ready"

curl -s --max-time 15 "https://$HOST/" | grep -q 'id="root"' || fail "o front não respondeu em /"
echo "ok  front"

[ "$(code "https://$HOST/api/v1/public/tenant-context")" = "200" ] || fail "tenant não resolvido em $HOST (provisionado?)"
echo "ok  tenant-context"

# /hubs vai para a API (401 sem token), não para o front: prova a rota do SignalR no vhost.
[ "$(code -X POST "https://$HOST/hubs/ping/negotiate?negotiateVersion=1")" = "401" ] || fail "/hubs não chega à API"
echo "ok  /hubs na API"

if [ -n "$DOLIBARR" ]; then
  curl -s -L --max-time 15 "https://$DOLIBARR/" | grep -qi dolibarr || fail "$DOLIBARR não está mais servindo o Dolibarr"
  echo "ok  $DOLIBARR continua no Dolibarr"
fi
echo "Fumaça ok em $HOST."
