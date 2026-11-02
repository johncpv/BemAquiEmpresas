# Runbook: deploy da homologação (FND-10, ADR 0015)

Para quem opera a VPS. Este runbook prepara a homologação **uma vez**; depois disso, a publicação pelo Actions liga com a variável `HOMOLOG_DEPLOY`. Desde a economia de minutos do Actions (21/10/2026), o CI roda só no PR pronto para revisão e não no push da `main`; com a variável ligada, dispare "Deploy homologação" à mão (workflow_dispatch). Enquanto ela estiver desligada, o deploy de homologação é feito pelo script local, que monta as imagens do commit da `main`, envia à VPS, roda o migrator e a fumaça.

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
cp deploy/homolog/.env.example $D/.env && chmod 0600 $D/.env   # ajuste hosts, alertas e SMTP
```

**Proxy confiável (D19):** a API só aceita `X-Forwarded-For`/`X-Forwarded-Proto` do container `stack-nginx` (`ReverseProxy__KnownProxyHosts__0` no compose). O nome é resolvido na subida e o log mostra o IP (`Proxy confiável stack-nginx resolvido para …`). Se o `stack-nginx` for recriado com outro IP, a API resolve de novo sozinha. Não use a sub-rede da `stack_stack`: outros projetos nessa rede poderiam forjar o IP do cliente. A variável `STACK_SUBNET` do `.env` não é mais usada.

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
4. Crie as variáveis do repositório `HOMOLOG_SSH_HOST` (IP da VPS) e `HOMOLOG_DEPLOY=true`. Na produção, `PROD_SSH_HOST` e `PROD_DEPLOY`.

## Cada deploy (manual)
- **Imagens:** o `deploy-homolog.yml` roda à mão (workflow_dispatch) na `main`, depois do CI verde do PR. Ele publica as imagens com a tag do SHA e chama `deploy.sh`.
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

# Produção (GOL-01a, ADR 0034)

Ambiente **novo**, em `/opt/projetos/bemaqui-empresas/`, projeto `bqe-prod`: nada é copiado da homologação (nem secret, nem key ring, nem volume). O único dado que passa é o banco do `konebras`, restaurado e recifrado pelo runbook da virada (`go-live.md`). Arquivos no repositório:

| Arquivo | Para que |
|---|---|
| `deploy/prod/docker-compose.yml` | Compose `bqe-prod-*`. Todo container não-root, `read_only`, `cap_drop: ALL`, `no-new-privileges`, com limite de CPU, memória e processos, inclusive o postgres (uid 70) e o clamav (uid 100), ensaiados assim em 02/11/2026. Postgres só na rede `internal`, sem `ports:`. Sem `importer` e sem `dolibarr-api`. O `check-compose.mjs` cobra tudo isso no CI quando o projeto é `bqe-prod` |
| `deploy/prod/.env.example` | Só nomes, sem valor (o compose recusa subir com variável obrigatória vazia), mais a lista dos arquivos de `secrets/` |
| `deploy/prod/deploy.sh` | Comando restrito da chave de deploy de produção (ordem abaixo) |
| `deploy/prod/backup-cron.sh` | Backup diário e trilha semanal do key ring, no cron, fora do horário da homologação |
| `deploy/nginx/00-bemaqui-empresas-common.conf` | `log_format bqe_masked` e os maps `$bqe_*`, **comuns** à homologação, à produção e ao Dolibarr (o mesmo nome em dois arquivos derruba o `nginx -t`; o prefixo `00-` faz o arquivo ser lido antes dos que usam o formato) |
| `deploy/prod/nginx/bemaqui-empresas.conf` | `app.konebras.com.br` (público) e `plataforma.bemaquiapp.com.br` (painel com allowlist, `/webhooks/` fora dela) |
| `deploy/prod/nginx/bemaqui-empresas.bootstrap.conf` | Só o ACME da plataforma e do `adm.`, antes da virada |
| `deploy/prod/nginx/adm-dolibarr.conf` | Dolibarr em `adm.konebras.com.br`, só leitura, allowlist de IP **ou** senha básica (`satisfy any`), API REST e instalador fechados |
| `.github/workflows/deploy-prod.yml` | Tag `v*` de commit da `main`, desligado por `vars.PROD_DEPLOY`, environment `production` com aprovação, secrets do environment, imagens em `ghcr.io/<dono>/prod/*:<sha>` |

**Decisões:**
- **Allowlist:** o host do tenant é público, como na homologação (os usuários entram de qualquer rede). A allowlist vale só para o painel da plataforma (super admin e Hangfire, ADR 0005 D2), com lista própria (`bemaqui-empresas.allow`), e nunca para `/webhooks/`. No host do tenant, `/webhooks/` dá 404 no próprio nginx (o `tenantKey` só vale na plataforma, ADR 0031).
- **Cabeçalhos de segurança:** vêm dos upstreams (API e `deploy/web/nginx.conf`), como na homologação; o vhost só tira a versão do nginx (repetir os cabeçalhos duplicaria a resposta). A fumaça confere o HSTS.
- **Registry só da produção** (`REGISTRY=ghcr.io/johncpv/prod`): a imagem web da homologação pode ter a galeria de moldes; com o mesmo nome e a mesma tag `<sha>`, uma sobrescreveria a outra no GHCR e no Docker da VPS.
- **Login do GHCR separado:** o `deploy.sh` e o `backup-cron.sh` usam `DOCKER_CONFIG=/opt/projetos/bemaqui-empresas/.docker`, com um token só de leitura da produção; revogar o da homologação não derruba a produção e vice-versa.
- **Backup:** buckets B2 só da produção, chaves sem `deleteFiles`, retenção de **30 dias** pelo ciclo de vida (a mesma da ADR 0015, e o prazo em que as anonimizações da LGPD-01b precisam ser reaplicadas numa restauração, `restauracao.md`). O diário leva também o **volume de arquivos** (`bqe-prod-files`: anexos, XML das notas, holerites, PDFs), montado só leitura no backup e cifrado com `age`.
- **Dumps antes do deploy:** em `predeploy-dumps/` (0700), cada arquivo 0600 e conferido com `pg_restore -l`; ficam os 5 últimos de cada banco (é dado real em claro no disco: não aumente sem motivo).

## O que o PO providencia antes (produção)
1. **DNS** (Cloudflare, A, DNS-only, `2.25.82.127`): `plataforma.bemaquiapp.com.br` e `adm.konebras.com.br`. O `app.konebras.com.br` já existe.
2. **B2:** buckets `bqe-prod-dumps` e `bqe-prod-keyring`, cada um com ciclo de vida de 30 dias (`daysFromUploadingToHiding: 30`, `daysFromHidingToDeleting: 1`) e **uma chave de aplicação por bucket**, restrita ao bucket, com `listBuckets`, `listFiles`, `readFiles` e `writeFiles` e **sem** `deleteFiles`. Nenhuma chave da homologação.
3. **Chave `age` de produção** (`age-keygen -o bqe-prod-backup.key` na estação do PO; a privada só no cofre).
4. **Certificado do key ring de produção** (`segredos-e-chaves.md`, "Produção"), com cópia no cofre.
5. **Token do GHCR** só de leitura para a produção (PAT clássico só com `read:packages`, nome "bqe-prod-vps").
6. **IPs liberados** do painel da plataforma de produção e do Dolibarr em `adm.`; usuário e senha da autenticação básica do `adm.`.

## Preparação da VPS (uma vez, antes do ensaio)
```bash
D=/opt/projetos/bemaqui-empresas
install -d -m 0755 $D $D/postgres && install -d -m 0700 $D/secrets $D/.docker $D/predeploy-dumps
cp deploy/prod/docker-compose.yml deploy/prod/deploy.sh deploy/prod/backup-cron.sh $D/
cp deploy/postgres/init-roles.sh $D/postgres/ && chmod 0755 $D/*.sh $D/postgres/init-roles.sh
cp deploy/prod/.env.example $D/.env && chmod 0600 $D/.env   # preencha; TENANT_HOST vazio até a virada
# secrets: segredos-e-chaves.md, seção "Produção" (tabela e comandos); depois confira:
find $D/secrets -type f ! -perm 0400 -print        # não pode listar nada
DOCKER_CONFIG=$D/.docker docker compose version    # o plugin do compose precisa ser achado com o DOCKER_CONFIG próprio
echo "<PAT bqe-prod-vps>" | DOCKER_CONFIG=$D/.docker docker login ghcr.io -u johncpv --password-stdin
cd $D && docker compose --profile backup config -q
```

## nginx da produção
1. **Comum + homologação, primeiro e juntos:** copie `deploy/nginx/00-bemaqui-empresas-common.conf` e o novo `deploy/nginx/bemaqui-empresas-homolog.conf` para `/opt/infra/nginx/conf.d/` (guarde o anterior fora do `conf.d`), `docker exec stack-nginx nginx -t` e reload. O vhost novo da homologação também traz a location `/webhooks/` fora da allowlist: em 02/11/2026, `POST https://plataforma-homolog.bemaquiapp.com.br/webhooks/emissor-fiscal/…` respondia **405 do front**, ou seja, a location do `emissor-fiscal.md` §2 não estava na VPS. Depois do reload, o mesmo `curl` de um IP fora da allowlist tem de dar 401.
2. **Bootstrap:** `deploy/prod/nginx/bemaqui-empresas.bootstrap.conf` como `conf.d/bemaqui-empresas.conf`, `nginx -t`, reload, e emita pelo certbot existente (volume `stack_certbot-etc`, webroot `/var/www/certbot`) os certificados de `plataforma.bemaquiapp.com.br` e `adm.konebras.com.br`.
3. **Na virada (GOL-01b), num reload só:** mova o vhost atual do Dolibarr (`app.konebras.com.br`) para fora do `conf.d` (é o arquivo do rollback), troque o bootstrap pelo `deploy/prod/nginx/bemaqui-empresas.conf` e instale `adm-dolibarr.conf` (com o upstream do Dolibarr copiado do vhost antigo no lugar de `CONFIRMAR-container-do-dolibarr`), `bemaqui-empresas.allow`, `adm-dolibarr.allow` e `adm-dolibarr.htpasswd` (`htpasswd -B -c adm-dolibarr.htpasswd <usuário>`, chmod 0640). `nginx -t` e reload; se o teste falhar, desfaça a cópia e não recarregue.
4. Confira: `SMOKE_PLATFORM_HOST=plataforma.bemaquiapp.com.br SMOKE_ADM_HOST=adm.konebras.com.br sh scripts/smoke.sh app.konebras.com.br`.

Ensaio local dos vhosts: `sh scripts/check-nginx.sh` (também no CI) faz o `nginx -t` antes e depois da virada, com homologação, produção e Dolibarr juntos.

## Primeira subida (ensaio e virada)
```bash
cd /opt/projetos/bemaqui-empresas
docker compose up -d postgres clamav               # cria os roles e o banco catalog (init-roles.sh)
ALLOW_KEY_RING_CREATION=true docker compose run --rm migrator   # só nesta vez: key ring NOVO ("Key ring do Data Protection pronto")
# promoção do tenant konebras e recifração: go-live.md (nunca "tenant provision" do zero com dado real)
docker compose up -d --no-deps --wait fiscal-pdf-reader bqe-prod-api bqe-prod-web
docker compose run --rm migrator platform-admin create --email <e-mail do super admin>
```

## Backup no cron do root (produção)
```
15 4 * * *  /opt/projetos/bemaqui-empresas/backup-cron.sh daily   >> /var/log/bqe-prod-backup.log 2>&1
40 4 * * 0  /opt/projetos/bemaqui-empresas/backup-cron.sh keyring >> /var/log/bqe-prod-backup.log 2>&1
```
Rode uma vez à mão, liste o bucket (`prod/<dia>/` com `globals`, `catalog`, `tenant_konebras` e `files`) e **tente apagar** um objeto com a chave do backup: o B2 tem de recusar.

## Ligar o pipeline de produção
1. Chave só de deploy de produção: `ssh-keygen -t ed25519 -f bqe-prod-deploy -N '' -C bqe-prod-deploy` (nunca a da homologação).
2. Em `/root/.ssh/authorized_keys`: `command="/opt/projetos/bemaqui-empresas/deploy.sh",restrict ssh-ed25519 AAAA… bqe-prod-deploy`.
3. GitHub → Settings → Environments → `production`: **Required reviewers** = PO, **Prevent self-review** ligado, **Deployment branches and tags** = só tags `v*`; secrets do **environment**: `PROD_SSH_KEY` (privada da `bqe-prod-deploy`) e `PROD_KNOWN_HOSTS` (`ssh-keyscan -t ed25519 2.25.82.127`). Não crie secrets `PROD_*` no nível do repositório.
4. Variáveis do repositório: `PROD_SSH_HOST` e, **só na virada**, `PROD_DEPLOY=true`. Proteja as tags `v*` (Settings → Rules → Tag rulesets: só o PO cria).
5. Publicar: `git tag vAAAA.MM.DD <sha da main> && git push origin vAAAA.MM.DD`; aprovar o deploy no Actions.

## Cada deploy de produção (`deploy.sh`)
1. Confere o compose do commit com o `.env` e os secrets da VPS (`docker compose config`); inválido, nada muda.
2. Troca o `IMAGE_TAG` e baixa as imagens do SHA; falhou, nada muda.
3. `pg_dump -Fc` do `catalog` e de cada `tenant_*` em `predeploy-dumps/`, `chmod 0600`, conferido com `pg_restore -l`; falhou, nada muda.
4. `docker compose run --rm migrator`; falhou, volta o compose e a tag (a versão anterior continua no ar, o migrator manda o alerta).
5. `up -d --no-deps --wait` de `fiscal-pdf-reader`, `bqe-prod-api` e `bqe-prod-web`; sem saúde, volta para a tag e o compose anteriores.
6. Fumaça local pelo stack-nginx (`/health/ready` e `tenant-context` em `TENANT_HOST`); falhou, volta como no passo 5.

Depois, o workflow roda `scripts/smoke.sh` de fora. Voltar o **banco** é manual, pelo dump do passo 3 (`restauracao.md`), só com o PO.

## Monitor externo
UptimeRobot em `https://app.konebras.com.br/health/ready` (5 min, alerta em 2 falhas), como em `monitoramento.md`, ligado na virada.
