# PLN-04 — Ocupação da fábrica por semana e centro

| Campo | Valor |
|---|---|
| ID | PLN-04 |
| Título | Horas comprometidas pelo plano ÷ capacidade, por semana e por centro de trabalho, com o consolidado da fábrica (até 100% no plano finito), o quadro "Depois de {data}" com a carga que ficou para frente, "Acima da capacidade" só quando a capacidade cai depois do plano, o drill-down até as etapas e o resumo das próximas semanas para o painel do dono e do supervisor |
| Tamanho | M |
| Status | Done |
| Requisitos | R-PLN-6 (ocupação da fábrica por semana: horas comprometidas ÷ capacidade) · R-PLN-1 · PRD §5.14 (Produção: ocupação da semana atual e das próximas 4) · PRD §6 |
| Dependências | PLN-01a (`schedule_allocations`, `IProductionSchedule`), CAD-03 (`IWorkCenterCapacity`), DSH-02 (`ColumnChart`), FIN-08 (`TreeTable` com coluna fixa e navegação por célula) |
| Bloqueia | DSH-01b (card Produção), DSH-03 (bloco Capacidade) |
| Ordem de execução | 9 (Marco 4.3 "Datas que a fábrica cumpre") |

## User story
Como **supervisor de produção**, quero **ver quanto da capacidade de cada centro já está tomada em cada semana**, para **saber se posso aceitar mais serviço, onde vai faltar gente e quando sobra tempo**.

## Contexto e referências
- PRD R-PLN-6; §5.14 (card Produção do painel do dono: "ocupação da fábrica (semana atual e próximas 4)").
- Protótipo (Painel): "Ocupação da fábrica — Horas comprometidas sobre a capacidade de 176 h por semana", barras por semana com o percentual, a linha "capacidade", barra acima de 100% em destaque e abaixo de 60% em tom baixo (exemplo com 104%).
- PLN-01a: o plano é **finito** — as alocações nunca passam da capacidade do dia; o que não cabe vai para as semanas seguintes. Por isso a ocupação pelo plano fica ≤ 100%; só passa disso quando a capacidade cai depois da última execução (exceção nova no calendário, parada), até o próximo recálculo.
- **ADR 0029** (D10): capacidade por centro (férias não reduzem); manutenção vira parada no `IWorkCenterCapacity` (PAT-03b, Fase 6).
- CAD-03: `IWorkCenterCapacity.GetAvailableHoursAsync` (horas por dia, com calendário, feriados e paradas).
- DSH-02 (D18): `ColumnChart` dentro do `ChartFrame` (FIN-07), com referência em y; FIN-08 (D18): `TreeTable` com coluna fixa e navegação por célula.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D10, D18; tabela 3.1, pontos 40 e 41; §4.4 padrões 1 e 3.

## Critérios de aceite
1. **Dado** `production.view`, **quando** abro Planejamento → Ocupação (`?aba=ocupacao`), **então** vejo, para as próximas 8 semanas a partir da atual (segunda a domingo; até 26 semanas), uma linha por centro de trabalho ativo e a linha "Fábrica", cada célula com horas comprometidas, capacidade e ocupação % (comprometidas ÷ capacidade, 1 casa; capacidade 0 → "—").
2. **Dado** a regra, **quando** a célula é calculada, **então** **comprometidas** = Σ horas de `schedule_allocations` do centro nos dias da semana, e **capacidade** = Σ horas disponíveis do centro nos mesmos dias (`IWorkCenterCapacity`, lida agora), contando na semana atual só os dias a partir de hoje (o plano começa hoje); a linha "Fábrica" = Σ comprometidas ÷ Σ capacidades; o dataset fixo do PLN-01a CA 7 dá, na semana de 26/10 a 01/11, Corte 8 de 8 h (100,0%) e Montagem 0 de 16 h (0,0%), e na semana de 02/11 a 08/11, Corte 10 de 32 h (31,3%) e Montagem 34 de 64 h (53,1%) (teste).
3. **Dado** a fila além do horizonte mostrado, **quando** a tela monta, **então** o quadro **"Depois de {data}"** mostra, por centro, as horas já planejadas depois da última semana visível e a data do último dia planejado ("Corte ocupado até 19/02"), para mostrar a carga que o percentual ≤ 100% esconde.
4. **Dado** uma célula em que as comprometidas passam da capacidade lida agora (a capacidade caiu depois da última execução do plano: exceção nova, parada), **quando** aparece, **então** usa o tom **crítico** com o texto "Acima da capacidade" e a ação "Recalcular agora" (com `production.edit`); é o único caso em que a ocupação passa de 100%.
5. **Dado** uma célula, **quando** clico ou dou Enter, **então** o painel lateral lista as etapas alocadas naquele centro e semana (OS, projeto, etapa, horas na semana, início e fim previstos), com link para a OS e o total igual ao da célula.
6. **Dado** as faixas, **quando** a célula aparece, **então** usa o tom de estado: abaixo de 60% **"Folga"** (informativo), de 60% a menos de 95% sem destaque, 95% ou mais **"Lotado"** (alerta), acima de 100% o crítico do CA 4; cor sempre dos tokens, com o texto do estado junto (não só a cor).
7. **Dado** a tabela, **quando** navego, **então** ela é a `TreeTable` (FIN-08) com a coluna do centro fixa na rolagem horizontal e a navegação por célula pelo teclado (setas, Home/End, Enter abre o drill-down).
8. **Dado** a visão em gráfico, **quando** escolho "Gráfico", **então** vejo o `ColumnChart` (DSH-02) com uma barra por semana (fábrica ou um centro escolhido), o rótulo direto do percentual em cada barra, a **referência em y "Capacidade" (100%)**, o resumo textual acima ("Semana mais ocupada: {semana}, {x}%. Corte ocupado até {data}.") e a tabela equivalente.
9. **Dado** os filtros, **quando** escolho linha (dos centros), centro e número de semanas, **então** a tabela e o gráfico filtram e os filtros ficam na URL.
10. **Dado** o `IFactoryLoad` em `Production.Contracts` (novo), **quando** o painel do dono (DSH-01b) ou o do supervisor (DSH-03) pedem `WeeklyAsync(weeks: 5, workCenterIds?)`, **então** recebem por semana e centro as comprometidas, a capacidade, o percentual, a faixa e a data "ocupado até", iguais aos da tela (teste).
11. **Dado** 20 centros e 26 semanas com 300 OS planejadas, **quando** abro, **então** a resposta chega em menos de 1 s (agregação no banco por centro e semana; capacidade lida uma vez por centro).
12. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `production.view`, 403 `forbidden`. `RequireModule` e `perm:`; sem custo na resposta; parâmetros por allowlist (422 fora dela).

## Regras de negócio e técnicas
- **Leitura do plano gravado** (PLN-01a): nada é recalculado aqui; a tela mostra "Plano de {hh:mm}" da última execução.
- **Ocupação até 100%** (ponto 40): pelo plano finito, com o "Depois de"; acima de 100% só quando a capacidade cai depois do plano.
- **Faixas** (ponto 41): < 60% "Folga", 60–95% sem destaque, ≥ 95% "Lotado".
- **Capacidade** por centro, sem descontar férias (ADR 0029).
- **Semana ISO** (segunda a domingo), datas `date` locais; horas `numeric(18,4)`, exibidas com 1 casa.
- **Centro inativo** não aparece; centro criado no meio do período tem capacidade 0 antes da criação (CAD-03).

## Permissões, eventos e tabelas
- **Permissões:** `production.view` (ver); `production.edit` (o "Recalcular agora" do CA 4, da PLN-01a).
- **Eventos:** nenhum.
- **Contratos:** novo `IFactoryLoad` (`Production.Contracts`); usa `IWorkCenterCapacity`, `IWorkCenters`.
- **Tabelas:** nenhuma nova; índice em `schedule_allocations (work_center_id, date)`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 1 (`ColumnChart` com referência "Capacidade", rótulo direto, resumo textual e tabela equivalente) e 3 (tabela analítica com coluna fixa, navegação por célula e drill-down com total igual ao da célula).
- **Planejamento → Ocupação**: alternância Tabela/Gráfico; `TreeTable` com a coluna do centro fixa e as semanas ("S45 · 02/11"); quadro "Depois de {data}".
- **Estados:** carregando; vazio ("Nenhuma etapa planejada no período." + "Ver a fila"); sem centros ("Cadastre os centros de trabalho para ver a ocupação." + "Centros de trabalho"); erro; "Acima da capacidade" (CA 4).
- **Propositivo:** célula "Lotado" leva às etapas da semana; "Folga" sugere "Ver a fila" para antecipar; "Acima da capacidade" oferece "Recalcular agora".
- **400 px:** uma semana por vez (seletor de semana) com os centros em lista; o gráfico ocupa a largura; botões com 44 px.
- **Ajuda:** `Ajuda/producao-planejamento-ocupacao.md` (aba `ocupacao`: por que a ocupação não passa de 100%, o "Depois de", o "Acima da capacidade"); `<Dica>` em "Comprometidas", "Capacidade", "Lotado", "Acima da capacidade" e "Depois de".

## Plano de testes
- **Unidade:** soma por semana, percentual, faixas e o acima da capacidade (CA 2, 4, 6).
- **Integração (Testcontainers):** dataset do PLN-01a com os números do CA 2; quadro "Depois de" (CA 3); exceção nova no calendário depois do plano dá "Acima da capacidade" (CA 4); drill-down igual à célula (CA 5); `IFactoryLoad` igual à tela (CA 10); desempenho (CA 11); três camadas e allowlist (CA 12).
- **Front (Vitest):** navegação por célula na `TreeTable` da ocupação (CA 7); `ColumnChart` com a referência (CA 8).
- **E2E (Playwright):** abrir a Ocupação, trocar para o gráfico, abrir uma célula pelo teclado; tela a 400 px.

## Fora do escopo
- Ocupação por operador e por turno.
- Paradas de manutenção na capacidade: PAT-03b (Fase 6), pelo `IWorkCenterCapacity`, sem mudar esta tela.
- "Carga para cumprir o prometido" (medida que passa de 100%): fora do MVP.
- "Se eu aceitar este orçamento": PLN-05.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 40–41):
1. Ocupação até 100% no plano finito, com o quadro "Depois de"; crítico "Acima da capacidade" só quando a capacidade cair depois do plano (CA 3, 4).
2. Faixas: < 60% "Folga", 60–95% sem destaque, ≥ 95% "Lotado" (CA 6).

Decisões da revisão aplicadas: D18 (`ColumnChart` da DSH-02 e `TreeTable` da FIN-08 reaproveitados; esta story não cria componente); D10 (capacidade por centro, sem férias); §4.4 padrões 1 e 3.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, arquitetura, front (Vitest) e E2E passando; integração (Testcontainers) e desempenho escritos, a rodar na suíte em container antes do merge.
- [x] Índice de apoio, se a medição pedir: nenhum novo; o `(date, work_center_id)` da PLN-01a já atende a consulta (confirmar o CA 11 na suíte em container).
- [x] Permissões só do `ModuleCatalog` (`production.view` e `production.edit`, já existentes).
- [x] Tela com estados, gráfico acessível e 400 px; sem cor literal.
- [x] Ajuda da aba; teste de cobertura passando.
- [x] Drill-down e "Acima da capacidade" acionáveis.
- [x] OpenAPI e cliente Orval regenerados (OpenAPI gerado pelo Host em processo; regenerar oficialmente com `BQ_UPDATE_OPENAPI`).
- [x] Story marcada no backlog; `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (§3.4 item 7) atualizados.

## Tarefas técnicas sugeridas
- [x] `FactoryLoadQuery` e `IFactoryLoad`; endpoint `/api/v1/production/planning/load`.
- [x] Aba Ocupação com a tabela (coluna fixa, navegação por célula) e o gráfico; ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, design (tabela analítica e gráfico).
- Gate: CA 2, 4, 5 e 10 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depende da DSH-02 e da FIN-08 pelos componentes) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** o protótipo mostra 104%, mas o plano finito nunca passa de 100%: ponto em aberto com proposta (quadro "Depois de"); os números do CA 2 foram conferidos com o cenário da PLN-01a, contando na semana atual só os dias a partir de hoje.
- **Correções da revalidação (21/10/2026):** pontos 40 e 41 decididos ("Depois de" e o crítico "Acima da capacidade" só quando a capacidade cai depois do plano, CA 3, 4; faixas, CA 6); D18: o `ColumnChart` vem da DSH-02 e a `TreeTable` da FIN-08 (esta story deixou de criar o `ColumnChart`), com a referência "Capacidade" e o resumo textual (CA 7, 8); capacidade sem férias (ADR 0029); CAs renumerados (12).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D10, D18, tabela 3.1 pontos 40–41, §4.4 padrões 1 e 3; ADR 0029); ordem mantida (Marco 4.3, ordem 9); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 25/10/2026 | 1.0 | Implementada (`Done`), sobre a PLN-02 (branch `feat/pln-02-fila`, com a PLN-01b e a PLN-01a, ainda fora da main). `FactoryLoadCalculator` puro no Domain (semanas ISO, só os dias a partir de hoje na semana atual, Σ comprometidas, Σ capacidade, % com 1 casa, faixas, "Fábrica" = Σ ÷ Σ); `FactoryLoadService` lê o plano gravado (`schedule_allocations` agregadas no banco por centro e dia; horas depois e último dia por centro numa consulta) e a capacidade de cada centro ativo uma vez pelo `IWorkCenterCapacity`, sem recalcular nem gravar; `IFactoryLoad.WeeklyAsync(weeks = 5, workCenterIds?)` no `Production.Contracts`, com a mesma conta da tela; endpoints `GET /production/planning/load` e `GET /production/planning/load/cell` (`production.view`, no grupo do `RequireModule`); aba Ocupação do Planejamento com "Plano de {hh:mm}", tabela, gráfico, "Depois de {data}", faixa "Acima da capacidade" com "Recalcular agora", painel da célula, filtros na URL e 400 px; ajuda `producao-planejamento-ocupacao.md`. Sem migration. **Desvios:** (1) CA 8: o `ColumnChart` (DSH-02) e o `ChartFrame` (FIN-07) estão em outras branches, fora da main: o gráfico usa o `Chart` (linhas) da main, que ganhou duas props opcionais, `yReferences` (a referência "Capacidade (100%)", com o eixo sempre incluindo 100) e `valueLabels` (rótulo direto do percentual em cada ponto); **trocar pelo `ColumnChart` dentro do `ChartFrame` depois do merge** (um ponto por semana vira uma barra). (2) CA 7: a `TreeTable` com coluna fixa e navegação por célula (FIN-08) também está fora da main: a tabela é o `OccupancyGrid` local da aba (role `grid`, coluna do centro `sticky`, foco móvel, setas, Home/End, Ctrl+Home/End, Enter ou Espaço abre o painel); **trocar pela `TreeTable` da FIN-08 depois do merge**. Não foi criado componente compartilhado. (3) Índice: nenhum novo; o `(date, work_center_id)` da PLN-01a já foi criado para esta consulta (intervalo de datas com `work_center_id = ANY`); o CA 11 fica a confirmar na suíte em container. (4) CA 11: a agregação no banco é por centro e **dia** (no máximo 182 linhas por centro), e a soma por semana é feita na memória pelo calculador puro; somar por semana ISO no SQL dependeria de função de data do PostgreSQL. (5) CA 9: a linha e o número de semanas vão para a API; o **centro** filtra no cliente (a tabela mostra só ele e o gráfico passa a ser dele), para o seletor manter todos os centros; a API também aceita `workCenterIds` (até 50) para os painéis. Com filtro de linha, a linha "Fábrica" vira "Total de {linha}"; com um centro escolhido, não há linha de total. Na tela a 400 px, a semana escolhida também vai para a URL (`?semana=`). (6) CA 4: além da célula em tom crítico, uma faixa acima da tabela conta as semanas "Acima da capacidade" e traz "Recalcular agora" (o mesmo recálculo do cabeçalho, PLN-01a); o painel da célula também traz o botão. Capacidade 0 com horas planejadas (feriado novo num dia já planejado) dá "Acima da capacidade" com "—" no percentual. (7) CA 6: a faixa é decidida pela razão sem arredondar (94,96% aparece como 95,0%, sem destaque), a opção mais segura (recomendação aceita, PO 24/10). (8) CA 5: o drill-down é `GET /production/planning/load/cell?workCenterId=&weekStart=` (sem `workCenterId` = linha "Fábrica", com os mesmos filtros); a semana precisa começar numa segunda, da atual até 25 semanas à frente (422 `planning_load.week`), centro inativo ou inexistente dá 404, e a lista vai até 500 etapas (o total soma todas). (9) CA 12: allowlist com 422 `planning_load.weeks` (1 a 26), `planning_load.centers` (até 50) e `planning_load.line` (até 120); id ou data mal formados dão 400 do próprio Minimal API. (10) "Plano de {hh:mm}" mostra também a data quando o último cálculo não é de hoje. (11) A tela de 400 px troca a tabela pela lista de uma semana abaixo de 768 px (`md`). (12) Testes: unidade (`FactoryLoadCalculatorTests`, com o cenário fixo rodado pelo motor) e arquitetura (`PlanningLoadRulesTests`) passando; integração (`FactoryLoadTests`: números do CA 2 com o relógio fixo em 30/10/2026, "Depois de", exceção nova, drill-down, `IFactoryLoad` × tela, desempenho e três camadas) escrita e não executada nesta máquina; ela grava as tabelas do plano direto, como o `ScheduleService` faria, para fixar o "hoje". 9 testes de arquitetura de outras stories não rodam no Windows com Smart App Control (bloqueio das DLLs). (13) OpenAPI gerado pelo Host em processo, sem banco: regenerar oficialmente com `BQ_UPDATE_OPENAPI`. Sem decisão pendente do PO. | Dex (dev) |
