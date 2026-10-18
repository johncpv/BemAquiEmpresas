# ADR 0015: Deploy em homologação, segredos em arquivo e backup cifrado

- Status: Aceito (18/10/2026). Os hosts e o provedor do bucket dependem da confirmação do PO (FND-10b).
- Contexto: a FND-10 deixou em aberto vários pontos: hosts, diretório na VPS, registry, storage externo, ferramenta de cifragem e se a story seria quebrada em duas. A inspeção (só leitura) da VPS em 18/10/2026 mostrou duas coisas:
  - os nomes `bemaqui-api`, `bemaqui-web` e `bemaqui-postgres` já são usados pelo projeto **bemaqui-atende**;
  - o `stack-nginx` (nginx 1.31) loga a URL completa, com a query string, no formato `main`.

## Decisão
- **Quebra:** a FND-10 é dividida em duas.
  - **FND-10a**, tudo o que fica no repositório: imagens, compose endurecido, vhost, workflows, backup, scripts e runbooks, validados localmente e no CI.
  - **FND-10b**, o que exige o PO e a VPS: DNS, certificados, secrets, bucket, primeiro deploy, monitor externo e o exercício de restauração com o backup real.
- **Hosts (proposta, confirmação do PO):**
  - tenant de homologação em `homolog.konebras.com.br`;
  - plataforma de homologação em `plataforma-homolog.bemaquiapp.com.br`, separada da plataforma de produção.
  - `app.konebras.com.br` não é tocado (ADR 0005 D1).
- **Nomes na VPS:**
  - diretório `/opt/projetos/bemaqui-empresas-homolog`;
  - projeto do compose `bqe-homolog`;
  - containers `bqe-homolog-*`, volumes `bqe-homolog-*` e o vhost `bemaqui-empresas-homolog.conf`, tudo prefixado para não colidir com o bemaqui-atende.
- **Nomes únicos na rede compartilhada** (adendo de 18/10/2026, no primeiro deploy):
  - O Docker registra o nome de cada serviço como apelido em todas as redes dele. Na `stack_stack`, `postgres` e `redis` são do bemaqui-atende, e `api` e `web` já são de vários projetos.
  - No primeiro deploy, a API resolveu `postgres` para o banco do bemaqui-atende. A senha foi recusada e nada foi lido ou gravado, mas isso mostrou o risco.
  - Regra: os serviços que entram na `stack_stack` se chamam `bqe-<ambiente>-api` e `bqe-<ambiente>-web`, e toda dependência é acessada pelo nome do container (`bqe-<ambiente>-postgres`, `bqe-<ambiente>-clamav`).
  - O `check-compose.mjs` reprova serviço com nome genérico na `stack_stack` e host genérico em `*__Host` ou `PGHOST`.
- **Imagens:** `ghcr.io/johncpv/bemaqui-empresas-{api,web,backup}`, com a tag do SHA do commit e a do ambiente.
  - O build usa `docker build` e `docker push` direto, sem actions de terceiros (a regra de actions fixadas por SHA continua simples).
  - **api:** a mesma imagem serve de API, de migrator (`migrate`) e de CLI. É alpine com ICU, tzdata e krb5, roda com uid 1654, healthcheck em `/health/live`.
  - **web:** `nginx-unprivileged` (uid 101), com os mesmos cabeçalhos de segurança da API.
- **Endurecimento:**
  - api, web, migrator e backup rodam não-root, com `read_only` + `tmpfs`, `cap_drop: ALL`, `no-new-privileges` e limites de CPU e memória;
  - o postgres fica só na rede `internal` (`internal: true`), sem `ports:`;
  - só web e api entram na `stack_stack`; o ClamAV e o backup saem para a internet pela rede `egress` do projeto;
  - `scripts/check-compose.mjs` reprova o CI se alguma dessas regras for quebrada.
- **Segredos:** cada segredo é um arquivo em `./secrets` (chmod 0400, dono = uid do container), montado como Docker secret.
  - O Host lê de `/run/secrets` todo arquivo com `__` no nome como chave de configuração (`ConnectionStrings__Catalog`, `Auth__SigningKey`, `Email__SmtpPassword`), via `AddSecretFiles`.
  - Nenhum segredo vai em variável de ambiente, porque ela aparece no `docker inspect`.
- **Deploy:** por SSH, com uma chave de **comando restrito** (`command="…/deploy.sh",restrict` no `authorized_keys`).
  - O workflow manda o SHA e, no stdin, a pasta `deploy/` do commit. O `deploy.sh` instala o compose, baixa as imagens e roda o migrator.
  - Se o migrator falhar, a API antiga continua no ar e o migrator envia o alerta.
  - Depois sobe api e web esperando `healthy`. Se a versão nova não ficar saudável, volta para a anterior.
  - O vhost do nginx **não** é mexido pelo pipeline: é instalado à mão, com `nginx -t` antes do reload.
- **Log do nginx:** o formato `bqe_masked` loga só o método, o caminho (`$uri`, sem query) e o Referer sem query.
  - Assim não vão para o log o token de convite e de redefinição, o `access_token` do SignalR nem o `Authorization`.
- **Backup:**
  - **Pipeline:** `pg_dump -Fc` por banco, mais `pg_dumpall --globals-only` (papéis), passando por `age` (chave pública na VPS, chave privada só com o PO, offline) e por `rclone rcat`, que envia ao S3 compatível. O dump nunca toca o disco em claro.
  - **Agenda:** o cron do root roda às 03:30 UTC todo dia e, aos domingos às 03:45, a trilha do key ring.
  - **Falha:** se o backup falhar, `alert raise --kind backup_failed` usa o caminho de alertas da FND-12.
  - **Provedor recomendado:** Backblaze B2, com chave de aplicação sem `deleteFiles` (CA 12) e regra de ciclo de vida de 30 dias. O key ring vai para outro bucket, com outra chave (CA 13).
  - **Credencial do Postgres:** o backup usa o superuser do Postgres do projeto, porque os bancos de tenant fazem `REVOKE CONNECT FROM PUBLIC`. Um role só de leitura exigiria `GRANT CONNECT` no provisionamento e fica como melhoria.
- **Produção:** o `deploy-prod.yml` (tag `v*`) está pronto e **desligado** (`vars.PROD_DEPLOY`) até a GOL-01. A homologação liga com `vars.HOMOLOG_DEPLOY=true`.

## Consequências
- O ensaio local de 18/10/2026 subiu a stack com segredos de teste e validou estes pontos:
  - migrator e provisionamento;
  - API saudável atrás do vhost;
  - log mascarado;
  - backup de 4 bancos e do key ring;
  - restauração de um tenant num ambiente separado em 24 s, com os segredos decifrando.
- O registro está em `docs/operacao/restauracao.md`. O exercício com o backup real da VPS faz parte da FND-10b.
- Os CAs 12 (credencial sem delete) e 16 (monitor externo) só podem ser provados no provedor real, na FND-10b.
