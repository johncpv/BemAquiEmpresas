# FND-02 — Catálogo e resolução de tenant pelo host

| Campo | Valor |
|---|---|
| ID | FND-02 |
| Título | Catálogo, `tenant_domains`, middleware por host, `TenantDbContext` dinâmico e endurecimento HTTP |
| Tamanho | G |
| Status | Done (02a #6, 02b; página do front na FND-09b) |
| Requisitos | R-PLT-1 · R-PLT-2 · PRD §6 Segurança (HTTPS, OWASP) |
| Dependências | FND-01, FND-11, FND-07; a página "Acesso suspenso" usa os tokens da FND-09a |
| Bloqueia | FND-03a, FND-12, FND-04 e todas as stories com dados de tenant |
| Ordem de execução | 4 |

## User story
Como **proprietário da plataforma (PO)**, quero **que cada requisição seja ligada ao tenant certo pelo host e use só o banco desse tenant**, para **que os dados de um cliente nunca apareçam para outro, mesmo com erro de programação**.

## Contexto e referências
- ADR 0001 (banco por tenant), **alterada pela ADR 0004** (resolução pelo host completo, `catalog.tenant_domains`).
- ADR 0005 D2: o host da plataforma `plataforma.bemaquiapp.com.br` fica **fora** de `tenant_domains`.
- `docs/02-arquitetura.md` §2.1 (cache de 5 min, `ITenantContext`, webhooks e jobs com tenant explícito, grupos SignalR `{tenantId}:{painel}`), §3 (um `TenantDbContext` com configurações por módulo e schemas por módulo), §5.
- `docs/03-modelo-dados.md` → `catalog.tenants`, `plans`.
- Revisão §2.1 (atualizar o modelo: `tenant_domains`; remover `kiosk_token_index`; marcar a 0001 como alterada), §2.2 (ADR 0006: interface de dados por módulo, configurações `internal`), §2.3 (CAs de segurança da FND-02).

## Critérios de aceite
**Resolução e isolamento**
1. **Dado** dois tenants de teste com hosts `a.localhost` e `b.localhost`, cada um com seu banco, **quando** gravo um registro pelo host A e listo pelo host B, **então** B não vê o registro.
2. **Dado** um host que não está em `catalog.tenant_domains` nem é o host da plataforma, **quando** faço qualquer requisição, **então** recebo 404 sem revelar se o tenant existe.
3. **Dado** um tenant com status `Suspended`, **quando** acesso pelo host dele, **então** a API responde 403 `code: "tenant_suspended"` e o front mostra a página amigável "Acesso suspenso" com orientação de contato.
4. **[Segurança]** **Dado** um tenant ativo, **quando** o super admin o suspende, **então** em menos de 10 s todas as requisições desse tenant passam a receber 403, mesmo com o cache de 5 min.
5. **Dado** o host `plataforma.bemaquiapp.com.br` (configurado, fora de `tenant_domains`), **quando** chamo uma rota de tenant (`/api/v1/...`), **então** recebo 404.
6. **[Segurança]** **Dado** um token emitido para o tenant em `a.localhost` (claim `tid` de A e `aud` do host de A), **quando** o uso em `b.localhost`, no REST e na conexão do hub SignalR, **então** ele é rejeitado com 401.
7. **[Segurança]** **Dado** a credencial do banco do tenant A, **quando** tento conectar no banco do tenant B, **então** a conexão é recusada (role por tenant; o `REVOKE CONNECT FROM PUBLIC` é aplicado no provisionamento, FND-03a, e simulado aqui pelo fixture de teste).
8. **Dado** um job ou um handler de outbox de um tenant, **quando** roda, **então** usa o `TenantDbContext` do `tenantId` recebido, nunca o do request.

**Endurecimento HTTP**
9. **[Segurança]** **Dado** uma requisição feita direto no container da API com `Host` e `X-Forwarded-For` forjados, **quando** ela não vem do proxy confiável (`stack-nginx` na rede `stack_stack`), **então** os cabeçalhos encaminhados são ignorados e o host forjado não resolve tenant.
10. **[Segurança]** **Dado** uma requisição com `Origin` de outro site, **quando** chamo a API, **então** não há cabeçalho `Access-Control-Allow-Origin` na resposta (mesma origem, sem CORS).
11. **[Segurança]** **Dado** qualquer resposta HTML ou da API, **quando** inspeciono os cabeçalhos, **então** há `Strict-Transport-Security`, `Content-Security-Policy` com `frame-ancestors 'none'`, `X-Content-Type-Options: nosniff` e `Referrer-Policy`.
12. **[Segurança]** **Dado** o ambiente `Production`, **quando** acesso `/swagger` ou `/openapi`, **então** recebo 404. Em `Development` e no CI o OpenAPI fica disponível para o Orval.

**Consistência dos docs**
13. **Dado** o PR da story, **quando** reviso os docs, **então** o `03-modelo-dados.md` tem `catalog.tenant_domains` (`tenant_id`, `host`, `is_primary`, `verified_at`, host único), não tem `kiosk_token_index`, e a ADR 0001 tem a nota "Alterada pela ADR 0004 (resolução pelo host)".

## Regras de negócio e técnicas
- Middleware de tenant: lê o host normalizado (minúsculas, sem porta) → procura em `tenant_domains` (cache em memória de 5 min) → registra `ITenantContext` no escopo → `TenantDbContext` usa a connection string decifrada pela FND-11 (`SecretKind.TenantConnection`).
- Suspensão em menos de 10 s: o cache precisa de invalidação ativa (ex.: `LISTEN/NOTIFY` do PostgreSQL no catálogo ou TTL curto só para o status). A escolha fica registrada na ADR 0006.
- Um único `TenantDbContext`, schemas por módulo, `ApplyConfigurationsFromAssembly` por módulo; cada módulo acessa os dados pela própria interface (ex.: `IIdentityDb`) e as configurações ficam `internal` (ADR 0006).
- Forwarded headers: `KnownNetworks`/`KnownProxies` limitados à rede do `stack-nginx`; `AllowedHosts` não é usado para tenant (é o catálogo que decide).
- Validação de token: além da assinatura, o `tid` precisa ser igual ao tenant resolvido e o `aud` igual ao host. Nesta story, os tokens de teste são emitidos por um helper; a emissão real chega na FND-04.
- Hub SignalR de teste mínimo, para provar o CA 6 no hub. Grupos `{tenantId}:{painel}`.
- Webhooks levam o tenant na URL e são servidos no host da plataforma (ADR 0005 D2); fora do escopo desta story.

## Permissões, eventos e tabelas
- **Tabelas (catálogo):** `catalog.tenants` (`key`, `legal_name`, `trade_name`, `document`, `status` Provisioning/Active/Suspended, `plan_id`, `db_name`, `connection_secret` cifrado), `catalog.tenant_domains`, `catalog.plans` (mínimo para a FK; `plan_modules` chega na FND-06).
- **Permissões:** nenhuma nova.
- **Eventos:** `TenantSuspended`/`TenantReactivated` no catálogo para invalidar cache (mecanismo interno, não outbox de tenant).

## Telas e estados
- **Página "Acesso suspenso"** (front, rota pública): título "Acesso suspenso", texto "O acesso desta empresa ao BemAqui Empresas está suspenso. Fale com o responsável pela conta.", sem dados internos. Precisa de ajuda em `Ajuda/` quando a FND-14 existir (ver Pontos em aberto).
- Sem outras telas.

## Plano de testes
- **Integração (Testcontainers, um PostgreSQL com três bancos: catálogo, A e B):** CA 1, 2, 3, 4 (suspende e mede < 10 s), 5, 6 (REST e hub), 7, 8.
- **Integração HTTP (`WebApplicationFactory`):** CA 9, 10, 11 e 12.
- **Arquitetura:** nenhum módulo usa `DbContext` diretamente fora da própria interface de dados.

## Fora do escopo
- Provisionamento e criação de roles reais (FND-03a).
- Autenticação e emissão de tokens (FND-04).
- Autenticação do super admin e telas da plataforma (FND-13, FND-03b).
- Verificação de domínio próprio do cliente (`verified_at`) por DNS: só o campo; o fluxo entra quando houver o segundo tenant (ADR 0004 §3).

## Pontos em aberto
- **Rota de contexto público da tela de login:** a tela de login precisa do nome e do logo do tenant antes de autenticar. O CLAUDE.md só permite rota anônima para login, convite, quiosque e webhooks. Proposta: considerar `GET /api/v1/auth/context` (nome, logo, status) parte do login. Confirmar com o PO.
- **Valor da CSP:** as origens permitidas (fontes self-hosted, `connect-src` para SignalR) ficam definidas na implementação; registrar na ADR 0006 ou num doc de segurança.
- **Ajuda de páginas públicas** (Acesso suspenso, Login): a regra AJD-7 cobre "toda rota do front". Confirmar se rotas públicas também precisam de ajuda.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de integração (Testcontainers) e HTTP passando.
- [ ] Migrations do catálogo criadas.
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Página "Acesso suspenso" com estado de erro claro.
- [ ] Ajuda da página: ver Pontos em aberto.
- [ ] Padrões e próximo passo propositivos: não se aplica.
- [ ] Auditoria ligada para alterações de status do tenant no catálogo.
- [ ] `docs/03-modelo-dados.md` e ADR 0001 atualizados (CA 13); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `CatalogDbContext` e migrations (`tenants`, `tenant_domains`, `plans`).
- [ ] `ITenantDirectory` (usado pela FND-07) e `ITenantContext`.
- [ ] Middleware de resolução por host com cache e invalidação ativa.
- [ ] `TenantDbContext` com conexão dinâmica e interfaces de dados por módulo.
- [ ] Validação de `tid` e `aud` no pipeline de autenticação e no hub.
- [ ] Forwarded headers com proxy confiável; cabeçalhos de segurança; Swagger desligado em produção; sem CORS.
- [ ] Fixture de teste com dois tenants e roles distintos.
- [ ] Atualizar `03-modelo-dados.md` e a ADR 0001.

## Quality gates
- Agentes previstos: @architect, @data-engineer, cybersecurity, @qa.
- Gate: o teste de isolamento entre tenants (CA 1, 6 e 7) é obrigatório e não pode ser marcado como ignorado.

## Validação do PO
Checklist do @po (Pax), 16/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança da revisão §2.3 que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** Dependência da FND-09a incluída para a página "Acesso suspenso".
- **Pré-condições para `Ready`:** Mecanismo de invalidação do cache (ADR 0006); decidir a rota pública de contexto do login.
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story detalhada com ADR 0004, ADR 0005 D2 e CAs de segurança da revisão | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
- **Parte A (FND-02a), 18/10/2026, branch `feat/fnd-02a-catalogo-tenant`.** A story foi dividida em 02a (catálogo e resolução) e 02b (endurecimento HTTP, `tid` e `aud` do token no REST e no hub, e página "Acesso suspenso").
  - **Catálogo:**
    - `CatalogDbContext` (módulo Platform) com `catalog.tenants`, `catalog.tenant_domains` (host único) e `catalog.plans`.
    - Auditoria própria e imutável em `audit.audit_log` do catálogo.
    - Trigger `pg_notify('tenant_changed')` e a migration `InitialCatalog`.
    - Auto-migração só em desenvolvimento e testes (`Catalog:AutoMigrate`).
  - **Resolução de tenant:**
    - `CatalogTenantResolver` resolve por host normalizado (minúsculas, sem porta) ou por id.
    - O cache é positivo por 5 min e negativo por 30 s, com limite de 1000 entradas.
    - O `CatalogChangeListener` (LISTEN) invalida o cache, e a suspensão vale em menos de 10 s (CA 4 testado).
  - **`TenantResolutionMiddleware`:**
    - host desconhecido → 404 genérico;
    - host da plataforma (`Platform:Host`) → 404 nas rotas de tenant;
    - tenant suspenso → 403 `tenant_suspended`;
    - em provisionamento → 503;
    - `/health` e `/api/v1/public/tenant-context` respondem em qualquer estado.
  - **Persistência e mensageria:**
    - O `TenantDbContext` usa a conexão do tenant do escopo, decifrada pelo `ISecretProtector`. Sem tenant no escopo ele falha, em vez de cair num banco padrão.
    - `TenantScopeFactory` atende jobs e outbox (CA 8).
    - Dispatcher do outbox e endpoints de auditoria, anexos e notificações ligados no Host.
  - **Contexto público:** `GET /api/v1/public/tenant-context`, anônimo (ADR 0009), devolve nome e status para a tela de login e para a página de suspensão.
  - **CAs atendidos:** 1, 2, 3 (API; a página do front fica na 02b), 4, 5, 7, 8 e 13.
    - O CA 7 é simulado no fixture: role por tenant com `REVOKE CONNECT FROM PUBLIC`. O provisionamento real é a FND-03a.
  - **Testes:**
    - O PostgreSQL de teste sobe com `max_connections=500`, por causa dos vários bancos e pools.
    - O servidor do Hangfire fica desligado nos smoke tests, e um teste próprio confere que ele sobe e cria o schema.

- **Parte B (FND-02b), 18/10/2026, branch `feat/fnd-02b-endurecimento-http`:**
  - **Proxy confiável:**
    - Os cabeçalhos `X-Forwarded-For` e `X-Forwarded-Proto` só são aceitos vindos das redes em `ReverseProxy:KnownNetworks`. O `Host` nunca vem de cabeçalho, porque o nginx já envia o host real.
    - Com `ReverseProxy:RequireTrustedProxy=true`, que fica ligado em homologação e produção (FND-10a), quem não vem do proxy recebe 404. Acesso direto ao container não resolve tenant. Só `/health` responde nesse caso.
  - **Cabeçalhos em toda resposta:**
    - HSTS (fora de Development);
    - CSP `default-src 'self'; ... connect-src 'self' wss:; object-src 'none'; frame-ancestors 'none'`;
    - `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy` (câmera e geolocalização só `self`) e COOP.
    - Respostas que já definem CSP, como o download em sandbox, mantêm a delas.
  - **Sem CORS:** API e front ficam na mesma origem; o CORS nunca é registrado.
  - **JWT:**
    - Validação de assinatura, emissor e validade.
    - No `OnTokenValidated`, `tid` precisa ser o tenant resolvido pelo host e `aud` precisa conter o host da requisição.
    - O token do hub vem na query, só para `/hubs`.
    - Política de fallback: toda rota exige autenticação, salvo as marcadas como anônimas (`/health`, contexto público e OpenAPI em dev).
  - **Hub:** `PingHub` (`/hubs/ping`), com grupos `{tenantId}:{painel}` montados no servidor a partir da claim.
  - **Sessão:** `GET /api/v1/me`, que a FND-04 completa.
  - **Chave de assinatura:**
    - `Auth:SigningKey` fica num secret e é obrigatória fora de Development. Em Development é aleatória por subida.
    - A emissão real dos tokens e a gestão da chave entram na FND-04.
  - **OpenAPI:** fora de Development, `/openapi` e `/swagger` respondem 404, e não 401. Em Development ficam disponíveis para o Orval, fora da resolução de tenant.
  - **CAs atendidos:** 6, 9, 10, 11 e 12.
  - **Página "Acesso suspenso" (CA 3, parte do front): fica para a FND-09b.**
    - O CLAUDE.md proíbe `fetch` escrito à mão, e o cliente gerado pelo Orval só chega na FND-09c.
    - A API já entrega o necessário: 403 `tenant_suspended`, e `GET /api/v1/public/tenant-context` com `status: Suspended`.
## QA Results
_A preencher pelo @qa._