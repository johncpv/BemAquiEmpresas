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
- **Banco de catálogo** (`catalog`): tenants, domínios (`tenant_domains`, inclusive o host próprio do portal, ADR 0040), connection string criptografada, plano, status, super admins, log de provisionamento. **Sem índice de quiosque no catálogo** (ADR 0030): a TV abre no host do tenant, que resolve o tenant como qualquer requisição, e o token de tela fica só no banco do tenant (`kiosk_tokens`, como hash).
- **Um banco por tenant**: todos os dados de negócio, inclusive usuários do tenant.
- Resolução: middleware lê o host da requisição → busca em `tenant_domains` no catálogo (cache em memória, 5 min) → registra `ITenantContext` no escopo → `TenantDbContext` usa a connection string do tenant.
- Webhooks e jobs não têm subdomínio:
  - **webhooks** (ADR 0031, FND-16) chegam no host da plataforma, em `/webhooks/{provedor}/{tenantKey}?t={token}`: `tenantKey` opaco (nunca o nome do tenant), token de 256 bits guardado só como hash (o log `bqe_masked` não grava a query), `/webhooks/*` fora da allowlist de IP da plataforma e fora do middleware de tenant por host (o tenant vem do `tenantKey`). O webhook nunca é verdade: o efeito vem da reconsulta à fonte (o Inter por mTLS, ADR 0032). O EmissorFiscal avisa pela rede interna `bqe-fiscal` (ADR 0027);
  - **jobs** Hangfire recebem `tenantId` como parâmetro, abrem o escopo do tenant e conferem o módulo liberado antes de rodar.
- SignalR: grupos nomeados `{tenantId}:{painel}` (ex.: `{tenantId}:production`, `{tenantId}:kiosk:{id}`), derivados das claims no servidor, sem método de entrada em grupo; a conexão fecha quando a permissão ou o security stamp mudam.
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
- Consultas de painel e relatório (ADR 0028, que revoga o módulo `Reporting` da ADR 0006): cada agregado é calculado no módulo dono, atrás de um contrato (`ICashForecast`, `IRevenueSummary`, `IProjectMargins`, `IFactoryLoad` …), com SQL parametrizado e ordenação por allowlist; o módulo `Dashboard` só compõe (Application e Endpoints, referenciando apenas `*.Contracts`, sem schema). Cache de 60 s com chave `{tenant}:{usuário}:{versão de permissão}` e contador de geração por tenant; materialized view só se a medição pedir.
- Planejamento finito (ADR 0029): um único escritor (`ScheduleService`) grava as datas previstas em tabelas do plano (`production.schedule_*`, `commercial.project_forecasts`), fora dos documentos e da auditoria.

## 4. Frontend
- Um app React com quatro "superfícies" e layouts próprios:
  1. **Backoffice** (`/app`) — dock inferior agrupado por área (conforme protótipo), tabelas com filtro/ordenação/paginação server-side (TanStack Table), formulários RHF+Zod.
  2. **Chão de fábrica PWA** (`/fab`) — telas grandes, fila do operador, apontamento, leitura de QR code (câmera), PIN em aparelho registrado (ADR 0026) e "Meu ponto" (ADR 0035). O funcionamento sem rede foi adiado (P-23).
  3. **Painel TV** (`/tv#token`, ADR 0030) — sem login de usuário: o token vai no fragmento (não chega ao servidor nem ao log), é trocado uma vez por um cookie `__Host-bq_kiosk` e apagado da barra (`replaceState`, `Referrer-Policy: no-referrer`); sessão de 30 dias sem uso e 180 dias absolutos; o principal de quiosque só faz GET do painel; SignalR e rotação de visões; o service worker não guarda `/tv` nem `/api/v1/kiosk`.
  4. **Portal do cliente** — Fase 8, em **host próprio** (ADR 0040), com identidade, chave de assinatura, `aud` e cookies próprios; um token do backoffice dá 401 no portal e vice-versa.
- Estado do servidor só em TanStack Query; estado local mínimo (Zustand se necessário).
- Cliente da API gerado por Orval a partir do OpenAPI.
- Permissões: o backend envia a lista de permissões do usuário; componente `<Can perm="...">` esconde ações; a autorização real é sempre no backend.
- Design: shadcn/ui, tema claro/escuro, densidade confortável no backoffice e grande no PWA/TV.

## 5. Segurança
- ASP.NET Core Identity (por tenant) com TOTP nativo; códigos de recuperação.
- Access token JWT 15 min; refresh token 7 dias, rotativo, em cookie httpOnly/Secure/SameSite=Strict; detecção de reuso revoga a família.
- Proprietário pode exigir 2FA para todos ou por perfil.
- Segredos por tenant (certificado A1, credenciais Inter, senha SMTP) criptografados com Data Protection + chave mestra fora do banco.
- Token de quiosque (ADR 0030): aleatório (256 bits), guardado com hash, mostrado uma vez, nunca no caminho nem na query; escopo somente leitura do painel; revogável em menos de 1 minuto (`kiosk.delete`); IP opcional.
- 2FA de novo (TOTP recente) nas ações críticas (ADR 0008, adendo de 21/10/2026, que decide a P-19): pagar, estornar, cancelar ou inutilizar NF-e, trocar conta de salário, fechar a folha, anonimizar titular, salvar credenciais do Inter e enviar o certificado A1.
- Auditoria: tabela `audit.audit_log` (entidade, id, ação, usuário, data, diff JSONB, ip). Login/logout/falhas também auditados.
- Rate limit por IP e por usuário no login e endpoints públicos.
- Rate limit por **políticas nomeadas** (não só em `/auth`): webhooks por provedor e tenant (FND-16), simulação de prazo (PLN-05), convite, recuperação e aceite do portal (POR-01a, POR-02), segunda via pelo portal (POR-01c), envio do certificado e teste de conexão (FIS-02a, BNK-01a).
- **Autoatendimento do funcionário** (ADR 0035): rotas `/api/v1/me/hr/*` ("Meu ponto", "Meu espelho", "Meus holerites") liberadas pela camada 1 e pelo vínculo com funcionário ativo, sem ação no grupo, numa lista versionada no teste de arquitetura; no tablet, só os endpoints `[PinSessionAllowed]` (ADR 0026, adendo).
- **Dado sensível por contrato:** `IEmployeeBankAccounts` (HR) é lido só no pagamento da OP, com `SensitiveRead` (ADR 0036); anexos autorizados por registro, não só por módulo, com prefixo de tenant no storage.
- **Rotas anônimas** (ADR 0009, adendo de 21/10/2026): além das da Fase 0, `/tv` (troca do token do fragmento por cookie), `/webhooks/*` (com o GET de verificação só para o provedor que o declara) e as do portal (login, convite, recuperação); teste de arquitetura reprova endpoint sem `RequirePermission`/`RequireModule` fora da lista (D19).

## 6. Integrações

### 6.1 EmissorFiscal
Projeto existente do PO em `D:\GIT\EmissorFiscal`. **Decidido na ADR 0027:** serviço HTTP separado, chamado pela rede interna dedicada `bqe-fiscal` (fora da `stack_stack`), com um tenant do Emissor por tenant do BemAqui e chave de API sem escopo `admin` (`SecretKind.FiscalApiKey`).
- O certificado A1 fica **só no cofre do EmissorFiscal**; o BemAqui guarda só validade, titular e thumbprint (Configurações → Fiscal → Certificado digital, FIS-02a).
- Parâmetros fiscais pré-configurados e editáveis em Configurações → Fiscal, marcados "A confirmar com a contadora" até a conferência (ADR 0042).
- Homologação SEFAZ no tenant `demo`; produção só na GOL-01b.
- As lacunas do Emissor (reconciliação com a SEFAZ, DANFE em PDF, `transp`, IBS/CBS, escopo `nfe:eventos`, NFS-e de Caçapava) são pré-condição externa das stories fiscais.
- O módulo Fiscal só conhece a interface `IFiscalEmitter` (FIS-01).

### 6.2 Banco Inter
- API de Cobrança (boleto com QR Code Pix), consulta, cancelamento, PDF; webhook de pagamento; API de extrato para conciliação; Pix cobrança se necessário.
- Autenticação OAuth2 client credentials + certificado mTLS do correntista, **por conta bancária** (ADR 0032), configurada em Configurações → Integrações → Banco Inter (BNK-01a); credencial só com escopos de cobrança, extrato e webhook.
- Um `SocketsHttpHandler` por conta, em cache pelo thumbprint, com o certificado só em memória; URL base fixa; saída de rede liberada só para os hosts do Inter.
- Multa, juros, desconto e régua de cobrança pré-configurados em Configurações → Cobrança (BNK-01b, FIN-09).
- Abstração `IBankBillingProvider` / `IBankStatementProvider` para outros bancos no futuro.
- Ambiente sandbox para desenvolvimento.

### 6.3 Outras
- Consulta de CNPJ (API pública, ex.: BrasilAPI) com cache.
- E-mail: SMTP/SES; registros de envio e falha.
- Leitura de PDF de NF de entrada (FIS-05): extração de texto (PdfPig) num worker isolado (`network_mode: none`, memória limitada, sistema de arquivos só leitura, kill no tempo limite); **sem OCR no MVP** (PDF sem texto: "Digitar a nota"); parser da chave de 44 dígitos e campos do DANFE; conferência manual obrigatória antes de dar entrada.
- WhatsApp (ADR 0041): API oficial da Meta por um parceiro (BSP), atrás do `IWhatsAppSender`, credenciais por tenant (`SecretKind.WhatsAppCredentials`) em Configurações → Integrações → WhatsApp (WPP-01).

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
  - `prod`: `app.konebras.com.br`. O pipeline está pronto e desligado até a GOL-01b. **Ambiente de produção novo** (ADR 0034): catálogo novo só com o `konebras`, key ring e certificado do Data Protection novos, segredos novos; a homologação ganha key ring novo e o `konebras` é apagado dela depois do fechamento do go-live. O go-live vem depois das Fases 0 a 8 concluídas (marco final "Go-live").
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
