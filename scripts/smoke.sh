#!/usr/bin/env sh
# Fumaça pós-deploy (FND-10 CA 1, 2 e 5; GOL-01a CA 3 e 7). Roda de fora da VPS (CI ou estação). Só lê: nunca escreve no tenant.
# Desde 02/11/2026 a stack bemaqui-empresas-homolog é a produção e serve o tenant konebras em app.konebras.com.br (go-live.md §0).
# Uso: sh scripts/smoke.sh [host do tenant]          (padrão: app.konebras.com.br)
#   produção atual: SMOKE_PLATFORM_HOST=plataforma-homolog.bemaquiapp.com.br SMOKE_REDIRECT_HOST=homolog.konebras.com.br sh scripts/smoke.sh
# Opcionais (variáveis de ambiente):
#   SMOKE_PLATFORM_HOST  host da plataforma: /webhooks/ chega à API fora da allowlist (401 da assinatura) e o painel fica fechado (404)
#                        para quem não está na allowlist. Rode de um IP FORA da allowlist (o runner do GitHub é).
#   SMOKE_REDIRECT_HOST  host antigo que só redireciona (301) para o host do tenant, com o mesmo caminho.
# Compatibilidade: o 2º argumento (host que "continua no Dolibarr") e SMOKE_ADM_HOST (Dolibarr em adm.) eram da fase antes da
# virada. O Dolibarr foi desligado e removido da VPS em 02/11/2026; os dois são aceitos e ignorados, com aviso.
set -eu
HOST="${1:-app.konebras.com.br}"
PLATFORM="${SMOKE_PLATFORM_HOST:-}"
REDIRECT="${SMOKE_REDIRECT_HOST:-}"
fail() { echo "FALHOU: $1" >&2; exit 1; }
code() { curl -s -o /dev/null -w '%{http_code}' --max-time 15 "$@"; }

[ -z "${2:-}" ] || echo "aviso: 2º argumento ($2) ignorado: o Dolibarr foi desligado em 02/11/2026" >&2
[ -z "${SMOKE_ADM_HOST:-}" ] || echo "aviso: SMOKE_ADM_HOST ignorado: o vhost adm. do Dolibarr foi removido em 02/11/2026" >&2

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

if [ -n "$REDIRECT" ]; then
  headers=$(curl -s -o /dev/null -D - --max-time 15 "https://$REDIRECT/login")
  printf '%s' "$headers" | head -1 | grep -q ' 301' || fail "$REDIRECT não respondeu 301"
  printf '%s' "$headers" | grep -qi "^location: https://$HOST/login" || fail "$REDIRECT não redireciona para https://$HOST com o mesmo caminho"
  echo "ok  $REDIRECT redireciona para $HOST"
fi

if [ -n "$PLATFORM" ]; then
  [ "$(code -X POST -H 'Content-Type: application/json' -d '{}' "https://$PLATFORM/webhooks/emissor-fiscal/AAAAAAAAAAAAAAAAAAAAAA")" = "401" ] \
    || fail "/webhooks/ em $PLATFORM não chega à API fora da allowlist (esperado 401 da assinatura)"
  echo "ok  /webhooks/ na plataforma, fora da allowlist"
  [ "$(code "https://$PLATFORM/hangfire")" = "404" ] || fail "o painel de $PLATFORM está aberto fora da allowlist"
  echo "ok  painel da plataforma fechado fora da allowlist"
fi
echo "Fumaça ok em $HOST."
