# FND-01 — Estrutura da solução, CI e cadeia de suprimentos

| Campo | Valor |
|---|---|
| ID | FND-01 |
| Título | Estrutura da solução, CI e cadeia de suprimentos |
| Tamanho | M |
| Status | Draft |
| Requisitos | PRD §6 (Segurança: OWASP Top 10) · base técnica de todos os R-PLT e R-SEC |
| Dependências | Nenhuma. **Pré-condição:** ADR 0006 redigida e aceita pelo PO (ver Pontos em aberto) |
| Bloqueia | Todas as demais stories da Fase 0 |
| Ordem de execução | 1 |

## User story
Como **desenvolvedor do BemAqui Empresas**, quero **a solução criada com a estrutura modular, o ambiente local e o CI com verificação de segurança**, para **que toda story seguinte comece de uma base que já impede acoplamento entre módulos e dependência vulnerável**.

## Contexto e referências
- `CLAUDE.md`: Stack, Arquitetura (árvore `src/`), Comandos, "Não usar" (MediatR/AutoMapper).
- `docs/02-arquitetura.md` §1 (monolito modular), §3 (estrutura do backend), §4 (frontend), §7 (CI no GitHub Actions).
- `docs/revisoes/2026-10-16-revisao-pre-fase0.md` §2.2 (ADR 0006: interface de dados por módulo, referência só por Id, NetArchTest "só `*.Contracts`") e §2.3 (CAs de segurança da FND-01).
- Revisão §3 (Licenças): incluir FluentValidation na stack; confirmar a faixa da licença QuestPDF Community.

## Critérios de aceite
1. **Dado** um clone limpo do repositório, **quando** executo `dotnet build && dotnet test` na raiz, **então** a solução compila e os testes passam.
2. **Dado** o projeto `src/Web`, **quando** executo `pnpm i && pnpm build`, **então** o build termina sem erro, com TypeScript em modo `strict`.
3. **Dado** a solução, **quando** inspeciono `src/Modules/`, **então** existem os 13 módulos do CLAUDE.md, cada um com os projetos `Domain`, `Application`, `Infrastructure`, `Endpoints` e `Contracts`, vazios mas compilando.
4. **Dado** o teste de arquitetura (NetArchTest), **quando** um módulo referencia `Domain`, `Application`, `Infrastructure` ou `Endpoints` de outro módulo, **então** o teste falha. Referência a `*.Contracts` de outro módulo é permitida.
5. **Dado** o teste de arquitetura, **quando** algum projeto referencia MediatR ou AutoMapper, **então** o teste falha.
6. **Dado** o teste de arquitetura, **quando** um projeto `Domain` referencia EF Core, ASP.NET Core ou Npgsql, **então** o teste falha.
7. **Dado** `docker compose up -d postgres`, **quando** rodo `dotnet run --project src/Host`, **então** o Host sobe com PostgreSQL 17 local e `/health` responde 200.
8. **Dado** um push ou PR, **quando** o workflow de CI roda, **então** ele executa build, `dotnet test` (com Testcontainers), `pnpm build`, `pnpm lint` e `pnpm test`, e o PR não pode ser integrado com o CI vermelho.
9. **[Segurança]** **Dado** o CI, **quando** `dotnet list package --vulnerable --include-transitive` encontra pacote com vulnerabilidade alta ou crítica, **então** o job falha.
10. **[Segurança]** **Dado** o CI, **quando** `pnpm audit --audit-level high` encontra vulnerabilidade, **então** o job falha.
11. **[Segurança]** **Dado** os arquivos em `.github/workflows/`, **quando** o CI verifica cada `uses:`, **então** toda action está fixada por SHA de commit completo (40 caracteres), com a versão em comentário, e uma action sem SHA faz o job falhar.
12. **[Segurança]** **Dado** o repositório, **quando** inspeciono `.github/dependabot.yml`, **então** o Dependabot está ativo para `nuget`, `npm` e `github-actions`.
13. **Dado** o `src/Web`, **quando** um arquivo `.ts`, `.tsx` ou `.css` fora do arquivo de tokens contém cor hex literal, **então** o `pnpm lint` falha (revisão §2.4; os tokens chegam na FND-09a).

## Regras de negócio e técnicas
- .NET 10 com `Nullable` e `TreatWarningsAsErrors` em `Directory.Build.props`; versões centralizadas em `Directory.Packages.props`.
- Sem MediatR e sem AutoMapper: handlers simples injetados e mapeamento manual. FluentValidation entra na stack (revisão §3).
- Código e nomes de projeto em inglês; textos de UI em português.
- Módulos se comunicam só por `*.Contracts` e por eventos via outbox (a outbox chega na FND-07). Entre módulos, referência só por Id (ADR 0006).
- Cada módulo terá uma interface de acesso aos próprios dados (ex.: `IFinanceDb`) e configurações EF `internal` (ADR 0006). Nesta story, só o esqueleto; o `TenantDbContext` chega na FND-02.
- Frontend: React 19, Vite, TypeScript `strict`, pnpm.
- CI no GitHub Actions; Testcontainers exige Docker no runner.

## Permissões, eventos e tabelas
Não se aplica.

## Telas e estados
Sem UI de negócio. O `src/Web` só abre uma página "BemAqui Empresas" para provar o build.

## Plano de testes
- **Arquitetura (xUnit + NetArchTest):** CA 4, 5 e 6, em `tests/Architecture.Tests`.
- **Integração (Testcontainers):** teste de fumaça que sobe PostgreSQL 17 e abre uma conexão, provando que o runner tem Docker.
- **Front:** Vitest com um teste de fumaça; a regra de lint do CA 13 é provada por um arquivo de exemplo que deve falhar.
- **CI:** script que falha se algum `uses:` não tiver SHA (CA 11).

## Fora do escopo
- `TenantDbContext`, catálogo e middleware de tenant (FND-02).
- Deploy e imagens de produção (FND-10).
- Tokens de design e componentes (FND-09a/b/c).

## Pontos em aberto
- **ADR 0006 (persistência e mensageria):** proposta na revisão §2.2, mas o arquivo `docs/adr/0006-*.md` ainda não existe. Precisa ser redigida e aceita pelo PO antes de esta story entrar em execução, porque fixa as regras do teste de arquitetura.
- **Licença QuestPDF Community:** a revisão §3 pede confirmar a faixa de receita. Não bloqueia a Fase 0 (PDF só na Fase 2), mas a decisão deve ser registrada antes de incluir o pacote.
- **Runner do CI:** não está registrado se o runner é hospedado pelo GitHub ou próprio.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes passando no CI (unidade, arquitetura e fumaça de integração).
- [ ] Migration criada: não se aplica.
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Tela com estados de vazio/carregando/erro: não se aplica.
- [ ] Ajuda da tela escrita ou atualizada: não se aplica.
- [ ] Padrões e próximo passo propositivos avaliados: não se aplica.
- [ ] Auditoria ligada: não se aplica.
- [ ] Story marcada como concluída no `docs/05-backlog.md`; ADR 0006 referenciada.

## Tarefas técnicas sugeridas
- [ ] Criar `BemAquiEmpresas.sln`, `Directory.Build.props` e `Directory.Packages.props`.
- [ ] Criar `src/Host`, `src/BuildingBlocks` e os 13 módulos com os 5 projetos cada.
- [ ] Criar `tests/Architecture.Tests`, `tests/BuildingBlocks.Tests` e o projeto de fumaça de integração.
- [ ] Criar `src/Web` (Vite, React 19, TS strict, ESLint, Vitest, pnpm).
- [ ] Regra de lint contra cor hex literal, exceto no arquivo de tokens.
- [ ] `docker-compose.yml` local com `postgres:17`.
- [ ] `/health` mínimo no Host (detalhes na FND-12).
- [ ] Workflow de CI com actions fixadas por SHA, `dotnet list package --vulnerable`, `pnpm audit` e verificação de SHA.
- [ ] `.github/dependabot.yml` para nuget, npm e github-actions.
- [ ] Atualizar `CLAUDE.md` (Stack) com FluentValidation.

## Quality gates
- Agentes previstos: @architect (estrutura e teste de arquitetura), @devops (CI), @qa.
- Gate: CI verde; o PR mostra o teste de arquitetura falhando quando um acoplamento proibido é forçado.

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
| 9 | Telas com estados, ajuda e comportamento propositivo | N/A |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** ADR 0006 redigida e aceita.
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story criada a partir do backlog e da revisão pré-Fase 0 | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
_A preencher pelo @dev._

## QA Results
_A preencher pelo @qa._
