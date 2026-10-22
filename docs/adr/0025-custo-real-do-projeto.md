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

## Adendo 21/10/2026 (revisão pré-Fases 4 a 8, decisão D8 do PO): comprometido por saldo e imposto pela nota
- Origem: `docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`, D8 e tabela 3.1 pontos 5 a 9. A DSH-02 somava o comprometido duas vezes (títulos previstos **e** pedidos de compra pelo `IPurchaseOrders`) e realizava imposto pela guia, que é da empresa e não do projeto.
- **Fonte única nos títulos do Finance.** O comprometido do projeto é lido só pelo `IPayables.ByProjectAsync`; o `IPurchaseOrders` não entra no cálculo de custo (continua servindo para mostrar a situação da compra, só leitura).
- **Comprometido de material por saldo**, por projeto e natureza:
  - comprometido de material = **max(0, Σ títulos de compra de item estocável do projeto (origens `Purchase` e `GoodsReceipt`, fora de `Cancelled`) − Σ baixas líquidas de material para as OS do projeto (requisição menos devolução, EST-03))**;
  - o título previsto (`Planned`) de serviço (instalador, frete, outros) continua no comprometido até ser confirmado, e então passa ao realizado (§1, sem mudança);
  - o max(0, …) evita comprometido negativo quando a baixa usa material comprado para outro projeto ou de estoque anterior; a diferença não vira crédito do projeto.
- **Imposto realizado só pela nota autorizada.**
  - Duas fontes novas na razão `commercial.project_costs`: **`InvoiceTax`** (tributos da NF-e ou NFS-e autorizada que entram no custo do projeto) e **`InvoiceTaxReversal`** (estorno, no cancelamento da nota), com o mesmo padrão de razão de diferenças (§2: `source_id` = id da nota, `seq`, valor anterior e novo).
  - Tributos que entram no custo: os marcados em Configurações → Fiscal (ADR 0042; padrão ICMS, PIS, COFINS, ISS e IBS/CBS; IPI fora), todos "A confirmar com a contadora".
  - **Data de corte:** só notas autorizadas a partir da data de corte do tenant (parâmetro `fiscal.project_tax_cutover_on`, preenchido na primeira nota real em produção, GOL-01b) geram linha; nota anterior ou de homologação nunca gera (sem recomposição, §3 não se aplica a imposto).
  - **Guia de imposto** (DARF, GARE, DAS) é custo da empresa: nunca entra no custo do projeto, nem como comprometido nem como realizado.
  - **Até existir nota real** do projeto, o projetado usa o imposto **orçado** (composição do orçamento aprovado), com a marca "Imposto orçado"; com a primeira nota, a parcela faturada passa a usar o imposto da nota e o resto continua pelo orçado.
- **Margem do consolidado** (vários projetos) é ponderada pela receita, nunca a média simples das margens.
- **Custo da não conformidade** (PRD-05) continua informativo, fora do realizado (item 78 da Fase 3).
- **[Segurança]** Drill-down de horas com nome de funcionário e custo-hora individual exige `hr.view_sensitive`, além de `projects.view_cost`; sem ela, as horas aparecem agregadas por centro de trabalho.

### Consequências do adendo
- **DSH-02** implementa o comprometido por saldo e o projetado com o imposto orçado, e troca o cálculo do Projeto 360 sem mudar o total projetado de projeto sem compra (teste de regressão sobre cópia do `konebras`).
- **FIS-03a** (NF-e) e **FIS-06** (NFS-e) publicam os eventos de nota autorizada e cancelada com os tributos por natureza; o consumidor do Commercial grava `InvoiceTax`/`InvoiceTaxReversal`. **FIS-03b** cobre o estorno no cancelamento.
- **FIS-02a** cria o parâmetro `fiscal.project_tax_cutover_on` e a lista de tributos no custo (ADR 0042).
- `docs/03-modelo-dados.md`: `project_costs.source` ganha `InvoiceTax` e `InvoiceTaxReversal`.
