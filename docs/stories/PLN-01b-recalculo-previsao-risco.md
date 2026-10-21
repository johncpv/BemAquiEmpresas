# PLN-01b — Recálculo por evento, data prevista do projeto e risco de atraso

| Campo | Valor |
|---|---|
| ID | PLN-01b |
| Título | O plano se refaz sozinho a cada mudança que afeta as datas (apontamento, etapa, OS, roteiro, qualidade, calendário, centro, aditivo); a data prevista de entrega do projeto (fim da fabricação + prazo de instalação) chega ao Commercial por evento; prevista > prometida vira risco no Kanban, no projeto e num aviso acionável |
| Tamanho | M (quebra da PLN-01) |
| Status | Draft |
| Requisitos | R-PLN-3 (recálculo, `04-fluxos` §3.4 itens 4 e 5) · R-PLN-4 (data prevista × prometida com alerta de risco) · R-COM-2 · R-PRP-5 · PRD §1.2 (desvio da data prevista ≤ 15%) · PRD §6 |
| Dependências | PLN-01a (motor, `IProductionSchedule`), COM-05 (Resumo e lista do projeto), COM-07 (aditivo muda a data prometida), PRD-02 (função única de atraso e risco), PRD-03a (apontamento), PRD-05 (qualidade), PRD-06a (conclusão), CAD-03 (centros e calendário, mesmo módulo), COM-06 (OS de instalação) |
| Bloqueia | PLN-02 (impacto nas datas), PLN-03 (prevista × prometida), PNL-01b (previsão no painel), DSH-01b (Entregas) |
| Ordem de execução | 7 (Marco 4.3 "Datas que a fábrica cumpre") |

## User story
Como **proprietário**, quero **que a data prevista de cada projeto se atualize sozinha quando a fábrica anda ou atrasa, e ser avisado quando ela passa da data prometida ao cliente**, para **renegociar antes, e não descobrir o atraso no dia da entrega**.

## Contexto e referências
- `04-fluxos-estados` §3.4: (4) "Data prevista do projeto = maior fim de etapa entre suas OS (+ prazo padrão de instalação configurável)"; (5) "Recalcula a cada mudança de prioridade, apontamento finalizado, mudança de calendário/manutenção, e de hora em hora"; (6) "Risco: prevista > prometida → alerta no dashboard e no painel". §2: `StepStatusChanged` → "Planning recalcula datas da fila"; `MaintenanceScheduled` → capacidade (Fase 6).
- ADR 0006: reação dentro do mesmo módulo é síncrona; entre módulos, pelo outbox. ADR 0016: mudança de calendário não publica evento; o planejamento lê o calendário ao recalcular (centros e calendário são do próprio Production, CAD-03).
- `03-modelo-dados`: `commercial.projects.promised_date` e `forecast_date`. COM-05 CA 3: "Entrega prevista: calculada a partir da Fase 4 (PLN-01)".
- PRD-02 (Fase 3, rascunho): atraso e risco pelo prometido "até o agendamento da Fase 4 (PLN-01), quando passam a usar a data prevista × prometida; a regra de risco fica numa função só, para a Fase 4 trocar a fonte" (ponto 3 de lá: proposta provisória "prometido em até 7 dias").
- COM-06: a OS de instalação tem `planned_start`/`planned_end` digitados e o CP estimado do instalador usa o `planned_end` (ADR 0019 §17).
- Protótipo (Painel): alerta "Ed. Monte Castelo vai atrasar 7 dias — Previsão 05/12, prometido ao cliente 28/11."

## Critérios de aceite
**Gatilhos**
1. **Dado** os comandos do Production que mudam o plano — gerar, editar etapas, trocar roteiro e cancelar OS (PRD-01); iniciar, pausar, retomar e finalizar etapa e fechar ou corrigir apontamento (PRD-03a); aprovar ou reprovar a qualidade (PRD-05); concluir a OS (PRD-06a); salvar centro de trabalho, calendário ou exceção do calendário (CAD-03); salvar a fila (PLN-02) — **quando** a transação faz commit, **então** o comando pede um recálculo ao `ScheduleService` (gatilho `Event`, reação dentro do módulo, ADR 0006), sem esperar o cálculo terminar.
2. **Dado** os eventos de outros módulos que mudam a data prometida ou a existência do projeto — `ContractAmended` (COM-07, prazo) e `ContractCancelled` —, **quando** o consumidor do Production processa (idempotente pela inbox), **então** pede um recálculo; a data prometida é lida pelo `IProjects` no cálculo (o `ProjectSummary` ganha `PromisedDate` e `SignedAt`, proposta).
3. **Dado** vários pedidos em sequência (ex.: 20 apontamentos fechados em 10 s), **quando** chegam, **então** são agrupados: há no máximo uma execução rodando e uma pendente por tenant (PLN-01a CA 11), e o resultado final é igual ao de uma execução feita do zero depois do último pedido (teste).
4. **Dado** um apontamento finalizado, **quando** o recálculo termina, **então** as novas datas aparecem na OS, no Kanban e no projeto em até 10 s depois do commit (proposta, Pontos em aberto 3), e o Production envia `OrderBoardChanged` só para as OS cujas datas mudaram (PRD-02).

**Data prevista do projeto**
5. **Dado** um projeto com OS de fabricação planejadas, **quando** o recálculo termina, **então** o **fim da fabricação** = maior `planned_end` entre as OS de fabricação não canceladas; e a **data prevista de entrega** = (a) com OS de instalação não cancelada: max(fim da fabricação + prazo padrão de instalação, `planned_end` da OS de instalação); (b) sem OS de instalação e com custo de instalação no orçamento do projeto: fim da fabricação + prazo padrão de instalação; (c) sem instalação: o fim da fabricação (proposta, Pontos em aberto 1); o prazo padrão é o parâmetro `planning.installation_days` (dias úteis do calendário padrão, seed 5, proposta, Pontos em aberto 2).
6. **Dado** um projeto com alguma OS sem previsão (PLN-01a CA 8) ou sem nenhuma OS de fabricação, **quando** o recálculo termina, **então** a data prevista fica vazia e o motivo é publicado ("OS {número} sem roteiro"); projeto `Fabricated` ou posterior não é recalculado (a data prevista fica como estava e o risco passa a ser só da instalação, pelo `planned_end` da OS de instalação).
7. **Dado** a data prevista ou o motivo de um projeto mudou, **quando** a execução grava, **então** o Production publica `ProjectForecastChanged` (`{ projectId, forecastDate?, fabricationEnd?, reason?, computedAt }`, `Production.Contracts`) no outbox, só para os projetos que mudaram; o consumidor do Commercial grava `projects.forecast_date` (e o motivo) **só se `computedAt` for mais novo** que o gravado (o outbox não garante ordem), idempotente pela inbox.
8. **Dado** o Projeto 360 (Resumo) e a lista de projetos (COM-05), **quando** abro, **então** "Entrega prevista" mostra a data (no lugar de "calculada a partir da Fase 4"), com a folga "{n} dias antes do prometido" ou "{n} dias depois do prometido" e o motivo quando estiver vazia.

**Risco**
9. **Dado** a função única de atraso e risco (PRD-02), **quando** esta story entra, **então** a regra passa a ser: **atrasado** = prometido < hoje e o projeto não entregue (como antes); **em risco** = data prevista > prometido; a regra provisória "prometido em até 7 dias" sai; Kanban, lista de OS, Projeto 360 e lista de projetos usam a mesma função (teste que confere as quatro telas com o mesmo dataset).
10. **Dado** um projeto que **entra** em risco (antes não estava, ou a data prevista piorou), **quando** o Commercial grava a nova previsão, **então** o responsável do projeto e os usuários com `production.approve` (resolvidos pelo `IUserDirectory` com filtro de permissão efetiva, proposta) recebem a notificação, pelo `INotificationService` do Commercial, dentro do consumidor (ADR 0019 §18), "{projeto} vai atrasar {n} dias: previsão {data}, prometido ao cliente {data}." com link para o Cronograma do projeto (PLN-03; até ela existir, para o Projeto 360), com `dedup_key` `project-at-risk:{projectId}:{forecastDate}`; sair do risco não notifica (proposta, Pontos em aberto 4).
11. **Dado** o painel do dono e a TV, **quando** esta story entra, **então** a previsão e o risco ficam disponíveis por `IProjects` (Commercial: `forecast_date`, folga, risco) para a DSH-01b e por `IProductionSchedule` para o PNL-01b.

**Parâmetro, permissões e segurança**
12. **Dado** `settings.edit`, **quando** altero "Prazo padrão de instalação (dias úteis)" em Configurações → Parâmetros → Produção (0 a 120), **então** grava em `production.parameters` (proposta, ADR 0016 M6) com auditoria e dispara um recálculo.
13. **Dado** dois tenants com apontamentos ao mesmo tempo, **quando** os recálculos rodam, **então** cada um calcula só o seu tenant (escopo do `TenantJob`, teste) e o `ProjectForecastChanged` de um não chega ao outro.
14. **Dado** o módulo `production` não liberado, **quando** um comando ou evento pediria recálculo, **então** nada é calculado; o parâmetro e as consultas respondem 403 `module_not_released`; sem grupo, 403 `forbidden`. `RequireModule` e `perm:`; o evento leva só ids, datas e o motivo como código (sem texto livre nem dado pessoal, ADR 0019 §19).

## Regras de negócio e técnicas
- **Pedido de recálculo** (`IScheduleTrigger.RequestAsync(reason)`): grava a marca "pendente" do tenant e enfileira o `PlanningRecalculateJob` se nada estiver rodando; chamado depois do commit (falha no pedido não desfaz o comando; o recorrente de hora em hora é a reserva).
- **Folga** = prometido − previsto em dias corridos; **risco** com folga < 0 (proposta: sem tolerância, Pontos em aberto 4).
- **Motivo** como código (`NoRouting`, `NoWorkCenter`, `NoCapacity`, `NoFabricationOrder`) traduzido na tela.
- Datas `date`; `computedAt` `timestamptz`.

## Permissões, eventos e tabelas
- **Permissões:** `production.view` (ver), `settings.edit` (parâmetro); a notificação vai para o responsável do projeto e para `production.approve`.
- **Eventos:** **publica** `ProjectForecastChanged` (`Production.Contracts`, novo); **consome** `ContractAmended`, `ContractCancelled` (Commercial); consumidor novo no Commercial para o `ProjectForecastChanged`.
- **Contratos:** `IProjects` (+ `PromisedDate`, `SignedAt`, `ForecastDate`, risco — proposta), `IProductionSchedule` (PLN-01a), `IInstallationOrders` (datas da instalação), custo de instalação no orçamento do projeto por `IProjects.BudgetedAsync(projectId, Installation)`.
- **Tabelas:** `production.parameters` (**proposta**, chave `planning.installation_days`); `commercial.projects` (+ `forecast_reason`, `forecast_computed_at`, **propostas**); `production.schedule_runs` (marca de pendente por tenant, proposta).

## Telas e estados
- **Projeto 360 → Resumo** e **lista de projetos** (COM-05): "Entrega prevista" com a folga; chip "Em risco" (âmbar) e faixa âmbar; atrasado continua vermelho.
- **Kanban e lista de OS** (PRD-01, PRD-02): faixa âmbar pelo risco novo; "Previsto {data}" do plano.
- **Estados:** "Sem previsão: {motivo}" com o link que resolve; "Recalculando…" no cabeçalho do Planejamento enquanto há execução pendente.
- **Propositivo:** a notificação de risco leva ao cronograma do projeto, onde se vê a etapa que empurra a data.
- **400 px:** a folga aparece abaixo da data, sem coluna nova.
- **Ajuda:** atualizar `Ajuda/projetos-projeto.md` (entrega prevista, folga, risco), `Ajuda/producao-kanban.md` (regra nova de risco), `Ajuda/producao-planejamento.md` (quando o plano se refaz) e `Ajuda/configuracoes-parametros.md`; `<Dica>` em "Entrega prevista", "Folga" e "Em risco".

## Plano de testes
- **Unidade:** data prevista nos casos (a), (b) e (c) e sem previsão (CA 5, 6); folga e risco (CA 9); agrupamento dos pedidos (CA 3).
- **Integração (Testcontainers, obrigatória):** cada gatilho do CA 1 pede um recálculo e as datas mudam (tabela de casos); consumidores do `ContractAmended` e do `ContractCancelled` (CA 2); 20 apontamentos em 10 s (CA 3); latência até 10 s (CA 4); `ProjectForecastChanged` fora de ordem não sobrescreve o mais novo (CA 7); notificação com `dedup_key` (CA 10); parâmetro (CA 12); dois tenants (CA 13); três camadas (CA 14).
- **E2E (Playwright):** fechar um apontamento acima do padrão e ver a entrega prevista do projeto mudar e o chip "Em risco" aparecer.

## Fora do escopo
- Manutenção e paradas reduzindo a capacidade: PAT-03 (Fase 6) e R-PRD-9 (D, sem story).
- Data prevista da OS de instalação calculada pelo motor: a instalação continua com datas digitadas.
- Renegociação da data prometida: só por aditivo (COM-07).

## Pontos em aberto
1. **Prazo de instalação na data prevista** (PO): proposta do CA 5 (casos a, b e c).
2. **Valor padrão do prazo de instalação** (PO/Konebras): proposta: 5 dias úteis, editável.
3. **Tempo até a data nova aparecer** (PO): proposta: até 10 s depois do apontamento (o painel da TV continua atualizando o cartão em menos de 2 s, PRD §6; só a data prevista pode levar até 10 s).
4. **Quem é avisado e quando** (PO): proposta: responsável do projeto e `production.approve`, só ao entrar em risco ou piorar, sem tolerância de dias. Alternativa: tolerância configurável (ex.: avisar só acima de 2 dias).
5. **Regra provisória de risco da Fase 3** (herdado do ponto 3 da PRD-02): esta story a substitui; se o PO decidir outra regra provisória, só muda o texto do CA 9.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, eventos fora de ordem, dois tenants) e E2E passando.
- [ ] Migration das colunas e da tabela de parâmetros (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com a previsão, a folga e o risco; 400 px.
- [ ] Ajudas atualizadas; teste de cobertura passando.
- [ ] Notificação de risco acionável e sem repetição.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (§2 com o `ProjectForecastChanged`, §3.4 itens 4 a 6) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IScheduleTrigger` chamado nos comandos do Production; consumidores de `ContractAmended` e `ContractCancelled`.
- [ ] Cálculo da data prevista do projeto; `ProjectForecastChanged` e o consumidor no Commercial com `computedAt`.
- [ ] Função de risco trocada; notificação; parâmetro; telas e ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (gatilhos e evento fora de ordem), @qa.
- Gate: CA 3, 5, 7, 9 e 13 obrigatórios.

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

- **Correções feitas na validação:** eventos do outbox fora de ordem tratados com `computedAt` (CA 7); a regra provisória de risco da PRD-02 é substituída pela prevista × prometida (`04-fluxos` §3.4 item 6); a notificação do Commercial passou a dizer como resolve os usuários com `production.approve` e que é síncrona no consumidor (ADR 0019 §18).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; quebra da PLN-01 (01a/01b) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
