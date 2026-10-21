# PRD-02 — Kanban da produção

| Campo | Valor |
|---|---|
| ID | PRD-02 |
| Título | Kanban das OS de fabricação com colunas por etapa do roteiro, cartões com projeto, cliente, horas apontadas × padrão, prazo, atraso e risco, filtros por projeto, cliente, linha e atraso, e atualização em tempo real por SignalR (primeiro hub de negócio, base do painel da TV) |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-PRD-2 (Kanban por etapa com filtros por projeto, cliente, linha e atraso) · PRD §5.14 (painel do supervisor; painel da TV atualiza em menos de 2 s, PRD §6) · R-PRP-5 · R-SEC-5 |
| Dependências | PRD-01 (OS de fabricação com etapas, `IProductionOrders`), ENG-01 (etapas do roteiro), FND-02 (SignalR com token do tenant, `PingHub`), FND-09e |
| Bloqueia | PRD-03a (o apontamento publica as mudanças que o Kanban mostra), PNL-01 (Fase 4: reaproveita o hub), DSH-03 |
| Ordem de execução | 14 (Marco 3.4 "Fábrica rodando") |

## User story
Como **supervisor de produção**, quero **ver todas as OS por etapa num quadro que se atualiza sozinho, com o que está atrasado ou em risco destacado**, para **saber onde a fábrica está travando e agir antes de atrasar a entrega**.

## Contexto e referências
- PRD §5.7 R-PRD-2; §5.14 "Painel do supervisor: Kanban + capacidade + não conformidades abertas + faltas de material" (o painel completo é a DSH-03, Fase 4); §6 (painel atualiza em < 2 s após apontamento).
- `04-fluxos-estados` §2: `StepStatusChanged` (Production) → SignalR painel TV/Kanban; `LaborEntryClosed` → SignalR atualiza painéis. §1 (etapa sequencial no MVP; a etapa só inicia com a anterior `Done`).
- `02-arquitetura` §2.1 (grupos SignalR `{tenantId}:{painel}`); ADR 0006 ("O push do SignalR para painéis acontece logo após o commit e não passa pelo outbox"); ADR 0009 (sino por SignalR com polling de 60 s como reserva).
- **Código atual:** só existe o `PingHub` (FND-02), que prova token e host do tenant. Nenhum hub de negócio.
- Protótipo (Produção): colunas P Projeto, C Corte, M Montagem, Q Qualidade, E Expedição com a letra no display de andar e "{n} OS, {h} h restantes"; cartão com OS, título, cliente, medidor de horas (apontadas/padrão, vermelho acima do padrão), "Previsto {data}" ou "Atrasada, previsto {data}", avatar do operador e "Em execução"; faixa vermelha para atrasada e âmbar para risco; filtros Linha 1, Linha 2, Todos os projetos, "2 atrasadas", "1 em risco"; "Arraste um cartão para simular a mudança de etapa" (ver Pontos em aberto 1).

## Critérios de aceite
**Quadro**
1. **Dado** `production.view`, **quando** abro "Kanban" (`/app/producao/kanban`), **então** vejo uma coluna por etapa (CA 2), cada uma com a letra da etapa no estilo display de andar, o nome, a quantidade de OS e as horas restantes (Σ max(padrão − apontadas, 0) das etapas atuais), e um cartão por OS de fabricação `Open`, `InProgress`, `Paused` ou `Quality` na coluna da **etapa atual** (a primeira não `Done`).
2. **Dado** OS com roteiros diferentes, **quando** o quadro monta as colunas, **então** as colunas são os nomes de etapa distintos das OS abertas, na ordem da menor sequência em que aparecem (as 5 do P-04 na ordem Projeto → Corte → Montagem → Qualidade → Expedição), e uma coluna "Sem roteiro" quando houver OS sem etapa (proposta, Pontos em aberto 2).
3. **Dado** um cartão, **quando** exibido, **então** mostra número da OS, produto, projeto e cliente (`IProjects.FindManyAsync`, `IParties.FindManyAsync` por página), o medidor horas apontadas × padrão da etapa (vermelho acima do padrão; apontadas = 0 até a PRD-03a), a data prometida do projeto ("Previsto {data}" enquanto não há agendamento, Fase 4), o operador em execução (iniciais, a partir da PRD-03a), os chips "Em execução", "Pausada: {motivo}", "Falta material" (EST-02) e "Sem BOM", e a faixa lateral vermelha quando atrasada (prometido < hoje) ou âmbar quando em risco (prometido em até 7 dias e a OS fora da última etapa; proposta, Pontos em aberto 3).
4. **Dado** um cartão, **quando** clico (ou Enter pelo teclado), **então** abre a OS (`/app/producao/os/$id`); o quadro é navegável pelo teclado (cada coluna é uma lista, setas entre cartões) e tem a alternativa em tabela ("Ver como lista").
5. **Dado** os filtros, **quando** escolho projeto, cliente, linha (do centro de trabalho da etapa atual), "Atrasadas" e "Em risco", **então** o quadro filtra e os filtros ficam na URL; os contadores "{n} atrasadas" e "{n} em risco" ficam visíveis como no protótipo e funcionam como filtro.

**Tempo real**
6. **Dado** o hub `ProductionHub` (`/hubs/production`, novo), **quando** o usuário com `production.view` conecta, **então** entra no grupo `{tenantId}:production` (o tenant vem da claim, como no `PingHub`; nenhum grupo é escolhido pelo cliente), e um usuário sem `production.view` ou de outro tenant é recusado (401/403).
7. **Dado** uma mudança na OS ou na etapa (criação, edição de etapas, cancelamento, e a partir da PRD-03a início, pausa e fim de etapa), **quando** a transação faz commit, **então** o Production envia `OrderBoardChanged` (`{ productionOrderId }`, só o id) ao grupo, e o cliente recarrega o cartão pela API (o push nunca leva dado); o cartão muda de coluna em até 2 s no teste (PRD §6).
8. **Dado** a conexão do SignalR caiu, **quando** o cliente detecta, **então** mostra "Atualização ao vivo pausada. Tentando reconectar…" (chip neutro "Ao vivo" vira "Offline"), recarrega o quadro a cada 60 s (reserva, ADR 0009) e, ao reconectar, recarrega o quadro inteiro.

**Sem mudar a etapa pelo arraste**
9. **Dado** o quadro, **quando** tento arrastar um cartão para outra coluna, **então** o cartão não muda de etapa: a etapa só avança pelo apontamento (PRD-03a) ou pela conclusão de etapa pelo supervisor (PRD-03a), e aparece a dica "A etapa avança pelo apontamento do operador." (proposta, Pontos em aberto 1); o arraste dentro da coluna também não reordena nesta fase (prioridade é a PLN-02).

**Desempenho, permissões e segurança**
10. **Dado** 300 OS abertas, **quando** abro o Kanban, **então** carrega em menos de 1 s (uma consulta por quadro, nomes por página, sem N+1) e o push de 50 mudanças por minuto não trava a tela (recarga agrupada por cartão, com espera de 300 ms).
11. **Dado** o módulo `production` não liberado, **quando** abro o Kanban, **então** a tela `ModuleNotReleased` e a API e o hub respondem 403 `module_not_released`; sem grupo ou sem `production.view`, 403 `forbidden` e o botão some. `RequireModule` e `perm:` no endpoint do quadro e no hub (teste de arquitetura estendido aos hubs).
12. **[Segurança]** **Dado** o hub, **quando** um cliente tenta entrar em grupo de outro tenant ou chamar método de servidor, **então** é recusado: o hub não tem método que entra em grupo escolhido pelo cliente, e a mensagem nunca leva dado além do id (teste com dois tenants: o tenant B não recebe o push do A).

## Regras de negócio e técnicas
- **Etapa atual** = a etapa de menor sequência não `Done` (sequencial no MVP).
- **Atraso e risco** pelo prometido do projeto até o agendamento da Fase 4 (PLN-01), quando passam a usar a data prevista × prometida; a regra de risco fica numa função só, para a Fase 4 trocar a fonte.
- **Push depois do commit** (ADR 0006): um `IProductionBoardNotifier` chamado no fim do comando, fora da transação; nada passa pelo outbox.
- **Consulta do quadro** no próprio Production (SQL com parâmetros pela conexão do `TenantDbContext`), nomes de projeto e cliente por `FindManyAsync`.
- Sem dado de custo no cartão (horas sim, valores não).

## Permissões, eventos e tabelas
- **Permissões:** `production.view`.
- **Eventos:** nenhum no outbox; mensagem SignalR `OrderBoardChanged` no grupo `{tenantId}:production`.
- **Contratos:** usa `IProjects`, `IParties`, `IWorkCenters`, `IStockReservations` (chip de falta).
- **Tabelas:** nenhuma nova; índice em `production_order_steps (order_id, status)` se a medição pedir.

## Telas e estados
- **Fábrica → Produção → Kanban** (`/app/producao/kanban`, aba nova no `modules.ts`, depois de "OS de fabricação").
- **Estados:** carregando (skeleton de colunas); vazio ("Nenhuma OS aberta. As OS nascem com a aprovação do orçamento." + "Ver OS de fabricação"); vazio filtrado com "Limpar filtros"; erro com "Tentar de novo"; "Offline" do tempo real (CA 8).
- **Propositivo:** cartão com "Falta material" leva à aba Material da OS; "Sem roteiro" e "Sem BOM" levam à ação que resolve (R-PRP-5).
- **400 px:** uma coluna por vez com seletor de etapa (abas com rolagem horizontal) e os cartões empilhados; contadores de atraso e risco no topo.
- **Ajuda:** `Ajuda/producao-kanban.md`; `<Dica>` em "Em risco" e "Horas restantes".

## Plano de testes
- **Unidade:** etapa atual, colunas com roteiros diferentes (CA 1, 2), atraso e risco (CA 3).
- **Integração (Testcontainers):** quadro com dataset fixo de 12 OS (colunas, contadores, filtros) (CA 1–5); hub com token do tenant, grupo do tenant, recusa sem permissão e dois tenants (CA 6, 11, 12); push depois do commit e não em rollback (CA 7); desempenho com 300 OS (CA 10).
- **E2E (Playwright):** abrir o Kanban, mudar uma OS em outra aba e ver o cartão atualizar; quadro a 400 px e pelo teclado (CA 4).

## Fora do escopo
- Mudança de etapa e apontamento: PRD-03a. Prioridade arrastável: PLN-02. Capacidade e Gantt: Fase 4. Painel da TV: PNL-01 (Fase 4, reaproveita o hub e o quadro).
- OS de instalação no Kanban: fora (elas têm a lista própria).

## Pontos em aberto
1. **Arrastar para mudar de etapa** (PO): o protótipo diz "Arraste um cartão para simular a mudança de etapa", mas o `04-fluxos` faz a etapa avançar pelo apontamento e em sequência. Proposta: sem arraste para mudar de etapa (CA 9); o supervisor conclui a etapa pela OS (PRD-03a).
2. **Colunas com roteiros diferentes** (PO): proposta do CA 2 (colunas pelos nomes de etapa); alternativa: um quadro por roteiro.
3. **Regra de "em risco"** (PO): proposta: prometido em até 7 dias e a OS fora da última etapa, até a Fase 4 trazer a data prevista.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, hub com dois tenants), desempenho e E2E passando.
- [ ] Permissões só do `ModuleCatalog`; teste de arquitetura cobre o hub.
- [ ] Tela com estados (inclusive o offline) e 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Alertas acionáveis nos chips.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ProductionHub` (Host) com `RequireModule`/`perm:`; `IProductionBoardNotifier` chamado depois do commit.
- [ ] `ProductionBoardQuery` e endpoint `/api/v1/production/board`.
- [ ] Página Kanban com colunas acessíveis, filtros na URL, cliente SignalR com reconexão e reserva por polling.

## Quality gates
- Agentes previstos: @dev, @architect (primeiro hub de negócio), design (quadro a 400 px e teclado), cybersecurity (isolamento do hub), @qa.
- Gate: CA 6, 7, 10 e 12 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** o arraste do protótipo conflita com a etapa sequencial do `04-fluxos`: virou ponto em aberto com proposta, sem decidir (CA 9); push só com o id (CA 7, 12); atraso pelo prometido até a Fase 4 (regras).
- **Pré-condições para `Ready`:** ponto em aberto 1.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
