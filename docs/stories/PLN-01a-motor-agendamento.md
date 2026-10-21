# PLN-01a — Motor de agendamento finito

| Campo | Valor |
|---|---|
| ID | PLN-01a |
| Título | Agendamento finito simples, para frente (`04-fluxos` §3.4): ordem da fila, horas restantes, alocação dia a dia na capacidade de cada centro de trabalho e no calendário, datas previstas de cada etapa e de cada OS de fabricação, gravação atômica do plano, recálculo manual e de hora em hora, contrato de leitura e de simulação |
| Tamanho | G (quebra da PLN-01: o motor, a gravação e os gatilhos manual e por hora; os gatilhos por evento, a previsão do projeto e o risco ficam na PLN-01b) |
| Status | Draft |
| Requisitos | R-PLN-3 (datas previstas de início e fim de cada etapa de cada OS pela prioridade, horas padrão ou restantes e capacidade) · R-PLN-1 (capacidade, turnos e calendário) · PRD §6 (Desempenho) · R-SEC-5 |
| Dependências | PRD-01 (OS de fabricação, etapas com centro, horas padrão e situação), PRD-03a (horas apontadas), PRD-05 (situação `Quality` e retorno de etapa), CAD-03 (`IWorkCenterCapacity`, centros e calendário) |
| Bloqueia | PLN-01b, PLN-02, PLN-03, PLN-04, PLN-05, PNL-01b, DSH-01b |
| Ordem de execução | 6 (Marco 4.3 "Datas que a fábrica cumpre") |

## User story
Como **supervisor de produção**, quero **que o sistema calcule, a partir da fila e das horas que cada centro tem por dia, quando cada etapa de cada OS vai começar e terminar**, para **dar ao cliente e ao dono uma data de entrega baseada na capacidade real da fábrica, e não no chute**.

## Contexto e referências
- PRD R-PLN-3 ("agendamento finito simples, para frente"); §1.1 problema 3; §1.2 (desvio da data prevista ≤ 15%).
- `04-fluxos-estados` §3.4: (1) projetos pela prioridade manual; no projeto, OS por item; na OS, etapas pela sequência; (2) horas restantes = padrão − apontadas (mínimo 10% do padrão se em andamento); (3) aloca a partir do primeiro dia livre do centro, respeitando calendário, horas por dia e o fim da etapa anterior; (4) data prevista do projeto = maior fim de etapa (+ prazo de instalação); (5) recalcula por evento e de hora em hora; (6) risco: prevista > prometida. Esta story faz 1 a 3 e o recálculo por hora e manual; 4 a 6 ficam na PLN-01b.
- Backlog (CA da PLN-01): "testes com cenários fixos (2 centros, 3 projetos, feriado) batendo datas esperadas; recálculo de 50 OS < 2 s".
- `04-fluxos` §1: etapa da OS `Waiting → Ready → InProgress ↔ Paused → Done | Rejected`, sequencial no MVP.
- CAD-03 (Done) e ADR 0016: `IWorkCenterCapacity.GetAvailableHoursAsync(workCenterId, from, to)` (até 2 anos; dia = 0 se o centro está inativo, ainda não existia ou o dia não é útil; senão o menor entre as horas da exceção e as horas por dia). "Mudança de calendário não publica evento: o planejamento (PLN-01) lê o calendário por chamada síncrona quando recalcula."
- `03-modelo-dados`: `production_orders.planned_start/planned_end`, `production_order_steps.planned_start/planned_end`, `standard_hours`, `work_center_id`; `labor_entries`. ADR 0006 (job recorrente global que enfileira por tenant; `TenantJob<T>`). ADR 0019 §12 (job idempotente, teste de execução dupla e de dois tenants).
- Fase 3 (rascunho): PRD-01 cria as etapas de fabricação com centro e horas (as da instalação continuam sem centro e com datas digitadas, COM-06); PRD-03a permite vários operadores na mesma etapa (ponto 3 de lá); PRD-05 devolve a OS a uma etapa anterior (as etapas do meio voltam a `Waiting`, sem apagar apontamentos).
- P-04 (decidida): Projeto → Corte → Montagem → Qualidade → Expedição; horas padrão no template, editáveis.

## Critérios de aceite
**Entrada do motor**
1. **Dado** o `FiniteScheduler` (Domain do Production, puro, sem banco), **quando** recebe a fila de projetos em ordem, as OS de fabricação não concluídas e não canceladas de cada projeto (pela sequência do item do projeto), as etapas de cada OS (sequência, centro, horas padrão, horas apontadas, situação), a capacidade diária de cada centro e a data de hoje, **então** devolve, para cada etapa não concluída, as horas alocadas por dia e por centro, o início e o fim previstos, e para cada OS o início (primeira etapa) e o fim (última etapa); a mesma entrada dá sempre a mesma saída (determinístico, teste).
2. **Dado** a ordem da fila, **quando** ainda não existe a fila manual (PLN-02), **então** os projetos entram pela data de assinatura do contrato e, no empate, pelo número do projeto (proposta, Pontos em aberto 1); com a PLN-02, pela posição da fila.
3. **Dado** as horas restantes de uma etapa, **quando** o motor calcula, **então**: `Done` = 0 (não aloca); `Waiting` e `Ready` = horas padrão; `InProgress` e `Paused` = max(padrão − apontadas, 10% do padrão); etapa devolvida pela qualidade e reaberta = horas padrão (retrabalho inteiro, proposta, Pontos em aberto 4); horas `numeric(18,4)`; apontadas = Σ minutos líquidos ÷ 60 dos apontamentos fechados e do aberto até agora.

**Alocação**
4. **Dado** as etapas em andamento (`InProgress` ou `Paused`), **quando** o motor aloca, **então** elas entram **antes** da fila, na ordem da fila, a partir de hoje no seu centro (quem já está na máquina continua; proposta, Pontos em aberto 2); depois, as demais etapas pela ordem do CA 1.
5. **Dado** uma etapa a alocar, **quando** o motor procura o começo, **então** ela começa no primeiro dia útil do seu centro a partir de max(hoje, dia seguinte ao fim previsto da etapa anterior da mesma OS) (proposta, Pontos em aberto 3) e consome as horas que sobram em cada dia (capacidade do dia − o que etapas de maior prioridade já ocuparam), dividindo-se em quantos dias precisar; uma etapa de menor prioridade pode ocupar a sobra de um dia antes de uma de maior prioridade, sem atrasá-la.
6. **Dado** OS diferentes do mesmo projeto, **quando** o motor aloca, **então** elas não esperam uma pela outra (só as etapas da mesma OS são sequenciais, `04-fluxos` §1).
7. **Dado** o cenário fixo de teste — hoje **sexta 30/10/2026**; calendário seg–sex com o feriado **02/11/2026** (0 h); **Corte** 8 h/dia e **Montagem** 16 h/dia; fila **P1, P2, P3**; P1: OS-1 com Corte 12 h (`Ready`) e Montagem 20 h; P2: OS-2 com Corte 6 h (`InProgress`, 4 h apontadas) e Montagem 10 h; P3: OS-3 com Corte 4 h e Montagem 4 h — **quando** o motor roda, **então** as datas previstas são exatamente:

| OS | Etapa | Horas | Início | Fim | Alocação |
|---|---|---|---|---|---|
| OS-2 | Corte | 2 (restantes) | 30/10 | 30/10 | 30/10: 2 h |
| OS-1 | Corte | 12 | 30/10 | 03/11 | 30/10: 6 h; 03/11: 6 h |
| OS-1 | Montagem | 20 | 04/11 | 05/11 | 04/11: 16 h; 05/11: 4 h |
| OS-2 | Montagem | 10 | 03/11 | 03/11 | 03/11: 10 h |
| OS-3 | Corte | 4 | 03/11 | 04/11 | 03/11: 2 h; 04/11: 2 h |
| OS-3 | Montagem | 4 | 05/11 | 05/11 | 05/11: 4 h |

   e os fins de fabricação são P1 05/11, P2 03/11 e P3 05/11; o teste também cobre: etapa com apontadas acima do padrão (6 h padrão, 7 h apontadas → 0,6 h), parada parcial (`Stoppage` com 4 h), centro inativo e centro criado depois de hoje.
8. **Dado** uma etapa sem centro, com horas padrão 0 ou cujo centro não tem capacidade nos próximos 2 anos, **quando** o motor roda, **então**: horas 0 → início = fim = o dia em que ela poderia começar (CA 5), sem ocupar capacidade; sem centro ou sem capacidade → a etapa e a OS ficam sem data, com o motivo ("Etapa {nome} sem centro de trabalho", "Centro {nome} sem capacidade nos próximos 2 anos"), e as etapas seguintes da OS também ficam sem data.
9. **Dado** OS sem etapas ("Sem roteiro", PRD-01) e OS de instalação, **quando** o motor roda, **então** não entram no cálculo; as datas da OS de instalação (digitadas na COM-06) nunca são alteradas.

**Gravação e execução**
10. **Dado** uma execução, **quando** termina, **então** numa transação só: grava `planned_start` e `planned_end` das etapas e das OS de fabricação (proposta: essas colunas ficam fora do diff de auditoria, Pontos em aberto 5), substitui as linhas de `production.schedule_allocations` (proposta: `step_id`, `production_order_id`, `work_center_id`, `date`, `hours`) e grava a execução em `production.schedule_runs` (proposta: início, fim, gatilho `Hourly`/`Manual`/`Event`, OS e etapas calculadas, OS com data alterada, duração, situação, erro); quem lê no meio da execução vê o plano anterior inteiro.
11. **Dado** duas execuções pedidas ao mesmo tempo no tenant, **quando** rodam, **então** só uma calcula por vez (advisory lock por tenant) e o pedido que chega durante o cálculo marca "pendente" e roda uma vez depois, nunca em paralelo nem em fila infinita (teste com 10 pedidos simultâneos: no máximo 2 execuções).
12. **Dado** o recorrente global `planning-recalculate` (de hora em hora, no `RecurringJobsRegistrar`, fuso de São Paulo), **quando** dispara, **então** enfileira um `TenantJob` por tenant ativo com o módulo `production` liberado, só com o `tenantId` nos argumentos; o job é idempotente (execução dupla não duplica nada) e um tenant não vê o outro (teste com dois tenants, ADR 0019 §12).
13. **Dado** `production.edit`, **quando** clico "Recalcular agora" no Planejamento, **então** a execução roda no request (ou na fila, se passar de 5 s) e a tela mostra "Recalculado às {hh:mm} ({gatilho}), {n} OS em {ms} ms"; sem `production.edit`, o botão some e a API responde 403.

**Leitura e simulação**
14. **Dado** o `IProductionSchedule` em `Production.Contracts` (novo), **quando** outro módulo ou tela chama `ForOrdersAsync(ids)`, `FabricationEndByProjectsAsync(ids)`, `AllocationsAsync(from, to, workCenterIds?)`, `LastRunAsync()` ou `SimulateAsync(queueOrder, extraProject?)`, **então** recebe as datas e alocações gravadas, ou, no `Simulate`, o resultado do motor em memória com outra ordem de fila ou com um projeto virtual, **sem gravar nada** (usado por PLN-02 e PLN-05).
15. **Dado** a OS de fabricação (aba Etapas) e o Kanban (PRD-02), **quando** abro, **então** cada etapa mostra "Previsto {início} → {fim}" e a OS mostra "Fim previsto {data}", com "Sem previsão: {motivo}" quando o motor não conseguiu (CA 8).

**Desempenho, permissões e segurança**
16. **Dado** 50 OS com 5 etapas cada, 6 centros e 2 anos de calendário, **quando** a execução roda, **então** termina em menos de 2 s (CA do backlog); com 300 OS, em menos de 5 s (proposta); a capacidade de cada centro é lida uma vez por execução (sem N+1).
17. **Dado** o módulo `production` não liberado, **quando** chamo as consultas ou o recálculo, **então** 403 `module_not_released`; sem grupo ou sem `production.view` (consultas) ou `production.edit` (recálculo), 403 `forbidden`. `RequireModule` e `perm:`; nenhuma resposta tem custo.

## Regras de negócio e técnicas
- **Motor puro** (`FiniteScheduler`): entrada e saída em tipos do Domain; o serviço de aplicação monta a entrada (fila, OS, etapas, apontamentos, `IWorkCenterCapacity`) e grava a saída.
- **Capacidade** = horas do centro por dia (CAD-03), independentemente de quantos operadores apontam na etapa (ponto 3 da PRD-03a); paradas de máquina (`production.downtimes`, R-PRD-9) e manutenção (PAT-5) não entram nesta story.
- **Dias** como `date` (local); a alocação é em horas por dia; arredondamento só na exibição (horas com 1 casa).
- **Uma execução, um plano:** as alocações são sempre o resultado da última execução bem-sucedida; execução com erro mantém o plano anterior e registra o erro em `schedule_runs` (alerta de job, FND-12).
- **Sem efeito financeiro, fiscal nem de estoque:** o plano é informação; nada exige confirmação além do botão de recálculo.

## Permissões, eventos e tabelas
- **Permissões:** `production.view` (ver o plano), `production.edit` (recalcular).
- **Eventos:** nenhum nesta story (os gatilhos por evento e o `ProjectForecastChanged` são da PLN-01b).
- **Contratos:** novo `IProductionSchedule` (`Production.Contracts`); usa `IWorkCenterCapacity`, `IProjects` (data de assinatura e número para a ordem provisória).
- **Tabelas:** `production.schedule_allocations` e `production.schedule_runs` (**propostas**); preenche `planned_start`/`planned_end` de `production_orders` e `production_order_steps` (fabricação).

## Telas e estados
- **Fábrica → Produção → Planejamento** (`/app/producao/planejamento`; entrada nova no `modules.ts`, depois do Kanban): nesta story, o cabeçalho da página com a última execução, "Recalcular agora" e os avisos de OS sem previsão; as abas Fila, Cronograma e Ocupação chegam na PLN-02, PLN-03 e PLN-04.
- **Estados:** carregando; vazio ("Nenhuma OS de fabricação aberta para planejar." + "Ver OS de fabricação"); erro da última execução ("O último cálculo falhou às {hh:mm}. As datas mostradas são do cálculo anterior." + "Recalcular agora"); em andamento ("Recalculando…").
- **Propositivo:** cada "Sem previsão" leva à ação que resolve (escolher roteiro, informar centro, revisar calendário).
- **400 px:** cabeçalho com a última execução e o botão; avisos em lista.
- **Ajuda:** `Ajuda/producao-planejamento.md` (rota `/app/producao/planejamento`; a aba padrão nasce aqui e as outras nas stories delas), com a regra do cálculo em linguagem simples; atualizar `Ajuda/producao-os.md` (aba Etapas, datas previstas); `<Dica>` em "Previsto", "Horas restantes" e "Sem previsão".

## Plano de testes
- **Unidade (obrigatória):** o cenário fixo do CA 7 e uma tabela de casos (horas restantes, em andamento primeiro, etapa anterior, sobra de dia, feriado, parada parcial, centro inativo e futuro, horas 0, sem centro, horizonte), determinismo.
- **Integração (Testcontainers):** gravação atômica e leitura durante o cálculo (CA 10); concorrência (CA 11); job recorrente com execução dupla e dois tenants (CA 12); recálculo manual e permissão (CA 13); `IProductionSchedule` com `Simulate` sem gravar (CA 14); desempenho com 50 e 300 OS (CA 16); três camadas (CA 17); OS de instalação intocada (CA 9).
- **E2E (Playwright):** abrir o Planejamento, recalcular e ver a data prevista na OS.

## Fora do escopo
- Gatilhos por evento, data prevista do projeto, prazo de instalação, risco e notificação: PLN-01b.
- Fila manual: PLN-02. Gantt: PLN-03. Ocupação: PLN-04. Simulação do orçamento: PLN-05.
- Agendamento para trás (a partir da data prometida), etapas em paralelo dentro da OS, recursos por operador, paradas e manutenção na capacidade.

## Pontos em aberto
1. **Ordem antes da fila manual** (PO): proposta: data de assinatura do contrato e número do projeto.
2. **Etapas em andamento primeiro** (PO): o `04-fluxos` §3.4 não diz. Proposta: o que já está na máquina continua antes da fila. Alternativa: tudo pela prioridade da fila (uma etapa em andamento de projeto menos prioritário pode ser empurrada).
3. **Início da etapa seguinte** (PO): proposta: no dia útil seguinte ao fim da anterior (sem ordem dentro do dia, conservador). Alternativa: no mesmo dia, com a sobra de capacidade do centro da etapa seguinte.
4. **Horas do retrabalho** (PO, ligado ao ponto 79 da PRD-05): proposta: a etapa reaberta pela NC conta as horas padrão inteiras. Alternativa: padrão − horas já apontadas desde a reabertura.
5. **Datas previstas na auditoria** (arquiteto): recalcular de hora em hora encheria o histórico da OS. Proposta: `planned_start`/`planned_end` fora do diff de auditoria e o registro de cada execução em `schedule_runs`. Alternativa: datas só em `schedule_allocations` (sem tocar nas colunas do 03).
6. **Itens que geram OS e uma OS por item** (herdado dos pontos 1 e 2 da PRD-01): o motor planeja por OS; a decisão muda o número de OS na fila, não a regra.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade (cenário fixo), integração (Testcontainers, job com dois tenants), desempenho e E2E passando.
- [ ] Migration das tabelas novas (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Job pelo `TenantJob<T>` e registrado no `RecurringJobsRegistrar`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados e 400 px.
- [ ] Ajuda do Planejamento e da aba Etapas; teste de cobertura passando.
- [ ] Avisos "Sem previsão" acionáveis.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` §3.4 (regras decididas) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `FiniteScheduler` (Domain) com testes de tabela e o cenário fixo.
- [ ] `ScheduleService` (montar entrada, lock, gravar), `schedule_runs` e `schedule_allocations`.
- [ ] `PlanningRecalculateJob` (`TenantJob`) e o recorrente global; endpoint `/api/v1/production/schedule/recalculate`.
- [ ] `IProductionSchedule`; datas na aba Etapas e no Kanban; página Planejamento e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (motor e gravação), @data-engineer (tabelas e desempenho), @qa.
- Gate: CA 5, 7, 10, 11 e 16 obrigatórios.

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

- **Correções feitas na validação:** quebra da PLN-01 em 01a (motor, gravação, recálculo manual e por hora) e 01b (gatilhos por evento, previsão e risco); o cenário fixo do backlog virou tabela de datas esperadas conferida à mão (CA 7); o `04-fluxos` §3.4 não define etapas em andamento, início da etapa seguinte nem retrabalho: pontos em aberto 2 a 4, sem decidir; datas previstas fora do diff de auditoria como proposta (ponto 5).
- **Pré-condições para `Ready`:** pontos em aberto 2, 3 e 5.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; quebra da PLN-01 (01a/01b) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
