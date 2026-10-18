#!/usr/bin/env sh
# "nginx -t" do vhost de homologação (FND-10 CA 4), num nginx da mesma versão do stack-nginx, com certificados de teste.
# Uso: sh scripts/check-nginx.sh
set -eu
ROOT=$(cd "$(dirname "$0")/.." && pwd -W 2>/dev/null || pwd)
MSYS_NO_PATHCONV=1 docker run --rm \
  -v "$ROOT/deploy/nginx:/conf:ro" \
  nginx:1.31-alpine sh -c '
    set -e
    apk add --no-cache openssl >/dev/null
    for h in homolog.konebras.com.br plataforma-homolog.bemaquiapp.com.br; do
      mkdir -p /etc/letsencrypt/live/$h
      openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj "/CN=$h" \
        -keyout /etc/letsencrypt/live/$h/privkey.pem -out /etc/letsencrypt/live/$h/fullchain.pem 2>/dev/null
    done
    rm -f /etc/nginx/conf.d/default.conf
    cp /conf/bemaqui-empresas-homolog.conf /etc/nginx/conf.d/
    cp /conf/bemaqui-empresas-homolog.allow.example /etc/nginx/conf.d/bemaqui-empresas-homolog.allow
    nginx -t
    cp /conf/bemaqui-empresas-homolog.bootstrap.conf /etc/nginx/conf.d/bemaqui-empresas-homolog.conf
    nginx -t'
