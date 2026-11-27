# ADR 0051: Ambiente SaaS multi-tenant e modelo híbrido de instalação

- Status: Aceito (25/11/2026), decisão do PO (John). Complementa as ADRs 0004 (infra na VPS compartilhada), 0020 (produção e promoção) e 0034 (ambiente de produção novo, runbook e inventário de segredos); reaproveita o endurecimento da ADR 0015.
- Contexto: o produto é multi-tenant desde a FND-03a (banco por tenant e catálogo próprio), mas hoje só existe uma stack, a da Konebras (`/opt/projetos/bemaqui-empresas-homolog`, containers `bqe-homolog-*`, que virou produção em 02/11/2026). O BemAqui precisa de um lugar para (a) gerir o próprio ecossistema com o produto (primeiro tenant) e (b) receber clientes novos sem montar uma stack por cliente, sem tocar na da Konebras e sem misturar catálogo, key ring ou segredos.

## Decisão

### 1. Modelo híbrido
- **Padrão: ambiente SaaS multi-tenant** do BemAqui, com stack e PostgreSQL **próprios**, na mesma VPS compartilhada (2.25.82.127), atrás do `stack-nginx` e do certbot existentes. Cada cliente novo é um tenant com **banco isolado** (banco por tenant, como já é) e entra pelo painel de plataforma.
- **Opção: ambiente dedicado por cliente** (VPS nossa ou do cliente), como a Konebras hoje. Migrar entre os dois modelos é **mover o banco do tenant** (`pg_dump`/`pg_restore` do `tenant_<chave>`, nova linha em `catalog.tenants` e `tenant_domains` no destino) e **recifrar os segredos do tenant** do key ring de origem para o do destino (a mesma recifração da ADR 0034 §2, por ferramenta do runbook), porque cada ambiente tem key ring e certificado do Data Protection próprios.
- **A Konebras segue na stack dela**, sem mudança. **Mesma versão (SHA) nos dois ambientes**; o deploy vai **primeiro ao SaaS** e, com a fumaça verde, **depois à Konebras**.

### 2. O ambiente SaaS (`deploy/saas/`)
- Diretório `/opt/projetos/bemaqui-saas/`; projeto compose **`bqe-saas`**; containers `bqe-saas-postgres`, `-clamav`, `-migrator`, `-api`, `-fiscal-pdf-reader`, `-web` e `-backup`; rede interna e volumes `bqe-saas-pgdata`, `-files`, `-clamdb` e `-pdf-socket`; secrets em `./secrets`. Nada é compartilhado com `bqe-homolog-*` (nem volume, nem secret, nem imagem: o `REGISTRY` do SaaS tem caminho próprio).
- **Catálogo novo e vazio; key ring e certificado do Data Protection novos; senhas, chave de assinatura do JWT e SMTP novos.** Nada é copiado da stack da Konebras.
- Mesmo endurecimento da produção (ADR 0015 e 0034): todo serviço não-root, `read_only`, `cap_drop: ALL`, `no-new-privileges`, limites de CPU, memória e processos; sem `ports:`; só `bqe-saas-api` e `bqe-saas-web` na `stack_stack`; `${VAR:?}` nas variáveis obrigatórias; sem `importer` e sem o secret do Dolibarr. O `scripts/check-compose.mjs` passa a cobrar as mesmas regras do projeto `bqe-saas` e o CI valida o compose e os vhosts.
- **Portas:** nenhuma porta é publicada no host (como na stack da Konebras). O `stack-nginx` alcança `bqe-saas-api:8080` e `bqe-saas-web:8080` pela `stack_stack`; os nomes dos containers são únicos na rede (a Konebras usa `bqe-homolog-*:8080`), então não há colisão de apelido nem de porta. Nada de `127.0.0.1:<porta>` é necessário.
- **Primeiro tenant = `bemaqui`** (o próprio BemAqui, gestão do ecossistema), provisionado pelo painel de plataforma (`tenant provision` pela CLI só no bootstrap, se o painel ainda não existir no momento). Clientes novos: pelo painel, um por vez.

### 3. Deploy
- `scripts/deploy-saas.sh` (mesmo padrão do `deploy-homolog.sh`): build local, envio por `docker save | docker load`, **dump antes** do `catalog` e de **todo** `tenant_*`, conferido com `pg_restore -l`, compose do commit validado com o `.env` e os secrets da VPS, `migrator` (o `TenantMigrator` em todos os tenants), `up --wait` e fumaça; falhou, volta a tag e o compose. `deploy/saas/deploy.sh` é a versão da VPS para um pipeline futuro (comando restrito de SSH), igual ao da produção.
- Migrations seguem expand/contract (ADR 0006): a versão anterior aceita o schema novo. Voltar o banco é manual, pelo dump, com o PO (`restauracao.md`).

### 4. DNS, vhosts e certificados
- **Domínios ainda não definidos.** Os templates em `deploy/saas/nginx/` usam os placeholders **`__PLATFORM_HOST__`** (painel de plataforma e super admin, Hangfire e `/webhooks/`) e **`__APP_HOST__`** (ERP do tenant `bemaqui`); cada cliente novo usa `__TENANT_HOST__` no `bemaqui-saas-tenant.conf.template`. A troca é por `sed` na instalação (runbook), e o CI valida os templates com hosts de teste.
- **Cada cliente novo precisa de** um registro **A, DNS-only** (nuvem cinza no Cloudflare) para 2.25.82.127, um **certificado** (certbot webroot, `/var/www/certbot`, volume `stack_certbot-etc`), um vhost a partir do template e o host em `catalog.tenant_domains` (domínio próprio do cliente continua aceito, ADR 0004).
- **Opção futura, não implementada:** curinga `*.<domínio>` com certbot DNS-01 via Cloudflare (um certificado e um vhost para todos os subdomínios). Fica registrada; só entra com decisão própria (exige token da API do Cloudflare com escopo de zona, a guardar como segredo).
- Painel da plataforma só para IPs da allowlist (ADR 0010 item 7); `/webhooks/` fora dela, só no host da plataforma (ADR 0031). `nginx -t` antes de todo reload; os formatos de log vêm do `00-bemaqui-empresas-common.conf` já instalado (não duplicar).

### 5. Backup
- **O backup externo (B2) fica DESLIGADO no início**: o ambiente começa vazio. O serviço `backup` existe no compose (profile `backup`), mas sem cron e sem os secrets do B2. Vale só o **dump antes do deploy** (`catalog` e todos os `tenant_*`, 0600, em `predeploy-dumps/`, os 5 últimos de cada banco).
- **Liga ANTES do primeiro cliente com dado real** (inclusive o tenant `bemaqui`, quando passar a ter dado que não se queira perder): buckets e chaves B2 próprios do SaaS (sem `deleteFiles`), chave `age` própria, cron do `backup-cron.sh` e um ensaio de restauração (`restauracao.md`). Item de checklist da primeira venda; o runbook repete o aviso.

### 6. Fora de escopo desta ADR
Preços, planos e cobrança dos clientes do SaaS; contrato e LGPD (papéis de controlador e operador); monitoramento específico (usa `monitoramento.md`); definição dos domínios. Cada um vira ADR ou story quando decidido (registrar em `docs/06-pendencias-riscos.md` se virar pendência).

## Consequências
- Novo diretório `deploy/saas/` (compose, `deploy.sh`, `backup-cron.sh`, `.env.example`, vhosts em template) e `scripts/deploy-saas.sh`; `check-compose.mjs`, `check-nginx.sh` e o CI cobrem o ambiente; `docs/operacao/deploy.md` ganha a seção "Ambiente SaaS" (preparação única da VPS, super admin, tenant `bemaqui`, ordem SaaS → Konebras).
- A stack da Konebras não muda. Todo deploy futuro passa a ter duas etapas na ordem SaaS → Konebras, com o mesmo SHA.
- O ambiente SaaS usa a mesma VPS da Konebras: um incidente de disco, memória ou rede da VPS atinge os dois. Aceito por enquanto; o ambiente dedicado em outra VPS é a saída (item 1).

## Alternativas consideradas
- **Colocar os clientes novos na stack da Konebras:** rejeitada; mistura catálogo, key ring e segredos de uma operação real com os de clientes, e o deploy da Konebras ficaria preso ao dos outros.
- **Uma stack dedicada por cliente como padrão:** rejeitada como padrão (custo operacional por cliente, deploy N vezes); continua como opção.
- **Curinga com DNS-01 desde já:** adiada (ver item 4).

## Adendo de 26/11/2026: endereços (decisão dos sócios)

- Cada subdomínio é uma aplicação. A raiz `bemaquiapp.com.br` será o institucional (hoje é o BemAqui Empregos). Até lá, o institucional provisório fica em `solucoes.bemaquiapp.com.br` ([deploy/site-solucoes](../../deploy/site-solucoes/README.md)), com uma página e as políticas de cada produto.
- ERP: `plataforma.bemaquiapp.com.br` (painel), `erp.bemaquiapp.com.br` (ERP do próprio BemAqui, mantido), **`<empresa>.erp.bemaquiapp.com.br`** para cada cliente do ambiente SaaS (registro curinga `*.erp` criado; o certificado continua por host, emitido no provisionamento) e domínio próprio do cliente como opção.
- Fiscal: `fiscal.` (gestão) e `notas.` (portal do cliente). Conecta: `conecta.`. Integrações: `integracoes.` (retorno de login e avisos dos marketplaces e do Conecta).
