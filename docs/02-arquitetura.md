# 02 — Arquitetura

## 1. Visão geral

```
                 ┌──────── VPS Hostinger compartilhada (Docker Compose) ───┐
 app.konebras  ─►│ stack-nginx + certbot (já existente, um cert por host)  │
                 │   ├─ web   (React build estático, PWA)                  │
                 │   └─ api   (.NET 10: REST + SignalR + Hangfire)         │
                 │          ├─ PostgreSQL: catalog                          │
                 │          ├─ PostgreSQL: tenant_konebras, tenant_x ...    │
                 │          └─ volume de arquivos (anexos, PDFs, XML)       │
                 │   backup job ─► storage externo (S3 compatível)         │
                 └──────────────────────────────────────────────────────────┘
 Integrações: EmissorFiscal (SEFAZ) · Banco Inter (cobrança, Pix, extrato, webhook) · SMTP/SES · API CNPJ
```

Monolito modular em um único processo. Escala da Konebras (10–15 funcionários) não justifica microsserviços; a separação por módulos permite extrair algo depois se precisar.

## 2. Multi-tenant: banco por tenant

### 2.1 Decisão
- **Banco de catálogo** (`catalog`): tenants, domínios (`tenant_domains`), connection string criptografada, plano, status, super admins, tokens de quiosque (índice), log de provisionamento.
- **Um banco por tenant**: todos os dados de negócio, inclusive usuários do tenant.
- Resolução: middleware lê o host da requisição → busca em `tenant_domains` no catálogo (cache em memória, 5 min) → registra `ITenantContext` no escopo → `TenantDbContext` usa a connection string do tenant.
- Webhooks e jobs não têm subdomínio: webhooks levam o tenant na URL (`/webhooks/inter/{tenantKey}`) + validação de assinatura; jobs Hangfire recebem `tenantId` como parâmetro e abrem o escopo do tenant.
- SignalR: grupos nomeados `{tenantId}:{painel}`.
- Migrations: `TenantMigrator` aplica a mesma migration em todos os bancos no deploy (com lock e log por tenant); falha em um tenant não bloqueia os demais, mas gera alerta.
- Provisionamento: cria banco → migra → seeds → cria usuário proprietário → envia convite.

### 2.2 Estimativa de esforço: banco único × banco por tenant
Comparação com a alternativa "banco único com coluna `TenantId` e filtro global do EF".

| Item | Banco único (TenantId) | Banco por tenant |
|---|---|---|
| Resolução de tenant e contexto | igual | igual |
| Isolamento de dados | filtro global + cuidado em toda query, risco de vazamento por bug | físico, risco quase nulo |
| Migrations | 1 banco | runner multi-banco, controle de falha parcial |
| Provisionamento | inserir linha | criar banco, migrar, semear |
| Jobs/SignalR/webhooks | precisam do tenant igualmente | idem + troca de conexão |
| Backup/restauração por cliente | difícil (restaurar 1 cliente sem afetar outros) | trivial (dump por banco) |
| Relatórios entre tenants (plataforma) | fácil | precisa agregar no catálogo |
| Custo de infra com poucos tenants | igual | igual (vários bancos no mesmo PostgreSQL) |

**Estimativa:** o banco por tenant acrescenta cerca de **15–25% ao esforço da Fase 0** (catálogo, migrator multi-banco, provisionamento, troca de conexão em jobs/webhooks). Diluído no MVP inteiro, fica em torno de **3–5% a mais**. Depois da fundação pronta, o custo marginal por funcionalidade é praticamente zero.
Já **migrar de banco único para banco por tenant depois** é caro (separar dados de todos os clientes, reescrever jobs e relatórios), por isso a decisão é feita agora.

Limite prático: com dezenas de tenants no mesmo PostgreSQL funciona bem; acima de ~200–300 bancos avaliar pooling (PgBouncer) e mais de um servidor. Não é preocupação do MVP.

## 3. Estrutura do backend
- `Host`: Program.cs, autenticação, autorização por permissão (policy dinâmica `perm:{codigo}`), middleware de tenant, ProblemDetails, rate limit, OpenAPI, hubs SignalR, dashboard Hangfire (só super admin).
- `BuildingBlocks`: `Result<T>`, `Error`, `PagedList`, `ITenantContext`, `ICurrentUser`, `IClock`, `INumberSequence`, outbox (`OutboxMessage` + dispatcher em job), interceptor de auditoria, soft delete, `IFileStorage` (disco local, abstraído para S3 depois), `IEmailSender`, `IPdfRenderer`.
- Módulos: cada um com seu `DbContext` parcial? **Não.** Um único `TenantDbContext` com configurações por módulo em assemblies separados (`modelBuilder.ApplyConfigurationsFromAssembly`), e schemas PostgreSQL por módulo (`commercial.contracts`, `finance.receivables`...) — mantém transação única entre módulos quando necessário, com fronteira lógica.
- Comunicação entre módulos: chamada síncrona via interface em `*.Contracts` (ex.: `IInventoryReservations.Reserve(...)`) quando precisa de resposta; evento de domínio via outbox quando é reação (ex.: `ContractSigned` → Finance cria recebíveis, Commercial cria projeto).
- Casos de uso: uma classe por comando/consulta (`CreateQuoteHandler`), validação com FluentValidation, retorno `Result`.
- Consultas pesadas de dashboard: SQL direto (Dapper) sobre views/materialized views, atualizadas por evento ou a cada 5 min.

## 4. Frontend
- Um app React com quatro "superfícies" e layouts próprios:
  1. **Backoffice** (`/app`) — dock inferior agrupado por área (conforme protótipo), tabelas com filtro/ordenação/paginação server-side (TanStack Table), formulários RHF+Zod.
  2. **Chão de fábrica PWA** (`/fab`) — telas grandes, fila do operador, apontamento, leitura de QR code (câmera), modo offline tolerante (fila de apontamentos guardada localmente e sincronizada).
  3. **Painel TV** (`/tv/{token}`) — sem login, SignalR, rotação de visões.
  4. **Portal do cliente** (`/portal`) — fase 8.
- Estado do servidor só em TanStack Query; estado local mínimo (Zustand se necessário).
- Cliente da API gerado por Orval a partir do OpenAPI.
- Permissões: o backend envia a lista de permissões do usuário; componente `<Can perm="...">` esconde ações; a autorização real é sempre no backend.
- Design: shadcn/ui, tema claro/escuro, densidade confortável no backoffice e grande no PWA/TV.

## 5. Segurança
- ASP.NET Core Identity (por tenant) com TOTP nativo; códigos de recuperação.
- Access token JWT 15 min; refresh token 7 dias, rotativo, em cookie httpOnly/Secure/SameSite=Strict; detecção de reuso revoga a família.
- Proprietário pode exigir 2FA para todos ou por perfil.
- Segredos por tenant (certificado A1, credenciais Inter, senha SMTP) criptografados com Data Protection + chave mestra fora do banco.
- Token de quiosque: aleatório (256 bits), guardado com hash, escopo somente leitura do painel, revogável, IP opcional.
- Auditoria: tabela `audit.audit_log` (entidade, id, ação, usuário, data, diff JSONB, ip). Login/logout/falhas também auditados.
- Rate limit por IP e por usuário no login e endpoints públicos.

## 6. Integrações

### 6.1 EmissorFiscal
Projeto existente do PO em `D:\GIT\EmissorFiscal`. **Story FIS-01 é uma análise** (Claude Code roda localmente e tem acesso ao diretório): identificar linguagem, se é biblioteca ou serviço, operações suportadas (NF-e, NFC-e, NFS-e, cancelamento, CC-e, Distribuição DF-e, manifestação), forma de configurar certificado, e suporte aos campos da Reforma Tributária. Resultado vira ADR com a forma de integração:
- (a) referência como biblioteca .NET no módulo Fiscal, ou
- (b) serviço separado no Docker Compose chamado por HTTP.
Em ambos os casos o módulo Fiscal só conhece a interface `IFiscalEmitter` (Emitir, Consultar, Cancelar, CartaCorrecao, BaixarPorChave).

### 6.2 Banco Inter
- API de Cobrança (boleto com QR Code Pix), consulta, cancelamento, PDF; webhook de pagamento; API de extrato para conciliação; Pix cobrança se necessário.
- Autenticação OAuth2 client credentials + certificado mTLS do correntista, por tenant.
- Abstração `IBankBillingProvider` / `IBankStatementProvider` para outros bancos no futuro.
- Ambiente sandbox para desenvolvimento.

### 6.3 Outras
- Consulta de CNPJ (API pública, ex.: BrasilAPI) com cache.
- E-mail: SMTP/SES; registros de envio e falha.
- Leitura de PDF de NF de entrada: extração de texto (PdfPig); se não houver texto, OCR; parser da chave de 44 dígitos e campos do DANFE; conferência manual obrigatória antes de dar entrada.

## 7. Infra e deploy
- **Compose:** compose próprio por ambiente: `/opt/projetos/bemaqui-empresas-homolog/` (projeto `bqe-homolog`, containers `bqe-homolog-*`) e, na GOL-01, `/opt/projetos/bemaqui-empresas/`.
  - Serviços: `api`, `migrator` (a mesma imagem da API), `web` (nginx-unprivileged), `postgres` 17 (só na rede interna, sem `ports:`), `clamav` e `backup`.
  - Só `web` e `api` entram na `stack_stack`. O endurecimento e o `check-compose.mjs` estão na ADR 0015.
- **Borda:** `stack-nginx` compartilhado da VPS, com um vhost por host (`deploy/nginx/`) e um certificado Let's Encrypt por host, pelo certbot existente.
  - O log `bqe_masked` não grava query string nem o Referer completo.
  - Sem Caddy e sem wildcard (ADR 0004).
- **CI e CD (GitHub Actions):** o CI faz build, testes (Testcontainers), o job "Infra" (compose, `nginx -t` e as três imagens) e E2E.
  - O CD publica as imagens no GHCR (tag = SHA) e faz o deploy por SSH, com chave de comando restrito (`deploy/homolog/deploy.sh`).
  - No deploy, o `migrator` roda antes (catálogo e todos os tenants, advisory lock, relatório, saída ≠ 0 se algum falhar). Só depois sobem `api` e `web`, esperando `healthy`, com volta automática para a versão anterior.
  - A API não migra sozinha em homologação e produção (ADR 0006; runbooks `docs/operacao/provisionamento.md` e `deploy.md`).
- **Ambientes:**
  - `dev`: local;
  - `homolog`: `homolog.konebras.com.br` e `plataforma-homolog.bemaquiapp.com.br` (ADR 0015; DNS pelo PO);
  - `prod`: `app.konebras.com.br`. O pipeline está pronto e desligado até a GOL-01.
- **Segredos:** arquivos em `./secrets`, montados como Docker secrets. O Host lê `/run/secrets/*__*` como configuração. Nada vai em variável de ambiente.
- **Backup (RPO 24 h):** `pg_dump` por banco e os papéis, cifrados com `age` em stream e enviados por `rclone` a um bucket S3 compatível (recomendação: Backblaze B2).
  - A credencial não tem delete; a retenção de 30 dias é regra de ciclo de vida do bucket.
  - A trilha do key ring vai para outro bucket.
  - A restauração está descrita e registrada em `docs/operacao/restauracao.md`.
- **Monitoramento:** health checks `/health/live` e `/health/ready`, UptimeRobot fora da VPS (ADR 0010) e logs Serilog JSON.

## 8. Padrões de dados
- Schemas por módulo; nomes em snake_case (convenção Npgsql).
- Toda tabela de negócio: `id uuid`, `created_at`, `created_by`, `updated_at`, `updated_by`, `is_deleted` (quando cadastro), `row_version` (concorrência otimista via `xmin`).
- Valores monetários `numeric(18,2)`, quantidades `numeric(18,4)`, custos unitários `numeric(18,6)`.
- Enums gravados como texto.
- Índices em todas as FKs e em campos de busca (documento, número, nome com `pg_trgm`).
