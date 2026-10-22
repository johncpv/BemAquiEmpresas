# ADR 0023: Motor de estoque

- Status: Aceito (21/10/2026). O PO (John) aprovou a revisão pré-Fase 3 (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): a seção 2.1 "Motor de estoque" entra como está, com a decisão D7 (segregação no estoque) e o adendo de fluxos e eventos (D4, D12, estorno `Fabricated → InProgress`, reconciliação a partir da fonte, `ProjectItemCreated` com tipo e implantação).
- Contexto: a EST-01a cria o motor que todo o resto da Fase 3 usa (reserva, requisição, recebimento, conclusão da OS, romaneio, inventário). A revisão marcou a EST-01a como NO-GO de segurança e a EST-02 como NO-GO de arquitetura: a reserva travava a linha do lote e não a posição do item no local; o custo médio podia perder atualização sob concorrência; a entrada e a saída manual sem aprovação funcionavam como ajuste disfarçado; e os consumidores das reservas e faltas confiavam na ordem dos eventos, que o outbox não garante (ADR 0019 §1).
- Complementa as ADRs 0006 (outbox), 0016 (`OpeningBalance`) e 0019 (§1 ordem dos eventos, §2 concorrência, §3 idempotência). Os títulos de compra estão na ADR 0024 e o custo do projeto na ADR 0025.

## Decisão

### 1. Princípio
- **O efeito de estoque de um documento é chamada síncrona; a projeção é evento.**
  - Síncrono, na transação do documento, pelo `IStockMovements` (`Inventory.Contracts`): recebimento (CMP-04), baixa e devolução da requisição (EST-03), entrada do produto acabado e liberação das reservas na conclusão (PRD-06a2), baixa do romaneio (PRD-06b), ajuste aprovado (EST-01b), inventário (EST-05). Falta de saldo responde 409 `insufficient_stock` e nada é gravado.
  - Por evento (outbox): custo do projeto (ADR 0025), notificações, painéis, Kanban e o aviso de falta para Compras.
- O documento de outro módulo nunca grava em tabela do `inventory` (ADR 0006); chama o contrato.

### 2. Posição agregada e detalhe por lote
- **`inventory.stock_positions`** (`item_id`, `location_id`, `quantity_on_hand`, `quantity_reserved`), uma linha por item e local, com os `CHECK`s `quantity_on_hand >= 0` e `quantity_reserved BETWEEN 0 AND quantity_on_hand`. **A reserva trava aqui**, nunca no lote.
- **`inventory.stock_balances`** guarda só o físico por item, local e lote (`lot_id` anulável), com `CHECK quantity_on_hand >= 0`. Os dois mudam **na mesma transação**; teste de propriedade: Σ lotes = posição, sempre.
- Chave única `UNIQUE NULLS NOT DISTINCT (item_id, location_id, lot_id)` no detalhe e `UNIQUE (item_id, location_id)` na posição. A primeira entrada usa `INSERT … ON CONFLICT DO NOTHING` seguido de `SELECT … FOR UPDATE`, para duas primeiras entradas paralelas não criarem duas linhas.
- **Disponível = físico − reservado**, lido da posição em todas as telas e contratos (`IStockBalances`).

### 3. Trava e custo médio
- O `PostAsync` trava, nesta ordem e sempre nela: **a linha do item** (`inventory.items`, onde mora o `average_cost`) → a posição (item, local) → o lote. Vários itens no mesmo documento: em ordem crescente de `item_id`. Assim o custo médio não perde atualização e duas transações não se travam em ordem inversa (deadlock).
- **`IStockMovements.PostAsync` exige transação aberta** (recusa com `InvalidOperationException` sem `CurrentTransaction`); o chamador é dono da transação.
- Custo médio ponderado móvel global por item (`04-fluxos` §3.1), em `numeric(18,6)`; total da linha em 2 casas `AwayFromZero`.

### 4. Movimento imutável
- `inventory.stock_movements` é imutável: um trigger recusa **qualquer** `UPDATE` e `DELETE`; teste de arquitetura e de integração.
- Cada movimento grava `balance_after` (físico da posição depois dele) e `avg_cost_after`; o kardex é lido pela ordem de lançamento (`posted_seq`, `bigint` de sequência), nunca pela data de negócio.
- **Estorno** é um movimento inverso (`reverses_movement_id`, único), pelo documento de origem. Estorno de entrada já consumida (o físico atual é menor que a entrada) recalcula o custo médio sem deixá-lo negativo nem zerá-lo: sai pelo custo da entrada e, se o físico restante zerar, o custo médio fica o último válido.
- **Idempotência** pela chave `(source_type, source_id, leg, movement_type)`: a mesma perna do mesmo documento nunca lança duas vezes (o recebimento tem uma perna por linha; a transferência tem duas, saída e entrada).
- Saldos e custo médio ficam **fora do diff da auditoria** (`[NotAudited]`): o movimento é a trilha.

### 5. Segregação nos movimentos manuais (D7)
- **Saldo inicial** (`OpeningBalance`, ADR 0016) só na janela de implantação (antes de `inventory.opening_completed_at`) **e** sem nenhum movimento no item e local; depois, só por ajuste (EST-01b).
- **Entrada ou saída manual** acima de **R$ 1.000,00** (parâmetro `inventory.manual_approval_amount`), ou com custo unitário que desvie mais de **20%** do custo médio (parâmetro `inventory.manual_cost_deviation_pct`), vira **documento pendente**, sem mexer no saldo, aprovado por **outra pessoa** com `inventory.approve`; o aprovador decide sobre o que foi enviado, sem editar.
- **Estorno de movimento manual:** o autor, com `inventory.edit`, até R$ 1.000,00 e em até 24 h; acima disso ou depois, outra pessoa com `inventory.approve`.
- Exceção do Proprietário único (regra D6 de 20/10/2026) num helper comum dos BuildingBlocks (`SoleOwnerRule`), com a marca "Autoaprovado" e notificação.
- **Resumo diário** das entradas, saídas e estornos manuais ao Proprietário (job do `RecurringJobsRegistrar`, idempotente por tenant e dia, `dedup_key`).
- Custo médio visível só com `inventory.view` e nunca no PWA do chão de fábrica: nenhum DTO do `shopfloor` traz custo (teste por reflexão nos DTOs).

### 6. Reservas e faltas: reconciliação a partir da fonte
- Os consumidores de `BomReleased`, `BomRevised`, `ProductionOrderCancelled` e da conclusão **não aplicam o delta do evento**: leem a fonte e reconciliam a reserva até o alvo.
  - Alvo da OS = necessidade da BOM liberada vigente (`IBoms.ReleasedForOrderAsync`), ou **0** se a OS está cancelada ou concluída (`IProductionOrders.StatusAsync`).
  - Evento de uma revisão antiga da BOM é ignorado (a fonte já está na revisão nova).
- `StockShortageChanged` é **só aviso** para Compras (CMP-01): a solicitação é recomposta a partir das faltas abertas, não do delta.
- Reserva ordenada pelo local padrão primeiro e depois os demais; material que chega completa as faltas automaticamente: primeiro a OS que originou a compra, depois a BOM liberada há mais tempo (pontos 27 a 29).

### 7. Fluxos e eventos (adendo ao `04-fluxos`)
- **D12 (etapas sem BOM):** a primeira etapa do roteiro inicia sem BOM; as seguintes exigem BOM liberada; o bloqueio liga junto com a ENG-02. Depois da última etapa a OS fica **"Pronta para concluir"** e a conclusão é explícita (PRD-06a1), com `production.approve`.
- **Estorno da conclusão:** a OS volta de `Completed` para `InProgress` e o projeto de `Fabricated` para `InProgress`; o título a receber da parcela já liberada não é estornado; o estorno é recusado se o título estiver em OP ou recebido, e notifica o Financeiro (ponto 83). *Aplicado na PRD-06a1 (22/10/2026):* título a receber não entra em OP, então a recusa olha a baixa total ou parcial da parcela "fabricação concluída" do projeto fabricado pelas OS, conferida de forma síncrona no Commercial (`IProjects.FabricationAsync`); o projeto só volta quando foi fabricado pelas OS (`fabricated_by_orders`); quando a última etapa é de qualidade, a OS estornada fica `Quality` (a regra única de situação).
- **D4 (expedição):** o romaneio confirmado baixa o produto acabado (`Shipment`, síncrono); na Fase 5 a NF-e de saída **referencia** o romaneio e nunca baixa de novo. A etapa "Expedição" do roteiro passa a ser "preparar e embalar" (P-04).
- **`ProjectItemCreated`** ganha `Kind` (`Equipment`/`Product`/`Service`), `IsOnboarding` e `RoutingTemplateId`, com valores padrão para as mensagens antigas no outbox (compatível); a Produção gera a OS automática só para `Equipment` fora de contrato de implantação (D14).
- **Numerações (D15):** `MOV-AAAA-000001`, `AJ-AAAA-0001`, `REQ-AAAA-0001`, `ROM-AAAA-0001`, `INV-AAAA-0001` e série `{código}-{AAAA}-{seq}`; prefixos únicos no tenant (teste).
- **Tempo real e avisos** só depois do commit, também nos consumidores do outbox (helper do BuildingBlocks).

## Consequências
- **EST-01a** implementa as seções 2 a 5 e o `IStockMovements`/`IStockBalances`; o `docs/03-modelo-dados.md` registra `stock_positions`, `stock_balances` só com o físico e as colunas novas de `stock_movements`.
- **EST-02** implementa a seção 6 e reconcilia as BOMs já liberadas na implantação (D3).
- **EST-01b, EST-03, EST-05, CMP-04, PRD-06a2 e PRD-06b** chamam o motor de forma síncrona; a EST-05 gera o ajuste em modo Delta.
- **PRD-01** estende o `ProjectItemCreated`; **ENG-02** liga o bloqueio por BOM; **PRD-06a1** cria o estado "Pronta para concluir" e o estorno.
- Migrations sem nada destrutivo; o trigger e os `CHECK`s são testados sobre cópia do banco do tenant `konebras` (ADR 0020).
