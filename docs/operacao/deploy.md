# Runbook: deploy da homologação (FND-10, ADR 0015)

Para quem opera a VPS. Este runbook prepara a homologação **uma vez**; depois disso, a publicação automática liga com a variável `HOMOLOG_DEPLOY`. Desde a economia de minutos do Actions (21/10/2026), o CI roda só no PR pronto para revisão e não no push da `main`; com a variável ligada, dispare "Deploy homologação" à mão (workflow_dispatch). Enquanto ela estiver desligada, o deploy de homologação é feito pelo script local, que monta as imagens do commit da `main`, envia à VPS, roda o migrator e a fumaça.

Regras que valem em toda a operação:
- **VPS compartilhada:** não mexa nos containers, volumes e vhosts dos outros projetos (`bemaqui-atende`, `konebras`, `nshome`…). `app.konebras.com.br` continua com o Dolibarr até a GOL-01.
- **nginx:** `docker exec stack-nginx nginx -t` **antes** de todo `nginx -s reload`. Se o teste falhar, desfaça a cópia e não recarregue.
- **Nomes únicos:** na `stack_stack`, `postgres`, `redis`, `api` e `web` já são apelidos de outros projetos. Por isso a API e a web se chamam `bqe-homolog-api` e `bqe-homolog-web`, e o banco e o ClamAV são acessados pelo nome do container (`bqe-homolog-postgres`, `bqe-homolog-clamav`). Nunca use host genérico numa connection string.
- **Segredos:** nunca entram no repositório, em chat, em ticket ou em variável de ambiente. Cada um é um arquivo em `secrets/`, com chmod 0400.

## O que o PO providencia antes
1. **DNS** no Cloudflare, tipo A, DNS-only (nuvem cinza), apontando para `2.25.82.127`:
   - `homolog.konebras.com.br`;
   - `plataforma-homolog.bemaquiapp.com.br`.
2. **Bucket** S3 compatível. A recomendação é o Backblaze B2.
   - Dois buckets: `bqe-homolog-dumps` e `bqe-homolog-keyring`.
   - Em cada um, uma regra de ciclo de vida que apaga versões com mais de 30 dias.
   - Uma chave de aplicação por bucket, com `listBuckets`, `listFiles`, `readFiles` e `writeFiles`, **sem** `deleteFiles`.
3. **Chave do backup:** rode `age-keygen -o bqe-backup.key` na estação do PO.
   - A **chave privada** vai para o cofre de senhas e nunca vai para a VPS.
   - Só a linha `age1…` (a chave pública) vai para a VPS.
4. **Certificado do key ring:** gere o `dp-cert.pfx` e a senha conforme `segredos-e-chaves.md` e guarde a cópia offline.
5. **IPs liberados** para o painel da plataforma de homologação.

## Preparação da VPS (uma vez)
```bash
D=/opt/projetos/bemaqui-empresas-homolog
install -d -m 0755 $D $D/postgres && install -d -m 0700 $D/secrets
cp deploy/homolog/docker-compose.yml deploy/homolog/deploy.sh deploy/homolog/backup-cron.sh $D/
cp deploy/postgres/init-roles.sh $D/postgres/ && chmod 0755 $D/*.sh $D/postgres/init-roles.sh
cp deploy/homolog/.env.example $D/.env && chmod 0600 $D/.env   # ajuste hosts, STACK_SUBNET, alertas e SMTP
docker network inspect stack_stack --format '{{(index .IPAM.Config 0).Subnet}}'   # valor de STACK_SUBNET
```

### Secrets (`$D/secrets`, chmod 0400)
O dono de cada arquivo precisa ser o uid do container que o lê: api e migrator são `1654`, o postgres (alpine) é `70` e o backup é `10001`.

| Arquivo | Conteúdo | Dono |
|---|---|---|
| `pg-superuser-password` | senha do superuser `postgres` | 70 |
| `pg-catalog-password` / `pg-provisioner-password` | senhas dos roles `bemaqui_catalog` e `bemaqui_provisioner` | 70 |
| `catalog-connection` | `Host=bqe-homolog-postgres;Database=catalog;Username=bemaqui_catalog;Password=…` | 1654 |
| `provisioner-connection` | `Host=bqe-homolog-postgres;Database=postgres;Username=bemaqui_provisioner;Password=…` | 1654 |
| `auth-signing-key` | `openssl rand -base64 48` | 1654 |
| `smtp-password` | senha do SMTP | 1654 |
| `dp-cert.pfx` / `dp-cert-password` | certificado do key ring e a senha dele | 1654 |
| `backup-pg-password` | a mesma senha do superuser | 10001 |
| `backup-age-recipients` | a chave pública `age1…` do PO | 10001 |
| `dumps-s3-{endpoint,bucket,access-key,secret-key}` | bucket dos dumps (ex.: endpoint `https://s3.us-west-004.backblazeb2.com`) | 10001 |
| `keyring-s3-{endpoint,bucket,access-key,secret-key}` | bucket da trilha do key ring, com outra chave | 10001 |

Para gerar uma senha sem quebra de linha: `openssl rand -hex 24 | tr -d '\n' > arquivo`. Depois, ajuste dono e permissão: `chown 1654:1654 arquivo && chmod 0400 arquivo`.

### Imagens e primeira subida
```bash
echo "<PAT com read:packages>" | docker login ghcr.io -u johncpv --password-stdin   # só leitura de pacotes
cd $D
docker compose up -d postgres clamav            # cria os roles e o banco catalog (init-roles.sh)
ALLOW_KEY_RING_CREATION=true docker compose run --rm migrator   # só nesta vez: cria o key ring ("Key ring do Data Protection pronto")
docker compose run --rm migrator tenant provision --key konebras --legal-name "WSP Equipamentos por Elevação Ltda" \
  --document 47.225.005/0001-86 --host homolog.konebras.com.br --owner-email <e-mail do PO> --plan Piloto
docker compose up -d --no-deps --wait bqe-homolog-api bqe-homolog-web
docker compose run --rm migrator platform-admin create --email <e-mail do super admin>   # link aparece uma vez
```

### nginx e certificados
1. Copie `deploy/nginx/bemaqui-empresas-homolog.bootstrap.conf` para `/opt/infra/nginx/conf.d/bemaqui-empresas-homolog.conf`.
2. Rode `nginx -t` e depois o reload.
3. Emita os dois certificados pelo certbot existente, sempre no volume `stack_certbot-etc`, com webroot `/var/www/certbot`, para `homolog.konebras.com.br` e `plataforma-homolog.bemaquiapp.com.br`.
4. Troque o arquivo pelo `deploy/nginx/bemaqui-empresas-homolog.conf`.
5. Crie `/opt/infra/nginx/conf.d/bemaqui-empresas-homolog.allow` a partir do `.allow.example`, com os IPs liberados.
6. Rode `nginx -t` e depois o reload.
7. Confira com `sh scripts/smoke.sh homolog.konebras.com.br app.konebras.com.br`.

### Backup no cron do root
```
30 3 * * *  /opt/projetos/bemaqui-empresas-homolog/backup-cron.sh daily   >> /var/log/bqe-homolog-backup.log 2>&1
45 3 * * 0  /opt/projetos/bemaqui-empresas-homolog/backup-cron.sh keyring >> /var/log/bqe-homolog-backup.log 2>&1
```
Rode uma vez à mão (`./backup-cron.sh daily`) e liste o bucket. Depois, **tente apagar** um objeto com a chave do backup: o provedor precisa recusar (CA 12).

### Ligar o pipeline
1. Gere uma chave só para o deploy: `ssh-keygen -t ed25519 -f bqe-homolog-deploy -N ''`.
2. Na VPS, em `/root/.ssh/authorized_keys`, inclua uma linha com o comando restrito:
   `command="/opt/projetos/bemaqui-empresas-homolog/deploy.sh",restrict ssh-ed25519 AAAA… bqe-homolog-deploy`
3. No GitHub, crie o environment `homolog` com os secrets `HOMOLOG_SSH_KEY` (a chave privada) e `HOMOLOG_KNOWN_HOSTS` (saída de `ssh-keyscan -t ed25519 2.25.82.127`).
4. Crie a variável do repositório `HOMOLOG_DEPLOY=true`.

## Cada deploy (automático)
- **Imagens:** o `deploy-homolog.yml` roda depois do CI verde na `main`. Ele publica as imagens com a tag do SHA e chama `deploy.sh`.
- **Ordem no `deploy.sh`:** compose do commit, depois `pull`, depois `migrator`, depois `api` e `web` esperando `healthy`.
- **Fumaça:** `scripts/smoke.sh` confere `/health/ready`, o front, o tenant, a rota `/hubs` e o Dolibarr em `app.konebras.com.br`.
- **Migrator falhou:** o deploy termina com erro, a versão anterior fica no ar e o alerta `migration_failed` é enviado.
- **API nova sem saúde:** o `deploy.sh` volta para a tag anterior.

## Voltar uma versão à mão
```bash
cd /opt/projetos/bemaqui-empresas-homolog
sed -i 's/^IMAGE_TAG=.*/IMAGE_TAG=<sha anterior>/' .env && docker compose up -d --no-deps --wait bqe-homolog-api bqe-homolog-web
```
As migrations seguem o padrão expand/contract, então a versão anterior aceita o schema mais novo.
