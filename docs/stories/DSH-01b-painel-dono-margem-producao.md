# DSH-01b — Painel do proprietário: margem, carteira, produção, entregas e resultado gerencial por projeto

| Campo | Valor |
|---|---|
| ID | DSH-01b |
| Título | Os cards que completam o painel do dono: margem projetada (com custos reais) dos projetos em andamento e realizada dos encerrados, com a orçada como contexto, e os piores projetos; projetos em andamento (etapa, entrega, contratado, margem, recebido); carteira a produzir e a faturar; produção (projetos em andamento, OS atrasadas, ocupação da semana atual e das próximas 4) e entregas (previstas no mês, em risco), com os alertas de projeto na faixa "Atenção"; e o "Resultado gerencial por projeto" (receita × custos diretos por período), que saiu do DRE (ADR 0028) |
| Tamanho | G (re-estimada de M: recebe o "Resultado gerencial por projeto" que saiu da FIN-08, com a lista, a visão mês a mês do projeto e o drill-down) |
| Status | Done |
| Requisitos | PRD §5.14 (Margem; Carteira; Produção; Entregas; cada card leva ao detalhe) · R-FIN-13 (DRE gerencial por projeto) · R-PRJ-5 · R-PLN-4 · R-PLN-6 · R-PRP-5 · PRD §6 (Desempenho) |
| Dependências | DSH-01a (módulo `Dashboard`, cache, `DashboardCard`, `Kpi`, faixa Atenção), DSH-02 (`IProjectMargins`, `ColumnChart`), FIN-08 (`IRevenueSummary.GrossRevenueByProjectsAsync`, `TreeTable`), PLN-01b (previsão e risco), PLN-04 (`IFactoryLoad`), PRD-02 (OS atrasadas), COM-05 (lista de projetos), FIN-01 (recebido) |
| Bloqueia | — (fecha o painel do dono) |
| Ordem de execução | 14 (Marco 4.5 "A visão completa do dono") |

## User story
Como **proprietário**, quero **ver no mesmo painel quanto os projetos estão rendendo, quanto ainda tenho para produzir e faturar, como está a fábrica, quais entregas vão atrasar e o resultado de cada projeto mês a mês**, para **fechar a visão da empresa sem abrir outra tela**.

## Contexto e referências
- PRD §5.14: "Margem: média prevista × realizada; ranking de projetos por margem (piores primeiro)"; "Carteira: valor contratado a produzir e a faturar"; "Produção: projetos em andamento, OS atrasadas, ocupação da fábrica (semana atual e próximas 4)"; "Entregas: previstas no mês, em risco (prevista > prometida)". R-FIN-13: DRE gerencial "por mês e por projeto".
- Protótipo (Painel): KPI "Margem real média (projetos ativos) 19,4% — −3,6 pp abaixo dos 23,0% orçados"; tabela "Projetos em andamento — Margem prevista no contrato e projetada com os custos reais" (Projeto, Etapa atual, Entrega com "+n dias"/"No prazo", Contratado, Margem, Recebido); "Ocupação da fábrica"; alerta "Ed. Monte Castelo vai atrasar 7 dias".
- **ADR 0028** (D9): §1 o "Resultado gerencial por projeto" sai do DRE (FIN-08) e vai para o módulo `Dashboard`, com a nota "Não fecha com o DRE mensal: o projeto soma pelo consumo, o DRE pela data do documento."; §2 permissão por card e DTO por forma de permissão; §3 cache por usuário e versão de permissão com contador de geração; §4 faturamento "provisório" até as notas reais.
- ADR 0019 §20 (D7): margem e custo só com `projects.view_cost`. ADR 0025 e adendo (D8): realizado, comprometido e projetado; imposto pelo orçado até a nota.
- DSH-01a: módulo `Dashboard`, cache e a regra "o painel não recalcula: chama o contrato do relatório"; FIN-08: `IRevenueSummary` (receita por título com "Provisório" até as notas; sinal como adiantamento, D4).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D4, D9, D18; tabela 3.1, pontos 12, 23 e 24; §4.3 (DSH-01a/01b: DTO por forma de permissão e `RequireModule` do dono em cada card); §4.4 padrões 1, 2 e 3.

## Critérios de aceite
**Cards**
1. **Dado** os cards desta story, **quando** o painel monta, **então** cada um segue a tabela (fonte por contrato, regra na `<Dica>` "Como calculamos", drill-down em "Ver origem"), com o `RequireModule` e a permissão do dono em cada endpoint de card (DSH-01a CA 4):

| Card | Permissão | Número | Fonte | Drill-down |
|---|---|---|---|---|
| Margem | `projects.view_cost` | **margem projetada (com custos reais)** dos projetos em andamento e **realizada** dos encerrados no ano, ponderadas pela receita, com a **orçada** como contexto e a diferença em pp; os 5 piores pelo desvio | `IProjectMargins.PortfolioAsync` (DSH-02) | Orçado × realizado |
| Projetos em andamento | `projects.view` | até 8 projetos (os atrasados e em risco primeiro): etapa, entrega, contratado; margem orçada → projetada com `projects.view_cost`; recebido % com `finance.view` | `IProjects`, `IProjectMargins`, `IReceivables` | Projeto 360 |
| Carteira | `projects.view` (+ `finance.view` para "a faturar") | a produzir e a faturar (CA 3) | `IProjects`, `IRevenueSummary` (FIN-08) | Lista de projetos filtrada |
| Produção | `production.view` | projetos em andamento, OS atrasadas, OS em risco e a ocupação da fábrica na semana atual e nas 4 seguintes | `IProductionOrders`, `IFactoryLoad` (PLN-04) | Kanban filtrado; Ocupação |
| Entregas | `projects.view` | entregas previstas no mês (data prevista no mês e não entregue), entregues no mês e em risco | `IProjects` (previsão de `project_forecasts` e risco, PLN-01b) | Lista de projetos filtrada |

2. **Dado** o card Margem com o dataset fixo do DSH-02, **quando** abro o painel, **então** os números são iguais aos do relatório "Orçado × realizado" com o filtro correspondente (teste que compara); o `Kpi` diz "Margem projetada (com custos reais) {x}% — {d} pp {abaixo/acima} dos {y}% orçados" e, no drill-down, os encerrados aparecem com a margem **realizada**; sem `projects.view_cost`, o card não é montado, a tabela de projetos não tem a coluna de margem e nenhum DTO do painel leva custo, orçado ou margem (teste de arquitetura).
3. **Dado** a carteira, **quando** calculada, **então** **a produzir** = Σ receita contratada (contrato + aditivos) dos projetos `Planned` e `InProgress`; **a faturar** = Σ receita contratada dos contratos `Active` − Σ receita já reconhecida desses projetos pelo `IRevenueSummary.GrossRevenueByProjectsAsync` (antes das notas reais, os títulos emitidos, com o chip "Provisório"; com as notas, as notas autorizadas); com o dataset fixo, os dois números batem com a soma da lista de projetos de origem (teste).
4. **Dado** a faixa "Atenção" (DSH-01a CA 10), **quando** esta story entra, **então** ela passa a considerar também o projeto mais atrasado em relação ao prometido ("{projeto} vai atrasar {n} dias — Previsão {data}, prometido ao cliente {data}.", crítico, com link para o cronograma do projeto) e continua mostrando no máximo 3 alertas, nesta ordem: caixa negativo, projeto em risco, decisões aguardando você, maior vencido.
5. **Dado** o cache da DSH-01a (ADR 0028 §3), **quando** chegam `ProjectForecastChanged`, `ContractAmended`, `ContractSigned`, `ReceivableSettled` e os eventos de custo do projeto (`StockIssued`, `StockReturned`, `LaborEntryClosed`, `LaborEntryCorrected`), **então** o contador de geração do tenant sobe e os cards são lidos de novo; sem evento, valem os 60 s.

**Resultado gerencial por projeto**
6. **Dado** `dashboard.view`, `projects.view_cost` e `finance.view`, **quando** abro "Resultado por projeto" (`/app/painel/resultado-por-projeto`, link no card Margem e no painel), **então** vejo uma linha por projeto (filtros na URL por situação, cliente e período, padrão os 12 meses até o mês corrente) com **Receita** do período (`IRevenueSummary.GrossRevenueByProjectsAsync`, com o chip "Provisório" nos meses sem nota real), **Custos diretos** por natureza com data no período (realizado do DSH-02 por período: baixas e devoluções de material, horas, títulos confirmados e imposto da nota; `IProjectMargins.RealizedByPeriodAsync`, novo) e **Resultado** = receita − custos diretos, com a margem %; no topo, a nota fixa "Não fecha com o DRE mensal: o projeto soma pelo consumo, o DRE pela data do documento." com a `<Dica>` que explica a diferença (compra para estoque no DRE × baixa no projeto).
7. **Dado** um projeto na lista, **quando** abro, **então** vejo a visão mês a mês dele na `TreeTable` (FIN-08: coluna fixa e navegação por célula), com as linhas Receita, (−) Custos diretos por natureza e = Resultado, a coluna Total e o `ColumnChart` (DSH-02) "Receita × custos diretos por mês" com a referência zero e o resumo textual.
8. **Dado** uma célula do resultado por projeto, **quando** clico ou dou Enter, **então** o painel lateral lista os documentos que somam o valor (títulos e sinais reconhecidos na receita; os lançamentos do drill-down do DSH-02 nos custos, com as horas agregadas por centro sem `hr.view_sensitive`) e o total é igual ao da célula (teste).
9. **Dado** o dataset fixo de teste (os projetos do DSH-02 com títulos do dataset da FIN-08), **quando** abro o resultado por projeto de três meses, **então** receita, custos e resultado de cada projeto batem centavo a centavo com a planilha do teste e a receita somada dos projetos é igual à parcela com projeto da linha Receita bruta do DRE no mesmo período (teste).

**Desempenho, permissões e segurança**
10. **Dado** 5 mil projetos, 200 mil custos de projeto e 300 OS, **quando** abro o painel completo (DSH-01a + DSH-01b) sem cache, **então** tudo chega em menos de 2 s (CA do backlog), cada card em menos de 1,5 s; o resultado por projeto com 5 mil projetos e 12 meses, em menos de 2 s.
11. **[Segurança]** **Dado** o módulo de um card não liberado (`projects`, `production`, `finance`), **quando** o painel monta, **então** o card não é montado; sem a permissão do card, também não; chamado direto, 403 com o código da camada (`module_not_released` ou `forbidden`); o resultado por projeto sem `projects.view_cost` ou sem `finance.view` responde 403 e o link não aparece; os DTOs são por forma de permissão (ADR 0028 §2).

## Regras de negócio e técnicas
- **Projetos em andamento** = `Planned`, `InProgress`, `Fabricated` e `Installing` (como o filtro padrão do DSH-02); **encerrados** = entregues ou concluídos no ano.
- **Margem** (ponto 24): projetada (com custos reais) nos em andamento, realizada nos encerrados, a orçada como contexto; ponderadas pela receita.
- **Carteira** (ponto 23): a produzir pelos projetos ainda não fabricados; a faturar = contratado − receita reconhecida (títulos emitidos com "Provisório" antes das notas reais; notas depois).
- **Resultado por projeto** (ponto 12): receita pela regra da FIN-08 por projeto; custos diretos pelo realizado do DSH-02 por período (não inclui comprometido nem projetado); sem rateio de despesas indiretas.
- **OS atrasadas e em risco** pela função única da PLN-01b; **ocupação** pelo `IFactoryLoad` (≤ 100% pelo plano finito, com o "ocupado até {data}" da PLN-04).
- Dinheiro `numeric(18,2)`; percentuais com 1 casa; diferença de margem em pontos percentuais.
- Endpoints no módulo `Dashboard` (ADR 0028), como os da DSH-01a.

## Permissões, eventos e tabelas
- **Permissões:** `dashboard.view`; `projects.view`, `projects.view_cost`, `finance.view`, `production.view` por card; resultado por projeto com `projects.view_cost` + `finance.view`; `hr.view_sensitive` só para nomes no drill-down de horas.
- **Eventos:** consome os do CA 5 só para subir o contador de geração.
- **Contratos:** `IProjectMargins` (DSH-02, + `RealizedByPeriodAsync`, novo), `IRevenueSummary.GrossRevenueByProjectsAsync` (FIN-08), `IProjects` (+ resumo de carteira e entregas), `IReceivables` (+ recebido por projeto em lote), `IProductionOrders` (+ contagens), `IFactoryLoad` (PLN-04).
- **Tabelas:** nenhuma nova.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 1 (`ColumnChart` com referência zero e resumo textual), 2 (`Kpi` e `DashboardCard`: número clicável até a origem, "Como calculamos", "Atualizado às", estados por card; faixa "Atenção" com até 3 alertas) e 3 (resultado por projeto na `TreeTable` com coluna fixa, navegação por célula e drill-down com total igual ao da célula).
- **Painel do proprietário** (`/app/painel`): `Kpi` "Margem projetada (com custos reais)" na linha de KPIs; tabela "Projetos em andamento" abaixo do caixa; grade com "Produção" (barras de ocupação do `ColumnChart`), "Carteira" e "Entregas".
- **Resultado por projeto** (`/app/painel/resultado-por-projeto` e `/app/painel/resultado-por-projeto/$projectId`): lista com filtros, nota fixa da diferença com o DRE, visão mês a mês do projeto e drill-down.
- **Estados:** carregando, vazio e erro por card, como na DSH-01a ("Nenhum projeto em andamento." + "Ver orçamentos enviados"); resultado por projeto vazio ("Nenhum projeto com receita ou custo no período." + "Limpar filtros").
- **Propositivo:** projeto em risco leva ao cronograma; OS atrasadas abrem o Kanban filtrado; a margem leva ao projeto que mais desviou; o resultado negativo de um projeto leva à aba Custos dele.
- **400 px:** a tabela de projetos mostra projeto, etapa e entrega (contratado, margem e recebido na linha expansível); os demais cards empilhados depois dos da DSH-01a; o resultado por projeto mostra um mês por vez e o drill-down em tela cheia; botões com 44 px.
- **Ajuda:** atualizar `Ajuda/painel.md` com os cards novos e as regras; `Ajuda/painel-resultado-por-projeto.md` (rota `/app/painel/resultado-por-projeto`: receita, custos diretos, por que não fecha com o DRE); `<Dica>` em "A produzir", "A faturar", "Margem projetada (com custos reais)", "Margem realizada", "Em risco", "Custos diretos" e "Provisório".

## Plano de testes
- **Unidade:** carteira e ordem da tabela de projetos (CA 1, 3); ordem da faixa Atenção (CA 4); montagem do resultado por projeto (CA 6, 7).
- **Integração (Testcontainers, obrigatória):** cada card igual à origem com o dataset fixo (CA 2, 3); margem projetada e realizada (CA 2); visibilidade por permissão e por módulo com os usuários do seed, inclusive sem `projects.view_cost` e sem `finance.view` (CA 2, 11); geração do cache por evento (CA 5); resultado por projeto com o dataset fixo e a receita somada igual à do DRE (CA 9); drill-down com total igual (CA 8); desempenho do painel completo e do resultado por projeto (CA 10).
- **Arquitetura:** DTOs do painel sem custo para quem não tem `projects.view_cost`.
- **E2E (Playwright):** painel completo do Proprietário, drill-down da margem e da entrega em risco; abrir o resultado por projeto, um projeto e uma célula pelo teclado; usuário do Financeiro sem a margem e sem o resultado por projeto; tela a 400 px.

## Fora do escopo
- Meta de margem e comparação com o ano anterior.
- Rateio de despesas indiretas por projeto (o resultado é só de custos diretos).
- Receita pela nota autorizada: FIS-03a e FIS-06 trocam a fonte do `IRevenueSummary`; nada muda aqui.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 12, 23 e 24):
1. Carteira como proposta; "a faturar" = contratado − receita reconhecida (títulos emitidos com "Provisório" antes das notas reais; notas depois) (D4; CA 3).
2. "Margem projetada (com custos reais)" nos em andamento e realizada nos encerrados, com a orçada como contexto (CA 1, 2).
3. "Resultado gerencial por projeto" no `Dashboard`, com a nota de que não fecha com o DRE mensal (D9, ADR 0028; CA 6 a 9); a story escolhida para ele é esta, porque já trata margem e projetos e vem depois do módulo `Dashboard` (DSH-01a), do DSH-02 e da FIN-08.

Decisões da revisão aplicadas: D9 e ADR 0028 (§1 a §4); D18 (`ColumnChart`, `TreeTable`, `Kpi` e `DashboardCard` reaproveitados); §4.3 (DTO por forma de permissão e `RequireModule` do dono, CA 1, 11); §4.4 padrões 1, 2 e 3.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers, dataset fixo comparado com as origens), arquitetura, desempenho e E2E passando.
- [x] Nenhuma migration.
- [x] Permissões só do `ModuleCatalog`; cada card com a permissão e o módulo do dono.
- [x] Cards e resultado por projeto com estados e 400 px.
- [x] Ajudas do painel e do resultado por projeto; teste de cobertura passando.
- [x] Alertas de projeto acionáveis.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/04-fluxos-estados.md` (resultado por projeto × DRE) atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] Contratos de resumo (carteira, entregas, contagens de OS, recebido por projeto) em lote; `IProjectMargins.RealizedByPeriodAsync`.
- [x] Endpoints dos cards e do resultado por projeto no módulo `Dashboard`; faixa Atenção ampliada.
- [x] Telas do painel e do resultado por projeto; ajudas.

## Quality gates
- Agentes previstos: @dev, @qa, design (painel completo e resultado por projeto), cybersecurity (margem por permissão).
- Gate: CA 2, 3, 6, 9 e 10 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depende da FIN-08 pela receita por projeto) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** margem só com `projects.view_cost` (D7) e teste de arquitetura nos DTOs; a carteira e a margem "realizada" do PRD × "projetada" do protótipo viraram pontos em aberto com proposta.
- **Correções da revalidação (21/10/2026):** pontos 23 e 24 decididos (a faturar pela receita reconhecida, "Provisório" antes das notas, CA 3; margem projetada com custos reais e realizada dos encerrados, CA 1, 2); o "Resultado gerencial por projeto" veio da FIN-08 para cá (ponto 12, D9, ADR 0028; CA 6 a 9), com `RealizedByPeriodAsync` e a nota de que não fecha com o DRE; cache pela geração do tenant (CA 5); módulo do dono em cada card (§4.3, CA 11); re-estimada de M para G; CAs renumerados (11).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; quebra da DSH-01 (01a/01b) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D4, D9, D18, tabela 3.1 pontos 12, 23 e 24, §4.3, §4.4 padrões 1, 2 e 3; ADR 0028); resultado gerencial por projeto vindo da FIN-08; re-estimada de M para G; ordem mantida (Marco 4.5, ordem 14); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 26/10/2026 | 1.0 | Implementada, `Done`. Entrega, desvios e recomendações abaixo | Dex (dev) |

**Entrega (1.0, 26/10/2026)**
- **Contratos (só `*.Contracts`, sem tabela nova nem migration):** `IProjectMargins.SummaryAsync` (consolidado + os N piores de um cálculo só), `RealizedByPeriodAsync` e `RealizedEntriesAsync` (realizado do DSH-02 pela data de cada lançamento: razão por `occurred_on`, imposto da nota em Impostos, títulos `Confirmed` pela emissão); `ProjectMarginFilter` com a situação `concluido` (entregues e encerrados) e `DeliveredFrom/To`, também nas rotas do Orçado × realizado; `IProjects.PortfolioAsync` (carteira com contratado = contrato + aditivos aprovados e a previsão pela função única) e `ListAsync`; `IReceivables.ReceivedByProjectsAsync` (em lote); `IRevenueSummary.GrossRevenueDocumentsAsync` (drill-down da receita); `IProjectPayableCosts.ConfirmedByMonthAsync`; `IInstallationOrders.LatestPlannedEndsAsync` (fim das OS de instalação em lote: a previsão da lista e do Projeto 360 deixou de fazer uma consulta por projeto em instalação).
- **Dashboard:** `OwnerProjectsService` (cards Margem, Projetos em andamento + colunas de margem e recebido, Carteira, Produção e Entregas), `ProjectResultsService` (lista, visão mês a mês e célula), `DashboardAccess` (permissão do dono com o módulo e o cache de 60 s), regras puras em `OwnerRules` e `ProjectResultRules`; faixa "Atenção" com `project_late` (caixa → projeto → decisões → maior vencido, até 3); geração do cache também por `ProjectForecastChanged`, `StockIssued`, `StockReturned`, `LaborEntryClosed` e `LaborEntryCorrected`.
- **Rotas** (grupo `/dashboard`, `dashboard` + `dashboard.view`; cada uma num grupo com um `RequireModule`): `cards/margin` (projects + `projects.view_cost`), `cards/active-projects` (projects + `projects.view`), `.../margins` (+ `projects.view_cost`), `.../received` (finance + `finance.view`), `cards/backlog` e `cards/deliveries` (projects + `projects.view`; o "a faturar" só com `finance.view`), `cards/production` (production + `production.view`), `project-results`, `project-results/{id}` e `.../cell` (projects + `projects.view_cost` e finance + `finance.view`). Nenhuma permissão nova no `ModuleCatalog`.
- **DTO por forma de permissão (ADR 0028 §2):** marca `[CostDto]` nos tipos com custo; `CompositionRulesTests` exige a marca em todo tipo com custo e que nenhum DTO sem custo alcance um com; `OwnerPanelTests.Rotas_que_devolvem_DTO_com_custo_exigem_projects_view_cost` confere a policy das rotas.
- **Front:** `MarginKpi` na linha de KPIs, `ActiveProjectsPanel` abaixo do caixa (a 400 px, projeto, etapa e entrega; o resto em "Valores"), `ProductionPanel` (`ColumnChart` de ocupação com a referência "capacidade"), `BacklogPanel` e `DeliveriesPanel`; `/app/painel/resultado-por-projeto` (lista com filtros na URL e a nota fixa) e `/app/painel/resultado-por-projeto/$id` (`ColumnChart` e `TreeTable` com coluna fixa, navegação por célula, um mês por vez a 400 px, drill-down no `SidePanel`). Orçado × realizado com "Entregues e encerrados" e o filtro de entrega (drill-down da margem realizada). Ajudas `painel.md` (atualizada), `painel-resultado-por-projeto.md` e `painel-resultado-do-projeto.md`; `docs/04` §3.9.
- **Testes:** `OwnerRulesTests` 6/6 (ordem da tabela, carteira, entregas, faixa Atenção, período e montagem); `OwnerPanelTests` 9/9 (Margem = relatório com o filtro, carteira por delta e pela lista de origem, projetos/entregas/Atenção, Produção = Kanban e ocupação, geração por evento, visibilidade por permissão e por módulo, rotas com custo, resultado por projeto centavo a centavo com a planilha, DRE e drill-down, realizado por período = realizado do DSH-02); `OwnerPanelPerformanceTests` (5 mil projetos, 200 mil custos, 300 OS); vitest e E2E `owner-panel.spec.ts` (desktop e celular).

**Desvios e recomendações aceitas (PO 24/10)**
- OS atrasadas e em risco do card Produção pelo `IProductionBoard.SummaryAsync` da DSH-03 (mesma contagem do Kanban), em vez de contagens novas no `IProductionOrders` — recomendação aceita (PO 24/10).
- "Margem realizada dos encerrados no ano" = entregues ou encerrados com a data de entrega (sem ela, a do encerramento) no ano — recomendação aceita (PO 24/10).
- % recebido do card sobre o contratado com os aditivos (a coluna da lista de projetos da COM-05 usa o contrato sem aditivo); a lista não mudou — recomendação aceita (PO 24/10).
- "Ver origem" de Carteira e Entregas vai à lista de projetos em andamento: a lista ainda não filtra por risco nem por "a faturar".
- Desempenho: o teste mede a melhor de três com usuário novo e 50% de folga (3 s no painel, 2,25 s por card, 3 s no resultado por projeto), como no DSH-02 CA 18, porque a suíte roda sob carga.
- O aviso de custo do projeto sobe a geração quando chega; o Commercial pode gravar a razão logo depois (outbox sem ordem): os 60 s do cache cobrem a diferença.
- Desempenho (CA 10), recomendação aceita (PO 24/10): o `DashboardCache` passou a fazer um cálculo só por chave em pedidos simultâneos (os cards de projeto leem a mesma carteira ao mesmo tempo), e o resultado por projeto, com 500 projetos ou mais, lê a receita e os títulos a pagar em escopos próprios do tenant (outras conexões), em paralelo com a razão, como no `ProjectMarginService.ComputeAsync`.
