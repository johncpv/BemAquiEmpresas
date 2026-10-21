# ADR 0025: Custo real do projeto

- Status: Aceito (21/10/2026). O PO (John) aprovou as decisões D2 (comprometido, realizado e projetado) e D3 (recompor o passado) e a correção D17 (a) da revisão pré-Fase 3 (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`), com a `commercial.project_costs` como razão de diferenças nascendo na PRD-04.
- Contexto: o Projeto 360 (COM-05, já no ar) soma como realizado **todo título a pagar não cancelado com projeto, inclusive os previstos** (`ProjectViews`: `payables.Where(p => !p.Cancelled)`). O título previsto do instalador já conta como realizado quando a OS de instalação nasce, e o previsto do pedido de compra contaria na aprovação e de novo na baixa do material para a OS. Além disso, evento publicado antes de existir o consumidor é dado como processado e se perde (saída avulsa com projeto antes da EST-03, apontamentos antes da PRD-04), e a homologação tem dado real (ADR 0020).
- **Muda a COM-05 já entregue.** Complementa as ADRs 0019 (§20 custo e margem com `projects.view_cost`), 0023 (motor de estoque) e 0024 (títulos de compra).

## Decisão

### 1. Três colunas por natureza (D2)
- **Realizado** = título a pagar **confirmado** (fora de `Planned` e de `Cancelled`) de **serviço** (instalador, frete, outros: natureza diferente de `Material` com item estocável) + **baixa de material para a OS** (requisição líquida de devolução, EST-03) + **horas apontadas** (PRD-04).
- **Comprometido** (coluna nova): título a pagar `Planned` com projeto (previsto do instalador, previsto do pedido) + título de compra de item estocável com projeto, confirmado ou não, enquanto o material não foi baixado para a OS. O material comprado para o projeto conta como realizado **só** na baixa.
- **Projetado** = realizado + max(orçado − realizado, comprometido em aberto).
- Exclusões: o movimento `Production` (entrada do produto acabado) e o `Shipment` (romaneio) **nunca** entram no custo do projeto (o custo já entrou pela baixa e pelas horas); títulos `Opening` com projeto entram como realizado pelo saldo da data de corte (sem mudança).
- Custo e margem continuam só com `projects.view_cost` (ADR 0019 §20); o DTO sem custo não traz as três colunas.

### 2. Razão de diferenças: `commercial.project_costs`
- Nasce na **PRD-04** (a primeira fonte que não é título): `project_id`, `cost_type`, `source` (`StockIssue`/`StockReturn`/`Labor`/`LaborCorrection`), `source_id`, `seq`, `amount` (com sinal), `occurred_on`, `recorded_at`, `is_backfill`; único em `(source, source_id, seq)`.
- É **razão de diferenças**: cada evento leva o valor anterior e o novo da fonte (`PreviousAmount`, `NewAmount`); o consumidor grava a diferença com o próximo `seq`. Evento repetido (mesmo `seq`) não grava de novo; evento fora de ordem grava a diferença certa porque compara com o último `NewAmount` aplicado.
- Os títulos a pagar continuam lidos pelo `IPayables.ByProjectAsync` (não são copiados para a razão).
- **Custo da OS na conclusão** (PRD-06a2) é lido por fontes síncronas (horas da Produção e `IStockMovements.CostByOrderAsync`), nunca pela razão do Commercial, e não depende da permissão de quem conclui.

### 3. Recompor o passado (D3)
- **A story que entrega um consumidor recompõe o que foi publicado antes dela**, a partir da fonte, uma vez, por job idempotente por tenant: PRD-04 (apontamentos fechados), EST-03 (saídas com projeto e requisições), EST-02 (BOMs já liberadas), CMP-01 (faltas abertas).
- As linhas recompostas levam `is_backfill = true` e aparecem com a marca **"Calculado na implantação"** (padrão de tela 11 da Fase 3).
- Teste: rodar a recomposição duas vezes e com o consumidor ao vivo ao mesmo tempo grava uma vez só.

### 4. Regressão da COM-05
- A PRD-04 troca o cálculo do `ProjectViews` pelo da seção 1 e acrescenta a coluna "Comprometido", com **teste de regressão sobre cópia dos dados do tenant `konebras`**: o realizado de cada projeto da Fase 2 cai exatamente pelo valor dos títulos `Planned` e passa a aparecer no comprometido; o total projetado não muda quando não há título previsto.
- A ajuda do Projeto 360 (`Ajuda/`) explica as três colunas na mesma story.

## Consequências
- **PRD-04:** cria a `project_costs`, o consumidor de `LaborEntryClosed`/`LaborEntryCorrected`, a recomposição dos apontamentos, a troca do cálculo da COM-05 e o teste de regressão.
- **EST-03:** consumidor de `StockIssued`/`StockReturned` na razão e recomposição das saídas com projeto.
- **CMP-03a e CMP-04:** previstos e títulos de compra de item estocável aparecem como comprometido.
- **PRD-06a2:** custo da OS pelas fontes síncronas.
- O `docs/03-modelo-dados.md` passa a descrever a `project_costs` como tabela (não view).
