#!/bin/sh
# Roda uma vez, na criação do volume do PostgreSQL (docker-entrypoint-initdb.d). Cria:
#   bemaqui_catalog     -> dono do banco "catalog" (catálogo, key ring, Hangfire); sem superuser
#   bemaqui_provisioner -> CREATEDB + CREATEROLE, sem superuser (FND-03a): cria o role e o banco de cada tenant
# As senhas vêm dos secrets do Docker; nada aqui vai para o log.
set -eu
catalog_password="$(cat /run/secrets/pg-catalog-password)"
provisioner_password="$(cat /run/secrets/pg-provisioner-password)"
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v catalog_password="$catalog_password" -v provisioner_password="$provisioner_password" <<'SQL'
CREATE ROLE bemaqui_catalog LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD :'catalog_password';
CREATE ROLE bemaqui_provisioner LOGIN NOSUPERUSER CREATEDB CREATEROLE PASSWORD :'provisioner_password';
CREATE DATABASE catalog OWNER bemaqui_catalog;
REVOKE CONNECT ON DATABASE catalog FROM PUBLIC;
GRANT CONNECT ON DATABASE catalog TO bemaqui_catalog;
SQL
