# FND-09c — DataTable, estados de tela e formulário base

| Campo | Valor |
|---|---|
| ID | FND-09c |
| Título | Cliente Orval + TanStack Query, DataTable server-side, Skeleton/EmptyState/ErrorState/InlineError, formulário RHF + Zod, confirmação e histórico por registro |
| Tamanho | M |
| Status | Done |
| Requisitos | PRD §6 Desempenho (listagem < 1 s com 50 mil registros) · R-SEC-5 (histórico consultável por registro) · R-PRP-2 (próximo passo após concluir) |
| Dependências | FND-09a; FND-07 (PagedList, ProblemDetails e endpoint de auditoria) |
| Bloqueia | Todas as telas com lista ou formulário (FND-03b, FND-04, FND-05, FND-06b, FND-06c, FND-15) |
| Ordem de execução | Paralela, depois da FND-09a; o histórico por registro depois da FND-07 |

## User story
Como **desenvolvedor das telas**, quero **componentes prontos de lista, estados e formulário ligados à API gerada**, para **que toda tela tenha carregando, vazio e erro, valide em PT-BR e nunca chame `fetch` à mão**.

## Contexto e referências
- `CLAUDE.md`: toda tela tem estados de carregando, vazio (com ação para começar) e erro (o que houve e como resolver); ações com verbo claro e confirmação com o mesmo verbo; API com cliente TS gerado por Orval, sem `fetch` à mão; React Hook Form + Zod; TanStack Query.
- `docs/02-arquitetura.md` §4: tabelas com filtro, ordenação e paginação server-side (TanStack Table); estado do servidor só no TanStack Query.
- Backlog original da FND-09 (CA): tabela padrão com filtro, ordenação e paginação server-side; estados em todos os componentes de lista.
- Revisão §2.4: Skeleton, EmptyState, ErrorState e InlineError ligados ao TanStack Query; Switch e ToggleGroup do Radix.

## Critérios de aceite
**API**
1. **Dado** o OpenAPI do Host, **quando** rodo `pnpm gen:api`, **então** o Orval gera o cliente TS e os hooks do TanStack Query; o CI falha se o cliente gerado estiver desatualizado em relação ao OpenAPI.
2. **Dado** o código do front, **quando** o lint roda, **então** chamada direta a `fetch`/`axios` fora do mutator do Orval falha.
3. **Dado** uma resposta 401 por access token vencido, **quando** o mutator a recebe, **então** faz um único refresh (FND-04) e repete a requisição; se o refresh falhar, leva ao login preservando a rota.

**DataTable**
4. **Dado** um endpoint paginado (`PagedList` da FND-07), **quando** uso o `DataTable`, **então** filtro, ordenação e paginação são enviados ao servidor e refletidos na URL (link compartilhável).
5. **Dado** um endpoint de teste com 50 mil registros, **quando** a tabela pede uma página com filtro e ordenação, **então** a resposta da API volta em menos de 1 s (medido no teste de integração).
6. **Dado** o `DataTable` carregando, vazio ou com erro, **quando** cada estado ocorre, **então** mostra `Skeleton` (linhas fantasmas), `EmptyState` (texto e ação para começar, fornecidos pela tela) ou `ErrorState` (o que houve e "Tentar de novo"); um filtro sem resultado mostra "Nenhum resultado para os filtros" com "Limpar filtros".
7. **Dado** a tabela a 400px, **quando** renderizada, **então** as colunas marcadas como secundárias somem ou a tabela vira lista de cartões, sem rolagem horizontal da página.

**Formulário**
8. **Dado** um formulário com Zod, **quando** um campo é inválido, **então** a mensagem aparece em PT-BR junto ao campo (`InlineError`), com `aria-describedby`, e o foco vai para o primeiro campo com erro ao enviar.
9. **Dado** uma resposta ProblemDetails com `errors` por campo (FND-07), **quando** o envio falha, **então** os erros do servidor aparecem nos campos correspondentes; erros sem campo aparecem no topo do formulário.
10. **Dado** um formulário alterado, **quando** tento sair da tela, **então** a tela pergunta com os verbos "Salvar" e "Descartar".
11. **Dado** uma ação destrutiva ou com efeito (ex.: "Desativar"), **quando** clico, **então** o `ConfirmDialog` usa o mesmo verbo no botão de confirmar, e o toast de sucesso usa o particípio ("Desativado").

**Próximo passo e histórico**
12. **Dado** uma ação concluída, **quando** a tela passa `nextStep` ao toast/painel de sucesso, **então** o usuário vê a continuação sugerida com link direto (padrão para R-PRP-2).
13. **Dado** um registro auditado, **quando** abro o componente `RecordHistory`, **então** vejo quem, quando (America/Sao_Paulo) e o que mudou (antes → depois), paginado, a partir do endpoint de auditoria da FND-07; dados mascarados continuam mascarados.

## Regras de negócio e técnicas
- Orval com mutator único (base URL relativa à mesma origem, access token em memória, cabeçalho anti-CSRF quando exigido, refresh).
- TanStack Table em modo manual (server-side); estado de tabela sincronizado com a busca da URL pelo TanStack Router.
- Zod com mensagens PT-BR centralizadas (`z.setErrorMap`).
- `Money`, `StatusChip` e formatação de datas vindos da FND-09a.

## Permissões, eventos e tabelas
- Sem tabela nova. Usa `GET /api/v1/audit/{entity}/{id}` (FND-07).

## Telas e estados
- Componentes: `DataTable`, `Skeleton`, `EmptyState`, `ErrorState`, `InlineError`, `Form` (RHF + Zod), `ConfirmDialog`, `Toast` com próximo passo, `RecordHistory`.
- Na galeria de desenvolvimento (FND-09a), cada componente aparece nos três estados.
- **Ajuda:** não se aplica (componentes); cada tela que os usa escreve a própria ajuda.

## Plano de testes
- **Unidade (Vitest + Testing Library):** estados do `DataTable` (CA 6); mapeamento de ProblemDetails para campos (CA 9); `ConfirmDialog` com verbo (CA 11); `RecordHistory` (CA 13).
- **Integração (Testcontainers):** endpoint de teste com 50 mil linhas e índice, página em < 1 s (CA 5).
- **E2E (Playwright):** filtro e ordenação refletidos na URL (CA 4); 400px (CA 7); refresh silencioso após 401 (CA 3, depois da FND-04).
- **CI:** cliente Orval atualizado (CA 1) e lint de `fetch` (CA 2).

## Fora do escopo
- Telas de negócio.
- Exportação para CSV/XLSX e ações em lote (R-PRP-6, desejável).
- Edição inline na tabela.

## Pontos em aberto
- **Tamanho de página padrão e máximo** do `DataTable` (sugestão: 25 e 100).
- **Endpoint de teste de desempenho:** usar uma tabela real da Fase 0 (ex.: `audit_log`) ou uma tabela só de teste.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração e E2E passando.
- [ ] Migration: não se aplica.
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Componentes com estados de carregando, vazio e erro.
- [ ] Ajuda: não se aplica.
- [ ] Padrões e próximo passo propositivos: componente de próximo passo disponível.
- [ ] Auditoria: componente `RecordHistory` entregue.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Configurar Orval (`pnpm gen:api`) e o mutator.
- [ ] Verificação no CI de cliente desatualizado; lint contra `fetch` direto.
- [ ] `DataTable` server-side com estado na URL.
- [ ] `Skeleton`, `EmptyState`, `ErrorState`, `InlineError`.
- [ ] Base de formulário RHF + Zod com mapa de erros PT-BR e ProblemDetails.
- [ ] `ConfirmDialog` e `Toast` com próximo passo.
- [ ] `RecordHistory`.

## Quality gates
- Agentes previstos: @dev, @ux-design-expert, @qa.
- Gate: teste de desempenho de 50 mil registros verde.

## Validação do PO
Checklist do @po (Pax), 16/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança da revisão §2.3 que se aplicam | N/A |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** Nenhuma bloqueante.
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story criada pela quebra da FND-09 (revisão §2.3) | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
- **Entrega (FND-09c), 18/10/2026, branch `feat/fnd-09c-datatable-orval`.** Os pontos em aberto foram resolvidos assim: a página padrão é 20 e o máximo 100, iguais ao `PageRequest` da FND-07; o teste de desempenho usa a tabela real `audit.audit_log`. O resto das decisões está no adendo da ADR 0009.
  - **Contrato da API (CA 1):**
    - o OpenAPI do Host fica versionado em `src/Web/openapi.json`, e o teste `OpenApiDocumentTests` falha se ele ficar desatualizado;
    - o Orval gera cliente e hooks do TanStack Query em `src/api/generated`, e o CI falha se o cliente gerado mudar;
    - os endpoints passaram a declarar `Produces<T>`, e o JSON usa `NumberHandling = Strict`.
  - **Lint (CA 2):** `fetch`, `axios` e `XMLHttpRequest` fora do mutator reprovam.
  - **Mutator (CA 3):**
    - o token fica só em memória;
    - no 401 faz um único refresh e repete a requisição; se o refresh falha, vai para `/entrar?volta=`;
    - lê o `X-Perm-Version` e recarrega `/me/permissions` quando muda;
    - converte ProblemDetails em `ApiError`.
  - **Ligações:** `AppProviders` liga o TanStack Query, os toasts, a retomada da sessão pelo cookie, o sino na API e o tema salvo no perfil (fecha o CA 9 da FND-09a).
  - **`DataTable` (CA 4, 6, 7):**
    - página, ordenação e busca no servidor e na URL;
    - estados de carregando, vazio com ação, "Nenhum resultado para os filtros" com "Limpar filtros" e erro com "Tentar de novo";
    - colunas secundárias somem a 400px.
  - **Desempenho (CA 5):** 50 mil linhas em `audit.audit_log` e página 2400 em menos de 1 s.
  - **Formulário (CA 8 a 10):**
    - `useZodForm` com Zod em PT-BR, `Field` com `aria-describedby`, foco no primeiro campo inválido;
    - `applyServerErrors` põe os erros do servidor nos campos e os demais no topo;
    - `UnsavedChangesGuard` pergunta "Salvar" ou "Descartar".
  - **`ConfirmDialog` e toast (CA 11, 12):** mesmo verbo no botão de confirmar, particípio no toast, e `toastDone` com próximo passo.
  - **`RecordHistory` (CA 13):** quem, quando e antes → depois, paginado e com o mascaramento do servidor.
  - **Auditoria e anexos:** o `IPermissionChecker` real (`CurrentUserPermissionChecker`) substituiu o DenyAll da FND-07, e as entidades auditáveis passaram a ser registradas por módulo.
  - **Testes:** 60 no Vitest e 21 no Playwright, com tabela na URL, 400px, formulário e confirmação; integração com o OpenAPI e com as 50 mil linhas.

## QA Results
_A preencher pelo @qa._