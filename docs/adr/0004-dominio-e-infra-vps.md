# ADR 0004 — Nome do produto, domínio do tenant e infraestrutura na VPS compartilhada

- Status: Aceito (16/10/2026) — decisão do PO (John)
- Resolve a pendência P-09.

## Decisão
1. **Nome do produto:** BemAqui Empresas.
2. **Domínio do tenant piloto:** `https://app.konebras.com.br`, o domínio próprio do cliente.
3. **Resolução de tenant pelo host completo**, não só pelo subdomínio:
   - nova tabela `catalog.tenant_domains` (`tenant_id`, `host`, `is_primary`, `verified_at`), com host único;
   - o middleware procura o host da requisição em `tenant_domains`. Host desconhecido → 404;
   - tenants futuros podem usar o próprio domínio (como a Konebras) ou um subdomínio do domínio da plataforma, a definir quando houver o segundo tenant. Os dois casos são só linhas em `tenant_domains`.
4. **Infraestrutura:** a VPS Hostinger da Konebras (2.25.82.127, acesso descrito em `D:\GIT\Konebras\CLAUDE.md`) é compartilhada com outros projetos e já tem um `stack-nginx` com Let's Encrypt (certbot, volume `stack_certbot-etc`) na rede Docker `stack_stack`. **Não usar o Caddy.** O BemAqui Empresas entra como mais um projeto:
   - compose próprio em `/opt/projetos/bemaqui-empresas/` (api, web e postgres 17 próprio), ligado à rede externa `stack_stack`;
   - virtual host `app.konebras.com.br` no `stack-nginx` (`/opt/infra/nginx/conf.d/`), com o padrão `resolver 127.0.0.11` + `set $upstream`, `nginx -t` antes do reload, e WebSocket liberado para o SignalR;
   - um certificado por host, emitido pelo certbot no volume existente. Não há wildcard.
5. **Substituição do sistema atual:** o BemAqui Empresas assume `app.konebras.com.br`, que hoje serve o Dolibarr. A troca acontece na FND-10.
   - O Dolibarr passa para **`adm.konebras.com.br`** e continua no ar até a migração (MIG-01) e o go-live (GOL-01). Só é desligado no fim da Fase 5, conforme o PRD.
   - Na troca, ajustar no Dolibarr a URL principal (`$dolibarr_main_url_root`) e a `DOLIBARR__BASEURL` do Konebras UI.
   - O Konebras UI (`erp.konebras.com.br`, um BFF sobre o Dolibarr) é desativado junto com o Dolibarr.

## Motivo
O cliente quer o sistema no domínio dele. A VPS já roda vários projetos atrás do nginx compartilhado, e um segundo proxy de borda (Caddy) disputaria as portas 80 e 443.

## Consequências
- O `docs/02-arquitetura.md` §2 e §7 foram atualizados.
- O teste de isolamento da FND-02 usa dois hosts distintos (ex.: `a.localhost` e `b.localhost`).
- Pré-requisitos da FND-10:
  - (a) registro A `adm.konebras.com.br` → 2.25.82.127 no Cloudflare, DNS-only (nuvem cinza), criado pelo PO;
  - (b) certificado para `adm.konebras.com.br`;
  - (c) backup do Dolibarr e do MariaDB antes da troca.
