# CMP-04 — Recebimento manual contra o pedido

| Campo | Valor |
|---|---|
| ID | CMP-04 |
| Título | Recebimento manual (sem NF-e) conferido contra o pedido de compra: quantidade, lote, local e custo de entrada com rateio de frete e IPI (P-16); entrada no estoque pelo motor da EST-01a (custo médio); recebimento parcial; confirmação dos títulos a pagar previstos com o valor recebido; estorno do recebimento; encerramento do saldo do pedido |
| Tamanho | G (re-estimada de M: junta a entrada no estoque com custo rateado, o recebimento parcial e a confirmação do CP por parcela) |
| Status | Draft |
| Requisitos | R-CMP-5 (recebimento manual, conferência contra o pedido, entrada no estoque com lote, efetivação do CP) · R-EST-6 (entrada por compra) · R-EST-7 (custo médio recalculado na entrada) · R-EST-8 (lote na matéria-prima) · R-FIN-4 · R-COM-4 (numeração REC) · R-PRP-2 ("recebimento → Conferir título a pagar") · R-PRP-7 · R-SEC-5 |
| Dependências | CMP-03a (pedido aprovado, títulos `Planned` por parcela), EST-01a (`IStockMovements`, lote), EST-02 (completar faltas ao chegar material), FIN-02 (`IPayables.ConfirmAsync`), CAD-02b (`IUnitConversion`, `IItemSupplierCodes`) |
| Bloqueia | EST-04 (lote do fornecedor na rastreabilidade); FIS-05 (Fase 5: recebimento por NF-e reaproveita este fluxo) |
| Ordem de execução | 12 (Marco 3.3 "Comprar o que falta"; fecha o ciclo compra → estoque → caixa) |

## User story
Como **almoxarife**, quero **dar entrada no material que chegou conferindo com o pedido, informando lote e local**, e como **financeiro**, quero **que o título a pagar previsto vire título confirmado com o valor recebido**, para **o estoque e o custo médio ficarem certos e o pagamento seguir para ordem de pagamento sem digitar de novo**.

## Contexto e referências
- PRD §5.10 R-CMP-5 ("Recebimento: por NF-e (XML/PDF) ou manual, conferência contra o pedido, entrada no estoque com lote, efetivação do CP"); o recebimento por NF-e é da Fase 5 (backlog: "Recebimento por NF na Fase 5").
- `04-fluxos-estados` §1 "Pedido de compra" (`Sent → PartiallyReceived → Received`); §2: `GoodsReceived` → Inventory entrada (custo médio); Finance confirma o payable (Open) com o valor da NF; §3.1 ("Custo de entrada da compra = valor do item na NF + rateio de frete/IPI quando não recuperável").
- `06-pendencias-riscos`: **P-16 decidida** (frete e IPI entram no custo de entrada, rateio proporcional ao valor dos itens); **P-01 aberta** (crédito de imposto: o IPI é tratado como não recuperável até a decisão, Pontos em aberto 1).
- `03-modelo-dados` → `purchasing.goods_receipts` (`number`, `purchase_order_id`, `incoming_invoice_id`, `received_at`, `status`), `goods_receipt_lines` (`receipt_id`, `po_line_id`, `item_id`, `quantity`, `lot_number`, `location_id`, `unit_cost`); `purchase_order_lines.quantity_received`. `04-fluxos` §4: `REC-AAAA-0001` (em `NumberFormats`).
- ADR 0019 §17 (confirmação do título estimado grava o vencimento real e tira a marca).

## Critérios de aceite
**Registrar o recebimento**
1. **Dado** `purchasing.create` (ver Pontos em aberto 6) e um pedido `Approved`, `Sent` ou `PartiallyReceived`, **quando** clico "Registrar recebimento", **então** o formulário vem com as linhas do pedido e o **saldo a receber** de cada uma, a data de recebimento (padrão hoje, ≤ hoje), o local padrão do item (ALM) e, por linha, a quantidade recebida (padrão: o saldo; na unidade de estoque ou do fornecedor, convertida pelo fator do `item_supplier_codes`), o lote (obrigatório quando o item controla lote; validade opcional) e o preço unitário do pedido (só leitura sem `purchasing.approve`, Pontos em aberto 3); frete e IPI do pedido proporcionais ao que está sendo recebido, editáveis.
2. **Dado** uma quantidade acima do saldo da linha, **quando** confirmo, **então** 422 na linha "Recebido acima do pedido: saldo {x}." (tolerância 0, Pontos em aberto 2).
3. **Dado** o recebimento preenchido, **quando** clico "Registrar recebimento" e confirmo no diálogo grande com o impacto (por item: quantidade, custo unitário de entrada com o rateio, custo médio antes → depois; total da entrada; títulos a pagar confirmados com valor e vencimento), **então** na mesma transação: nasce o recebimento `Completed` com número `REC-2026-0001`; cada linha vira uma entrada `Receipt` pelo `IStockMovements` (origem `GoodsReceipt`, com lote, local e o **custo unitário de entrada** = (valor da linha + rateio do frete + rateio do IPI) ÷ quantidade, 6 casas); `quantity_received` das linhas do pedido sobe; o pedido passa a `PartiallyReceived` ou `Received`; e o evento `GoodsReceived` (`{ goodsReceiptId, purchaseOrderId, partyId, projectId, receivedOn, amount, lines: [{ poLineId, quantity, amount }] }`) vai para o outbox; com `Idempotency-Key`; a mensagem diz "Recebimento registrado".
4. **Dado** o rateio (P-16), **quando** o frete é R$ 100,00 e as linhas valem R$ 300,00 e R$ 700,00, **então** o frete vai R$ 30,00 e R$ 70,00 (proporcional ao valor, a última linha absorve a diferença de arredondamento), e o mesmo vale para o IPI; o custo médio de cada item bate com o cálculo de referência (dataset fixo).
5. **Dado** a entrada, **quando** o item tem faltas de reserva em OS (EST-02), **então** o motor completa as reservas na mesma transação e o `NextSteps` mostra "Ver OS que receberam material" com as OS atendidas.

**Caixa: confirmar o título a pagar**
6. **Dado** o `GoodsReceived`, **quando** o consumidor do Finance processa, **então** confirma os títulos `Planned` do pedido no valor **recebido**: (proposta, Pontos em aberto 4) cada recebimento confirma, por parcela da condição, o valor recebido nesta entrega (`IPayables.ConfirmAsync` por parcela, com o vencimento real = data do recebimento + dias da parcela), tira a marca "Estimado" e reduz os `Planned` restantes do pedido pelo mesmo valor; idempotente pela inbox e por `(goodsReceiptId, parcela)`.
7. **Dado** o pedido `Received` (todo o saldo recebido), **quando** o Finance processa, **então** não sobra título `Planned` do pedido; a diferença entre o previsto e o recebido (frete ou IPI editados) fica no histórico do título.
8. **Dado** o recebimento registrado, **quando** o `NextSteps` aparece, **então** oferece "Conferir título a pagar" (com `finance.view`, R-PRP-2) e "Ver movimentos de estoque".

**Estorno e encerramento**
9. **Dado** `purchasing.approve` (Pontos em aberto 5) e um recebimento sem consumo posterior que impeça o estorno, **quando** clico "Estornar recebimento" com motivo e confirmo no diálogo `destructive` com o impacto, **então** na mesma transação nascem os movimentos inversos pelo motor (EST-01a CA 11: custo médio recalculado tirando a entrada; saldo insuficiente → 409 `insufficient_stock` com o item), `quantity_received` desce, o pedido volta a `PartiallyReceived`/`Sent`, e o evento `GoodsReceiptReversed` faz o Finance voltar os títulos confirmados por este recebimento a `Planned` estimado (título já em OP: 409 `payable_in_payment_order` antes de estornar, com o link).
10. **Dado** um pedido `PartiallyReceived` cujo saldo não virá, **quando** clico "Encerrar saldo do pedido" (`purchasing.edit`) com motivo, **então** o pedido fica `Received` com a marca "Encerrado com saldo de {x}", os títulos `Planned` restantes são cancelados (`IPayables.CancelFromOriginAsync` por parcela) e as linhas de SC ligadas voltam a `Open` pela quantidade não recebida (proposta).

**Consulta, permissões e auditoria**
11. **Dado** `purchasing.view`, **quando** abro "Recebimentos" (`/app/compras/recebimentos`), **então** vejo número, data, pedido, fornecedor, itens, valor e situação (Registrado ok, Estornado neutro); o pedido mostra a aba Recebimentos; o item ganha "Última compra" (preço e fornecedor), usado pela CMP-02 e pela CMP-03a.
12. **Dado** o módulo `purchasing` (ou `inventory`) não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; registrar sem `purchasing.create`, estornar sem `purchasing.approve`, encerrar sem `purchasing.edit`: 403. `RequireModule` e `perm:`; IDOR em `/purchase-orders/{id}/receipts/{receiptId}` e `/goods-receipts/{id}/lines/{lineId}`; linha de pedido de outro pedido: 422.
13. **[Segurança]** **Dado** dois recebimentos simultâneos do mesmo saldo, **quando** chegam juntos, **então** só um passa do saldo (transição pela entidade do pedido com `xmin`; o segundo recebe 409 `concurrency_conflict` ou 422 de saldo) e o estoque não entra duas vezes (idempotência por `(GoodsReceipt, recebimentoId, linha)` no `IStockMovements`).
14. **Dado** qualquer recebimento, estorno ou encerramento, **quando** abro o histórico, **então** vejo antes/depois, usuário e data; o movimento de estoque aponta para o recebimento (link) e o título a pagar aponta para o pedido e o recebimento.

## Regras de negócio e técnicas
- **Tabelas** (03 + proposta): `goods_receipts` (+ `party_id`, `freight`, `ipi_amount`, `amount`, `status` Completed/Reversed, `reversed_at`, `reversed_by`, `reversal_reason`, `xmin`); `goods_receipt_lines` (+ `freight_share`, `ipi_share`, `supplier_unit`, `supplier_quantity`, `lot_id`, `movement_id`); `purchase_orders.closed_with_balance`, `close_reason`.
- **Custo de entrada** = valor da linha (qtd × preço do pedido) + rateio do frete + rateio do IPI (P-16), ÷ quantidade na unidade de estoque, 6 casas; IPI tratado como não recuperável enquanto a P-01 estiver aberta.
- **Valor recebido** (para o CP) = Σ (qtd × preço) + frete + IPI deste recebimento, 2 casas.
- **Projeto:** a entrada no estoque leva o `project_id` do pedido só como referência (não é custo do projeto: o custo nasce na baixa para a OS, EST-03; ver CMP-03a ponto 4).
- **Lote:** criado na entrada (`inventory.lots`, `supplier_party_id` = fornecedor, `received_at`), base da rastreabilidade (EST-04).
- **Nunca apagar:** recebimento só é estornado; movimento de estoque só pelo inverso.
- Guid v7; numeração `REC`; `xmin`; auditoria `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.create` (registrar), `purchasing.edit` (encerrar saldo), `purchasing.approve` (estornar; proposta), `purchasing.view`.
- **Eventos (`Purchasing.Contracts`):** `GoodsReceived`, `GoodsReceiptReversed`, `PurchaseOrderClosed` (saldo encerrado). **Consumidor no Finance:** confirma, volta e cancela as parcelas.
- **Contratos:** `IStockMovements` (entrada), `IPayables` por parcela (CMP-03a), `IUnitConversion`, `IItemSupplierCodes`; `IPurchaseOrders.LastPurchaseAsync(itemId)` (novo) para a "Última compra".
- **Tabelas:** `purchasing.goods_receipts`, `purchasing.goods_receipt_lines` (com as colunas acima).

## Telas e estados
- **Pedido → "Registrar recebimento"** (página `/app/compras/pedidos/$id/recebimento`), **Fábrica → Compras → Recebimentos** (`/app/compras/recebimentos`, detalhe `/app/compras/recebimentos/$id`, abas Itens, Financeiro, Histórico de alterações).
- **Verbos:** "Registrar recebimento" / "Recebimento registrado"; "Estornar recebimento" / "Recebimento estornado"; "Encerrar saldo do pedido" / "Saldo do pedido encerrado".
- **Estados:** carregando; vazio ("Nenhum recebimento. Os pedidos aprovados aparecem em 'A receber'." + "Ver pedidos a receber"); erro dentro do formulário; modo leitura do recebimento registrado.
- **Propositivo:** quantidades preenchidas com o saldo, local padrão, lote sugerido pelo padrão do fornecedor quando houver (texto livre); a lista "A receber" (pedidos aprovados com saldo, ordenados pela previsão de entrega, com atraso em vermelho) é a entrada natural do almoxarife; próximos passos do CA 5 e 8. Toda entrada exige confirmação com o impacto (R-PRP-7).
- **400 px:** linhas em cartões (item, saldo, recebido, lote, local); o impacto do diálogo em tela cheia; "Ler código de barras do lote" fica fora do MVP.
- **Ajuda:** `Ajuda/compras-recebimento-novo.md`, `Ajuda/compras-recebimentos.md`, `Ajuda/compras-recebimento.md` (abas `itens` padrão, `financeiro`, `historico`); `<Dica>` em "Custo de entrada", "Rateio do frete" e "Encerrar saldo".

## Plano de testes
- **Unidade:** rateio do frete e do IPI com arredondamento na última linha (CA 4); custo de entrada e valor recebido; parcelas confirmadas por recebimento parcial (CA 6).
- **Integração (Testcontainers, obrigatória: movimenta estoque e financeiro):** recebimento total e parcial com entrada, lote e custo médio na mesma transação e rollback (CA 1–4); acima do saldo (CA 2); faltas completadas (CA 5); Finance confirma parcelas, tira "Estimado" e não sobra `Planned` (CA 6, 7); estorno com saldo insuficiente e título em OP (CA 9); encerrar saldo cancela parcelas e devolve SC (CA 10); três camadas e IDOR (CA 12); recebimentos concorrentes (CA 13); auditoria e links (CA 14); dataset fixo pedido → recebimento → fluxo de caixa.
- **E2E (Playwright):** receber parcialmente um pedido de dois itens com lote, ver o custo médio e o título confirmado; tela a 400 px.

## Fora do escopo
- Recebimento por NF-e (XML/PDF/DF-e): FIS-05/FIS-06 (Fase 5), que reaproveitam este fluxo.
- Devolução ao fornecedor: fora do MVP (estorno do recebimento cobre o erro de lançamento).
- Frete de transportadora em título separado: Pontos em aberto 7.

## Pontos em aberto
1. **IPI recuperável** (contadora, P-01 aberta): proposta: tratar como não recuperável (entra no custo) até a decisão da P-01.
2. **Tolerância de quantidade** (PO): proposta: 0% (recebido acima do pedido é recusado; o excedente vira outro pedido).
3. **Preço diferente do pedido** (PO): no recebimento manual não há NF. Proposta: o preço é o do pedido; só quem tem `purchasing.approve` muda, com motivo, e a diferença aparece no histórico; o recebimento por NF-e (Fase 5) traz o preço da nota.
4. **Confirmação do CP no recebimento parcial** (PO/Financeiro): proposta do CA 6 (cada recebimento confirma, por parcela, o valor recebido); alternativas: confirmar tudo no primeiro recebimento, ou só no recebimento final.
5. **Quem estorna o recebimento** (PO/segurança): proposta: `purchasing.approve` (mexe em estoque e em título a pagar).
6. **Quem registra o recebimento** (PO): o almoxarife recebe; proposta: `purchasing.create`, que o Almoxarifado ganha no seed patch da CMP-01; alternativa: `inventory.create`.
7. **Frete com outro fornecedor** (PO): proposta: fora do MVP; o frete do pedido é do próprio fornecedor.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda de cada tela e aba; teste de cobertura passando.
- [ ] Confirmação com impacto em estoque e caixa e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `GoodsReceipt` e `GoodsReceiptLine` (Domain); `CostAllocator` puro (rateio P-16).
- [ ] `GoodsReceiptService` (registrar, estornar) e `PurchaseOrderCloseService`, usando o `IStockMovements`.
- [ ] Consumidor no Finance de `GoodsReceived`, `GoodsReceiptReversed`, `PurchaseOrderClosed`.
- [ ] `IPurchaseOrders.LastPurchaseAsync`; telas "A receber", recebimento e lista; ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (cadeia Purchasing → Inventory → Finance), @data-engineer (dataset de custo), @qa.
- Gate: CA 3, 4, 6, 9 e 13 obrigatórios.

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

- **Correções feitas na validação:** re-estimada de M para G; rateio da P-16 com teste de arredondamento (CA 4); a entrada não vira custo do projeto (regras, ligado à CMP-03a ponto 4); estorno com título já em OP bloqueado antes de mexer no estoque (CA 9); confirmação parcial do CP como ponto em aberto, com proposta.
- **Pré-condições para `Ready`:** pontos em aberto 4, 5 e 6 (e a decisão do ponto 4 da CMP-03a).
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; re-estimada de M para G | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
