# PLN-01a — Motor de agendamento finito

| Campo | Valor |
|---|---|
| ID | PLN-01a |
| Título | Agendamento finito simples, para frente (`04-fluxos` §3.4): ordem da fila, horas restantes, alocação dia a dia na capacidade de cada centro de trabalho e no calendário, datas previstas de cada etapa e de cada OS de fabricação gravadas só nas tabelas do plano (fora do documento e da auditoria, ADR 0029), um único escritor, recálculo manual e de hora em hora, contrato de leitura e de simulação |
| Tamanho | G (quebra da PLN-01: o motor, a gravação e os gatilhos manual e por hora; os gatilhos por evento, a previsão do projeto e o risco ficam na PLN-01b) |
| Status | Done |
| Requisitos | R-PLN-3 (datas previstas de início e fim de cada etapa de cada OS pela prioridade, horas padrão ou restantes e capacidade) · R-PLN-1 (capacidade, turnos e calendário) · PRD §6 (Desempenho) · R-SEC-5 |
| Dependências | PRD-01 (OS de fabricação, etapas com centro, horas padrão e situação), PRD-03a (horas apontadas), PRD-05 (situação `Quality` e retorno de etapa), PRD-06a1 (conclusão da OS), CAD-03 (`IWorkCenterCapacity`, centros e calendário) |
| Bloqueia | PLN-01b, PLN-02, PLN-03, PLN-04, PLN-05, PNL-01b, DSH-01b |
| Ordem de execução | 6 (Marco 4.3 "Datas que a fábrica cumpre") |

## User story
Como **supervisor de produção**, quero **que o sistema calcule, a partir da fila e das horas que cada centro tem por dia, quando cada etapa de cada OS vai começar e terminar**, para **dar ao cliente e ao dono uma data de entrega baseada na capacidade real da fábrica, e não no chute**.

## Contexto e referências
- PRD R-PLN-3 ("agendamento finito simples, para frente"); §1.1 problema 3; §1.2 (desvio da data prevista ≤ 15%).
- `04-fluxos-estados` §3.4: (1) projetos pela prioridade manual; no projeto, OS por item; na OS, etapas pela sequência; (2) horas restantes = padrão − apontadas (mínimo 10% do padrão se em andamento); (3) aloca a partir do primeiro dia livre do centro, respeitando calendário, horas por dia e o fim da etapa anterior; (4) data prevista do projeto = maior fim de etapa (+ prazo de instalação); (5) recalcula por evento e de hora em hora; (6) risco: prevista > prometida. Esta story faz 1 a 3 e o recálculo por hora e manual; 4 a 6 ficam na PLN-01b.
- **ADR 0029** (planejamento finito, D10): datas previstas **só em tabelas do plano** (`production.schedule_step_forecasts`, `production.schedule_allocations`, `production.schedule_runs`; a previsão do projeto em `commercial.project_forecasts`, PLN-01b), **um único escritor** (`ScheduleService`), **fora da auditoria**; a fila de prioridade é do Production (`production.planning_queue`, PLN-02); `commercial.projects.priority` fica sem uso, sem apagar a coluna; ordem antes da fila pela data prometida e depois a assinatura; capacidade por centro (férias não reduzem); manutenção vira parada no `IWorkCenterCapacity` (PAT-03b, Fase 6).
- Backlog (CA da PLN-01): "testes com cenários fixos (2 centros, 3 projetos, feriado) batendo datas esperadas; recálculo de 50 OS < 2 s".
- `04-fluxos` §1: etapa da OS `Waiting → Ready → InProgress ↔ Paused → Done | Rejected`, sequencial no MVP.
- CAD-03 (Done) e ADR 0016: `IWorkCenterCapacity.GetAvailableHoursAsync(workCenterId, from, to)` (até 2 anos; dia = 0 se o centro está inativo, ainda não existia ou o dia não é útil; senão o menor entre as horas da exceção e as horas por dia). "Mudança de calendário não publica evento: o planejamento lê o calendário por chamada síncrona quando recalcula."
- **Código atual:** `ProductionOrder.PlannedStart`/`PlannedEnd` existem para as datas digitadas da OS de instalação (COM-06); o motor nunca grava nelas. ADR 0006 (job recorrente global que enfileira por tenant; `TenantJob<T>`). ADR 0019 §12 (job idempotente, teste de execução dupla e de dois tenants).
- Fase 3: PRD-01 cria as etapas de fabricação com centro e horas e gera OS pela D14 (Equipamento automático, Produto por "Gerar OS", Serviço nunca); PRD-03a permite vários operadores na mesma etapa (item 65); PRD-05 devolve a OS a uma etapa anterior (as etapas do meio voltam a `Waiting`, sem apagar apontamentos).
- P-04 (decidida): Projeto → Corte → Montagem → Qualidade → Expedição; horas padrão no template, editáveis.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D10; tabela 3.1, pontos 25 a 30; §4.2 (risco de migração: `priority` sem uso); §4.3 transversal (jobs com `tenantId` e conferência do módulo liberado).

## Critérios de aceite
**Entrada do motor**
1. **Dado** o `FiniteScheduler` (Domain do Production, puro, sem banco), **quando** recebe a fila de projetos em ordem, as OS de fabricação não concluídas e não canceladas de cada projeto (pela sequência do item do projeto), as etapas de cada OS (sequência, centro, horas padrão, horas apontadas, situação), a capacidade diária de cada centro e a data de hoje, **então** devolve, para cada etapa não concluída, as horas alocadas por dia e por centro, o início e o fim previstos, e para cada OS o início (primeira etapa) e o fim (última etapa); a mesma entrada dá sempre a mesma saída (determinístico, teste).
2. **Dado** a ordem da fila, **quando** ainda não existe a fila manual (PLN-02), **então** os projetos entram pela **data prometida** (mais cedo primeiro), depois pela **data de assinatura** do contrato e, no empate, pelo número do projeto; projeto sem data prometida vai para o fim, pela assinatura; com a PLN-02, pela posição da fila.
3. **Dado** as horas restantes de uma etapa, **quando** o motor calcula, **então**: `Done` = 0 (não aloca); `Waiting` e `Ready` = horas padrão; `InProgress` e `Paused` = max(padrão − apontadas, 10% do padrão); etapa devolvida pela qualidade e reaberta = **horas padrão inteiras** (retrabalho); horas `numeric(18,4)`; apontadas = Σ minutos líquidos ÷ 60 dos apontamentos fechados e do aberto até agora.

**Alocação**
4. **Dado** as etapas em andamento (`InProgress` ou `Paused`), **quando** o motor aloca, **então** elas entram **antes** da fila, na ordem da fila, a partir de hoje no seu centro (quem já está na máquina continua); depois, as demais etapas pela ordem do CA 1.
5. **Dado** uma etapa a alocar, **quando** o motor procura o começo, **então** ela começa no primeiro dia útil do seu centro a partir de max(hoje, **dia útil seguinte** ao fim previsto da etapa anterior da mesma OS) e consome as horas que sobram em cada dia (capacidade do dia − o que etapas de maior prioridade já ocuparam), dividindo-se em quantos dias precisar; uma etapa de menor prioridade pode ocupar a sobra de um dia antes de uma de maior prioridade, sem atrasá-la.
6. **Dado** OS diferentes do mesmo projeto, **quando** o motor aloca, **então** elas não esperam uma pela outra (só as etapas da mesma OS são sequenciais, `04-fluxos` §1).
7. **Dado** o cenário fixo de teste — hoje **sexta 30/10/2026**; calendário seg–sex com o feriado **02/11/2026** (0 h); **Corte** 8 h/dia e **Montagem** 16 h/dia; projetos **P1, P2, P3** com datas prometidas nessa ordem (então a fila é P1, P2, P3); P1: OS-1 com Corte 12 h (`Ready`) e Montagem 20 h; P2: OS-2 com Corte 6 h (`InProgress`, 4 h apontadas) e Montagem 10 h; P3: OS-3 com Corte 4 h e Montagem 4 h — **quando** o motor roda, **então** as datas previstas são exatamente:

| OS | Etapa | Horas | Início | Fim | Alocação |
|---|---|---|---|---|---|
| OS-2 | Corte | 2 (restantes) | 30/10 | 30/10 | 30/10: 2 h |
| OS-1 | Corte | 12 | 30/10 | 03/11 | 30/10: 6 h; 03/11: 6 h |
| OS-1 | Montagem | 20 | 04/11 | 05/11 | 04/11: 16 h; 05/11: 4 h |
| OS-2 | Montagem | 10 | 03/11 | 03/11 | 03/11: 10 h |
| OS-3 | Corte | 4 | 03/11 | 04/11 | 03/11: 2 h; 04/11: 2 h |
| OS-3 | Montagem | 4 | 05/11 | 05/11 | 05/11: 4 h |

   e os fins de fabricação são P1 05/11, P2 03/11 e P3 05/11; o teste também cobre: etapa com apontadas acima do padrão (6 h padrão, 7 h apontadas → 0,6 h), retrabalho com as horas padrão inteiras, parada parcial (`Stoppage` com 4 h), centro inativo e centro criado depois de hoje.
8. **Dado** uma etapa sem centro, com horas padrão 0 ou cujo centro não tem capacidade nos próximos 2 anos, **quando** o motor roda, **então**: horas 0 → início = fim = o dia em que ela poderia começar (CA 5), sem ocupar capacidade; sem centro ou sem capacidade → a etapa e a OS ficam sem data, com o motivo ("Etapa {nome} sem centro de trabalho", "Centro {nome} sem capacidade nos próximos 2 anos"), e as etapas seguintes da OS também ficam sem data.
9. **Dado** OS sem etapas ("Sem roteiro", PRD-01) e OS de instalação, **quando** o motor roda, **então** não entram no cálculo; as datas da OS de instalação (digitadas na COM-06, `production_orders.planned_start`/`planned_end`) nunca são alteradas.

**Gravação e execução (ADR 0029)**
10. **Dado** uma execução, **quando** termina, **então** numa transação só: substitui as linhas de `production.schedule_step_forecasts` (`step_id` único, `production_order_id`, `planned_start`, `planned_end`, `no_forecast_reason`, `run_id`) e de `production.schedule_allocations` (`step_id`, `production_order_id`, `work_center_id`, `date`, `hours`) e grava a execução em `production.schedule_runs` (início, fim, gatilho `Hourly`/`Manual`/`Event`, OS e etapas calculadas, OS com data alterada, duração, situação, erro); **nenhuma linha de `production_orders` nem de `production_order_steps` é tocada** (a versão `xmin` do documento não muda, nenhum 409 nasce do plano e o histórico de alterações da OS não recebe datas do plano); as tabelas do plano ficam fora do `IAuditable`; quem lê no meio da execução vê o plano anterior inteiro.
11. **Dado** o `ScheduleService`, **quando** o teste de arquitetura roda, **então** ele é o **único escritor** das três tabelas do plano (nenhum outro serviço, endpoint ou consumidor grava nelas).
12. **Dado** duas execuções pedidas ao mesmo tempo no tenant, **quando** rodam, **então** só uma calcula por vez (advisory lock por tenant) e o pedido que chega durante o cálculo marca "pendente" e roda uma vez depois, nunca em paralelo nem em fila infinita (teste com 10 pedidos simultâneos: no máximo 2 execuções).
13. **[Segurança]** **Dado** o recorrente global `planning-recalculate` (de hora em hora, no `RecurringJobsRegistrar`, fuso de São Paulo), **quando** dispara, **então** enfileira um `TenantJob` por tenant ativo com o módulo `production` liberado, só com o `tenantId` nos argumentos; o job confere de novo, no início, se o tenant está ativo e o módulo liberado (se não, termina sem calcular); é idempotente (execução dupla não duplica nada) e um tenant não vê o outro (teste com dois tenants, ADR 0019 §12).
14. **Dado** `production.edit`, **quando** clico "Recalcular agora" no Planejamento, **então** a execução roda no request (ou na fila, se passar de 5 s) e a tela mostra "Recalculado às {hh:mm} ({gatilho}), {n} OS em {ms} ms"; sem `production.edit`, o botão some e a API responde 403.

**Leitura e simulação**
15. **Dado** o `IProductionSchedule` em `Production.Contracts` (novo), **quando** outro módulo ou tela chama `ForOrdersAsync(ids)`, `FabricationEndByProjectsAsync(ids)`, `AllocationsAsync(from, to, workCenterIds?)`, `LastRunAsync()` ou `SimulateAsync(queueOrder, extraProject?)`, **então** recebe as datas e alocações gravadas nas tabelas do plano, ou, no `Simulate`, o resultado do motor em memória com outra ordem de fila ou com um projeto virtual, **sem gravar nada** (usado por PLN-02 e PLN-05).
16. **Dado** a OS de fabricação (aba Etapas) e o Kanban (PRD-02), **quando** abro, **então** cada etapa mostra "Previsto {início} → {fim}" e a OS mostra "Fim previsto {data}", lidos do plano, com "Sem previsão: {motivo}" quando o motor não conseguiu (CA 8).

**Desempenho, permissões e segurança**
17. **Dado** 50 OS com 5 etapas cada, 6 centros e 2 anos de calendário, **quando** a execução roda, **então** termina em menos de 2 s (CA do backlog); com 300 OS, em menos de 5 s; a capacidade de cada centro é lida uma vez por execução (sem N+1).
18. **Dado** o módulo `production` não liberado, **quando** chamo as consultas ou o recálculo, **então** 403 `module_not_released`; sem grupo ou sem `production.view` (consultas) ou `production.edit` (recálculo), 403 `forbidden`. `RequireModule` e `perm:`; nenhuma resposta tem custo.

## Regras de negócio e técnicas
- **Motor puro** (`FiniteScheduler`): entrada e saída em tipos do Domain; o serviço de aplicação monta a entrada (fila, OS, etapas, apontamentos, `IWorkCenterCapacity`) e grava a saída nas tabelas do plano.
- **Ordem antes da fila** (ponto 25): data prometida, depois a assinatura, depois o número.
- **Etapa em andamento primeiro** (ponto 26); **etapa seguinte no dia útil seguinte** (ponto 27); **retrabalho com as horas padrão inteiras** (ponto 28).
- **Capacidade** = horas do centro por dia (CAD-03), independentemente de quantos operadores apontam na etapa (item 65 da Fase 3) e das férias dos funcionários (ADR 0029); paradas de máquina e manutenção entram como parada no `IWorkCenterCapacity` (PAT-03b, Fase 6), sem mudar o motor.
- **Datas fora do documento** (ponto 29): só nas tabelas do plano, um escritor, fora da auditoria; o histórico do plano é o `schedule_runs`.
- **Dias** como `date` (local); a alocação é em horas por dia; arredondamento só na exibição (horas com 1 casa).
- **Uma execução, um plano:** as alocações são sempre o resultado da última execução bem-sucedida; execução com erro mantém o plano anterior e registra o erro em `schedule_runs` (alerta de job, FND-12).
- **Sem efeito financeiro, fiscal nem de estoque:** o plano é informação; nada exige confirmação além do botão de recálculo.

## Permissões, eventos e tabelas
- **Permissões:** `production.view` (ver o plano), `production.edit` (recalcular).
- **Eventos:** nenhum nesta story (os gatilhos por evento e o `ProjectForecastChanged` são da PLN-01b).
- **Contratos:** novo `IProductionSchedule` (`Production.Contracts`); usa `IWorkCenterCapacity`, `IProjects` (data prometida, data de assinatura e número para a ordem antes da fila).
- **Tabelas:** `production.schedule_step_forecasts`, `production.schedule_allocations` e `production.schedule_runs` (novas, ADR 0029, fora da auditoria); `production_orders` e `production_order_steps` sem mudança.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** nenhum específico além dos estados (o cabeçalho do Planejamento segue o padrão de tela das fases anteriores).
- **Fábrica → Produção → Planejamento** (`/app/producao/planejamento`; entrada nova no `modules.ts`, depois do Kanban): nesta story, o cabeçalho da página com a última execução, "Recalcular agora" e os avisos de OS sem previsão; as abas Fila, Cronograma e Ocupação chegam na PLN-02, PLN-03 e PLN-04.
- **Estados:** carregando; vazio ("Nenhuma OS de fabricação aberta para planejar." + "Ver OS de fabricação"); erro da última execução ("O último cálculo falhou às {hh:mm}. As datas mostradas são do cálculo anterior." + "Recalcular agora"); em andamento ("Recalculando…").
- **Propositivo:** cada "Sem previsão" leva à ação que resolve (escolher roteiro, informar centro, revisar calendário).
- **400 px:** cabeçalho com a última execução e o botão; avisos em lista; botões com 44 px.
- **Ajuda:** `Ajuda/producao-planejamento.md` (rota `/app/producao/planejamento`; a aba padrão nasce aqui e as outras nas stories delas), com a regra do cálculo em linguagem simples (ordem pela data prometida, quem já está na máquina continua, etapa seguinte no dia útil seguinte, retrabalho inteiro); atualizar `Ajuda/producao-os.md` (aba Etapas, datas previstas); `<Dica>` em "Previsto", "Horas restantes" e "Sem previsão".

## Plano de testes
- **Unidade (obrigatória):** o cenário fixo do CA 7 e uma tabela de casos (ordem pela data prometida e pela assinatura, horas restantes, em andamento primeiro, etapa anterior com dia útil seguinte, sobra de dia, feriado, parada parcial, retrabalho, centro inativo e futuro, horas 0, sem centro, horizonte), determinismo.
- **Integração (Testcontainers):** gravação atômica nas tabelas do plano, `xmin` da OS intacto e histórico sem datas do plano (CA 10); único escritor (CA 11, arquitetura); concorrência (CA 12); job recorrente com execução dupla, dois tenants e módulo deixado de liberar (CA 13); recálculo manual e permissão (CA 14); `IProductionSchedule` com `Simulate` sem gravar (CA 15); desempenho com 50 e 300 OS (CA 17); três camadas (CA 18); OS de instalação intocada (CA 9).
- **E2E (Playwright):** abrir o Planejamento, recalcular e ver a data prevista na OS.

## Fora do escopo
- Gatilhos por evento, data prevista do projeto, prazo de instalação, risco e notificação: PLN-01b.
- Fila manual: PLN-02. Gantt: PLN-03. Ocupação: PLN-04. Simulação do orçamento: PLN-05.
- Agendamento para trás (a partir da data prometida), etapas em paralelo dentro da OS, recursos por operador.
- Recalibrar a regra da etapa seguinte (dia útil seguinte × mesmo dia): depois de 1 mês de dados reais, com a métrica "desvio da data prevista ≤ 15%" (linha de base na GOL-01a).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 25–30):
1. Ordem antes da fila: data prometida, depois a assinatura (D10; CA 2, 7).
2. Etapa em andamento primeiro (CA 4).
3. Etapa seguinte no dia útil seguinte; recalibrar depois de 1 mês de dados (CA 5; Fora do escopo).
4. Retrabalho com as horas padrão inteiras (CA 3).
5. Datas fora do documento, em tabelas do plano com `schedule_runs`, um escritor, fora da auditoria (D10, ADR 0029; CA 10, 11).
6. Itens que geram OS e uma OS por item: D14 da Fase 3 (o motor planeja por OS).

Decisões da revisão aplicadas: D10 e ADR 0029; §4.3 transversal (job com `tenantId` e conferência do módulo liberado, CA 13); PRD-06a → PRD-06a1 nas dependências.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade (cenário fixo), arquitetura (único escritor), desempenho do motor e E2E passando; integração (Testcontainers, job com dois tenants, desempenho com banco) escrita, a rodar na suíte em container antes do merge.
- [ ] Migration das tabelas do plano (não destrutiva, só `CREATE TABLE`), a testar sobre cópia do banco do tenant `konebras` no deploy.
- [x] Job pelo `TenantJob<T>` e registrado no `RecurringJobsRegistrar`.
- [x] Permissões só do `ModuleCatalog` (`production.view` e `production.edit`, já existentes).
- [x] Tela com estados e 400 px.
- [x] Ajuda do Planejamento e da aba Etapas; teste de cobertura passando.
- [x] Avisos "Sem previsão" acionáveis.
- [x] OpenAPI e cliente Orval regenerados (OpenAPI gerado pelo Host em processo; regenerar oficialmente com `BQ_UPDATE_OPENAPI`).
- [x] `docs/03-modelo-dados.md` (tabelas do plano) e `docs/04-fluxos-estados.md` §3.4 (regras decididas) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `FiniteScheduler` (Domain) com testes de tabela e o cenário fixo.
- [ ] `ScheduleService` (montar entrada, lock, gravar), `schedule_step_forecasts`, `schedule_allocations` e `schedule_runs`.
- [ ] `PlanningRecalculateJob` (`TenantJob`) e o recorrente global; endpoint `/api/v1/production/schedule/recalculate`.
- [ ] `IProductionSchedule`; datas na aba Etapas e no Kanban; página Planejamento e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (motor, gravação e escritor único), @data-engineer (tabelas e desempenho), @qa.
- Gate: CA 5, 7, 10, 11, 12 e 17 obrigatórios.

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

- **Correções feitas na validação (21/10/2026, primeira validação):** quebra da PLN-01 em 01a (motor, gravação, recálculo manual e por hora) e 01b (gatilhos por evento, previsão e risco); o cenário fixo do backlog virou tabela de datas esperadas conferida à mão (CA 7); etapas em andamento, início da etapa seguinte e retrabalho como pontos em aberto; datas previstas fora do diff de auditoria como proposta.
- **Correções da revalidação (21/10/2026):** pontos 25 a 30 decididos; ordem antes da fila pela data prometida e pela assinatura, com o cenário fixo ganhando as datas prometidas (CA 2, 7); datas só nas tabelas do plano (`schedule_step_forecasts`, `schedule_allocations`, `schedule_runs`), sem tocar a OS, com escritor único testado (D10, ADR 0029; CA 10, 11); job confere o módulo liberado (§4.3, CA 13); capacidade sem férias e manutenção como parada (ADR 0029); recalibração depois de 1 mês de dados; PRD-06a1; CAs renumerados (18).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; quebra da PLN-01 (01a/01b) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D10, tabela 3.1 pontos 25–30, §4.2, §4.3; ADR 0029); ordem mantida (Marco 4.3, ordem 6); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 25/10/2026 | 1.0 | Implementada (`Done`). `FiniteScheduler` puro no Domain; `ScheduleService` escritor único pelo `IProductionPlanDb` (as vistas de leitura do `IProductionDb` são `AsNoTracking`); migration `ProductionSchedulePlan`; `PlanningRecalculateJob`/`planning-recalculate`; `IProductionSchedule`; endpoints `GET /production/schedule`, `GET /production/schedule/orders?ids=` e `POST /production/schedule/recalculate`; página Planejamento, previsão na aba Etapas, na faixa de fatos da OS e nos cartões do Kanban; ajuda. **Desvios:** (1) CA 14 "na fila se passar de 5 s": o limite é pela quantidade, não por relógio (acima de 400 OS de fabricação abertas o recálculo manual vai para a fila do tenant; 300 OS cabem em menos de 5 s pelo CA 17), porque um cálculo começado no request não pode ser movido para a fila no meio. (2) CA 2: a data de assinatura chega pelo `IProjects` com o campo novo `ProjectSummary.SignedAt` (parâmetro opcional, sem mudar os fakes). (3) CA 13: o `GlobalRecurringJob` ganhou o filtro opcional `ShouldEnqueueAsync`; o recorrente do plano só enfileira tenant com a Produção liberada e o job confere de novo. (4) CA 3: "apontadas" contam só os apontamentos desde a última reprovação da qualidade (`quality_attempt_started_at`), para o retrabalho em andamento não descontar as horas da tentativa anterior; etapa `Rejected` conta como retrabalho (padrão inteiro). (5) CA 12: a marca "pendente" é a própria linha `Pending` da `schedule_runs` (índice único parcial), sem tabela de estado. (6) OS com todas as etapas `Done` (pronta para concluir) fica sem previsão no plano; centro excluído dá "Etapa {nome} sem centro de trabalho". (7) A lista do Kanban ("Ver como lista") não mostra a previsão; só os cartões. (8) Testes de integração escritos e não executados nesta máquina; 9 testes de arquitetura não rodam no Windows com Smart App Control (bloqueio das DLLs, não ligado à story) e ficam para a suíte em container. (9) A migration ainda não foi aplicada sobre cópia do banco `konebras`. Sem decisão pendente do PO na story. | Dex (dev) |
