# PRD-06a1 — Conclusão da OS e fabricação concluída do projeto

| Campo | Valor |
|---|---|
| ID | PRD-06a1 |
| Título | Conclusão explícita da OS de fabricação a partir de "Pronta para concluir" (`production.approve`, login completo); projeto "Fabricado" quando todas as OS de fabricação não canceladas concluem, com `ProjectFabricated` por evento liberando a parcela "fabricação concluída"; sem entrada no estoque; registro manual bloqueado quando o projeto tem OS; estorno da conclusão |
| Tamanho | P |
| Status | Done |
| Requisitos | R-PRD-7 (conclusão da OS; produto acabado na PRD-06a2) · R-PRJ-6 (marco "fabricação concluída" libera parcela) · R-COM-4 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | PRD-05, PRD-04, COM-04b |
| Bloqueia | PRD-06a2 |
| Ordem de execução | 8 (Marco 3.1 "Fábrica visível") |

## User story
Como **supervisor de produção**, quero **concluir a OS quando o equipamento passou por todas as etapas e pela qualidade**, e como **financeiro**, quero **que a parcela "fabricação concluída" seja liberada sozinha quando todas as OS do projeto terminarem**, para **o cliente ser cobrado na hora certa sem ninguém precisar lembrar de registrar a fabricação**.

## Contexto e referências
- PRD §5.7 R-PRD-7; §5.5 R-PRJ-6.
- `04-fluxos-estados` §1 "OS (fabricação)" e "Projeto" (`→ Fabricated` "todas OS de fabricação concluídas; até a PRD-06, registro manual 'Registrar fabricação concluída'"); §2: `ProductionOrderCompleted`; se todas as OS do projeto → `ProjectFabricated` → Finance libera os recebíveis com gatilho `FabricationDone` (vencimento = data do evento + prazo).
- **Código atual:** o `ProjectFabricated` já existe em `Commercial.Contracts` e é publicado pelo endpoint manual `POST /projects/{id}/fabrication` (COM-04b, `projects.edit`); a COM-04b libera os títulos `FabricationDone` com o registro de gatilhos `project_trigger_events` (ADR 0019 §1), idempotente por `(project_id, event)`. `IReceivables.ForContractAsync` (`Finance.Contracts`) devolve as parcelas com `MilestoneId`, `Status` e `HasSettlement`.
- **Revisão pré-Fase 3:** `docs/revisoes/2026-10-21-revisao-pre-fase3.md` (D1 quebra e ordem, D12, tabela 1.1 itens 81–83, §2.1 "Conclusão e romaneio", §2.2 ponto 83, §2.3 padrões 4, 5, 10, §4 Marco 3.1).
- **ADR 0023 §7** (D12: "Pronta para concluir" e conclusão explícita; estorno `Completed → InProgress` e `Fabricated → InProgress`); **ADR 0026** §2 e §4 e **ADR 0003, adendo de 21/10/2026** (a conclusão fica com `production.approve` e login completo, fora da sessão por PIN); ADR 0019 §1 (registro de gatilhos), §18 (notificação síncrona).
- Quebra da PRD-06a (D1): o produto acabado, as séries, os lotes consumidos e a liberação das reservas foram para a **PRD-06a2** (Marco 3.4).

## Critérios de aceite
**Pronta para concluir**
1. **Dado** uma OS de fabricação com todas as etapas `Done` (a última finalizada pela PRD-03a ou aprovada na qualidade pela PRD-05), **quando** a última etapa grava, **então** a OS passa ao estado gravado **`ReadyToComplete`** ("Pronta para concluir", D12, ADR 0023 §7) com o chip informativo; a migration passa a esse estado as OS que já estão com todas as etapas `Done`; o estado aparece na lista de OS e no Kanban, e a lista de OS abre para quem tem `production.approve` no filtro "Aguardando você" (OS prontas para concluir).

**Concluir**
2. **Dado** `production.approve` numa sessão completa e uma OS `ReadyToComplete` sem apontamento aberto, **quando** clico "Concluir OS", **então** o diálogo de impacto mostra: a OS passa a "Concluída"; se é a última OS de fabricação não cancelada do projeto, o projeto passa a "Fabricado" e as parcelas "fabricação concluída" que serão liberadas, com valor e vencimento (com `finance.view`; sem ela, "Libera a parcela do marco {marco}."); e a linha "O produto acabado ainda não entra no estoque nesta versão." com `<Dica>`.
3. **Dado** a confirmação, **quando** grava, **então** na mesma transação a OS vai a `Completed` (`actual_end`, `completed_by`) e o evento `ProductionOrderCompleted` (`{ productionOrderId, projectId, quantity, completedOn }`) vai para o outbox; **nada** entra no estoque nesta story; a mensagem diz "OS concluída".
4. **Dado** uma OS fora de `ReadyToComplete` (etapa pendente, qualidade não aprovada, apontamento aberto) ou cancelada, **quando** tento concluir, **então** 409 com o motivo ("Falta aprovar a Qualidade", "Há apontamento aberto na etapa {etapa}") e o botão fica desabilitado com o mesmo texto.
5. **Dado** a OS, **quando** concluo, **então** ela conclui com a quantidade inteira (ponto 82): não existe conclusão parcial nem campo de quantidade.
6. **[Segurança]** **Dado** um usuário só com `production.approve_quality`, ou uma sessão aberta por PIN (`amr = ["pin"]`), **quando** chama "Concluir OS" ou "Estornar conclusão", **então** 403 (`forbidden` ou `pin_session_scope` quando a PRD-03b existir): os endpoints exigem `production.approve` e não entram na lista permitida da sessão por PIN (ADR 0026 §4; D6).

**Fabricação concluída do projeto**
7. **Dado** a última OS de fabricação não cancelada do projeto concluída, **quando** o consumidor do Commercial recebe o `ProductionOrderCompleted` e confere por `IProductionOrders.ByProjectAsync` que todas as OS **de fabricação** não canceladas estão `Completed` (as OS de instalação não contam), **então** passa o projeto a `Fabricated` (`fabricated_on` = data da conclusão) e publica `ProjectFabricated` (`{ projectId, contractId, occurredOn }`); a COM-04b libera os títulos com gatilho `FabricationDone` (`Planned → Open`, vencimento = data + prazo), mesmo que o evento chegue antes do título (registro de gatilhos, ADR 0019 §1).
8. **Dado** o projeto já `Fabricated` à mão antes desta story, **quando** a última OS conclui, **então** o Commercial não muda o projeto nem publica o evento de novo, e o Finance não libera duas vezes (idempotente por `(project_id, event)`).
9. **Dado** um projeto com ao menos uma OS de fabricação não cancelada, **quando** alguém tenta "Registrar fabricação concluída" à mão (COM-04b), **então** 409 "A fabricação deste projeto é concluída pelas OS ({n} abertas)." com o link para as OS, e o botão some (ponto 81); o registro manual continua para projeto **sem** OS de fabricação.

**Estorno da conclusão**
10. **Dado** `production.approve` numa sessão completa e uma OS `Completed`, **quando** clico "Estornar conclusão" com motivo (obrigatório, até 500) e confirmo no diálogo `destructive` com o impacto (a OS volta a "Em andamento"; o projeto volta de "Fabricado" a "Em andamento" quando foi fabricado pelas OS; a parcela já liberada continua em aberto), **então** a OS vai de `Completed` a `InProgress` com a última etapa `Ready`, e o evento `ProductionOrderCompletionReversed` (`{ productionOrderId, projectId, reversedOn }`) vai para o outbox; o consumidor do Commercial passa o projeto de `Fabricated` a `InProgress` quando ele foi fabricado pelas OS (não à mão) e notifica quem tem `finance.edit`: "Conclusão da {OS} estornada. A parcela {marco} já liberada continua em aberto." com link; **o título a receber não é estornado** e o registro de gatilho fica (a nova conclusão não libera de novo).
11. **[Segurança]** **Dado** a parcela "fabricação concluída" do projeto já em OP ou recebida (baixa total ou parcial, `HasSettlement`), **quando** tento estornar a conclusão da OS que fabricou o projeto, **então** 409 "A parcela {marco} já foi recebida. Fale com o Financeiro antes de estornar." (ponto 83), conferido de forma síncrona antes de gravar (o Production consulta o Commercial, que lê as parcelas por `IReceivables.ForContractAsync`); o projeto em `Installing`, `Delivered` ou `Closed` também recusa (409 "O projeto já passou da fabricação.").

**Consulta, permissões e auditoria**
12. **Dado** `production.view`, **quando** abro a OS concluída, **então** o `OrderHeader` mostra "Concluída em {data} por {nome}" e o projeto mostra na linha do tempo "Fabricação concluída pelas OS" com a data.
13. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; concluir e estornar sem `production.approve`: 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/complete` e `/production-orders/{id}/reverse-completion` (OS de outro tenant ou de instalação: 404).
14. **[Segurança]** **Dado** duas conclusões (ou conclusão e estorno) simultâneas da mesma OS, **quando** chegam juntas, **então** só uma grava (`xmin`), e o `ProjectFabricated` não duplica (inbox e idempotência por origem).
15. **Dado** a conclusão ou o estorno, **quando** abro o histórico da OS e a linha do tempo do projeto, **então** vejo quem, quando, o motivo do estorno e o efeito no projeto.

## Regras de negócio e técnicas
- **Estado `ReadyToComplete`** entre `InProgress`/`Quality` e `Completed` (D12); o `04-fluxos` §1 é atualizado.
- **Projeto `Fabricated` automático** substitui o manual para projetos com OS de fabricação (`04-fluxos` §1 previa a troca "até a PRD-06"); `fabricated_by_orders` no projeto distingue do registro manual (para o estorno).
- **Sem estoque nesta story:** as OS concluídas antes da PRD-06a2 ficam sem entrada no estoque (documentado na PRD-06a2 e na ajuda).
- **Estorno** não mexe em título a receber; a decisão é do Financeiro (ponto 83).
- Guid v7; `xmin`; auditoria `IAuditable`; notificações dentro do módulo síncronas (ADR 0019 §18).

## Permissões, eventos e tabelas
- **Permissões:** `production.approve` (concluir, estornar), `production.view`, `finance.view` (parcelas no diálogo), `finance.edit` (recebe o aviso do estorno). Nenhuma permissão nova.
- **Eventos (`Production.Contracts`):** **publica** `ProductionOrderCompleted` e `ProductionOrderCompletionReversed`; **consumidor:** Commercial (projeto `Fabricated`/`InProgress`, `ProjectFabricated`, aviso ao Financeiro).
- **Contratos:** `IProductionOrders.ByProjectAsync` (com o tipo da OS); no Commercial, consulta síncrona do estorno (parcela "fabricação concluída" recebida, projeto além de `Fabricated`) lendo `IReceivables.ForContractAsync`.
- **Tabelas:** `production.production_orders` (`status` `ReadyToComplete`, `actual_end`, `completed_by`, `completion_reversed_at`, `completion_reversal_reason`); `commercial.projects` (`fabricated_by_orders`).

## Telas e estados
- Padrões de tela da Fase 3: 4, 5, 10.
- **OS → "Concluir OS"** e **"Estornar conclusão"** no `OrderHeader`; chip "Pronta para concluir" na lista, no Kanban e na OS.
- **Verbos:** "Concluir OS" / "OS concluída"; "Estornar conclusão" / "Conclusão estornada".
- **Estados:** botão desabilitado com o motivo enquanto falta etapa ou há apontamento aberto; erro dentro do diálogo; vazio do filtro "Aguardando você" ("Nenhuma OS pronta para concluir.").
- **Propositivo:** depois de concluir, `NextSteps` "Ver parcela liberada" (com `finance.view`, quando liberou) e "Ver projeto"; "Novo romaneio" entra com a PRD-06b.
- **400 px:** diálogo em tela cheia.
- **Ajuda:** atualização de `Ajuda/producao-os.md` ("Pronta para concluir", "Concluir OS", "Estornar conclusão" e o aviso de que o produto acabado ainda não entra no estoque) e de `Ajuda/projetos-projeto.md` ("Fabricação concluída automática" e por que o botão manual some); `<Dica>` em "Pronta para concluir" ("Todas as etapas e a qualidade foram feitas. Falta o supervisor concluir a OS.").

## Plano de testes
- **Unidade:** transições `ReadyToComplete`, `Completed` e estorno (CA 1, 3, 10); regra "última OS de fabricação do projeto" sem contar as de instalação e as canceladas (CA 7).
- **Integração (Testcontainers, obrigatória: libera recebível):** estado gravado e migration das OS já prontas (CA 1); diálogo e conclusão com evento na mesma transação, sem estoque (CA 2, 3); bloqueios (CA 4); `production.approve_quality` recusada (CA 6); última OS → `Fabricated` → título `FabricationDone` liberado ponta a ponta, inclusive com o evento antes do título (CA 7); já fabricado à mão (CA 8); manual recusado com OS e permitido sem OS (CA 9); estorno com aviso ao Financeiro e reconclusão sem liberar de novo (CA 10); estorno recusado com parcela recebida e projeto entregue (CA 11); três camadas e IDOR (CA 13); concorrência (CA 14); histórico e linha do tempo (CA 15).
- **E2E (Playwright):** concluir a última OS de um projeto de teste e ver o projeto "Fabricado" e a parcela liberada sozinha (validação do PO no Marco 3.1).

## Fora do escopo
- Entrada do produto acabado, séries, lotes consumidos e liberação das reservas: PRD-06a2 (Marco 3.4).
- Romaneio e expedição: PRD-06b. NF-e de saída: Fase 5.
- Conclusão parcial (parte das unidades): fora do MVP (ponto 82).
- Estorno do título a receber: decisão do Financeiro, fora desta story.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, D1, D12 e tabela 1.1, itens 81–83):
1. Registro manual de fabricação bloqueado quando o projeto tem OS de fabricação; mantido para projeto sem OS (CA 9).
2. Conclusão parcial fora; a OS conclui com a quantidade inteira (CA 5).
3. Estorno da conclusão: `Completed → InProgress` e projeto `Fabricated → InProgress`, sem estornar o título; recusado se o título já estiver em OP ou recebido; notifica o Financeiro (CA 10, 11).
4. A OS fica "Pronta para concluir" depois da última etapa e a conclusão é explícita, com `production.approve` e login completo (D12, D6; CA 1, 2, 6).
5. Quebra da PRD-06a (D1): esta story vai ao Marco 3.1, sem entrada no estoque; o PO valida no Marco 3.1 a parcela "fabricação concluída" liberada sozinha.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos (CA 6 com a sessão por PIN quando a PRD-03b existir; ver Change Log 1.0).
- [x] Testes de unidade e E2E passando; integração (Testcontainers, cadeia até o recebível) escrita em `FabricationTests.Completion.cs`, a rodar no Docker (`scripts/test-docker.sh`).
- [x] Migration `ProductionOrderCompletion` criada (só `ADD COLUMN` e o `UPDATE` das OS prontas); falta o teste sobre cópia do banco do tenant `konebras` (no deploy).
- [x] ADR 0023 §7 aplicada (D12 e estorno da conclusão), com nota de aplicação.
- [x] Permissões só do `ModuleCatalog` (nenhuma nova: `production.approve`, `production.view`, `finance.view`, `finance.edit`).
- [x] Telas com estados e 400 px.
- [x] Ajudas atualizadas; teste de cobertura passando.
- [x] Diálogo de impacto e próximos passos aplicados.
- [x] Auditoria ligada (`ProductionOrder`, etapas e `Project` são `IAuditable`).
- [ ] OpenAPI e cliente Orval regenerados: `openapi.json` editado à mão e cliente gerado com `pnpm gen:api`; regenerar com `BQ_UPDATE_OPENAPI=1` no Docker.
- [x] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` §1 e §2 (`ReadyToComplete`, projeto `Fabricated` automático, estorno `Fabricated → InProgress`) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Estado `ReadyToComplete` e `ProductionOrderCompletionService` (concluir, estornar).
- [ ] Consumidor do Commercial (`Fabricated`, `ProjectFabricated`, estorno, aviso ao Financeiro) e bloqueio do registro manual.
- [ ] Consulta síncrona do estorno no Commercial (parcela recebida, projeto além de `Fabricated`).
- [ ] Diálogo de conclusão e de estorno, filtro "Aguardando você", ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (cadeia Production → Commercial → Finance), @qa.
- Gate: CA 3, 6, 7, 10, 11 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK (ajustada: sem o produto acabado) |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (Marco 3.1) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação, na PRD-06a):** quebra da PRD-06 (expedição na PRD-06b); movimento `Production` fora do custo do projeto; `ProjectFabricated` automático substituindo o manual só para projeto com OS; estorno da conclusão sem mexer na parcela liberada.
- **Correções da revalidação (21/10/2026):** quebra da PRD-06a (D1): esta story fica com a conclusão, o projeto "Fabricado", a parcela e o estorno, sem estoque; estado gravado `ReadyToComplete` (D12); conclusão só com `production.approve` e login completo (D6, CA 6); registro manual bloqueado com OS (ponto 81); sem conclusão parcial (ponto 82); estorno recusado com a parcela em OP ou recebida e aviso ao Financeiro (ponto 83, CA 11); filtro "Aguardando você".
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PRD-06, como PRD-06a) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções (PRD-06a) | Pax (PO) |
| 21/10/2026 | 0.3 | Quebra da PRD-06a (D1): conclusão da OS no Marco 3.1, sem estoque; decisões do PO (D6, D12, tabela 1.1 itens 81–83) e correções da revisão pré-Fase 3 (§2.1 conclusão e fluxos, ADR 0023 §7; §2.2 ponto 83; §2.3 padrões 4, 5, 10) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 22/10/2026 | 1.0 | Implementada (`Done`). Desvios: (1) "título em OP" não se aplica a título a receber: o estorno é recusado pela baixa total ou parcial (`HasSettlement`, `PartiallyPaid`, `Paid`) da parcela "fabricação concluída" do projeto fabricado pelas OS, lida por `IProjects.FabricationAsync` (contrato e aditivos aprovados); (2) estorno com a última etapa de qualidade deixa a OS `Quality` (regra única de situação), não `InProgress`; o checklist da tentativa continua marcado; (3) a sessão por PIN (PRD-03b) ainda não existe: os endpoints exigem `production.approve` e ficam fora de qualquer lista do chão de fábrica; o `pin_session_scope` e o teste da lista permitida entram com a PRD-03b; (4) o aviso ao Financeiro sai só quando o projeto voltou (fabricado pelas OS) e há parcela "fabricação concluída" já liberada, um por parcela, com link para o título; (5) a linha do tempo do projeto mostra a conclusão e o **último** estorno de cada OS (com quem e o motivo); o histórico da OS guarda todos; depois do estorno, "Fabricação concluída pelas OS" some da linha do tempo (fica no histórico do projeto); (6) conclusão e estorno são métodos do `FabricationOrderService` (partial `Completion`), não um `ProductionOrderCompletionService` separado; regras sem banco em `CompletionRules`; (7) colunas: `completed_by`, `completion_reversed_at`, `completion_reversed_by`, `completion_reversal_reason` (no lugar de `ready_to_complete_at` e `reopen_*` previstos no modelo); (8) o `Down` da migration volta `ReadyToComplete` a `InProgress`; a volta automática do deploy **sem** o `Down` deixaria a versão anterior sem ler `ReadyToComplete` (enum em texto): rodar o `Down` antes de voltar; (9) a lista de OS abre em "Aguardando você" (`?situacao=pronta`) para quem tem `production.approve`, com a opção "Todas"; o `DataTable` ganhou `filteredEmpty` para o vazio desse filtro; (10) o Kanban e o job de OS atrasada passam a incluir `ReadyToComplete`; (11) "evento antes do título" (CA 7) fica coberto pelos testes do registro de gatilhos da COM-04b; (12) `openapi.json` editado à mão, a regenerar com `BQ_UPDATE_OPENAPI`; testes de integração escritos e não executados (fila do Docker) | Dex (dev) |
| 22/10/2026 | 1.1 | Decisão do PO: o Financeiro (`finance.edit`) é avisado de todo estorno de conclusão que devolve o projeto. Sem parcela liberada, recebe um aviso único ("Nenhuma parcela tinha sido liberada: não há nada a ajustar no financeiro.") com link para o projeto. Isso substitui o desvio 4 da 1.0. Migration refeita sobre a main (`20261022192931`) | Dex (dev) |
