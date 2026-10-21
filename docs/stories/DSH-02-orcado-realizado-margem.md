# DSH-02 — Orçado × realizado e margem, por projeto e consolidado

| Campo | Valor |
|---|---|
| ID | DSH-02 |
| Título | Um cálculo só de orçado, realizado, comprometido, projetado, desvio e margem por natureza de custo (material, mão de obra, instalação, frete, impostos, outros), usado pelo Projeto 360, pelo relatório consolidado de margens e pelo painel do dono; ranking dos projetos pela margem (piores primeiro) e drill-down até o documento de origem de cada custo |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-PRJ-5 (orçado × realizado por natureza e margem atual × prevista) · PRD §5.14 (Margem: média prevista × realizada; ranking de projetos por margem, piores primeiro) · PRD §1.1 problema 1 · PRD §1.2 (100% dos projetos com orçado × realizado em tempo real) · R-PRP-5 · PRD §6 (Desempenho) |
| Dependências | COM-05 (aba Custos, fórmula de projetado e margem), COM-07 (orçado ajustado por aditivo e receita com aditivos), EST-03 (`commercial.project_costs` de material, `IProjectCosts`), PRD-04 (mão de obra em `project_costs`), COM-06 (título do instalador com projeto), CMP-03a (compra para o projeto, comprometido; ponto 4 de lá), PRD-05 (custo da NC, ponto 78 de lá) |
| Bloqueia | DSH-01b (card Margem e tabela de projetos), FIN-08 (DRE por projeto reaproveita o realizado) |
| Ordem de execução | 2 (Marco 4.1 "O dinheiro do jeito que ele vai acontecer") |

## User story
Como **proprietário**, quero **ver, projeto a projeto e no total, quanto orcei, quanto já gastei de verdade em cada tipo de custo e quanto a margem vai fechar**, para **saber quais obras estão comendo o meu lucro e agir enquanto ainda dá**.

## Contexto e referências
- PRD R-PRJ-5; §5.14 (Margem); §1.1 problema 1; §1.2 ("100% dos projetos com orçado × realizado visível em tempo real").
- `04-fluxos-estados` §3.3: orçado = `project_budget` por tipo (do contrato, ajustado por aditivo); realizado = material (saídas para OS do projeto + compras diretas ao projeto), mão de obra (apontamentos), instalação (payables do instalador), frete e outros (payables com projeto), impostos (NF autorizadas); margem realizada = receita contratada − realizado; margem projetada = receita − (realizado + restante), restante = orçado − realizado por tipo, nunca negativo.
- COM-05 (Done): aba Custos com Orçado, Realizado (títulos a pagar do projeto não cancelados, pela natureza), Projetado e Desvio, e a margem projetada no Resumo. A legenda "Material e horas entram com a produção (Fase 3)" sai na PRD-04.
- Fase 3 (rascunho em revisão, `docs/fase3-stories`): EST-03 cria `commercial.project_costs` como **tabela** (`project_id`, `cost_type`, `source` StockIssue/StockReturn/Labor, `source_id`, `amount` com sinal, `date`, `production_order_id`), só o Commercial grava, e o `IProjectCosts` (leitura por projeto e por OS); PRD-04 grava a mão de obra pelo `LaborEntryClosed`/`LaborEntryCorrected` (estorno + novo, nada apagado); CMP-03a ponto 4 propõe que o título de compra de item estocável **não** conte como realizado (fica "Comprometido") e que o material realizado venha só da baixa; `IPurchaseOrders` expõe o comprometido por projeto.
- ADR 0019 §20 (D7): custo e margem só com `projects.view_cost`, DTO separado (não campo nulo) e teste de arquitetura.
- Protótipo: painel "Projetos em andamento" (margem prevista no contrato × projetada com os custos reais) e detalhe do projeto (tabela por natureza com Orçado, Realizado, Projetado e Desvio; "margem orçada → projetada").

## Critérios de aceite
**Um cálculo só**
1. **Dado** o `IProjectMargins` em `Commercial.Contracts` (novo), **quando** a aba Custos do Projeto 360, o relatório desta story ou o painel do dono pedem os números de um projeto, **então** todos usam o mesmo `ProjectMarginCalculator` (Domain do Commercial) e recebem, por natureza: orçado, realizado, comprometido, projetado, desvio (valor e %), e no total a receita contratada, a margem orçada, a realizada e a projetada (valor e %); um teste compara as três saídas para o mesmo dataset.
2. **Dado** o realizado por natureza, **quando** é calculado, **então** segue o `04-fluxos` §3.3 com as fontes deste projeto: **Material** = Σ `project_costs` StockIssue/StockReturn (baixa e devolução para a OS, EST-03) + títulos a pagar com projeto e natureza Material que não sejam de compra de item estocável (CMP-03a ponto 4, proposta); **Mão de obra** = Σ `project_costs` Labor (PRD-04) + títulos a pagar com natureza Mão de obra; **Instalação** = títulos a pagar com natureza Instalação (inclui o do instalador, COM-06); **Frete** e **Outros** = títulos a pagar com projeto da natureza; **Impostos** = títulos a pagar com natureza Impostos até a Fase 5 (as NF autorizadas entram com a FIS-03, Pontos em aberto 3). Títulos `Cancelled` não entram.
3. **Dado** um título a pagar `Planned` com projeto (CP previsto do instalador, previsão do pedido de compra) e os pedidos de compra aprovados e não recebidos para o projeto (`IPurchaseOrders`, CMP-03a), **quando** a tabela é montada, **então** eles aparecem na coluna **Comprometido** e não no Realizado (proposta, Pontos em aberto 2); o **projetado** = realizado + max(orçado − realizado, comprometido ainda não realizado), por natureza (o restante nunca é negativo).
4. **Dado** a receita contratada, **quando** a margem é calculada, **então** receita = valor do contrato + Σ `value_delta` dos aditivos aprovados (COM-07); margem orçada = (receita − Σ orçado) ÷ receita; realizada = receita − Σ realizado; projetada = (receita − Σ projetado) ÷ receita; percentuais com 1 casa na tela e `numeric(9,4)` no cálculo; receita 0 → "—".
5. **Dado** o dataset fixo de teste (3 projetos: um com material baixado, devolução parcial e horas corrigidas; um com instalação acima do orçado; um só com compra comprometida), **quando** abro a aba Custos de cada um e o relatório, **então** orçado, realizado, comprometido, projetado, desvio e as três margens batem centavo a centavo com a planilha do teste.

**Relatório consolidado**
6. **Dado** `projects.view_cost`, **quando** abro "Orçado × realizado" (`/app/projetos/margens`), **então** vejo uma linha por projeto com número (link), nome, cliente, Etapa (`StageIndicator`), receita contratada, Σ orçado, Σ realizado, Σ projetado, margem orçada %, margem projetada % e desvio em pontos percentuais, **ordenada do pior para o melhor desvio** (ranking do PRD §5.14), com filtros na URL por situação do projeto (padrão: em andamento, ou seja, `Planned`, `InProgress`, `Fabricated`, `Installing`), cliente, responsável e período de assinatura.
7. **Dado** o relatório, **quando** olho o topo, **então** vejo o consolidado dos projetos filtrados: receita, orçado, realizado, projetado e a margem orçada × projetada **ponderadas pela receita** (Σ ÷ Σ, proposta, Pontos em aberto 4), e a tabela por natureza somada (orçado, realizado, comprometido, projetado, desvio).
8. **Dado** um projeto com margem projetada abaixo da orçada, **quando** aparece no relatório, **então** a linha tem o chip de alerta "Margem {x} pp abaixo" (crítico quando a projetada é negativa: "Prejuízo projetado") e o filtro "Só com margem abaixo da orçada" (R-PRP-5).

**Drill-down até a origem**
9. **Dado** uma célula de Realizado ou Comprometido (de um projeto e uma natureza, ou do consolidado), **quando** clico, **então** o painel lateral lista os lançamentos que somam o valor: tipo de origem (Baixa para OS, Devolução, Apontamento, Correção de apontamento, Título a pagar, Pedido de compra), documento, data, valor com sinal e o link para o documento; o total do painel é igual ao da célula (teste); o link só aparece se o usuário tem a permissão do módulo dono do documento (`inventory.view`, `production.view`, `finance.view`, `purchasing.view`), senão a linha mostra o documento sem link.
10. **Dado** o Orçado de uma natureza, **quando** clico, **então** vejo a origem: o orçamento do contrato (COM-04a) e cada aditivo aprovado com o delta (COM-07), com link.

**Desempenho, permissões e segurança**
11. **Dado** 5 mil projetos, 200 mil linhas em `project_costs` e 50 mil títulos a pagar com projeto, **quando** abro o relatório, **então** a primeira página chega em menos de 1 s e o consolidado em menos de 2 s (agregação no banco, uma consulta por fonte, nomes por página com `IParties.FindManyAsync`); a aba Custos de um projeto, em menos de 300 ms.
12. **[Segurança]** **Dado** um usuário sem `projects.view_cost` (ex.: Engenharia e Supervisão de produção do seed), **quando** chama o relatório, o `IProjectMargins` por endpoint ou o drill-down, **então** 403 `forbidden`, o item "Orçado × realizado" não aparece no menu e nenhum DTO de lista ou resumo tem propriedade de custo, orçado ou margem (teste de arquitetura da ADR 0019 §20 estendido aos DTOs novos).
13. **Dado** o módulo `projects` não liberado, **quando** chamo, **então** 403 `module_not_released` e a tela `ModuleNotReleased`; sem grupo, 403 `forbidden`. `RequireModule` e `perm:`; SQL só com parâmetros e ordenação por allowlist (422 fora dela); IDOR em `/projects/{id}/costs/{costType}/entries`.

## Regras de negócio e técnicas
- **Fontes por contrato, nunca por tabela alheia:** o Commercial lê `project_costs` (dele), os títulos a pagar por `IPayables.ByProjectAsync`/um novo `IPayables.CostSummaryByProjectsAsync(ids)` em lote (Finance.Contracts), e o comprometido de compras por `IPurchaseOrders` (Purchasing.Contracts, CMP-03a).
- **Projetado** por natureza = realizado + max(orçado − realizado, comprometido não realizado, 0); **desvio** = projetado − orçado (valor e % do orçado; orçado 0 → "—").
- **Margens:** orçada, realizada e projetada como no CA 4; consolidado ponderado pela receita.
- **Dinheiro** `numeric(18,2)`, somas em `decimal`, arredondamento só no total da linha (ADR 0019 §9); percentuais `numeric(9,4)`.
- **A aba Custos do COM-05 passa a usar o `ProjectMarginCalculator`** (mesmos números do relatório), com a coluna Comprometido; a margem projetada do Resumo também.
- Sem cache nesta story: as consultas atendem a meta; o painel do dono decide o cache (DSH-01b).

## Permissões, eventos e tabelas
- **Permissões:** `projects.view_cost` (tudo desta story); `projects.view` (abrir o projeto); permissões dos módulos donos só para o link do drill-down.
- **Eventos:** nenhum novo.
- **Contratos:** novo `IProjectMargins` (`Commercial.Contracts`: `ForProjectAsync`, `ForProjectsAsync(ids)`, `PortfolioAsync(filtro)`); usa `IProjectCosts` (EST-03), `IPayables` (+ `CostSummaryByProjectsAsync`, proposta), `IPurchaseOrders` (CMP-03a), `IParties`.
- **Tabelas:** nenhuma nova; índice em `commercial.project_costs (project_id, cost_type)` se a medição pedir.

## Telas e estados
- **Comercial → Projetos → Orçado × realizado** (`/app/projetos/margens`; entrada nova no `modules.ts`, área Comercial, só com `projects.view_cost`): h1 "Quanto cada projeto está rendendo de verdade."; consolidado no topo ("facts" com receita, orçado, realizado, projetado, margem orçada → projetada); tabela por natureza; lista ranqueada.
- **Projeto 360 → Custos** (existente): coluna Comprometido, drill-down nas células, mesma regra.
- **Estados:** carregando (skeleton do consolidado e da lista); vazio ("Nenhum projeto em andamento. Os projetos nascem quando um orçamento é aprovado." + "Ver orçamentos enviados"); vazio filtrado com "Limpar filtros"; erro com "Tentar de novo".
- **Propositivo:** o chip "Margem abaixo" leva à aba Custos do projeto com a natureza que mais desviou destacada; do drill-down, o documento de origem.
- **400 px:** lista com número, cliente, margem orçada → projetada e o chip (receita, orçado, realizado e projetado na linha expansível); tabela por natureza em cartões; o painel lateral do drill-down abre em tela cheia.
- **Ajuda:** `Ajuda/projetos-margens.md` (rota `/app/projetos/margens`) e atualizar `Ajuda/projetos-projeto-custos.md` (Comprometido, drill-down, fontes do realizado); `<Dica>` em "Realizado", "Comprometido", "Projetado", "Desvio", "Margem projetada" e "pp".

## Plano de testes
- **Unidade:** realizado por fonte, comprometido, projetado com restante e comprometido, desvio, margens com receita 0 e com aditivo (CA 2–4), com tabela de casos.
- **Integração (Testcontainers, obrigatória):** dataset fixo dos 3 projetos (CA 5); as três saídas iguais (CA 1); ranking e filtros (CA 6–8); drill-down soma igual à célula e link por permissão (CA 9, 10); desempenho com volume (CA 11); sem `projects.view_cost` (CA 12); três camadas, allowlist e IDOR (CA 13).
- **Arquitetura:** DTOs de lista e resumo sem custo para quem não tem `projects.view_cost`.
- **E2E (Playwright):** abrir o relatório, ordenar, abrir um drill-down e ir ao documento; usuário da Engenharia sem o item no menu; tela a 400 px.

## Fora do escopo
- Impostos pelas NF autorizadas: FIS-03 (Fase 5).
- DRE por projeto (receita por competência): FIN-08.
- Custo de NC como natureza própria: segue a decisão do ponto 78 da PRD-05.
- Rateio de despesas indiretas (administrativas) por projeto: fora do MVP.

## Pontos em aberto
1. **Contar duas vezes a compra para o projeto** (PO, **pré-condição**, herdado do ponto 4 da CMP-03a e do ponto 6 da EST-03): proposta: título de compra de item estocável não é realizado (vai para Comprometido) e o realizado de material vem só da baixa para a OS; serviço ou material entregue direto na obra entra como título com projeto e natureza Material. Alternativa: contar o título e não contar a baixa.
2. **Previsto (`Planned`) no realizado** (PO): a COM-05 CA 8 conta como realizado os títulos a pagar não cancelados, inclusive `Planned`. Proposta: `Planned` e pedidos aprovados vão para a coluna nova Comprometido; muda o número que a COM-05 mostra hoje (registrar na COM-05 e na ajuda).
3. **Impostos antes da Fase 5** (PO/contadora): proposta: títulos a pagar com natureza Impostos e projeto; as NF autorizadas substituem a fonte na FIS-03, sem contar duas vezes.
4. **Margem média do consolidado** (PO): proposta: ponderada pela receita (Σ ÷ Σ). Alternativa: média simples das margens dos projetos.
5. **Custo da não conformidade** (PO, herdado do ponto 78 da PRD-05): o retrabalho já entra pelas horas e pelo material; um custo estimado da NC digitado entraria em Outros? Proposta: não entra no realizado (é estimativa); aparece só na NC.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dataset fixo), arquitetura e E2E passando.
- [ ] Sem migration de tabela (só índice, se a medição pedir).
- [ ] Permissões só do `ModuleCatalog`; DTOs sem custo para quem não tem a ação.
- [ ] Telas com estados e 400 px; aba Custos do COM-05 com o mesmo cálculo.
- [ ] Ajuda da tela nova e da aba Custos; teste de cobertura passando.
- [ ] Alertas acionáveis e drill-down até a origem.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/04-fluxos-estados.md` §3.3 atualizado com as fontes e o comprometido decididos; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ProjectMarginCalculator` (Domain) e `IProjectMargins` (Contracts) com as consultas em lote.
- [ ] `IPayables.CostSummaryByProjectsAsync` (Finance) e uso do `IPurchaseOrders` (Purchasing).
- [ ] Endpoints `/api/v1/projects/margins`, `/api/v1/projects/{id}/costs/{costType}/entries`; tela, aba Custos e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, @data-engineer (agregação com volume), design (relatório e drill-down).
- Gate: CA 1, 2, 5, 9 e 12 obrigatórios.

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

- **Correções feitas na validação:** o realizado de material depende da decisão da Fase 3 sobre contar a compra duas vezes (ponto 1, pré-condição); a coluna Comprometido muda o número da COM-05 e ficou como ponto em aberto (2), sem alterar a COM-05 por conta própria; um cálculo só para Projeto 360, relatório e painel (CA 1).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
