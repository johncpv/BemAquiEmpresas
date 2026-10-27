# ADR 0009 — Bibliotecas e padrões técnicos da Fase 0

- Status: Aceito (16/10/2026)
- Responde aos pontos técnicos em aberto 15 a 28 de `docs/stories/README.md`.
- Regra geral: na dúvida, vale o padrão mais seguro e mais simples.

## Bibliotecas e CI
- **Testes:** xUnit v3, NetArchTest e Testcontainers.
  - **FluentAssertions fixado em 7.2.2**, a última versão Apache 2.0. A partir da 8, a licença é comercial e paga, o que vai contra o "Não usar licença comercial" do CLAUDE.md. Se um dia o 7.x não servir, a alternativa é o fork AwesomeAssertions.
- **Validação:** FluentValidation.
- **QuestPDF Community:** gratuita para empresa com receita bruta anual abaixo de US$ 1 milhão. A Konebras (ME) e o titular do BemAqui se enquadram hoje. Reavaliar a cada novo tenant ou se a receita mudar. O pacote só entra na Fase 2.
- **Runner do CI:** hospedado pelo GitHub (`ubuntu-latest`), com Docker disponível para o Testcontainers.
- **Registry de imagens:** GHCR privado.

## Autenticação e sessão
- **Cookie de refresh:** `__Host-bq_rt`, com `Secure`, `HttpOnly`, `SameSite=Strict` e `Path=/`.
  - O prefixo `__Host-` exige `Path=/`, o que derruba o "Path restrito" proposto na revisão.
  - A proteção do refresh e do logout fica no servidor: exigem `Origin` igual ao host do tenant e o header `X-CSRF: 1`.
- **Rotas anônimas:** além de login, convite, quiosque e webhooks, ficam anônimas:
  - `GET /api/v1/public/tenant-context`, que devolve só o nome e o logo para a tela de login;
  - `/health/live` e `/health/ready`, sem detalhes.
- **Cache de tenant:** o cache em memória é invalidado por `LISTEN/NOTIFY` do PostgreSQL no catálogo (canal `tenant_changed`), com TTL de reserva de 60 s. Suspender um tenant vale em menos de 10 s.

## Anexos e LGPD
- **Tipos permitidos:** pdf, png, jpg, webp, xml, xlsx, docx, csv, dwg, dxf e zip.
  - Limite de 25 MB por arquivo.
  - Validação de extensão e de magic bytes.
- **Antivírus (ClamAV):** se estiver fora do ar, o arquivo fica "aguardando verificação" e o download é bloqueado até a varredura.
- **`[PersonalData]` na Fase 0:** nome, e-mail e telefone do usuário, e IP na auditoria. O diff da auditoria mascara esses campos para quem não tem `identity.view`.

## Tempo real, e-mail e ajuda
- **Sino de notificações:** SignalR, com polling de 60 s como reserva.
- **E-mail:**
  - Fluid para todos os templates, inclusive os do sistema.
  - SMTP da plataforma por padrão, com SMTP próprio opcional por tenant, cifrado conforme a ADR 0007.
  - SPF e DKIM do domínio remetente são configurados antes do go-live.
- **Ajuda:**
  - Arquivos `Ajuda/*.md` embutidos no projeto `Endpoints` de cada módulo. As telas do shell e da plataforma ficam no Host.
  - No cabeçalho, `padrao: sim` marca a aba padrão da tela.
  - A busca do "Pergunte ao BemAqui" usa termos normalizados, como no NSHome.
  - O teste de cobertura inclui as rotas públicas e as da plataforma.

## Infra de homologação
- Host `homolog.konebras.com.br`, com DNS pelo PO, no diretório `/opt/projetos/bemaqui-empresas-homolog/`.
- **Backup:**
  - cifrado com `age`; a chave pública fica na VPS e a privada fica offline, com o PO;
  - armazenado em bucket S3-compatível, com o provedor escolhido na FND-10b;
  - credencial com permissão só de `PutObject`.

## Modelo de dados
- O `03-modelo-dados.md` recebe, na story que criar cada uma:
  - `user_preferences` (tema, ajudas vistas);
  - `platform_audit_log`;
  - `ai_settings` e `ai_usage_logs`;
  - `inbox_messages`;
  - `platform_alerts`;
  - `data_protection_keys`.

## Adendo (FND-09c, 18/10/2026): contrato da API para o front
- **OpenAPI versionado:** o documento do Host (`/openapi/v1.json`, só em Development) fica em `src/Web/openapi.json` e é a entrada do Orval.
  - O teste `OpenApiDocumentTests` falha quando o documento fica desatualizado. Para atualizar, rode `BQ_UPDATE_OPENAPI=1 sh scripts/test-docker.sh --filter-class "*OpenApiDocumentTests"`.
  - O CI do front roda `pnpm gen:api` e falha se o cliente gerado em `src/api/generated` mudar.
- **Tipos de resposta:** os endpoints declaram `.Produces<T>()`, para o cliente gerado ter os tipos de resposta.
- **Números no JSON:** `NumberHandling = Strict`. O padrão do ASP.NET aceitava número como string, e o OpenAPI do .NET 10 publicava `integer|string`.
- **Mutator único** (`src/api/http.ts`): cuida da mesma origem, do access token só em memória, do `X-CSRF`, do refresh silencioso no 401 (um por vez), do `X-Perm-Version` e do ProblemDetails convertido em `ApiError`. Fora dele, `fetch`, `axios` e `XMLHttpRequest` reprovam no lint.
- **Bibliotecas do front:** TanStack Query 5, TanStack Table 8 (a 9 tem outra API), React Hook Form, Zod 4 (mensagens em `pt`), sonner (toasts), Radix (Dialog, Popover, DropdownMenu, ToggleGroup) e cmdk.

- Ver ADR 0016 (rota anônima `GET /api/v1/public/tenant-logo`, dados sensíveis por rota e auditoria sem dado pessoal), ADR 0017 (importação do Dolibarr) e ADR 0018 (LGPD).

- Ver ADR 0021 (mensagens no topo do conteúdo, no lugar do toast) e ADR 0022 (geração de PDF com QuestPDF atrás do `IPdfRenderer`).

## Adendo 21/10/2026 (revisão pré-Fase 3, decisão D5 do PO): rota anônima do PIN
- Ficam anônimas, **só com o cookie do aparelho registrado válido** (`__Host-bq_dev`), `POST /api/v1/auth/pin` (entrar por PIN) e `GET /api/v1/auth/pin/users` (primeiro nome e inicial de quem tem PIN no aparelho). Sem o cookie, as duas respondem 404, como rota inexistente.
- Valem as mesmas proteções do refresh e do logout: `Origin` igual ao host do tenant, `X-CSRF: 1`, rate limit do login (ADR 0010 item 6), resposta e tempo uniformes (ADR 0011).
- O cookie `__Host-bq_dev` segue o padrão do `__Host-bq_rt` (`Secure`, `HttpOnly`, `SameSite=Strict`, `Path=/`). Detalhes na ADR 0026.

## Adendo 21/10/2026 (revisão pré-Fases 4 a 8, decisões D11, D13, D16 e D19 do PO): rotas anônimas novas e teste de rotas
- **Painel da TV (ADR 0030):** a página `/tv` do front é pública (sem dado nenhum até a sessão existir). A troca do token é **`POST /api/v1/kiosk/session`**, anônima, com o token do **fragmento** (`/tv#{token}`) no corpo, que devolve o cookie `__Host-bq_kiosk`; tempo e mensagem uniformes, rate limit próprio. Depois disso, `GET /api/v1/kiosk/*` e `/hubs/kiosk` exigem o esquema `Kiosk`, não usuário. A rota `/tv/{token}` deixa de existir.
- **Webhooks (ADR 0031):** **`POST /webhooks/{provider}/{tenantKey}`**, fora de `/api/v1`, anônima de usuário, com validador de origem obrigatório por provedor; fora do middleware de tenant por host.
- **Portal do cliente (ADR 0040)**, só no host do portal: `POST /api/v1/portal/auth/login`, `POST /api/v1/portal/auth/refresh` e `POST /api/v1/portal/auth/logout` (com `Origin` do host do portal e `X-CSRF: 1`, como o backoffice), `POST /api/v1/portal/auth/2fa` (segundo passo do login), `GET` e `POST /api/v1/portal/invites/{token}` (validar e aceitar o convite), `POST /api/v1/portal/auth/password-reset` e `POST /api/v1/portal/auth/password-reset/confirm`, e o `GET /api/v1/public/tenant-context` no host do portal. Resposta uniforme e rate limit por política nomeada em todas.
- **Teste de arquitetura das rotas (D19):** reprova todo endpoint que não tenha `RequirePermission`/`RequireModule` (ou `RequireSelfService`, ADR 0035, ou o esquema `Kiosk`, ADR 0030) e que **não esteja na lista versionada de rotas anônimas** (arquivo no projeto de testes de arquitetura, com o método, o caminho e a ADR que a autoriza). Entrar na lista exige ADR; a lista nasce com as rotas deste adendo, as do adendo do PIN e as anteriores (login, refresh, logout, convite, recuperação de senha, `tenant-context`, `tenant-logo`, `/health/live|ready`).

## Adendo 21/10/2026 (revisão pré-Fases 4 a 8, ponto 29; aplicado na BNK-03a em 27/10/2026): OFX só no dono `BankStatementBatch`
- O OFX (`.ofx`, `application/x-ofx`) **não entra na allowlist geral**. É um tipo extra declarado só pelo dono `BankStatementBatch`
  (`AttachmentOwnerRules.ExtraTypes`), com magic bytes próprios: cabeçalho `OFXHEADER:` (1.x SGML) ou XML com `<?OFX`/`<OFX>` (2.x), texto sem
  página HTML. Até 5 MB.
- O arquivo é lido pelo sistema, então a triagem (`AttachmentService.ScreenAsync`: permissão de gravar do dono, limite de envios, tamanho, tipo
  e **antivírus**) vem **antes** do parser; com o antivírus indisponível, recusa (falha fechada). Depois da leitura, o arquivo bruto vira anexo do
  lote (`StoreScreenedAsync`), na mesma transação. A rota genérica de anexos recusa o dono (o arquivo entra só por "Importar extrato").
- Parser próprio e restrito: 2.x com `DtdProcessing.Prohibit` e sem resolver; qualquer `<!DOCTYPE`/`<!ENTITY` recusa; limite de lançamentos e de
  tempo (10 s).
