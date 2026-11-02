#!/usr/bin/env sh
# "nginx -t" dos vhosts (FND-10 CA 4; GOL-01a CA 3) num nginx da mesma versão do stack-nginx, com certificados de teste autoassinados.
# Os arquivos entram juntos no mesmo conf.d, como no stack-nginx compartilhado: assim um log_format ou map repetido também reprova.
# Cenários: (1) produção atual (desde 02/11/2026 a stack da homologação serve o app.): comum + bemaqui-empresas-homolog.conf;
# (2) ambiente novo da fase 2: comum + deploy/prod/nginx/bemaqui-empresas.conf, sozinho (ele também serve o app.; o
# adm-dolibarr.conf não entra: o Dolibarr foi desligado em 02/11/2026); (3) bootstrap da homologação sozinho com o comum.
# Uso: sh scripts/check-nginx.sh
set -eu
ROOT=$(cd "$(dirname "$0")/.." && pwd -W 2>/dev/null || pwd)
MSYS_NO_PATHCONV=1 docker run --rm \
  -v "$ROOT/deploy:/deploy:ro" \
  nginx:1.31-alpine sh -c '
    set -e
    apk add --no-cache openssl >/dev/null
    for h in homolog.konebras.com.br plataforma-homolog.bemaquiapp.com.br app.konebras.com.br plataforma.bemaquiapp.com.br adm.konebras.com.br; do
      mkdir -p /etc/letsencrypt/live/$h
      openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj "/CN=$h" \
        -keyout /etc/letsencrypt/live/$h/privkey.pem -out /etc/letsencrypt/live/$h/fullchain.pem 2>/dev/null
    done
    C=/etc/nginx/conf.d
    reset() {
      rm -f $C/*
      cp /deploy/nginx/00-bemaqui-empresas-common.conf $C/
      cp /deploy/nginx/bemaqui-empresas-homolog.allow.example $C/bemaqui-empresas-homolog.allow
      cp /deploy/prod/nginx/bemaqui-empresas.allow.example $C/bemaqui-empresas.allow
      cp /deploy/prod/nginx/adm-dolibarr.allow.example $C/adm-dolibarr.allow
      printf "teste:\$2y\$05\$abcdefghijklmnopqrstuu5Y0Qx0Qx0Qx0Qx0Qx0Qx0Qx0Qx0Qx0\n" > $C/adm-dolibarr.htpasswd
    }
    echo "== 1. produção atual (stack da homologação em app., homolog. com 301)"
    reset
    cp /deploy/nginx/bemaqui-empresas-homolog.conf $C/
    nginx -t
    echo "== 2. ambiente novo da fase 2 (deploy/prod, sozinho)"
    reset
    cp /deploy/prod/nginx/bemaqui-empresas.conf $C/
    nginx -t
    echo "== 3. bootstrap da homologação"
    reset
    cp /deploy/nginx/bemaqui-empresas-homolog.bootstrap.conf $C/bemaqui-empresas-homolog.conf
    nginx -t'
