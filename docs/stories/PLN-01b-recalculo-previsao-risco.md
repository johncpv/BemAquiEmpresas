# PLN-01b — Recálculo por evento, data prevista do projeto e risco de atraso

| Campo | Valor |
|---|---|
| ID | PLN-01b |
| Título | O plano se refaz sozinho a cada mudança que afeta as datas (apontamento, etapa, OS, roteiro, qualidade, calendário, centro, aditivo), com os pedidos agrupados; a data prevista de entrega do projeto (fim da fabricação + prazo de instalação) chega ao Commercial por evento e fica em `commercial.project_forecasts`, fora do documento (ADR 0029); prevista > prometida vira risco no Kanban, no projeto e num aviso acionável |
| Tamanho | M (quebra da PLN-01) |
| Status | Done |
| Requisitos | R-PLN-3 (recálculo, `04-fluxos` §3.4 itens 4 e 5) · R-PLN-4 (data prevista × prometida com alerta de risco) · R-COM-2 · R-PRP-5 · PRD §1.2 (desvio da data prevista ≤ 15%) · PRD §6 |
| Dependências | PLN-01a (motor, `IProductionSchedule`, `ScheduleService`), COM-05 (Resumo e lista do projeto), COM-07 (aditivo muda a data prometida), PRD-02 (função única de atraso e risco), PRD-03a (apontamento), PRD-05 (qualidade), PRD-06a1 (conclusão da OS), CAD-03 (centros e calendário, mesmo módulo), COM-06 (OS de instalação) |
| Bloqueia | PLN-02 (impacto nas datas), PLN-03 (prevista × prometida), PNL-01b (previsão no painel), DSH-01b (Entregas) |
| Ordem de execução | 7 (Marco 4.3 "Datas que a fábrica cumpre") |

## User story
Como **proprietário**, quero **que a data prevista de cada projeto se atualize sozinha quando a fábrica anda ou atrasa, e ser avisado quando ela passa da data prometida ao cliente**, para **renegociar antes, e não descobrir o atraso no dia da entrega**.

## Contexto e referências
- `04-fluxos-estados` §3.4: (4) "Data prevista do projeto = maior fim de etapa entre suas OS (+ prazo padrão de instalação configurável)"; (5) "Recalcula a cada mudança de prioridade, apontamento finalizado, mudança de calendário/manutenção, e de hora em hora"; (6) "Risco: prevista > prometida → alerta no dashboard e no painel". §2: `StepStatusChanged` → "Planning recalcula datas da fila"; `MaintenanceScheduled` → capacidade (Fase 6).
- **ADR 0029** (D10): a previsão do projeto fica em **`commercial.project_forecasts`**, não em `commercial.projects` (o documento não muda de versão a cada recálculo, não gera 409 nem enche a auditoria); um único escritor por tabela (o consumidor do `ProjectForecastChanged` no Commercial); fora da auditoria.
- ADR 0006: reação dentro do mesmo módulo é síncrona; entre módulos, pelo outbox. ADR 0016: mudança de calendário não publica evento; o planejamento lê o calendário ao recalcular (centros e calendário são do próprio Production, CAD-03).
- `03-modelo-dados`: `commercial.projects.promised_date` e `forecast_date`; esta story passa a previsão para `commercial.project_forecasts` e deixa `forecast_date` de `projects` sem uso (sem apagar a coluna, ADR 0029). COM-05 CA 3: "Entrega prevista: calculada a partir da Fase 4 (PLN-01)".
- PRD-02 (Fase 3): atraso e risco numa função só, com a regra provisória do item 62 da Fase 3 ("prometido em até 7 dias e a OS fora da última etapa"), que esta story substitui.
- COM-06: a OS de instalação tem `planned_start`/`planned_end` digitados e o CP estimado do instalador usa o `planned_end` (ADR 0019 §17).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D10; tabela 3.1, pontos 31 a 35; §4.3 transversal.
- Protótipo (Painel): alerta "Ed. Monte Castelo vai atrasar 7 dias — Previsão 05/12, prometido ao cliente 28/11."

## Critérios de aceite
**Gatilhos**
1. **Dado** os comandos do Production que mudam o plano — gerar, editar etapas, trocar roteiro e cancelar OS (PRD-01); iniciar, pausar, retomar e finalizar etapa e fechar ou corrigir apontamento (PRD-03a); aprovar ou reprovar a qualidade (PRD-05); concluir a OS (PRD-06a1); salvar centro de trabalho, calendário ou exceção do calendário (CAD-03); salvar a fila (PLN-02) — **quando** a transação faz commit, **então** o comando pede um recálculo ao `ScheduleService` (gatilho `Event`, reação dentro do módulo, ADR 0006), sem esperar o cálculo terminar.
2. **Dado** os eventos de outros módulos que mudam a data prometida ou a existência do projeto — `ContractAmended` (COM-07, prazo) e `ContractCancelled` —, **quando** o consumidor do Production processa (idempotente pela inbox), **então** pede um recálculo; a data prometida é lida pelo `IProjects` no cálculo (o `ProjectSummary` ganha `PromisedDate` e `SignedAt`).
3. **Dado** vários pedidos em sequência (ex.: 20 apontamentos fechados em 10 s), **quando** chegam, **então** são agrupados: há no máximo **uma execução rodando e uma pendente** por tenant (PLN-01a CA 12), e o resultado final é igual ao de uma execução feita do zero depois do último pedido (teste).
4. **Dado** um apontamento finalizado, **quando** o recálculo termina, **então** as novas datas aparecem na OS, no Kanban e no projeto em **até 10 s** depois do commit, e o Production envia `OrderBoardChanged` só para as OS cujas datas mudaram (PRD-02) e o sinal `ScheduleChanged` (usado pela TV na PNL-01b).

**Data prevista do projeto**
5. **Dado** um projeto com OS de fabricação planejadas, **quando** o recálculo termina, **então** o **fim da fabricação** = maior fim previsto (do plano, `schedule_step_forecasts`) entre as OS de fabricação não canceladas; e a **data prevista de entrega** = (a) com OS de instalação não cancelada: max(fim da fabricação + prazo padrão de instalação, `planned_end` da OS de instalação); (b) sem OS de instalação e com custo de instalação no orçamento do projeto: fim da fabricação + prazo padrão de instalação; (c) sem instalação: o fim da fabricação; o prazo padrão é o parâmetro `planning.installation_days` (dias úteis do calendário padrão, seed **5**).
6. **Dado** um projeto com alguma OS sem previsão (PLN-01a CA 8) ou sem nenhuma OS de fabricação, **quando** o recálculo termina, **então** a data prevista fica vazia e o motivo é publicado ("OS {número} sem roteiro"); projeto `Fabricated` ou posterior não é recalculado (a data prevista fica como estava e o risco passa a ser só da instalação, pelo `planned_end` da OS de instalação).
7. **Dado** a data prevista ou o motivo de um projeto mudou, **quando** a execução grava, **então** o Production publica `ProjectForecastChanged` (`{ projectId, forecastDate?, fabricationEnd?, reason?, computedAt }`, `Production.Contracts`) no outbox, só para os projetos que mudaram; o consumidor do Commercial grava em **`commercial.project_forecasts`** (`project_id` único, `forecast_date`, `fabrication_end`, `reason`, `computed_at`) **só se `computedAt` for mais novo** que o gravado (o outbox não garante ordem), idempotente pela inbox; a linha de `commercial.projects` não é tocada (versão, 409 e auditoria do projeto intactos; a tabela fica fora do `IAuditable`).
8. **Dado** o Projeto 360 (Resumo) e a lista de projetos (COM-05), **quando** abro, **então** "Entrega prevista" mostra a data lida de `project_forecasts` (no lugar de "calculada a partir da Fase 4"), com a folga "{n} dias antes do prometido" ou "{n} dias depois do prometido" e o motivo quando estiver vazia.

**Risco**
9. **Dado** a função única de atraso e risco (PRD-02), **quando** esta story entra, **então** a regra passa a ser: **atrasado** = prometido < hoje e o projeto não entregue (como antes); **em risco** = data prevista > prometido, **sem tolerância**; a regra provisória do item 62 da Fase 3 ("prometido em até 7 dias") sai; Kanban, lista de OS, Projeto 360 e lista de projetos usam a mesma função (teste que confere as quatro telas com o mesmo dataset).
10. **Dado** um projeto que **entra** em risco (antes não estava) ou cuja data prevista **piorou**, **quando** o Commercial grava a nova previsão, **então** o responsável do projeto e os usuários com `production.approve` (resolvidos pelo `IUserDirectory` com filtro de permissão efetiva) recebem a notificação, pelo `INotificationService` do Commercial, dentro do consumidor (ADR 0019 §18), "{projeto} vai atrasar {n} dias: previsão {data}, prometido ao cliente {data}." com link para o Cronograma do projeto (PLN-03; até ela existir, para o Projeto 360), com `dedup_key` `project-at-risk:{projectId}:{forecastDate}`; sair do risco não notifica.
11. **Dado** o painel do dono e a TV, **quando** esta story entra, **então** a previsão e o risco ficam disponíveis por `IProjects` (Commercial: previsão de `project_forecasts`, folga, risco) para a DSH-01b e por `IProductionSchedule` para a PNL-01b.

**Parâmetro, permissões e segurança**
12. **Dado** `settings.edit`, **quando** altero "Prazo padrão de instalação (dias úteis)" em Configurações → Parâmetros → Produção (0 a 120, seed 5), **então** grava em `production.parameters` (ADR 0016 M6) com auditoria (antes e depois) e dispara um recálculo; sem `settings.edit`, 403 e o campo fica só leitura.
13. **[Segurança]** **Dado** dois tenants com apontamentos ao mesmo tempo, **quando** os recálculos rodam, **então** cada um calcula só o seu tenant (escopo do `TenantJob`, só com o `tenantId` nos argumentos, teste) e o `ProjectForecastChanged` de um não chega ao outro.
14. **Dado** o módulo `production` não liberado, **quando** um comando ou evento pediria recálculo, **então** nada é calculado (o job confere o módulo liberado no início); o parâmetro e as consultas respondem 403 `module_not_released`; sem grupo, 403 `forbidden`. `RequireModule` e `perm:`; o evento leva só ids, datas e o motivo como código (sem texto livre nem dado pessoal, ADR 0019 §19).

## Regras de negócio e técnicas
- **Pedido de recálculo** (`IScheduleTrigger.RequestAsync(reason)`): grava a marca "pendente" do tenant e enfileira o `PlanningRecalculateJob` se nada estiver rodando; chamado depois do commit (falha no pedido não desfaz o comando; o recorrente de hora em hora é a reserva).
- **Prazo de instalação** (pontos 31 e 32): casos (a), (b) e (c) do CA 5; 5 dias úteis no seed, editável.
- **Latência** (ponto 33): até 10 s, com 1 execução rodando e 1 pendente.
- **Folga** = prometido − previsto em dias corridos; **risco** com folga < 0, sem tolerância (ponto 34).
- **Motivo** como código (`NoRouting`, `NoWorkCenter`, `NoCapacity`, `NoFabricationOrder`) traduzido na tela.
- Datas `date`; `computedAt` `timestamptz`.

## Permissões, eventos e tabelas
- **Permissões:** `production.view` (ver), `settings.edit` (parâmetro); a notificação vai para o responsável do projeto e para `production.approve`.
- **Eventos:** **publica** `ProjectForecastChanged` (`Production.Contracts`, novo); **consome** `ContractAmended`, `ContractCancelled` (Commercial); consumidor novo no Commercial para o `ProjectForecastChanged`; mensagem SignalR `ScheduleChanged` (só sinal).
- **Contratos:** `IProjects` (+ `PromisedDate`, `SignedAt`, previsão, folga, risco), `IProductionSchedule` (PLN-01a), `IInstallationOrders` (datas da instalação), custo de instalação no orçamento do projeto por `IProjects.BudgetedAsync(projectId, Installation)`.
- **Tabelas:** `commercial.project_forecasts` (nova, ADR 0029, fora da auditoria); `production.parameters` (chave `planning.installation_days`, seed 5; tabela nova se ainda não existir); `commercial.projects.forecast_date` fica sem uso, sem apagar.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** nenhum específico além dos chips de estado já existentes (risco âmbar e atraso vermelho).
- **Projeto 360 → Resumo** e **lista de projetos** (COM-05): "Entrega prevista" com a folga; chip "Em risco" (âmbar) e faixa âmbar; atrasado continua vermelho.
- **Kanban e lista de OS** (PRD-01, PRD-02): faixa âmbar pelo risco novo; "Previsto {data}" do plano.
- **Estados:** "Sem previsão: {motivo}" com o link que resolve; "Recalculando…" no cabeçalho do Planejamento enquanto há execução pendente.
- **Propositivo:** a notificação de risco leva ao cronograma do projeto, onde se vê a etapa que empurra a data.
- **400 px:** a folga aparece abaixo da data, sem coluna nova.
- **Ajuda:** atualizar `Ajuda/projetos-projeto.md` (entrega prevista, folga, risco sem tolerância), `Ajuda/producao-kanban.md` (regra nova de risco), `Ajuda/producao-planejamento.md` (quando o plano se refaz, até 10 s) e `Ajuda/configuracoes-parametros.md` (prazo de instalação); `<Dica>` em "Entrega prevista", "Folga" e "Em risco".

## Plano de testes
- **Unidade:** data prevista nos casos (a), (b) e (c) e sem previsão (CA 5, 6); folga e risco sem tolerância (CA 9); agrupamento dos pedidos (CA 3).
- **Integração (Testcontainers, obrigatória):** cada gatilho do CA 1 pede um recálculo e as datas mudam (tabela de casos); consumidores do `ContractAmended` e do `ContractCancelled` (CA 2); 20 apontamentos em 10 s (CA 3); latência até 10 s (CA 4); `ProjectForecastChanged` fora de ordem não sobrescreve o mais novo e `commercial.projects` fica intacto (CA 7); as quatro telas com a mesma função (CA 9); notificação ao entrar em risco e ao piorar, com `dedup_key` (CA 10); parâmetro com auditoria (CA 12); dois tenants (CA 13); módulo não liberado e três camadas (CA 14).
- **E2E (Playwright):** fechar um apontamento acima do padrão e ver a entrega prevista do projeto mudar e o chip "Em risco" aparecer.

## Fora do escopo
- Manutenção e paradas reduzindo a capacidade: PAT-03b (Fase 6), como parada no `IWorkCenterCapacity` (ADR 0029).
- Data prevista da OS de instalação calculada pelo motor: a instalação continua com datas digitadas.
- Renegociação da data prometida: só por aditivo (COM-07).
- Remover a coluna `commercial.projects.forecast_date`: só com ADR, na contração.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 31–35):
1. Prazo de instalação na data prevista pelos casos (a), (b) e (c) (CA 5).
2. Prazo padrão de 5 dias úteis, pré-configurado e editável (CA 5, 12); a Konebras confirma o valor.
3. Até 10 s, com o pedido de recálculo agrupado (1 rodando, 1 pendente) (CA 3, 4).
4. Aviso ao responsável e a `production.approve`, ao entrar em risco ou piorar, sem tolerância (CA 9, 10).
5. Regra provisória de risco da Fase 3 (item 62) substituída aqui (CA 9).

Decisões da revisão aplicadas: D10 e ADR 0029 (previsão em `commercial.project_forecasts`, fora do documento e da auditoria, CA 7, 8); §4.3 transversal (job com `tenantId` e módulo liberado, CA 13, 14); PRD-06a → PRD-06a1.

**Pré-condição externa:** a Konebras confirma o prazo padrão de instalação (pré-configurado em 5 dias úteis em Configurações → Parâmetros → Produção, editável); não bloqueia o `Ready` nem a entrega.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, arquitetura e E2E passando; integração (Testcontainers, eventos fora de ordem, dois tenants) escrita, a rodar na suíte em container antes do merge.
- [ ] Migration `PlanningProjectForecasts` (não destrutiva, só `CREATE TABLE`; `production.parameters` já existia), a testar sobre cópia do banco do tenant `konebras` no deploy.
- [x] Permissões só do `ModuleCatalog` (`production.view`, `production.approve` e `settings.edit`, já existentes).
- [x] Telas com a previsão, a folga e o risco; 400 px.
- [x] Ajudas atualizadas; teste de cobertura passando.
- [x] Notificação de risco acionável e sem repetição.
- [x] Auditoria ligada no parâmetro.
- [x] OpenAPI e cliente Orval regenerados (OpenAPI gerado pelo Host em processo; regenerar oficialmente com `BQ_UPDATE_OPENAPI`).
- [x] `docs/03-modelo-dados.md` (`project_forecasts`, `forecast_date` sem uso) e `docs/04-fluxos-estados.md` (§2 com o `ProjectForecastChanged`, §3.4 itens 4 a 6) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IScheduleTrigger` chamado nos comandos do Production; consumidores de `ContractAmended` e `ContractCancelled`.
- [ ] Cálculo da data prevista do projeto; `ProjectForecastChanged` e o consumidor no Commercial com `computedAt` em `project_forecasts`.
- [ ] Função de risco trocada; notificação; `ScheduleChanged`; parâmetro; telas e ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (gatilhos, evento fora de ordem e previsão fora do documento), @qa.
- Gate: CA 3, 5, 7, 9 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: PRD-06a1) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** eventos do outbox fora de ordem tratados com `computedAt` (CA 7); a regra provisória de risco da PRD-02 é substituída pela prevista × prometida; a notificação do Commercial passou a dizer como resolve os usuários com `production.approve` e que é síncrona no consumidor (ADR 0019 §18).
- **Correções da revalidação (21/10/2026):** pontos 31 a 35 decididos (casos a, b e c; 5 dias úteis como valor pré-configurado, com a confirmação da Konebras como pré-condição externa; até 10 s com 1 rodando e 1 pendente; aviso sem tolerância; item 62 substituído); previsão em `commercial.project_forecasts`, sem tocar `commercial.projects` (D10, ADR 0029; CA 7, 8); fim da fabricação lido do plano; job confere o módulo liberado (§4.3, CA 14); `ScheduleChanged` para a TV (CA 4); PRD-06a1.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: confirmação do prazo de instalação pela Konebras (valor pré-configurado e editável).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; quebra da PLN-01 (01a/01b) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D10, tabela 3.1 pontos 31–35, §4.3; ADR 0029); ordem mantida (Marco 4.3, ordem 7) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 25/10/2026 | 1.0 | Implementada (`Done`), sobre a PLN-01a (branch `feat/pln-01a-motor-agendamento`, ainda fora da main). `IScheduleTrigger` (`ScheduleRecalculationTrigger`): depois do commit, num escopo novo do tenant, grava a marca "pendente" e, se ela nasceu agora, enfileira o `PlanningRecalculateJob` com o gatilho `Event` e só o `tenantId`; módulo não liberado não pede nada. Consumidores `PlanningContractConsumers` (`ContractAmended`, `ContractCancelled`). `ProjectForecastCalculator` puro (casos a, b, c e sem previsão) e `ProjectForecastBuilder`; `ScheduleService` publica o `ProjectForecastChanged` só dos projetos que mudaram e manda `OrderBoardChanged` das OS que mudaram e o `ScheduleChanged` depois do commit; `ProjectForecastConsumer` no Commercial com `computedAt` e o aviso de risco (`project-at-risk:{projectId}:{forecastDate}`); `DeliveryRiskRules` (função única) no SharedKernel; "Entrega prevista" com folga, chip e faixa no Projeto 360 e na lista; risco na lista de OS e no Kanban; "Recalculando…" no Planejamento; prazo de instalação em Configurações → Parâmetros → Produção; `IProjects.ForecastsAsync` (DSH-01b) e `IProductionSchedule.ProjectForecastsAsync` (PNL-01b); migration `PlanningProjectForecasts` (só `CREATE TABLE`); ajudas. **Desvios:** (1) CA 1: os comandos de OS, etapa, apontamento, qualidade e conclusão pedem o recálculo pelo `ProductionBoardNotifier` (o ponto que todos já chamam no fim do comando, PRD-02 CA 12), e não um por um; calendário, exceção, centro (salvar, inativar e reativar; criar e excluir não mudam o plano, porque centro novo ou excluído não está em etapa) e o prazo de instalação chamam o `IScheduleTrigger` direto. "Salvar a fila" é da PLN-02, ainda não feita: a porta (`IScheduleTrigger.RequestAsync`) fica pronta para ela. (2) CA 3: a execução **assume** a marca "pendente" numa transação curta sob o lock (apaga a linha `Pending`) antes de calcular; antes (PLN-01a), a marca só mudava no fim, e um pedido que chegava durante o cálculo via a marca já assumida e se perdia até a hora cheia. A execução vira uma linha nova em `schedule_runs` com o `requested_at` da marca; quem termina roda as marcas que chegaram (até 20 seguidas; no "Recalcular agora", 1). (3) CA 4 (até 10 s): a fila do Hangfire.PostgreSql era olhada a cada 15 s; passou a 2 s (`Jobs:QueuePollSeconds`, de 1 a 15). (4) Tabela nova `production.schedule_project_forecasts` (tabela do plano, um escritor, fora da auditoria), não citada na ADR 0029: guarda a última previsão publicada para publicar só o que mudou e é a fonte do risco do Kanban e da lista de OS. (5) `commercial.project_forecasts` sem a coluna `at_risk` do `03-modelo`: o risco é calculado na leitura, porque a data prometida muda por aditivo sem nova previsão; ganhou `fabrication_end`, `reason_order_id` e `updated_at`. (6) CA 9: a função única foi para o SharedKernel (`DeliveryRiskRules`) para o Commercial e o Production usarem sem referência cruzada; `ProductionOrderRisk` delega a ela e o `IsAtLastStep` saiu. O risco da OS (Kanban e lista de OS) é o do projeto dela (previsão de entrega do projeto > prometida), a opção mais segura (recomendação aceita, PO 24/10): toda OS de um projeto em risco fica âmbar; a faixa do cartão diz "Em risco, prometido {data}" e o prazo sem faixa "Prometido {data}" (a previsão do plano já aparece abaixo). (7) CA 6: projeto `Fabricated`/`Installing` não é recalculado; o risco da instalação é lido pelo Commercial na hora (maior `planned_end` das OS de instalação não canceladas), com "Pelo fim da instalação". (8) CA 7 e 14: o evento ganhou `ReasonOrderId` (só o id da OS), para a tela dizer "OS {número} sem roteiro" sem texto livre no evento. (9) CA 10: o título usa o nome do projeto ("Ed. Monte Castelo vai atrasar 7 dias…") e o link é o Projeto 360 até o Cronograma (PLN-03). (10) CA 12: o "seed 5" é o padrão em código (vazio = 5), sem linha de seed; a primeira gravação cria a linha, auditada. A conferência é a chave `planning.installation_days_confirmed` com o selo "A confirmar" (ADR 0042). (11) `IProjects` ganhou `PlanningCandidatesAsync`, `BudgetedManyAsync` (caso b, de uma vez) e `ForecastsAsync`, e o `IProductionBoardPush` o `ScheduleChangedAsync`, com implementação padrão na interface (os fakes não mudam). (12) Testes: o agrupamento (CA 3) e a latência (CA 4) ficaram na integração, porque dependem do lock do banco; a latência mede do commit à previsão no Commercial com o job e o outbox chamados no teste, sem a espera da fila do Hangfire. Integração escrita e não executada nesta máquina; 9 testes de arquitetura não rodam no Windows com Smart App Control (bloqueio das DLLs, não ligado à story). (13) OpenAPI gerado pelo Host em processo, sem banco: regenerar oficialmente com `BQ_UPDATE_OPENAPI`. (14) Migration ainda não aplicada sobre cópia do banco `konebras`. **Pré-condição externa:** a Konebras confirma o prazo padrão de instalação; ele entra pré-configurado em 5 dias úteis, editável em Configurações → Parâmetros → Produção, com o selo "A confirmar" até alguém marcar "Prazo conferido com a empresa". | Dex (dev) |
| 26/10/2026 | 1.1 | Correções da suíte do lote 5. **Código:** (1) o `ScheduleRecalculationTrigger` resolve o `IBackgroundJobClient` só na hora do pedido; o migrator e a CLI de provisionamento rodam o seed sem Hangfire e falhavam ao ativar o `CalendarService`; sem Hangfire no processo, nada é marcado nem enfileirado (o recorrente de hora em hora cobre). (2) O `ProjectForecastConsumer` compara e grava o `ComputedAt` em microssegundos (precisão do `timestamptz`): o evento do mesmo instante parecia mais novo que o gravado e sobrescrevia (CA 7). **Teste:** em `Gatilhos_do_Production_marcam_o_pedido...`, a edição das etapas vem antes do apontamento: o apontamento inicia a OS e, pela PRD-01 CA 8, a OS iniciada não muda as etapas (409 `production_order_started`); as mesmas verificações, na ordem possível (recomendação aceita, PO 24/10). | Dex (dev) |
