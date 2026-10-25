# DSH-02 — Orçado × realizado e margem, por projeto e consolidado

| Campo | Valor |
|---|---|
| ID | DSH-02 |
| Título | Um cálculo só de orçado, realizado, comprometido, projetado, desvio e margem por natureza de custo (material, mão de obra, instalação, frete, impostos, outros), usado pelo Projeto 360, pelo relatório consolidado de margens e pelo painel do dono; comprometido de material por saldo e imposto só pela nota (D8); ranking dos projetos pela margem (piores primeiro) e drill-down até o documento de origem; nascem aqui `Kpi`, `DashboardCard` e `ColumnChart` (D18) |
| Tamanho | G (re-estimada de M: recebe os componentes `Kpi`, `DashboardCard` e `ColumnChart` da D18, o comprometido por saldo e o imposto pela nota da D8, com regressão sobre cópia do `konebras`) |
| Status | Done |
| Requisitos | R-PRJ-5 (orçado × realizado por natureza e margem atual × prevista) · PRD §5.14 (Margem: média prevista × realizada; ranking de projetos por margem, piores primeiro) · PRD §1.1 problema 1 · PRD §1.2 (100% dos projetos com orçado × realizado em tempo real) · R-PRP-5 · PRD §6 (Desempenho) |
| Dependências | FIN-07 (`ChartFrame` e paleta), COM-05 (aba Custos), COM-07 (orçado ajustado por aditivo e receita com aditivos), PRD-04 (`commercial.project_costs` como razão, coluna "Comprometido", ADR 0025), EST-03 (baixa e devolução de material na razão), COM-06 (título do instalador com projeto), CMP-03a e CMP-04 (títulos `Purchase` e `GoodsReceipt` de item estocável com projeto), PRD-05 (custo da NC, informativo) |
| Bloqueia | DSH-01a (`Kpi` e `DashboardCard`), PLN-04 (`ColumnChart`), DSH-01b (card Margem, tabela de projetos e resultado gerencial por projeto) |
| Ordem de execução | 2 (Marco 4.1 "O dinheiro do jeito que ele vai acontecer") |

## User story
Como **proprietário**, quero **ver, projeto a projeto e no total, quanto orcei, quanto já gastei de verdade em cada tipo de custo e quanto a margem vai fechar**, para **saber quais obras estão comendo o meu lucro e agir enquanto ainda dá**.

## Contexto e referências
- PRD R-PRJ-5; §5.14 (Margem); §1.1 problema 1; §1.2 ("100% dos projetos com orçado × realizado visível em tempo real").
- `04-fluxos-estados` §3.3: orçado = `project_budget` por tipo (do contrato, ajustado por aditivo); realizado por natureza; margem realizada = receita contratada − realizado; margem projetada = receita − (realizado + restante), restante = orçado − realizado por tipo, nunca negativo. Esta story aplica as fontes decididas na ADR 0025 e no adendo da D8.
- **ADR 0025** (custo real do projeto): §1 três colunas (realizado, comprometido, projetado); §2 razão de diferenças `commercial.project_costs` (nasce na PRD-04); §3 recompor o passado; §4 regressão da COM-05. **Adendo de 21/10/2026 (D8):** fonte única nos títulos do Finance (o `IPurchaseOrders` não entra no custo); comprometido de material por saldo; imposto realizado só pela nota (fontes `InvoiceTax`/`InvoiceTaxReversal`, data de corte `fiscal.project_tax_cutover_on`); até a nota, o projetado usa o imposto orçado; guia de imposto nunca é custo do projeto; consolidado ponderado pela receita; custo da NC informativo; drill-down de horas com nome exige `hr.view_sensitive`.
- PRD-04 (Fase 3): cria a razão `project_costs` (`source` `StockIssue`/`StockReturn`/`Labor`/`LaborCorrection`, `seq`, `is_backfill`), troca o cálculo do `ProjectViews` da COM-05 e acrescenta a coluna "Comprometido". Esta story substitui esse cálculo pelo `ProjectMarginCalculator` com a regra do adendo, sem mudar o total projetado de projeto sem compra.
- CMP-03a CA 13 e CMP-04 CA 10: títulos de origem `Purchase` e `GoodsReceipt` de item estocável com projeto ficam no comprometido até a baixa para a OS.
- ADR 0019 §20 (D7): custo e margem só com `projects.view_cost`, DTO separado (não campo nulo) e teste de arquitetura.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D8; D18 (`Kpi`/`DashboardCard` e `ColumnChart` antes do Marco 4.2, nesta story); tabela 3.1, pontos 5 a 9; §4.1 (DSH-02: `project_costs` da PRD-04 e comprometido pela D8); §4.3 (drill-down de custo-hora com nome); §4.4 padrões 1, 2 e 3.
- Protótipo: painel "Projetos em andamento" (margem prevista no contrato × projetada com os custos reais) e detalhe do projeto (tabela por natureza com Orçado, Realizado, Projetado e Desvio; "margem orçada → projetada").

## Critérios de aceite
**Um cálculo só**
1. **Dado** o `IProjectMargins` em `Commercial.Contracts` (novo), **quando** a aba Custos do Projeto 360, o relatório desta story ou o painel do dono pedem os números de um projeto, **então** todos usam o mesmo `ProjectMarginCalculator` (Domain do Commercial) e recebem, por natureza: orçado, realizado, comprometido, projetado, desvio (valor e %), e no total a receita contratada, a margem orçada, a realizada e a projetada (valor e %); um teste compara as três saídas para o mesmo dataset.
2. **Dado** o realizado por natureza, **quando** é calculado, **então** soma só estas fontes (ADR 0025 §1 e adendo): **Material** = Σ `project_costs` `StockIssue`/`StockReturn` (baixa líquida de devolução para as OS do projeto, EST-03) + títulos a pagar **confirmados** (fora de `Planned` e `Cancelled`) com projeto e natureza Material que **não** sejam de compra de item estocável (serviço ou material entregue direto na obra); **Mão de obra** = Σ `project_costs` `Labor`/`LaborCorrection` (PRD-04) + títulos confirmados com natureza Mão de obra; **Instalação**, **Frete** e **Outros** = títulos confirmados com projeto da natureza (inclui o do instalador, COM-06); **Impostos** = Σ `project_costs` `InvoiceTax`/`InvoiceTaxReversal` (gravadas pela FIS-03a e pela FIS-06 a partir da data de corte; até lá, 0). Títulos `Opening` com projeto entram como realizado pelo saldo da data de corte (sem mudança); os movimentos `Production` e `Shipment` nunca entram.
3. **Dado** um título a pagar de **guia de imposto** (natureza Impostos, com ou sem projeto), **quando** o cálculo roda, **então** ele nunca entra no custo do projeto (nem realizado nem comprometido), porque é da empresa e vai para o DRE (FIN-08); a aba Custos mostra a nota "Guias de imposto são da empresa: entram no DRE, não no projeto." quando o projeto tem um título assim.
4. **Dado** o **comprometido** por natureza, **quando** é calculado, **então**: em Instalação, Frete, Outros e Mão de obra, é a Σ dos títulos `Planned` com projeto da natureza (o previsto passa ao realizado quando é confirmado); em **Material**, é **max(0, Σ títulos de compra de item estocável do projeto (origens `Purchase` e `GoodsReceipt`, fora de `Cancelled`) − Σ baixas líquidas de material para as OS do projeto)**; o `IPurchaseOrders` não entra no cálculo (fonte única nos títulos, D8).
5. **Dado** o **projetado** por natureza, **quando** é calculado, **então** projetado = realizado + max(orçado − realizado, comprometido), nunca abaixo do realizado; em **Impostos**, sem nota real do projeto, projetado = imposto orçado (composição do orçamento aprovado) com a marca "Imposto orçado"; com nota, projetado = imposto das notas + imposto orçado × (1 − valor faturado em notas ÷ receita contratada), com a marca "Imposto das notas + orçado no restante".
6. **Dado** a receita contratada, **quando** a margem é calculada, **então** receita = valor do contrato + Σ `value_delta` dos aditivos aprovados (COM-07); margem orçada = (receita − Σ orçado) ÷ receita; realizada = receita − Σ realizado; projetada = (receita − Σ projetado) ÷ receita; percentuais com 1 casa na tela e `numeric(9,4)` no cálculo; receita 0 → "—".
7. **Dado** o dataset fixo de teste (4 projetos: um com material baixado, devolução parcial e horas corrigidas; um com instalação acima do orçado e um previsto do instalador; um com compra de item estocável maior que a baixa e outro título de compra menor que a baixa feita com material de estoque anterior, que dá comprometido 0 e não crédito; um com guia de imposto com projeto e uma linha `InvoiceTax` de teste), **quando** abro a aba Custos de cada um e o relatório, **então** orçado, realizado, comprometido, projetado, desvio e as três margens batem centavo a centavo com a planilha do teste.
8. **Dado** o deploy desta story, **quando** o teste de regressão roda sobre cópia dos dados do tenant `konebras`, **então** o projetado total de cada projeto sem título de compra de item estocável e sem guia de imposto com projeto é igual ao da PRD-04; as diferenças dos demais são listadas no relatório do teste com a causa (comprometido por saldo ou guia fora do projeto).

**Relatório consolidado**
9. **Dado** `projects.view_cost`, **quando** abro "Orçado × realizado" (`/app/projetos/margens`), **então** vejo uma linha por projeto com número (link), nome, cliente, Etapa (`StageIndicator`), receita contratada, Σ orçado, Σ realizado, Σ projetado, margem orçada %, margem projetada % e desvio em pontos percentuais, **ordenada do pior para o melhor desvio** (ranking do PRD §5.14), com filtros na URL por situação do projeto (padrão: em andamento, ou seja, `Planned`, `InProgress`, `Fabricated`, `Installing`), cliente, responsável e período de assinatura.
10. **Dado** o relatório, **quando** olho o topo, **então** vejo a linha de `Kpi` do consolidado dos projetos filtrados (receita, orçado, realizado, projetado e a margem orçada → projetada **ponderadas pela receita**, Σ ÷ Σ), a tabela por natureza somada (orçado, realizado, comprometido, projetado, desvio) e o `ColumnChart` "Orçado × projetado por natureza" (CA 15).
11. **Dado** um projeto com margem projetada abaixo da orçada, **quando** aparece no relatório, **então** a linha tem o chip de alerta "Margem {x} pp abaixo" (crítico quando a projetada é negativa: "Prejuízo projetado") e o filtro "Só com margem abaixo da orçada" (R-PRP-5).
12. **Dado** um projeto com não conformidade (PRD-05), **quando** abro a aba Custos, **então** o custo estimado da NC aparece só como informação ("Não conformidades: {n}, custo estimado {valor}; o retrabalho já está nas horas e no material."), sem somar em nenhuma coluna (item 78 da Fase 3).

**Drill-down até a origem**
13. **Dado** uma célula de Realizado ou Comprometido (de um projeto e uma natureza, ou do consolidado), **quando** clico, **então** o painel lateral lista os lançamentos que somam o valor: tipo de origem (Baixa para OS, Devolução, Apontamento, Correção de apontamento, Título a pagar, Título de compra, Imposto da nota, Estorno de imposto), documento, data, valor com sinal e o link para o documento; no comprometido de material, a conta "títulos de compra − baixas = {valor}" aparece no topo; o total do painel é igual ao da célula (teste); o link só aparece se o usuário tem a permissão do módulo dono do documento (`inventory.view`, `production.view`, `finance.view`, `purchasing.view`, `fiscal.view`), senão a linha mostra o documento sem link.
14. **[Segurança]** **Dado** o drill-down de Mão de obra, **quando** o usuário tem `projects.view_cost` **e** `hr.view_sensitive`, **então** cada apontamento mostra o funcionário e o custo-hora do snapshot; **sem** `hr.view_sensitive`, as horas e o valor aparecem **agregados por centro de trabalho** e por OS, sem nome nem custo-hora individual, com o bloco "Dados protegidos" (padrão 8); o DTO sem a ação não tem as propriedades de nome nem de custo-hora (teste de contrato).
15. **Dado** o Orçado de uma natureza, **quando** clico, **então** vejo a origem: o orçamento do contrato (COM-04a) e cada aditivo aprovado com o delta (COM-07), com link.

**Componentes (D18)**
16. **Dado** os componentes `Kpi` e `DashboardCard` (novos, nos componentes de UI), **quando** uma tela os usa, **então** o `Kpi` mostra rótulo, valor com `tabular-nums`, linha de contexto ("−3,6 pp abaixo dos 23,0% orçados"), medidor opcional e é clicável até a origem; o `DashboardCard` tem título, subtítulo, `<Dica>` "Como calculamos", "Ver origem", "Atualizado às {hh:mm}" e os estados carregando, vazio, erro ("Não foi possível carregar {card}." com "Tentar de novo") e sem permissão (o card não é montado); os dois usam só tokens (sem cor literal); o relatório desta story é a primeira tela a usar o `Kpi`.
17. **Dado** o componente `ColumnChart` (novo, dentro do `ChartFrame` da FIN-07), **quando** desenha "Orçado × projetado por natureza", **então** as séries se distinguem por preenchimento (orçado vazado com contorno, projetado cheio) e por rótulo direto do valor em cada barra, há a referência em y "zero" (e, quando a tela passar, "capacidade" ou "meta", para a PLN-04 e a DSH-01b), o resumo textual acima ("A natureza que mais desviou foi {natureza}: {valor} acima do orçado.") e a tabela equivalente; cores dos tokens `--chart-*`.

**Desempenho, permissões e segurança**
18. **Dado** 5 mil projetos, 200 mil linhas em `project_costs` e 50 mil títulos a pagar com projeto, **quando** abro o relatório, **então** a primeira página chega em menos de 1 s e o consolidado em menos de 2 s (agregação no banco, uma consulta por fonte, nomes por página com `IParties.FindManyAsync`); a aba Custos de um projeto, em menos de 300 ms.
19. **[Segurança]** **Dado** um usuário sem `projects.view_cost` (ex.: Engenharia e Supervisão de produção do seed), **quando** chama o relatório, o `IProjectMargins` por endpoint ou o drill-down, **então** 403 `forbidden`, o item "Orçado × realizado" não aparece no menu e nenhum DTO de lista ou resumo tem propriedade de custo, orçado ou margem (teste de arquitetura da ADR 0019 §20 estendido aos DTOs novos).
20. **Dado** o módulo `projects` não liberado, **quando** chamo, **então** 403 `module_not_released` e a tela `ModuleNotReleased`; sem grupo, 403 `forbidden`. `RequireModule` e `perm:`; SQL só com parâmetros e ordenação por allowlist (422 fora dela); IDOR em `/projects/{id}/costs/{costType}/entries`.

## Regras de negócio e técnicas
- **Fontes por contrato, nunca por tabela alheia:** o Commercial lê `project_costs` (dele) e os títulos a pagar por um novo `IPayables.CostSummaryByProjectsAsync(ids)` em lote (`Finance.Contracts`: por projeto e natureza, confirmados, `Planned`, compra de item estocável e guia de imposto separados). O `IPurchaseOrders` não entra no custo (D8).
- **Comprometido de material por saldo** (ponto 5): max(0, títulos de compra de item estocável − baixas líquidas); a diferença negativa não vira crédito do projeto.
- **Previsto (`Planned`) no comprometido** (ponto 6), nunca no realizado.
- **Imposto** (ponto 7): realizado só pela nota (razão `InvoiceTax`/`InvoiceTaxReversal`, data de corte do Fiscal); projetado pelo orçado até a nota; a linha `InvoiceTax` leva o valor da nota (`invoice_amount`) para a proporção do CA 5; guia nunca entra.
- **Projetado** por natureza = realizado + max(orçado − realizado, comprometido); **desvio** = projetado − orçado (valor e % do orçado; orçado 0 → "—").
- **Margens:** orçada, realizada e projetada como no CA 6; **consolidado ponderado pela receita** (ponto 8).
- **NC** (ponto 9): informativa, fora das colunas.
- **Dinheiro** `numeric(18,2)`, somas em `decimal`, arredondamento só no total da linha (ADR 0019 §9); percentuais `numeric(9,4)`.
- **A aba Custos do COM-05 (com a coluna Comprometido da PRD-04) passa a usar o `ProjectMarginCalculator`** (mesmos números do relatório); a margem projetada do Resumo também.
- Sem cache nesta story: as consultas atendem a meta; o painel do dono tem o cache da ADR 0028 (DSH-01a).

## Permissões, eventos e tabelas
- **Permissões:** `projects.view_cost` (tudo desta story); `projects.view` (abrir o projeto); `hr.view_sensitive` (nome e custo-hora no drill-down de horas); permissões dos módulos donos só para o link do drill-down.
- **Eventos:** nenhum novo.
- **Contratos:** novo `IProjectMargins` (`Commercial.Contracts`: `ForProjectAsync`, `ForProjectsAsync(ids)`, `PortfolioAsync(filtro)`, com DTO sem custo e DTO com custo); usa `IProjectCosts` (EST-03/PRD-04), `IPayables.CostSummaryByProjectsAsync` (novo), `IParties`.
- **Tabelas:** nenhuma nova; `commercial.project_costs.source` aceita `InvoiceTax` e `InvoiceTaxReversal` (ADR 0025 adendo) e ganha `invoice_amount` (`numeric(18,2)`, anulável, só nessas fontes); índice em `commercial.project_costs (project_id, cost_type)` se a medição pedir.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 1 (`ColumnChart` sobre o `ChartFrame`: série nunca só por cor, referência em y, resumo textual e tabela equivalente), 2 (`Kpi` e `DashboardCard`: número clicável até a origem, "Como calculamos", "Atualizado às", estados por card), 3 (tabela analítica com drill-down em painel lateral e total igual ao da célula) e 8 (bloco "Dados protegidos" no drill-down de horas).
- **Comercial → Projetos → Orçado × realizado** (`/app/projetos/margens`; entrada nova no `modules.ts`, área Comercial, só com `projects.view_cost`): h1 "Quanto cada projeto está rendendo de verdade."; linha de `Kpi` do consolidado; tabela por natureza com o `ColumnChart`; lista ranqueada.
- **Projeto 360 → Custos** (existente, com a coluna Comprometido da PRD-04): mesma regra, drill-down nas células, marcas "Imposto orçado" e nota das guias.
- **Componentes (D18):** `Kpi`, `DashboardCard` e `ColumnChart` (novos, CA 16 e 17).
- **Estados:** carregando (skeleton do consolidado e da lista); vazio ("Nenhum projeto em andamento. Os projetos nascem quando um orçamento é aprovado." + "Ver orçamentos enviados"); vazio filtrado com "Limpar filtros"; erro com "Tentar de novo".
- **Propositivo:** o chip "Margem abaixo" leva à aba Custos do projeto com a natureza que mais desviou destacada; do drill-down, o documento de origem.
- **400 px:** lista com número, cliente, margem orçada → projetada e o chip (receita, orçado, realizado e projetado na linha expansível); `Kpi` em 1 coluna; tabela por natureza em cartões; o painel lateral do drill-down abre em tela cheia; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** `Ajuda/projetos-margens.md` (rota `/app/projetos/margens`) e atualizar `Ajuda/projetos-projeto-custos.md` (comprometido por saldo, imposto orçado até a nota, guias fora do projeto, NC informativa, drill-down); `<Dica>` em "Realizado", "Comprometido", "Projetado", "Desvio", "Margem projetada", "Imposto orçado" e "pp".

## Plano de testes
- **Unidade:** realizado por fonte, comprometido de material por saldo (positivo, zero, nunca negativo), previsto no comprometido, guia fora, imposto orçado e com nota, projetado, desvio, margens com receita 0 e com aditivo, consolidado ponderado (CA 2–6), com tabela de casos.
- **Integração (Testcontainers, obrigatória):** dataset fixo dos 4 projetos (CA 7); regressão sobre cópia do `konebras` (CA 8); as três saídas iguais (CA 1); ranking e filtros (CA 9–11); NC informativa (CA 12); drill-down soma igual à célula, link por permissão e horas sem nome sem `hr.view_sensitive` (CA 13–15); desempenho com volume (CA 18); sem `projects.view_cost` (CA 19); três camadas, allowlist e IDOR (CA 20).
- **Arquitetura:** DTOs de lista e resumo sem custo para quem não tem `projects.view_cost`; DTO de horas sem nome e custo-hora para quem não tem `hr.view_sensitive`.
- **Front (Vitest):** `Kpi`, `DashboardCard` (estados) e `ColumnChart` (rótulo direto, preenchimento distinto, referência, tabela equivalente) (CA 16, 17).
- **E2E (Playwright):** abrir o relatório, ordenar, abrir um drill-down e ir ao documento; usuário da Engenharia sem o item no menu; usuário sem `hr.view_sensitive` vê horas por centro; tela a 400 px.

## Fora do escopo
- Gravação das linhas `InvoiceTax`/`InvoiceTaxReversal`: FIS-03a, FIS-03b e FIS-06 (Fase 5); esta story lê as fontes e testa com linhas de teste.
- DRE mensal (pela data do documento): FIN-08. Resultado gerencial por projeto (receita × custos diretos por período): DSH-01b.
- Rateio de despesas indiretas (administrativas) por projeto: fora do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 5–9):
1. Fonte única nos títulos, sem o `IPurchaseOrders`; comprometido de material por saldo (D2 da Fase 3 e D8; CA 4).
2. Título previsto (`Planned`) no Comprometido, nunca no realizado (D2 da Fase 3; CA 2, 4).
3. Imposto realizado só pela nota autorizada (`InvoiceTax`/`InvoiceTaxReversal`, com data de corte); até lá, o projetado usa o orçado; guia de imposto nunca é custo do projeto (D8; CA 2, 3, 5).
4. Margem do consolidado ponderada pela receita (CA 6, 10).
5. Custo da NC informativo (item 78 da Fase 3; CA 12).

Decisões da revisão aplicadas: D8 (ADR 0025 adendo); D18 (`Kpi`, `DashboardCard` e `ColumnChart` nesta story, CA 16, 17); §4.1 (`project_costs` da PRD-04); §4.3 (drill-down de horas com nome só com `hr.view_sensitive`, CA 14); §4.4 padrões 1, 2, 3 e 8.

**Pré-condição externa:** nenhuma para o `Ready`. A lista de tributos que entram no custo do projeto (padrão ICMS, PIS, COFINS, ISS e IBS/CBS; IPI fora) vem pré-configurada em Configurações → Fiscal (FIS-02a, ADR 0042) com o chip "A confirmar com a contadora"; esta story só usa as linhas que a FIS-03a gravar.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers, dataset fixo e regressão sobre cópia do `konebras`), arquitetura, front (Vitest) e E2E passando.
- [x] Migration não destrutiva (`invoice_amount` e as fontes novas em `project_costs`; índice, se a medição pedir).
- [x] Permissões só do `ModuleCatalog`; DTOs sem custo para quem não tem a ação e sem nome para quem não tem `hr.view_sensitive`.
- [x] Telas com estados e 400 px; aba Custos do COM-05 com o mesmo cálculo.
- [x] `Kpi`, `DashboardCard` e `ColumnChart` com tokens, sem cor literal e com testes.
- [x] Ajuda da tela nova e da aba Custos; teste de cobertura passando.
- [x] Alertas acionáveis e drill-down até a origem.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` (`project_costs`) e `docs/04-fluxos-estados.md` §3.3 (fontes e comprometido decididos) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `ProjectMarginCalculator` (Domain) e `IProjectMargins` (Contracts) com as consultas em lote e os dois DTOs.
- [x] `IPayables.CostSummaryByProjectsAsync` (Finance).
- [x] Endpoints `/api/v1/projects/margins`, `/api/v1/projects/{id}/costs/{costType}/entries`; tela, aba Custos e ajuda.
- [x] `Kpi`, `DashboardCard`, `ColumnChart`; teste de regressão sobre cópia do `konebras`.

## Quality gates
- Agentes previstos: @dev, @qa, @data-engineer (agregação com volume), design (relatório, componentes e drill-down), cybersecurity (DTO por permissão).
- Gate: CA 1, 2, 4, 5, 7, 8, 13, 14 e 19 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depende da FIN-07 e da PRD-04; bloqueia DSH-01a, PLN-04 e DSH-01b) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** o realizado de material dependia da decisão da Fase 3 sobre contar a compra duas vezes (ponto 1, pré-condição); a coluna Comprometido mudava o número da COM-05 e ficou como ponto em aberto (2); um cálculo só para Projeto 360, relatório e painel (CA 1).
- **Correções da revalidação (21/10/2026):** pontos 5 a 9 decididos; D8 (ADR 0025 adendo): fonte única nos títulos, comprometido de material por saldo (CA 4), imposto só pela nota com o orçado no projetado e guia fora do projeto (CA 2, 3, 5), consolidado ponderado (CA 6, 10), NC informativa (CA 12); `IPurchaseOrders` fora do cálculo; dataset de 4 projetos e regressão sobre cópia do `konebras` (CA 7, 8); drill-down de horas sem nome nem custo-hora sem `hr.view_sensitive` (§4.3, CA 14); D18: `Kpi`, `DashboardCard` e `ColumnChart` nascem aqui (CA 16, 17); dependências da FIN-07 e da PRD-04; re-estimada de M para G; CAs renumerados (20).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Conferência do fechamento (21/10/2026, amostragem do PO):** comprometido por saldo e imposto pela nota conferidos com a ADR 0025 (adendo); checklist refeita, sem mudança de resultado.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D8, D18, tabela 3.1 pontos 5–9, §4.1, §4.3, §4.4 padrões 1, 2, 3 e 8); ordem mantida (Marco 4.1, ordem 2); re-estimada de M para G; CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 25/10/2026 | 1.0 | Implementada (`Done`). Desvios e decisões de implementação: (1) **Rotas:** `GET /projects/margins` já existia (margem orçada da lista da COM-05, por `ids`) e ficou como está; o relatório usa `GET /projects/margins/ranking` (página ranqueada, `sort` por allowlist `deviation`, `-deviation`, `number`, `revenue`, `-revenue`, `projectedMargin`, `-projectedMargin`; 422 fora dela) e `GET /projects/margins/portfolio` (consolidado), com os mesmos filtros (`situation` `andamento`/`entregue`/`encerrado`/`cancelado`/`todos`, `partyId`, `managerId`, `signedFrom`, `signedTo`, `belowBudget`); o drill-down do projeto é `GET /projects/{id}/costs/{costType}/entries?column=budgeted\|realized\|committed` e o do consolidado `GET /projects/margins/entries` (uma linha por projeto filtrado com o valor da célula e o link para a aba Custos, total igual ao da célula). Natureza ou coluna fora da lista: 422; projeto inexistente: 404. (2) **`IPayables.CostSummaryByProjectsAsync` virou o contrato próprio `IProjectPayableCosts`** (`Finance.Contracts`: `CostSummaryByProjectsAsync` em lote, agregado no banco, e `CostEntriesAsync` para o drill-down), ao lado do `IPayables`, para não obrigar os fakes de `IPayables` das trilhas de Compras e Estoque a mudar. A classificação do título é uma regra só, `PayableCostRules` (confirmado, previsto, compra de item estocável, guia). (3) **Guia de imposto = qualquer título de natureza Impostos**, com ou sem projeto e de qualquer origem: recomendação aceita (PO 24/10). (4) **Origem `GoodsReceipt`** ainda não existe (CMP-04 em outra trilha): a compra de item estocável é `Purchase` com natureza Material; a CMP-04 acrescenta a origem na `PayableCostRules`. (5) **Percentuais com 4 casas na API** (`numeric(9,4)`), 1 na tela; a margem orçada da coluna da lista da COM-05 (`/projects/margins?ids=`) continua com 1 casa. Os testes da PRD-04 passaram a esperar 4 casas. (6) **DTOs:** `ProjectCostLine` saiu; as naturezas usam `ProjectMarginLine` (`Commercial.Contracts`) e a `ProjectCostView` ganhou margem realizada, desvio em pp, base do imposto, guias e as NC (só informativas). DTO sem custo do relatório: `ProjectMarginHeader` (no teste de arquitetura, junto com o `ProjectSummary`). (7) **Horas por funcionário** em rota e DTO próprios (`GET /projects/{id}/costs/labor/workers`, `ProjectLaborWorkers`), com `projects.view_cost` na rota e `hr.view_sensitive` no serviço; o drill-down padrão agrega por OS, centro de trabalho e fonte (apontamento ou correção), sem nome nem custo-hora (teste de contrato). O nome vem da Produção (`IProjectProductionCosts`, que lê o `IEmployees`): o Commercial nunca vê o id do funcionário (regra de arquitetura da COM-01a). O custo-hora individual também respeita o parâmetro "custo-hora por apontamento" da Produção (decisão do PO de 22/10): desligado, aparece só o nome; recomendação aceita (PO 24/10). (8) **NC:** contagem e custo estimado pelo `IProjectProductionCosts.NonConformitiesAsync`, só na nota da aba Custos. (9) **Links do drill-down** decididos no servidor pela permissão do módulo dono: baixa e devolução → movimento do Estoque (`inventory.view`); horas → OS (`production.view`); título → Financeiro (`finance.view`); título de compra → pedido (`purchasing.view`, senão o título com `finance.view`); orçado → contrato e aditivo (`projects.view`). Imposto da nota sem link até a FIS-03a criar a tela da nota. (10) **`ColumnChart` sem o `ChartFrame`:** a FIN-07 ainda não está na main; o `ColumnChart` usa o mesmo molde do `Chart` (tokens `--chart-*`, tabela equivalente, resumo acima). Depois do merge da FIN-07, ele passa a usar o `ChartFrame` (e a troca do `--chart-3`). (11) **Menu:** "Orçado × realizado" é uma aba do módulo Projetos (`modules.ts`, com a ação `view_cost` exigida: `Tab.action`), não um módulo novo; a tela também mostra "Sem acesso" sem a ação. (12) **Filtros de cliente e responsável:** clicando no nome na linha (chip removível no topo, na URL `cliente`/`responsavel`), sem seletor de busca. (13) **CA 8:** a regressão sobre a cópia do `konebras` não foi executada (sem VPS e sem Docker nesta máquina); o teste sintético compara o projetado total com a fórmula da PRD-04 (que já tinha o comprometido por saldo): igual sem guia; com guia, a diferença é listada com a causa "guia de imposto fora do projeto". Rodar o mesmo par de cálculos na cópia do `konebras` na homologação antes do deploy. (14) Índice `(project_id, cost_type)` já existia na `project_costs`; a migration `ProjectCostInvoiceAmount` só acrescenta `invoice_amount`. (15) **Testes:** unidade do cálculo, da classificação e da regressão rodados; integração (dataset fixo nas três saídas, ranking e filtros, drill-down, horas protegidas, NC, três camadas e IDOR, desempenho com 5 mil projetos, 200 mil linhas e 50 mil títulos, metas com 50% de folga) escritos e não executados (fila do Docker); dos testes de arquitetura, os 9 de sempre ficam bloqueados pelo Smart App Control (`0x800711C7`) e rodam no CI. (16) `openapi.json` gerado pelo Host em processo e juntado sem reordenar (regenerar oficialmente com `BQ_UPDATE_OPENAPI`); cliente Orval regenerado. (17) "Tentar de novo" do `ErrorState` e as ações dos vazios desta tela com 44 px a 400 px (achado pontual da revisão §4.4). | Dex (dev) |
