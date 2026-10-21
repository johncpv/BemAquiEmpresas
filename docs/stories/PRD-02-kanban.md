# PRD-02 — Kanban da produção

| Campo | Valor |
|---|---|
| ID | PRD-02 |
| Título | Kanban das OS de fabricação com colunas pelo código da etapa (letra do roteiro) e filtro por roteiro, cartões com projeto, cliente, horas apontadas × padrão, prazo, atraso e risco com texto, filtros por projeto, cliente, linha e atraso, "Concluir etapa" pelo menu do cartão (sem arraste), notificação diária "OS atrasada" e atualização em tempo real por SignalR (primeiro hub de negócio, base do painel da TV e do `/fab`) |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-PRD-2 (Kanban por etapa com filtros por projeto, cliente, linha e atraso) · R-COM-2 (notificação "OS atrasada") · PRD §5.14 (painel do supervisor; painel da TV atualiza em menos de 2 s, PRD §6) · R-PRP-5 · R-SEC-5 |
| Dependências | PRD-01 (OS de fabricação com etapas, `IProductionOrders`), FND-02 (SignalR com token do tenant, `PingHub`) |
| Bloqueia | PRD-03a |
| Ordem de execução | 3 (Marco 3.1 "Fábrica visível") |

## User story
Como **supervisor de produção**, quero **ver todas as OS por etapa num quadro que se atualiza sozinho, com o que está atrasado ou em risco destacado**, para **saber onde a fábrica está travando e agir antes de atrasar a entrega**.

## Contexto e referências
- PRD §5.7 R-PRD-2; §5.1 R-COM-2 (notificação "OS atrasada"); §5.14 "Painel do supervisor: Kanban + capacidade + não conformidades abertas + faltas de material" (o painel completo é a DSH-03, Fase 4); §6 (painel atualiza em < 2 s após apontamento).
- `04-fluxos-estados` §2: `StepStatusChanged` (Production) → SignalR painel TV/Kanban; `LaborEntryClosed` → SignalR atualiza painéis. §1 (etapa sequencial no MVP; a etapa só inicia com a anterior `Done`; pela D12, depois da última etapa a OS fica "Pronta para concluir").
- `02-arquitetura` §2.1 (grupos SignalR `{tenantId}:{painel}`); ADR 0006 ("O push do SignalR para painéis acontece logo após o commit e não passa pelo outbox"); ADR 0009 (sino por SignalR com polling de 60 s como reserva).
- **Revisão pré-Fase 3** (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): D10 (Kanban sem arraste, "Concluir etapa", colunas pelo código da etapa, job "OS atrasada"), D12; tabela 1.1, itens 60 a 62; §2.1 "Outros" (`RequireModule` e permissão no `MapHub`, avisos depois do commit); §2.2 PRD-02; §2.3 padrões 2 e 3 e os achados "Kanban sem interativo aninhado e sem `draggable`".
- ADR 0023 §7 (tempo real e avisos só depois do commit, também nos consumidores do outbox, helper do BuildingBlocks); ADR 0026 §3 e §4 (o `ProductionHub` com o grupo do chão de fábrica entra na lista permitida da sessão por PIN; a sessão cai com o security stamp).
- **Código atual:** só existe o `PingHub` (FND-02, `app.MapHub<PingHub>` no `Program.cs`), que prova token e host do tenant; o token do WebSocket chega pela query `access_token` (`TenantJwtAuthentication`). Nenhum hub de negócio.
- Protótipo (Produção): colunas P Projeto, C Corte, M Montagem, Q Qualidade, E Expedição com a letra no display de andar e "{n} OS, {h} h restantes"; cartão com OS, título, cliente, medidor de horas (apontadas/padrão, vermelho acima do padrão), "Previsto {data}" ou "Atrasada, previsto {data}", avatar do operador e "Em execução"; faixa vermelha para atrasada e âmbar para risco; filtros Linha 1, Linha 2, Todos os projetos, "2 atrasadas", "1 em risco"; "Arraste um cartão para simular a mudança de etapa" (desligado pela D10).

## Critérios de aceite
**Quadro**
1. **Dado** `production.view`, **quando** abro "Kanban" (`/app/producao/kanban`), **então** vejo uma coluna por código de etapa (CA 2), cada uma com a letra no estilo display de andar, o nome, a quantidade de OS e as horas restantes (Σ max(padrão − apontadas, 0) das etapas atuais), e um cartão por OS de fabricação `Open`, `InProgress`, `Paused` ou `Quality` na coluna da **etapa atual** (a primeira não `Done`); a OS com todas as etapas `Done` e ainda não concluída fica na última coluna, "Pronta para concluir" (D12; a conclusão é da PRD-06a1).
2. **Dado** OS com roteiros diferentes, **quando** o quadro monta as colunas, **então** as colunas são os **códigos de etapa** (a letra do roteiro, D10, ENG-01) distintos das OS abertas, na ordem da menor sequência em que cada código aparece (empate pela ordem alfabética da letra; as 5 do P-04 na ordem P, C, M, Q, E); o nome da coluna é o nome mais usado para aquele código nas OS abertas; há uma coluna "Sem roteiro" quando houver OS sem etapa; o filtro "Roteiro" (na URL) mostra só as OS copiadas daquele roteiro (`routing_template_id`).
3. **Dado** um cartão, **quando** exibido, **então** mostra número da OS, produto, projeto e cliente (`IProjects.FindManyAsync`, `IParties.FindManyAsync` por página), o medidor horas apontadas × padrão da etapa (vermelho acima do padrão, com o texto "+{h} h acima do padrão"; apontadas = 0 até a PRD-03a), a data prometida do projeto (`PromisedDate`, "Previsto {data}" enquanto não há agendamento, Fase 4), o operador em execução (iniciais, a partir da PRD-03a), os chips "Em execução", "Pausada: {motivo}" (PRD-03a), "Falta material" (EST-02) e "Sem BOM" (ENG-02), e a faixa lateral **sempre com texto** (padrão 2): vermelha com "Atrasada {n} d" quando o prometido < hoje, âmbar com "Em risco, previsto {data}" quando em risco (CA 4).
4. **Dado** a regra de "em risco" (ponto 62), **quando** qualquer tela ou job da Produção avalia uma OS, **então** usa **uma função só** (`ProductionOrderRisk`, no Domain da Produção): atrasada = prometido < hoje (America/Sao_Paulo) e OS não concluída; em risco = prometido entre hoje e hoje + 7 dias e a OS fora da última etapa; a lista da PRD-01 e o job do CA 11 usam a mesma função, e a Fase 4 troca só a fonte da data; "Em risco" tem a `<Dica>` "Prometido em até 7 dias e a OS ainda não chegou à última etapa."
5. **Dado** um cartão, **quando** clico no número da OS (ou Enter pelo teclado), **então** abre a OS (`/app/producao/os/$id`); o cartão **não tem interativo aninhado**: o link do número e o botão do menu ("Ações da OS {número}") são irmãos, nunca um dentro do outro; o quadro é navegável pelo teclado (cada coluna é uma lista, setas entre cartões) e tem a alternativa em tabela ("Ver como lista").
6. **Dado** os filtros, **quando** escolho projeto, cliente, linha (do centro de trabalho da etapa atual), roteiro, "Atrasadas" e "Em risco", **então** o quadro filtra e os filtros ficam na URL; os contadores "{n} atrasadas" e "{n} em risco" ficam visíveis como no protótipo e funcionam como filtro.

**Sem arraste; "Concluir etapa" pelo menu (D10)**
7. **Dado** o quadro, **quando** inspeciono os cartões, **então** nenhum elemento tem `draggable` nem handler de arraste (teste de componente): a etapa só avança pelo apontamento (PRD-03a) ou pela ação "Concluir etapa"; a ordem dentro da coluna também não muda pelo quadro nesta fase (prioridade é a PLN-02).
8. **Dado** `production.approve` e um cartão cuja etapa atual não é de qualidade, **quando** abro o menu do cartão, escolho "Concluir etapa {letra} {nome}", informo o motivo (obrigatório, até 200) e confirmo no diálogo com o mesmo verbo, **então** na mesma transação a etapa vai a `Done` com a marca "Concluída pelo supervisor", a próxima fica `Ready`, a OS vai a `InProgress` (com `actual_start`) se estava `Open`, e o evento `ProductionStepStarted` (`{ productionOrderId, projectId, stepId, startedAt }`, contrato em `Production.Contracts`, criado aqui porque a PRD-02 vem antes, e o mesmo da PRD-03a CA 7) vai para o outbox quando a OS sai de `Open`, para o Commercial passar o projeto a `InProgress` se ainda estava `Planned`; a mensagem é "Etapa concluída" e o cartão muda de coluna. Etapa de qualidade não aparece no menu (é decidida na PRD-05); sem `production.approve`, o item some do menu e a API responde 403. A PRD-03a amplia esta ação para fechar os apontamentos abertos da etapa (CA 20 de lá).
9. **Dado** a etapa atual de outra OS, de OS `Cancelled`/`Completed` ou uma etapa que não é a atual, **quando** chamo "Concluir etapa", **então** 409 "Esta etapa não é a etapa atual da OS." (sequencial no MVP); duas conclusões juntas: só uma grava (`xmin`).

**Tempo real**
10. **Dado** o hub `ProductionHub` (`/hubs/production`, novo), **quando** um usuário conecta, **então** o `MapHub` exige `RequireModule("production")` e a autorização do hub: com `production.view` entra no grupo `{tenantId}:production`; com `shopfloor.view` entra no grupo `{tenantId}:shopfloor` (o `/fab` da PRD-03a); o tenant vem da claim, como no `PingHub`, e nenhum grupo é escolhido pelo cliente; sem nenhuma das duas permissões, ou de outro tenant, a conexão é recusada (401/403).
11. **Dado** o job diário "OS atrasada" (R-COM-2, `RecurringJobsRegistrar`, por tenant com `tenantId`, às 07:00 de America/Sao_Paulo, valor inicial, parâmetro), **quando** roda, **então** confere o módulo `production` liberado no tenant, avalia as OS abertas pela função do CA 4 e notifica o responsável do projeto e os membros com `production.approve`: "OS {número} de {projeto} está atrasada {n} d." com link, `dedup_key` `os-late:{id}:{prometido}` (uma vez por OS e data prometida); rodar duas vezes no mesmo dia não duplica.
12. **Dado** uma mudança na OS ou na etapa (criação, edição de etapas, cancelamento, "Concluir etapa", e a partir da PRD-03a início, pausa e fim de etapa), **quando** a transação faz commit, **então** o Production envia `OrderBoardChanged` (`{ productionOrderId }`, só o id) aos grupos `production` e `shopfloor` do tenant, e o cliente recarrega o cartão pela API (o push nunca leva dado); o aviso sai **só depois do commit**, também quando a mudança vem de um consumidor do outbox (helper do BuildingBlocks, ADR 0023 §7); rollback não envia nada; o cartão muda de coluna em até 2 s no teste (PRD §6).
13. **Dado** a conexão do SignalR caiu, **quando** o cliente detecta, **então** mostra "Atualização ao vivo pausada. Tentando reconectar…" (chip neutro "Ao vivo" vira "Offline"), recarrega o quadro a cada 60 s (reserva, ADR 0009) e, ao reconectar, recarrega o quadro inteiro.

**Desempenho, permissões e segurança**
14. **Dado** 300 OS abertas, **quando** abro o Kanban, **então** carrega em menos de 1 s (uma consulta por quadro, nomes por página, sem N+1) e o push de 50 mudanças por minuto não trava a tela (recarga agrupada por cartão, com espera de 300 ms).
15. **Dado** o módulo `production` não liberado, **quando** abro o Kanban, **então** a tela `ModuleNotReleased` e a API e o hub respondem 403 `module_not_released`; sem grupo ou sem `production.view`, 403 `forbidden` e o botão some. `RequireModule` e `perm:` no endpoint do quadro, no "Concluir etapa" e no `MapHub` (teste de arquitetura estendido aos hubs).
16. **[Segurança]** **Dado** o hub, **quando** um cliente tenta entrar em grupo de outro tenant ou chamar método de servidor, **então** é recusado: o hub não tem método que entra em grupo escolhido pelo cliente, e a mensagem nunca leva dado além do id (teste com dois tenants: o tenant B não recebe o push do A).
17. **[Segurança]** **Dado** um usuário conectado ao hub, **quando** o `perm_version` dele muda (perdeu `production.view` ou `shopfloor.view`, saiu do grupo) ou o security stamp muda (senha trocada, sessão revogada, usuário desativado), **então** as conexões abertas dele são fechadas logo depois do commit da mudança, e a reconexão passa de novo pela autorização do CA 10 (teste: perder a permissão e não receber o próximo push).
18. **[Segurança]** **Dado** o token que o cliente do SignalR manda na query `access_token`, **quando** a requisição passa pelo nginx (`stack-nginx`) e pelo Serilog, **então** o valor não aparece em nenhum log: o `log_format` do nginx para `/hubs/` grava a URL sem a query, e o enriquecedor de requisição do Serilog troca `access_token` por `***` (teste do Serilog com o log capturado; conferência do `log_format` no deploy).

## Regras de negócio e técnicas
- **Etapa atual** = a etapa de menor sequência não `Done` (sequencial no MVP); sem nenhuma, a OS fica "Pronta para concluir" (D12).
- **Colunas pelo código** (D10): a letra vem da etapa da OS (copiada do roteiro na PRD-01), não do nome.
- **Atraso e risco** pelo prometido do projeto (`IProjects.PromisedDate`) até o agendamento da Fase 4 (PLN-01), quando passam a usar a data prevista × prometida; a regra fica numa função só (CA 4).
- **"Concluir etapa" do supervisor** (D10): não exige apontamento; não vale para etapa de qualidade; o bloqueio por BOM das etapas depois da primeira (D12) liga com a ENG-02, que o aplica também a esta ação.
- **Push depois do commit** (ADR 0006, ADR 0023 §7): um `IProductionBoardNotifier` chamado no fim do comando, fora da transação, pelo helper comum do BuildingBlocks; nada passa pelo outbox.
- **Consulta do quadro** no próprio Production (SQL com parâmetros pela conexão do `TenantDbContext`), nomes de projeto e cliente por `FindManyAsync`.
- Sem dado de custo no cartão (horas sim, valores não).

## Permissões, eventos e tabelas
- **Permissões:** `production.view` (quadro e grupo `production` do hub), `production.approve` ("Concluir etapa"), `shopfloor.view` (grupo `shopfloor` do hub).
- **Eventos:** **publica** `ProductionStepStarted` (outbox, CA 8); o Commercial consome para passar o projeto a `InProgress`; mensagem SignalR `OrderBoardChanged` nos grupos `{tenantId}:production` e `{tenantId}:shopfloor`.
- **Contratos:** usa `IProjects` (+ `PromisedDate`), `IParties`, `IWorkCenters`; `IStockReservations` (chip de falta) a partir da EST-02.
- **Tabelas:** nenhuma nova; índice em `production_order_steps (order_id, status)` se a medição pedir.

## Telas e estados
- **Padrões de tela da Fase 3:** 2 (faixa lateral com texto), 3 (indicador de etapa na coluna e no cartão), 4 (diálogo "Concluir etapa" com o mesmo verbo).
- **Fábrica → Produção → Kanban** (`/app/producao/kanban`, aba nova no `modules.ts`, depois de "OS de fabricação").
- **Verbos:** "Concluir etapa" / "Etapa concluída"; "Ver como lista"; "Limpar filtros".
- **Estados:** carregando (skeleton de colunas); vazio ("Nenhuma OS aberta. As OS nascem com a aprovação do orçamento." + "Ver OS de fabricação"); vazio filtrado com "Limpar filtros"; erro com "Tentar de novo"; "Offline" do tempo real (CA 13).
- **Propositivo:** cartão com "Falta material" leva à aba Material da OS; "Sem roteiro" e "Sem BOM" levam à ação que resolve (R-PRP-5); a notificação "OS atrasada" abre a OS; depois de "Concluir etapa" na última etapa, o cartão mostra "Pronta para concluir" com o atalho para a OS.
- **400 px:** uma coluna por vez com seletor de etapa (abas com rolagem horizontal, letra e nome) e os cartões empilhados; contadores de atraso e risco no topo; a faixa lateral mantém o texto.
- **Ajuda:** `Ajuda/producao-kanban.md` (explica que o quadro não arrasta e como a etapa avança); `<Dica>` em "Em risco" (CA 4), "Horas restantes" e "Concluir etapa" ("Use quando a etapa terminou sem apontamento. Fica registrado quem concluiu e por quê.").

## Plano de testes
- **Unidade:** etapa atual e "Pronta para concluir", colunas pelo código com roteiros diferentes e empate (CA 1, 2); função de atraso e risco com tabela de casos, inclusive a virada do dia em America/Sao_Paulo (CA 4).
- **Componentes (Vitest):** cartão sem `draggable` e sem interativo aninhado, faixa com texto (CA 3, 5, 7).
- **Integração (Testcontainers):** quadro com dataset fixo de 12 OS (colunas, contadores, filtros, roteiro) (CA 1–6); "Concluir etapa" com `production.approve`, evento e projeto `InProgress`, etapa de qualidade fora, etapa que não é a atual e concorrência (CA 8, 9); hub com token do tenant, grupos `production` e `shopfloor`, recusa sem permissão e dois tenants (CA 10, 15, 16); job "OS atrasada" idempotente e com o módulo conferido (CA 11); push depois do commit, também de consumidor do outbox, e não em rollback (CA 12); conexão fechada ao perder a permissão e ao mudar o security stamp (CA 17); token fora do log do Serilog (CA 18); desempenho com 300 OS (CA 14).
- **E2E (Playwright):** abrir o Kanban, concluir uma etapa pelo menu e ver o cartão mudar de coluna em outra aba; quadro a 400 px e pelo teclado (CA 5).

## Fora do escopo
- Apontamento, início e pausa da etapa: PRD-03a. Prioridade arrastável: PLN-02. Capacidade e Gantt: Fase 4. Painel da TV: PNL-01 (Fase 4, reaproveita o hub e o quadro).
- Conclusão da OS: PRD-06a1. Decisão da etapa de qualidade: PRD-05.
- OS de instalação no Kanban: fora (elas têm a lista própria).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 60–62):
1. D10: sem arraste e sem `draggable`; ação "Concluir etapa" no menu do cartão para `production.approve` (CA 7–9).
2. D10: colunas pelo código da etapa (a letra do roteiro) e filtro por roteiro (CA 2, 6).
3. "Em risco" = prometido em até 7 dias e a OS fora da última etapa, numa função só, com `<Dica>` (CA 4).

Decisão da revisão aplicada: D10 (job diário "OS atrasada", R-COM-2, CA 11).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, componentes, integração (Testcontainers, hub com dois tenants), desempenho e E2E passando.
- [ ] Permissões só do `ModuleCatalog`; teste de arquitetura cobre o `MapHub`.
- [ ] Token do hub fora dos logs do nginx e do Serilog.
- [ ] Tela com estados (inclusive o offline) e 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Alertas acionáveis nos chips; faixa lateral com texto.
- [ ] Nota no protótipo: arraste desligado, D10.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/04-fluxos-estados.md` atualizado (`ProductionStepStarted` também pela "Concluir etapa"); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ProductionHub` (Host) com `RequireModule`/`perm:` no `MapHub`, grupos `production` e `shopfloor`, registro de conexões por usuário fechado com o `perm_version` e o security stamp.
- [ ] `IProductionBoardNotifier` pelo helper de avisos depois do commit (BuildingBlocks).
- [ ] `ProductionOrderRisk` (Domain); `ProductionBoardQuery` e endpoint `/api/v1/production/board`; "Concluir etapa" e `ProductionStepStarted`; consumidor do Commercial.
- [ ] Job "OS atrasada" no `RecurringJobsRegistrar`.
- [ ] Redação do `access_token` no Serilog e `log_format` do nginx para `/hubs/`.
- [ ] Página Kanban com colunas acessíveis, sem `draggable`, filtros na URL, cliente SignalR com reconexão e reserva por polling.

## Quality gates
- Agentes previstos: @dev, @architect (primeiro hub de negócio, avisos depois do commit), design (quadro a 400 px, teclado e cartão sem interativo aninhado), cybersecurity (isolamento do hub, conexão fechada e token fora dos logs), @qa.
- Gate: CA 4, 7, 8, 10, 12, 14, 16, 17 e 18 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK (ajustado: R-COM-2) |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CA 17, 18) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: Marco 3.1, ordem 3) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** o arraste do protótipo conflita com a etapa sequencial do `04-fluxos`: virou ponto em aberto com proposta, sem decidir (CA 9); push só com o id (CA 7, 12); atraso pelo prometido até a Fase 4 (regras).
- **Correções da revalidação (21/10/2026):** pontos 60 a 62 decididos (D10: sem arraste nem `draggable`, "Concluir etapa" no menu com `production.approve`, colunas pelo código da etapa e filtro por roteiro; "em risco" numa função só com `<Dica>`); job diário "OS atrasada" (R-COM-2, CA 11); coluna "Pronta para concluir" (D12); §2.1: `RequireModule` e permissão no `MapHub`, avisos depois do commit também nos consumidores do outbox (CA 12, 15); §2.2: grupo do chão de fábrica com `shopfloor.view`, conexão fechada com permissão ou security stamp e token fora dos logs (CA 10, 17, 18); design: cartão sem interativo aninhado e faixa lateral com texto (padrão 2, CA 3, 5); nota do protótipo no DoD; dependências e bloqueios pela nova ordem; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D10, D12, tabela 1.1 itens 60–62) e correções da revisão pré-Fase 3 (§2.1 `MapHub` e avisos depois do commit, §2.2 PRD-02, §2.3 padrões 2, 3 e 4 e Kanban sem interativo aninhado); nova ordem (Marco 3.1, ordem 3); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
