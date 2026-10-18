# FND-13 — Autenticação do super admin no host da plataforma

| Campo | Valor |
|---|---|
| ID | FND-13 |
| Título | Login do super admin em `plataforma.bemaquiapp.com.br`, 2FA obrigatório, allowlist de IP e painel Hangfire protegido |
| Tamanho | M |
| Status | Done |
| Requisitos | R-PLT-4 (acesso ao painel da plataforma) · PRD §6 Segurança |
| Dependências | FND-02 (host da plataforma fora de `tenant_domains`), FND-04 (mecanismo de sessão), FND-05 (TOTP), FND-11, FND-12 |
| Bloqueia | FND-03b, FND-06 (tela de liberação de módulos), FND-10 (vhost da plataforma) |
| Ordem de execução | 10 |

## User story
Como **super admin da plataforma**, quero **entrar num host próprio da plataforma, com 2FA obrigatório e só a partir de IPs autorizados**, para **administrar os tenants sem que o painel fique exposto nos hosts dos clientes**.

## Contexto e referências
- ADR 0005 D2: host da plataforma `plataforma.bemaquiapp.com.br` (super admin, painel Hangfire, webhooks), fora de `tenant_domains`, com 2FA obrigatório e allowlist de IP.
- `docs/02-arquitetura.md` §3: dashboard Hangfire só para super admin.
- `docs/03-modelo-dados.md` → `catalog.platform_admins` (`email`, `password_hash`, `totp_secret`).
- Revisão §2.3: FND-13 nova; CA da FND-03 "painel da plataforma e `/hangfire` respondem 404 em host de tenant".

## Critérios de aceite
1. **Dado** o servidor, **quando** rodo `bemaqui platform-admin create --email <e-mail>`, **então** o primeiro super admin é criado no catálogo com convite de definição de senha e 2FA; não existe cadastro de super admin por rota pública.
2. **Dado** um IP fora da allowlist configurada, **quando** acessa qualquer rota do host da plataforma (exceto webhooks, que têm validação própria nas fases seguintes), **então** recebe 404, antes de chegar ao login.
3. **Dado** um super admin com IP permitido, **quando** informa e-mail e senha corretos, **então** é obrigatoriamente levado ao 2FA (configuração no primeiro acesso, desafio nos seguintes); sem 2FA não há sessão.
4. **[Segurança]** **Dado** um token ou cookie de super admin (`aud` do host da plataforma), **quando** é usado num host de tenant, **então** é rejeitado com 401; **dado** um token de usuário de tenant, **quando** é usado no host da plataforma, **então** é rejeitado com 401.
5. **[Segurança]** **Dado** um host de tenant, **quando** acesso `/hangfire`, `/platform` ou qualquer rota da plataforma, **então** recebo 404.
6. **Dado** o host da plataforma com super admin logado, **quando** acesso `/hangfire`, **então** vejo o painel; sem login, **então** sou levado ao login da plataforma.
7. **[Segurança]** **Dado** o login da plataforma, **quando** erro a senha 5 vezes ou estouro o rate limit, **então** valem o mesmo bloqueio (15 min), o anti-replay de TOTP e as respostas uniformes da FND-04/FND-05.
8. **Dado** qualquer login, falha, bloqueio ou logout de super admin, **quando** acontece, **então** é auditado no catálogo com IP e horário UTC.
9. **Dado** a sessão do super admin, **quando** fica inativa pelo tempo configurado, **então** expira e exige novo login com 2FA.

## Regras de negócio e técnicas
- Esquema de autenticação separado (`Platform`), com `aud` = host da plataforma, chave de assinatura própria e cookie próprio.
- `platform_admins` no catálogo; senha com o hasher do Identity; TOTP com `SecretKind.TotpSecret` no escopo `platform` (FND-11).
- Allowlist de IP por configuração (lista de CIDRs), avaliada sobre o IP real do forwarded header confiável (FND-02).
- Middleware de roteamento por host: rotas da plataforma só existem no host da plataforma; rotas de tenant só em hosts de `tenant_domains`.
- Hangfire Dashboard com `IDashboardAuthorizationFilter` exigindo o esquema `Platform`.

## Permissões, eventos e tabelas
- **Tabelas:** `catalog.platform_admins` (+ `recovery_codes`, `lockout_end`, `last_login_at`, `is_active`), `catalog.platform_audit_log` (nova, ver Pontos em aberto), `catalog.platform_refresh_tokens` (ou equivalente).
- **Permissões:** não usa `modulo.acao`; todo super admin ativo tem acesso ao painel da plataforma.
- **Eventos:** nenhum.

## Telas e estados
Superfície "Plataforma", sem dock de tenant (layout próprio simples, mesma identidade visual da FND-09a).
- **Entrar na plataforma:** e-mail, senha, "Entrar"; depois o desafio 2FA. Mesmos estados da FND-04/FND-05.
- **Configurar 2FA (primeiro acesso):** mesmo componente da FND-05, sem opção "Fazer depois".
- **Ajuda:** ver Pontos em aberto (cobertura de ajuda na superfície Plataforma).

## Plano de testes
- **Integração (Testcontainers + `WebApplicationFactory` com dois hosts):** CA 2 (IP fora da allowlist), 3 (2FA obrigatório), 4 (tokens cruzados), 5 (404 em host de tenant), 6 (Hangfire protegido), 7 (bloqueio e rate limit), 8 (auditoria).
- **E2E (Playwright):** criar super admin pela CLI, primeiro acesso com 2FA e abrir o Hangfire.

## Fora do escopo
- Tela de tenants (FND-03b) e de liberação de módulos (FND-06).
- Webhooks no host da plataforma (Fase 5).
- Perfis diferentes de super admin (todos têm acesso total).

## Pontos em aberto
- **IPs da allowlist:** quais IPs ou faixas o PO usa; e como acessar fora deles (VPN?). Sem isso, o PO pode se trancar fora.
- **Status para IP fora da allowlist:** esta story usa 404 (não revela o painel), no mesmo padrão do CA 5. Confirmar com o PO.
- **Auditoria da plataforma:** o modelo de dados não tem tabela de auditoria no catálogo; propõe-se `catalog.platform_audit_log` com a mesma forma de `audit.audit_log`. Atualizar o 03.
- **Host da plataforma em homologação:** `plataforma.bemaquiapp.com.br` atende homologação e produção, ou haverá um host de homologação da plataforma? (ver FND-10).
- **Ajuda na superfície Plataforma:** a regra AJD-7 vale para as rotas da plataforma? Confirmar.
- **Tempo de inatividade da sessão** do super admin: não definido.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de integração e E2E passando.
- [ ] Migrations do catálogo criadas.
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Telas com estados de carregando e erro.
- [ ] Ajuda das telas conforme decisão.
- [ ] Padrões e próximo passo propositivos: primeiro acesso leva direto à configuração do 2FA.
- [ ] Auditoria ligada no catálogo.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Esquema de autenticação `Platform` e cookie/JWT próprios.
- [ ] CLI `bemaqui platform-admin create`.
- [ ] Middleware de allowlist de IP e de roteamento por host.
- [ ] Reuso dos componentes de senha, bloqueio, rate limit e TOTP.
- [ ] Filtro de autorização do Hangfire Dashboard.
- [ ] Telas de login e 2FA da superfície Plataforma.

## Quality gates
- Agentes previstos: @dev, cybersecurity, @qa.
- Gate: testes de tokens cruzados e de 404 em host de tenant obrigatórios.

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

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** IPs da allowlist e acesso de fora deles; status da recusa (404 proposto).
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story nova, criada a partir da revisão pré-Fase 0 e da ADR 0005 D2 | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
- **API (FND-13), 18/10/2026, branch `feat/fnd-13-super-admin`.** Decisões na ADR 0012, que tem a sessão em cookie opaco e os tempos padrão de 30 min de inatividade e 8 h de teto. A allowlist segue a ADR 0010 item 7.
  - **CLI (CA 1):** `platform-admin create --email` cria o super admin convidado e mostra, uma única vez, o link de definição de senha (24 h). Não existe rota pública de cadastro.
  - **Login (CA 3):** `POST /api/v1/platform/auth/login` sempre devolve um desafio, de configuração no primeiro acesso e de verificação depois. A sessão só nasce em `/2fa/confirm` ou `/2fa/verify`.
  - **Esquema `Platform` (CA 4):** usa o cookie `__Host-bq_pa`, preso ao host da plataforma e conferido no catálogo. Ele ignora `Authorization`, então um token de tenant não vale na plataforma, e o cookie da plataforma não vale num tenant.
  - **Roteamento por host (CA 5):** `/api/v1/platform` e `/hangfire` respondem 404 nos hosts de tenant. No host da plataforma, só elas existem.
  - **Allowlist (CA 2):** fica no nginx; `Platform:AllowedNetworks` é uma camada opcional na app. Fora da lista, a resposta é 404, antes do login.
  - **Hangfire (CA 6):** sem sessão, `/hangfire` redireciona para `/entrar?volta=/hangfire`. Com sessão, abre o painel, e o filtro do dashboard confere o esquema `Platform`.
  - **Regras compartilhadas (CA 7):** bloqueio, resposta uniforme, anti-replay atômico, 5 tentativas por desafio e o mesmo rate limit. `Totp`, `Base32`, `OpaqueToken` e `PasswordPolicy` foram para `BuildingBlocks.Security`.
  - **Auditoria (CA 8):** os eventos vão para a `audit.audit_log` do catálogo (`entity = "PlatformAuth"`), com IP e UTC.
  - **Inatividade (CA 9):** configurável em `PlatformAuth`.
  - **Anti-CSRF:** todo POST em `/api/v1/platform` exige `Origin` e `X-CSRF: 1`.
  - **Testes:** 8 de integração.
  - **Pendente:** telas Entrar, Definir senha e 2FA da superfície Plataforma, depois da FND-09a/b/c; E2E.

- **Telas (FND-13), 18/10/2026, branch `feat/fnd-03b-plataforma`.**
  - `/plataforma/entrar`: senha e, em seguida, a configuração do 2FA no primeiro acesso, ou o desafio nos seguintes.
  - `/plataforma/definir-senha`: o link da CLI.
  - Layout próprio (Tenants, Jobs e Sair).
  - O mutator trata o 401 da plataforma levando ao login dela.
  - Caminhos sob `/plataforma` (adendo da ADR 0012).
  - Ajuda: `plataforma-entrar.md`, `plataforma-tenants.md` e `plataforma-tenant.md`.

## QA Results
_A preencher pelo @qa._