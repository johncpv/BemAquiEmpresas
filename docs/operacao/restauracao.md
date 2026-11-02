# Runbook: restauração de backup (FND-10 CA 15, ADR 0015)

O backup diário guarda, por dia, em `<bucket>/homolog/<AAAA-MM-DD>/`:
- `globals-*.sql.age`: os papéis do PostgreSQL;
- `catalog-*.dump.age`: catálogo, key ring e Hangfire;
- `tenant_<key>-*.dump.age`: um arquivo por tenant.

O key ring tem também uma trilha separada, em outro bucket: `keyring-*.dump.age`, semanal.

Tudo é cifrado com `age`. Para decifrar, são necessárias três peças:
- a **chave privada do backup**, que fica no cofre do PO e nunca vai para a VPS;
- o **`dp-cert.pfx` e a senha dele**, também no cofre, para que os segredos do key ring voltem a decifrar;
- uma credencial de leitura do bucket.

## Restaurar um tenant num ambiente separado
Faça numa máquina ou VPS de teste, nunca no PostgreSQL da homologação ou da produção.

```bash
# 1. Baixe o dia desejado (rclone com credencial de leitura) para um diretório de trabalho.
rclone copy dest:bqe-homolog-dumps/homolog/2026-10-18 ./restore

# 2. Suba um PostgreSQL 17 vazio, numa rede só dele.
docker network create bqe-restore
docker run -d --name bqe-restore-pg --network bqe-restore --network-alias postgres -e POSTGRES_PASSWORD=<temporária> postgres:17-alpine

# 3. Restaure papéis, catálogo e o tenant (a imagem do backup já tem psql, pg_restore e age).
docker run --rm --network bqe-restore -v "$PWD/restore:/dumps:ro" -v "$PWD/bqe-backup.key:/key:ro" \
  -e PGHOST=postgres -e PGUSER=postgres -e PGPASSWORD=<temporária> --user 0 --entrypoint sh \
  ghcr.io/johncpv/bemaqui-empresas-backup:homolog -c '
  set -e
  age -d -i /key /dumps/globals-*.sql.age | grep -v "^CREATE ROLE postgres;\|^ALTER ROLE postgres " | psql -q -v ON_ERROR_STOP=1 -d postgres
  psql -d postgres -c "CREATE DATABASE catalog OWNER bemaqui_catalog"
  age -d -i /key /dumps/catalog-*.dump.age | pg_restore --exit-on-error -d catalog
  psql -d postgres -c "CREATE DATABASE tenant_<key> OWNER tenant_<key>"
  age -d -i /key /dumps/tenant_<key>-*.dump.age | pg_restore --exit-on-error -d tenant_<key>'

# 4. Confira o key ring. Se a tabela estiver vazia, restaure o keyring-*.dump.age da trilha separada.
#    psql -d catalog -c "select count(*) from security.data_protection_keys"

# 5. Suba a API apontando para o banco restaurado, com o dp-cert.pfx e a senha da cópia offline e
#    AllowKeyRingCreation=false. Se ela subir e o tenant responder, os segredos decifram.
```

## Produção: banco e arquivos (GOL-01a)
Na produção o dia fica em `bqe-prod-dumps/prod/<AAAA-MM-DD>/` e traz, além dos dumps, **`files-*.tar.age`**: o volume `bqe-prod-files` inteiro (anexos, XML das notas, holerites, PDFs), com a mesma cifra e a mesma retenção de 30 dias. Restaurar só o banco deixa o sistema apontando para arquivos que não existem; o exercício de restauração (e o ensaio da GOL-01a CA 7) cobre os dois:

```bash
# Depois dos passos 1 a 3, com a chave privada de PRODUÇÃO (bqe-prod-backup.key) e o mesmo diretório ./restore:
docker volume create bqe-restore-files
docker run --rm -v "$PWD/restore:/dumps:ro" -v "$PWD/bqe-prod-backup.key:/key:ro" -v bqe-restore-files:/data/files \
  --user 0 --entrypoint sh ghcr.io/johncpv/prod/bemaqui-empresas-backup:<sha> -c '
  set -e
  age -d -i /key /dumps/files-*.tar.age | tar -C /data/files -xf -
  chown -R 1654:1654 /data/files
  find /data/files -type f | wc -l'
# Conferência: o número de arquivos bate com o da origem (docker run --rm -v bqe-prod-files:/f:ro alpine find /f -type f | wc -l, no
# mesmo dia) e um anexo, um XML de nota e um holerite abrem pela API restaurada (montada com -v bqe-restore-files:/data/files).
```
- O volume e a rede do ensaio (`bqe-restore-*`) são apagados no fim; nunca restaure por cima do `bqe-prod-files` sem ADR e janela com o PO.
- Os dumps antes de cada deploy (`/opt/projetos/bemaqui-empresas/predeploy-dumps/`, só banco, 5 últimos por banco) servem para voltar uma migração; não substituem este backup.

- **Anonimizações do titular (LGPD-01b, ADR 0037 §2):** o backup não é editado; antes de devolver o banco restaurado ao uso, **reaplique os pedidos de anonimização concluídos depois da data do backup**. Liste-os no banco vivo (`select number, subject_type, subject_id, anonymized_at from settings.privacy_requests where anonymization_status = 'Done' and anonymized_at > '<data do backup>'`) e, no ambiente restaurado, refaça cada um em Configurações → Privacidade (o pedido, a identidade conferida e "Anonimizar", com o mesmo titular). Sem isso, o dado de quem pediu a anonimização volta ao banco. Os backups expiram em até 30 dias (FND-10b).
- **Outros tenants:** os que não foram restaurados aparecem no log com "database does not exist". Fica restrito a eles; o tenant restaurado funciona normalmente.
- **Banco de produção:** para trocar o banco de **um** tenant no PostgreSQL de produção, siga os mesmos passos com outro nome de banco e troque só depois de conferir. Isso exige ADR e janela combinada com o PO.

## Registro dos exercícios

| Data | Ambiente | Origem do backup | Escopo | Duração | Resultado |
|---|---|---|---|---|---|
| 18/10/2026 | Ensaio local (Docker Desktop), stack de homologação com segredos de teste | Backup do próprio ensaio, cifrado com `age`, enviado a um S3 local (`rclone serve s3`) | Papéis, catálogo e 1 de 2 tenants (`tenant_konebras`) | 24 s até os dados; cerca de 15 s até a API responder | OK. O tenant resolveu pelo host, os segredos decifraram com o certificado e `/health/ready` deu `Healthy`. O tenant não restaurado ficou só com erro de conexão no log, isolado. |
| — | Homologação (FND-10b) | Backup real do bucket | 1 tenant | — | Pendente |
| — | Ensaio da produção (GOL-01a CA 7) | Backup real de produção (B2 `bqe-prod-dumps`) | Papéis, catálogo, `tenant_konebras` e `files-*.tar.age` | — | Pendente |
