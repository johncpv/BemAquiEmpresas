#!/usr/bin/env sh
# Fumaça pós-deploy (FND-10 CA 1, 2 e 5; GOL-01a CA 3 e 7). Roda de fora da VPS (CI ou estação).
# Uso: sh scripts/smoke.sh <host do tenant> [host que deve continuar no Dolibarr]
#   homologação: sh scripts/smoke.sh homolog.konebras.com.br app.konebras.com.br   (antes da virada o app. ainda é o Dolibarr)
#   produção:    SMOKE_PLATFORM_HOST=plataforma.bemaquiapp.com.br SMOKE_ADM_HOST=adm.konebras.com.br sh scripts/smoke.sh app.konebras.com.br
# Opcionais (variáveis de ambiente):
#   SMOKE_PLATFORM_HOST  host da plataforma: /webhooks/ chega à API fora da allowlist (401 da assinatura) e o painel fica fechado (404)
#                        para quem não está na allowlist. Rode de um IP FORA da allowlist (o runner do GitHub é).
#   SMOKE_ADM_HOST       Dolibarr em adm.: responde com a senha básica (401) ou, de um IP liberado, com a tela do Dolibarr.
set -eu
HOST="${1:?uso: sh scripts/smoke.sh <host do tenant> [host do Dolibarr]}"
DOLIBARR="${2:-}"
PLATFORM="${SMOKE_PLATFORM_HOST:-}"
ADM="${SMOKE_ADM_HOST:-}"
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

curl -s -o /dev/null -D - --max-time 15 "https://$HOST/" | grep -qi '^strict-transport-security:' || fail "o front respondeu sem HSTS"
echo "ok  cabeçalhos de segurança (HSTS)"

[ "$(code "https://$HOST/api/v1/public/tenant-context")" = "200" ] || fail "tenant não resolvido em $HOST (provisionado? host em tenant_domains?)"
echo "ok  tenant-context"

# /hubs vai para a API (401 sem token), não para o front: prova a rota do SignalR no vhost.
[ "$(code -X POST "https://$HOST/hubs/ping/negotiate?negotiateVersion=1")" = "401" ] || fail "/hubs não chega à API"
echo "ok  /hubs na API"

if [ -n "$PLATFORM" ]; then
  [ "$(code -X POST -H 'Content-Type: application/json' -d '{}' "https://$PLATFORM/webhooks/emissor-fiscal/AAAAAAAAAAAAAAAAAAAAAA")" = "401" ] \
    || fail "/webhooks/ em $PLATFORM não chega à API fora da allowlist (esperado 401 da assinatura)"
  echo "ok  /webhooks/ na plataforma, fora da allowlist"
  [ "$(code "https://$PLATFORM/hangfire")" = "404" ] || fail "o painel de $PLATFORM está aberto fora da allowlist"
  echo "ok  painel da plataforma fechado fora da allowlist"
fi

if [ -n "$ADM" ]; then
  status=$(code "https://$ADM/")
  if [ "$status" = "401" ]; then
    echo "ok  $ADM pede a senha básica"
  else
    curl -s -L --max-time 15 "https://$ADM/" | grep -qi dolibarr || fail "$ADM não responde com a senha básica nem com o Dolibarr (status $status)"
    echo "ok  $ADM serve o Dolibarr (IP liberado)"
  fi
fi

if [ -n "$DOLIBARR" ]; then
  curl -s -L --max-time 15 "https://$DOLIBARR/" | grep -qi dolibarr || fail "$DOLIBARR não está mais servindo o Dolibarr"
  echo "ok  $DOLIBARR continua no Dolibarr"
fi
echo "Fumaça ok em $HOST."
