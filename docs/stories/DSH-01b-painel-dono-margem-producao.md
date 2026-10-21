# DSH-01b — Painel do proprietário: margem, carteira, produção e entregas

| Campo | Valor |
|---|---|
| ID | DSH-01b |
| Título | Os cards que completam o painel do dono: margem média orçada × projetada e os piores projetos, projetos em andamento (etapa, entrega, contratado, margem, recebido), carteira a produzir e a faturar, produção (projetos em andamento, OS atrasadas, ocupação da semana atual e das próximas 4) e entregas (previstas no mês, em risco), com os alertas de projeto na faixa "Atenção" |
| Tamanho | M (quebra da DSH-01) |
| Status | Draft |
| Requisitos | PRD §5.14 (Margem; Carteira; Produção; Entregas; cada card leva ao detalhe) · R-PRJ-5 · R-PLN-4 · R-PLN-6 · R-PRP-5 · PRD §6 (Desempenho) |
| Dependências | DSH-01a (estrutura, cache, `DashboardCard`, `Kpi`), DSH-02 (`IProjectMargins`), PLN-01b (previsão e risco), PLN-04 (`IFactoryLoad`), PRD-02 (OS atrasadas), COM-05 (lista de projetos), FIN-01 (títulos emitidos) |
| Bloqueia | — (fecha o painel do dono) |
| Ordem de execução | 14 (Marco 4.5 "A visão completa do dono") |

## User story
Como **proprietário**, quero **ver no mesmo painel quanto os projetos estão rendendo, quanto ainda tenho para produzir e faturar, como está a fábrica e quais entregas vão atrasar**, para **fechar a visão da empresa sem abrir outra tela**.

## Contexto e referências
- PRD §5.14: "Margem: média prevista × realizada; ranking de projetos por margem (piores primeiro)"; "Carteira: valor contratado a produzir e a faturar"; "Produção: projetos em andamento, OS atrasadas, ocupação da fábrica (semana atual e próximas 4)"; "Entregas: previstas no mês, em risco (prevista > prometida)".
- Protótipo (Painel): KPI "Margem real média (projetos ativos) 19,4% — −3,6 pp abaixo dos 23,0% orçados"; tabela "Projetos em andamento — Margem prevista no contrato e projetada com os custos reais" (Projeto, Etapa atual, Entrega com "+n dias"/"No prazo", Contratado, Margem, Recebido); "Ocupação da fábrica"; alerta "Ed. Monte Castelo vai atrasar 7 dias".
- ADR 0019 §20 (D7): margem e custo só com `projects.view_cost`.
- DSH-01a: estrutura, cache por tenant com invalidação e a regra "o painel não recalcula: chama o contrato do relatório".

## Critérios de aceite
1. **Dado** os cards desta story, **quando** o painel monta, **então** cada um segue a tabela (fonte por contrato, regra na `<Dica>` "Como calculamos", drill-down em "Ver origem"):

| Card | Permissão | Número | Fonte | Drill-down |
|---|---|---|---|---|
| Margem | `projects.view_cost` | margem orçada e projetada dos projetos em andamento, ponderadas pela receita, e a diferença em pp; os 5 piores pelo desvio | `IProjectMargins.PortfolioAsync` (DSH-02) | Orçado × realizado |
| Projetos em andamento | `projects.view` | até 8 projetos (os em risco e atrasados primeiro): etapa, entrega, contratado; margem orçada → projetada com `projects.view_cost`; recebido % com `finance.view` | `IProjects`, `IProjectMargins`, `IReceivables` | Projeto 360 |
| Carteira | `projects.view` (+ `finance.view` para "a faturar") | a produzir e a faturar (CA 3) | `IProjects`, `IReceivables` | Lista de projetos filtrada |
| Produção | `production.view` | projetos em andamento, OS atrasadas, OS em risco e a ocupação da fábrica na semana atual e nas 4 seguintes | `IProductionOrders`, `IFactoryLoad` (PLN-04) | Kanban filtrado; Ocupação |
| Entregas | `projects.view` | entregas previstas no mês (data prevista no mês e não entregue), entregues no mês e em risco | `IProjects` (previsão e risco, PLN-01b) | Lista de projetos filtrada |

2. **Dado** o card Margem com o dataset fixo do DSH-02, **quando** abro o painel, **então** os números são iguais aos do relatório "Orçado × realizado" com o filtro "em andamento" (teste que compara); sem `projects.view_cost`, o card não aparece, a tabela de projetos não tem a coluna de margem e nenhum DTO do painel leva custo, orçado ou margem (teste de arquitetura).
3. **Dado** a carteira, **quando** calculada, **então** **a produzir** = Σ receita contratada (contrato + aditivos) dos projetos `Planned` e `InProgress`; **a faturar** = Σ receita contratada dos contratos `Active` − Σ valor dos títulos a receber do contrato já emitidos (`Open`, `PartiallyPaid` e `Paid`; não `Planned` nem `Cancelled`) (proposta, Pontos em aberto 1); com o dataset fixo, os dois números batem com a soma da lista de projetos de origem (teste).
4. **Dado** a faixa "Atenção" (DSH-01a CA 8), **quando** esta story entra, **então** ela passa a considerar também o projeto mais atrasado em relação ao prometido ("{projeto} vai atrasar {n} dias — Previsão {data}, prometido ao cliente {data}.", crítico, com link para o cronograma do projeto) e continua mostrando no máximo 3 alertas, nesta ordem: caixa negativo, projeto em risco, OP aguardando, maior vencido.
5. **Dado** o cache e a invalidação da DSH-01a, **quando** chegam `ProjectForecastChanged`, `ContractAmended`, `ContractSigned`, `ReceivableSettled` e os eventos de custo do projeto (`StockIssued`, `StockReturned`, `LaborEntryClosed`, `LaborEntryCorrected`), **então** os cards afetados são invalidados; sem evento, valem os 60 s.
6. **Dado** 5 mil projetos, 200 mil custos de projeto e 300 OS, **quando** abro o painel completo (DSH-01a + DSH-01b) sem cache, **então** tudo chega em menos de 2 s (CA do backlog), cada card em menos de 1,5 s.
7. **Dado** o módulo de um card não liberado (`projects`, `production`), **quando** o painel monta, **então** o card não aparece; sem a permissão do card, também não; chamado direto, 403 com o código da camada (`module_not_released` ou `forbidden`).

## Regras de negócio e técnicas
- **Projetos em andamento** = `Planned`, `InProgress`, `Fabricated` e `Installing` (como o filtro padrão do DSH-02).
- **OS atrasadas e em risco** pela função única da PLN-01b; **ocupação** pelo `IFactoryLoad` (≤ 100% pelo plano finito, com o "ocupado até {data}" da PLN-04).
- Dinheiro `numeric(18,2)`; percentuais com 1 casa; diferença de margem em pontos percentuais.
- Endpoints no mesmo lugar dos da DSH-01a (Pontos em aberto 1 de lá).

## Permissões, eventos e tabelas
- **Permissões:** `dashboard.view`; `projects.view`, `projects.view_cost`, `finance.view`, `production.view` por card.
- **Eventos:** consome os do CA 5 só para invalidar o cache.
- **Contratos:** `IProjectMargins` (DSH-02), `IProjects` (+ resumo de carteira e entregas, proposta), `IReceivables` (+ emitido por contrato em lote, proposta), `IProductionOrders` (+ contagens, proposta), `IFactoryLoad` (PLN-04).
- **Tabelas:** nenhuma nova.

## Telas e estados
- **Painel do proprietário** (`/app/painel`): KPI "Margem projetada média" na linha de KPIs; tabela "Projetos em andamento" abaixo do caixa; grade com "Produção" (com as barras de ocupação do `ColumnChart` da PLN-04), "Carteira" e "Entregas".
- **Estados:** carregando, vazio e erro por card, como na DSH-01a ("Nenhum projeto em andamento." + "Ver orçamentos enviados").
- **Propositivo:** projeto em risco leva ao cronograma; OS atrasadas abrem o Kanban filtrado; a margem leva ao projeto que mais desviou.
- **400 px:** a tabela de projetos mostra projeto, etapa e entrega (contratado, margem e recebido na linha expansível); os demais cards empilhados depois dos da DSH-01a.
- **Ajuda:** atualizar `Ajuda/painel.md` com os cards novos e as regras; `<Dica>` em "A produzir", "A faturar", "Margem projetada média" e "Em risco".

## Plano de testes
- **Unidade:** carteira e ordem da tabela de projetos (CA 1, 3); ordem da faixa Atenção (CA 4).
- **Integração (Testcontainers, obrigatória):** cada card igual à origem com o dataset fixo (CA 2, 3); visibilidade por permissão com os usuários do seed, inclusive sem `projects.view_cost` (CA 2, 7); invalidação por evento (CA 5); desempenho do painel completo (CA 6).
- **Arquitetura:** DTOs do painel sem custo para quem não tem `projects.view_cost`.
- **E2E (Playwright):** painel completo do Proprietário, drill-down da margem e da entrega em risco; usuário do Financeiro sem a margem (se o PO tirar a ação) e com o recebido; tela a 400 px.

## Fora do escopo
- Meta de margem e comparação com o ano anterior.
- Faturamento pela NF-e (Fase 5).

## Pontos em aberto
1. **Carteira** (PO): proposta do CA 3 (a produzir pelos projetos ainda não fabricados; a faturar pelo contratado menos os títulos emitidos). Alternativa para "a faturar": contratado menos o recebido.
2. **"Margem realizada" do PRD** (PO): o PRD fala em "média prevista × realizada"; o protótipo mostra "margem real média" com os custos reais e o restante orçado (a projetada do `04-fluxos` §3.3). Proposta: o card mostra a **projetada** dos projetos em andamento e a **realizada** (receita − realizado) só para os entregues e encerrados, no drill-down.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dataset fixo comparado com as origens), arquitetura, desempenho e E2E passando.
- [ ] Nenhuma migration.
- [ ] Permissões só do `ModuleCatalog`; cada card com a permissão do dono.
- [ ] Cards com estados e 400 px.
- [ ] Ajuda do painel atualizada; teste de cobertura passando.
- [ ] Alertas de projeto acionáveis.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Contratos de resumo (carteira, entregas, contagens de OS) em lote.
- [ ] Endpoints dos cards, invalidação e faixa Atenção ampliada; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, design (painel completo), cybersecurity (margem por permissão).
- Gate: CA 2, 3 e 6 obrigatórios.

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

- **Correções feitas na validação:** margem só com `projects.view_cost` (D7) e teste de arquitetura nos DTOs; a carteira e a margem "realizada" do PRD × "projetada" do protótipo viraram pontos em aberto com proposta.
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; quebra da DSH-01 (01a/01b) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
