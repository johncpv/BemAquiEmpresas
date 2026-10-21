# PLN-04 — Ocupação da fábrica por semana e centro

| Campo | Valor |
|---|---|
| ID | PLN-04 |
| Título | Horas comprometidas pelo plano ÷ capacidade, por semana e por centro de trabalho, com o consolidado da fábrica, a fila que passa do horizonte, o drill-down até as etapas e o resumo das próximas semanas para o painel do dono e do supervisor |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-PLN-6 (ocupação da fábrica por semana: horas comprometidas ÷ capacidade) · R-PLN-1 · PRD §5.14 (Produção: ocupação da semana atual e das próximas 4) · PRD §6 |
| Dependências | PLN-01a (`schedule_allocations`, `IProductionSchedule`), CAD-03 (`IWorkCenterCapacity`), FND-09e (tokens de gráfico) |
| Bloqueia | DSH-01b (card Produção), DSH-03 (bloco Capacidade) |
| Ordem de execução | 9 (Marco 4.3 "Datas que a fábrica cumpre") |

## User story
Como **supervisor de produção**, quero **ver quanto da capacidade de cada centro já está tomada em cada semana**, para **saber se posso aceitar mais serviço, onde vai faltar gente e quando sobra tempo**.

## Contexto e referências
- PRD R-PLN-6; §5.14 (card Produção do painel do dono: "ocupação da fábrica (semana atual e próximas 4)").
- Protótipo (Painel): "Ocupação da fábrica — Horas comprometidas sobre a capacidade de 176 h por semana", barras por semana com o percentual, a linha "capacidade", barra acima de 100% em destaque e abaixo de 60% em tom baixo (exemplo com 104%).
- PLN-01a: o plano é **finito** — as alocações nunca passam da capacidade do dia; o que não cabe vai para as semanas seguintes. Por isso a ocupação pelo plano fica sempre ≤ 100% (Pontos em aberto 1).
- CAD-03: `IWorkCenterCapacity.GetAvailableHoursAsync` (horas por dia, com calendário, feriados e paradas).
- FND-09e/COM-08: o `Chart` só desenha linhas; a COM-08 fez barras próprias com os tokens `--chart-*` e tabela equivalente.

## Critérios de aceite
1. **Dado** `production.view`, **quando** abro Planejamento → Ocupação (`?aba=ocupacao`), **então** vejo, para as próximas 8 semanas a partir da atual (segunda a domingo; até 26 semanas), uma linha por centro de trabalho ativo e a linha "Fábrica", cada célula com horas comprometidas, capacidade e ocupação % (comprometidas ÷ capacidade, 1 casa; capacidade 0 → "—").
2. **Dado** a regra, **quando** a célula é calculada, **então** **comprometidas** = Σ horas de `schedule_allocations` do centro nos dias da semana, e **capacidade** = Σ horas disponíveis do centro nos mesmos dias (`IWorkCenterCapacity`), contando na semana atual só os dias a partir de hoje (o plano começa hoje); a linha "Fábrica" = Σ comprometidas ÷ Σ capacidades; o dataset fixo do PLN-01a CA 7 dá, na semana de 26/10 a 01/11, Corte 8 de 8 h (100,0%) e Montagem 0 de 16 h (0,0%), e na semana de 02/11 a 08/11, Corte 10 de 32 h (31,3%) e Montagem 34 de 64 h (53,1%) (teste).
3. **Dado** a fila além do horizonte mostrado, **quando** a tela monta, **então** o quadro "Depois de {data}" mostra, por centro, as horas já planejadas depois da última semana visível e a data do último dia planejado ("Corte ocupado até 19/02"), para mostrar a carga que o percentual ≤ 100% esconde (proposta, Pontos em aberto 1).
4. **Dado** uma célula, **quando** clico, **então** o painel lateral lista as etapas alocadas naquele centro e semana (OS, projeto, etapa, horas na semana, início e fim previstos), com link para a OS e o total igual ao da célula.
5. **Dado** as faixas, **quando** a célula aparece, **então** usa o tom de estado: abaixo de 60% "Folga" (informativo), de 60% a 95% sem destaque, 95% ou mais "Lotado" (alerta) (proposta, Pontos em aberto 2); cor sempre dos tokens, com o texto do estado junto (não só a cor).
6. **Dado** a visão em gráfico, **quando** escolho "Gráfico", **então** vejo barras por semana (fábrica ou um centro escolhido) com a linha de capacidade e o rótulo do percentual, num componente `ColumnChart` novo nos componentes de UI (tokens `--chart-*`, tabela equivalente acessível por teclado, reaproveitando o padrão das barras da COM-08).
7. **Dado** os filtros, **quando** escolho linha (dos centros), centro e número de semanas, **então** a tabela e o gráfico filtram e os filtros ficam na URL.
8. **Dado** o `IFactoryLoad` em `Production.Contracts` (novo), **quando** o painel do dono (DSH-01b) ou o do supervisor (DSH-03) pedem `WeeklyAsync(weeks: 5, workCenterIds?)`, **então** recebem por semana e centro as comprometidas, a capacidade e o percentual, iguais aos da tela (teste).
9. **Dado** 20 centros e 26 semanas com 300 OS planejadas, **quando** abro, **então** a resposta chega em menos de 1 s (agregação no banco por centro e semana; capacidade lida uma vez por centro).
10. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `production.view`, 403 `forbidden`. `RequireModule` e `perm:`; sem custo na resposta; parâmetros por allowlist (422 fora dela).

## Regras de negócio e técnicas
- **Leitura do plano gravado** (PLN-01a): nada é recalculado aqui; a tela mostra "Plano de {hh:mm}" da última execução.
- **Semana ISO** (segunda a domingo), datas `date` locais; horas `numeric(18,4)`, exibidas com 1 casa.
- **Centro inativo** não aparece; centro criado no meio do período tem capacidade 0 antes da criação (CAD-03).

## Permissões, eventos e tabelas
- **Permissões:** `production.view`.
- **Eventos:** nenhum.
- **Contratos:** novo `IFactoryLoad` (`Production.Contracts`); usa `IWorkCenterCapacity`, `IWorkCenters`.
- **Tabelas:** nenhuma nova; índice em `schedule_allocations (work_center_id, date)`.

## Telas e estados
- **Planejamento → Ocupação**: alternância Tabela/Gráfico; tabela com a primeira coluna fixa (centro) e as semanas ("S45 · 02/11"); quadro "Depois de {data}".
- **Estados:** carregando; vazio ("Nenhuma etapa planejada no período." + "Ver a fila"); sem centros ("Cadastre os centros de trabalho para ver a ocupação." + "Centros de trabalho"); erro.
- **Propositivo:** célula "Lotado" leva às etapas da semana; "Folga" sugere "Ver a fila" para antecipar.
- **400 px:** uma semana por vez (seletor de semana) com os centros em lista; o gráfico ocupa a largura.
- **Ajuda:** `Ajuda/producao-planejamento-ocupacao.md` (aba `ocupacao`); `<Dica>` em "Comprometidas", "Capacidade", "Lotado" e "Depois de".

## Plano de testes
- **Unidade:** soma por semana, percentual e faixas (CA 2, 5).
- **Integração (Testcontainers):** dataset do PLN-01a com os números do CA 2; quadro "Depois de" (CA 3); drill-down igual à célula (CA 4); `IFactoryLoad` igual à tela (CA 8); desempenho (CA 9); três camadas e allowlist (CA 10).
- **E2E (Playwright):** abrir a Ocupação, trocar para o gráfico, abrir uma célula; tela a 400 px.

## Fora do escopo
- Ocupação por operador e por turno.
- Paradas de máquina e manutenção na capacidade (R-PRD-9, PAT-5).
- "Se eu aceitar este orçamento": PLN-05.

## Pontos em aberto
1. **Ocupação acima de 100%** (PO): o protótipo mostra 104%, mas o plano finito nunca passa de 100%. Proposta: ocupação pelo plano (≤ 100%) mais o quadro "Depois de {data}" com a carga que ficou para frente. Alternativa: uma segunda medida, "carga para cumprir o prometido" (horas que precisariam ser feitas na semana para entregar no prazo), que pode passar de 100%.
2. **Faixas de cor** (PO): proposta: < 60% folga, ≥ 95% lotado.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), desempenho e E2E passando.
- [ ] Índice de apoio, se a medição pedir.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados, gráfico acessível e 400 px; `ColumnChart` com tokens e sem cor literal.
- [ ] Ajuda da aba; teste de cobertura passando.
- [ ] Drill-down acionável.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `FactoryLoadQuery` e `IFactoryLoad`; endpoint `/api/v1/production/planning/load`.
- [ ] `ColumnChart` (UI) e a aba Ocupação; ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, design (gráfico de barras e acessibilidade).
- Gate: CA 2, 4 e 8 obrigatórios.

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

- **Correções feitas na validação:** o protótipo mostra 104%, mas o plano finito nunca passa de 100%: ponto em aberto 1 com proposta (quadro "Depois de"); os números do CA 2 foram conferidos com o cenário da PLN-01a, contando na semana atual só os dias a partir de hoje.
- **Pré-condições para `Ready`:** ponto em aberto 1.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
