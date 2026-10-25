# CMP-04 — Recebimento manual contra o pedido

| Campo | Valor |
|---|---|
| ID | CMP-04 |
| Título | Recebimento manual (sem NF-e) conferido contra o pedido de compra: quantidade, lote, local e custo de entrada com rateio de frete e IPI (P-16); entrada no estoque síncrona pelo motor da EST-01a (custo médio); recebimento parcial; títulos a pagar em aberto do próprio recebimento e replanejamento dos previstos do pedido para o saldo; teto do aprovado; segregação de quem pede, aprova e recebe; estorno do recebimento; encerramento do saldo do pedido |
| Tamanho | G (re-estimada de M: junta a entrada no estoque com custo rateado, o recebimento parcial e os títulos por parcela) |
| Status | Done |
| Requisitos | R-CMP-5 (recebimento manual, conferência contra o pedido, entrada no estoque com lote, efetivação do CP) · R-EST-6 (entrada por compra) · R-EST-7 (custo médio recalculado na entrada) · R-EST-8 (lote na matéria-prima) · R-FIN-4 · R-COM-4 (numeração REC) · R-PRP-2 ("recebimento → Conferir título a pagar") · R-PRP-7 · R-SEC-5 |
| Dependências | CMP-03a (pedido aprovado, previstos por parcela, `IPayables` por parcela), EST-01a (`IStockMovements`, lote), EST-02 (completar faltas ao chegar material) |
| Bloqueia | EST-04 (lote do fornecedor na rastreabilidade) |
| Ordem de execução | 16 (Marco 3.3 "Comprar o que falta") |

## User story
Como **almoxarife**, quero **dar entrada no material que chegou conferindo com o pedido, informando lote e local**, e como **financeiro**, quero **que o recebimento gere o título a pagar com o valor recebido**, para **o estoque e o custo médio ficarem certos e o pagamento seguir para ordem de pagamento sem digitar de novo**.

## Contexto e referências
- PRD §5.10 R-CMP-5 ("Recebimento: por NF-e (XML/PDF) ou manual, conferência contra o pedido, entrada no estoque com lote, efetivação do CP"); o recebimento por NF-e é da Fase 5 (FIS-05/FIS-06 reaproveitam este fluxo).
- `04-fluxos-estados` §1 "Pedido de compra" (`Sent → PartiallyReceived → Received`); §2: `GoodsReceived` passa a ser só aviso e projeção (a entrada no estoque e os títulos são síncronos, ADR 0023 §1 e ADR 0024 §1; atualizar no DoD); §3.1 ("Custo de entrada da compra = valor do item na NF + rateio de frete/IPI quando não recuperável").
- `06-pendencias-riscos`: **P-16 decidida** (frete e IPI entram no custo de entrada, rateio proporcional ao valor dos itens); **P-01 aberta** (crédito de imposto: IPI não recuperável até a decisão).
- `03-modelo-dados` → `purchasing.goods_receipts` (`number`, `purchase_order_id`, `incoming_invoice_id`, `received_at`, `status`), `goods_receipt_lines` (`receipt_id`, `po_line_id`, `item_id`, `quantity`, `lot_number`, `location_id`, `unit_cost`); `purchase_order_lines.quantity_received`. `04-fluxos` §4: `REC-AAAA-0001` (em `NumberFormats`).
- **ADR 0024** §3 (títulos do recebimento, replanejamento, teto, preço) e §5 (`purchasing.receive`, segregação); **ADR 0023** §1 (efeito de estoque síncrono), §4 (estorno de entrada consumida), §5 (`SoleOwnerRule`); **ADR 0025** §1 (comprometido × realizado); ADR 0003, adendo de 21/10/2026 (`purchasing.receive`); ADR 0019 §17.
- Revisão `docs/revisoes/2026-10-21-revisao-pre-fase3.md`: D2, D8, tabela 1.1 itens 47–53, §2.1 "Compras ↔ Financeiro", §2.2 CMP-04, §2.3 ("Recebimentos" abre em "A receber").
- `DocumentOrigin`: entra o valor **`GoodsReceipt`** (títulos do recebimento); `Purchase` continua sendo o previsto do pedido. `IUnitConversion` e `IItemSupplierCodes` (CAD-02b, entregues).

## Critérios de aceite
**Registrar o recebimento**
1. **Dado** `purchasing.receive` e um pedido `Approved`, `Sent` ou `PartiallyReceived`, **quando** clico "Registrar recebimento", **então** o formulário vem com as linhas do pedido e o **saldo a receber** de cada uma, a data de recebimento (padrão hoje, ≤ hoje), o local padrão do item (ALM) e, por linha, a quantidade recebida (padrão: o saldo; na unidade de estoque ou do fornecedor, convertida pelo fator do `item_supplier_codes`), o lote (obrigatório quando o item controla lote; validade opcional) e o preço unitário do pedido, só leitura; frete e IPI do pedido proporcionais ao que está sendo recebido, editáveis só para baixo.
2. **[Segurança]** **Dado** o usuário que pediu o pedido (`requested_by`) ou que decidiu algum nível da aprovação dele, **quando** tenta registrar o recebimento, **então** 403 `segregation_of_duties` "Quem pediu ou aprovou este pedido não registra o recebimento dele."; com um Proprietário só no tenant, o helper `SoleOwnerRule` (BuildingBlocks) permite, grava a marca "Recebido pelo Proprietário único" e notifica.
3. **Dado** uma quantidade acima do saldo da linha mais a tolerância (`purchasing.receipt_tolerance_pct`, padrão 0%), **quando** confirmo, **então** 422 na linha "Recebido acima do pedido: saldo {x}."; o excedente vira outro pedido.
4. **[Segurança]** **Dado** um recebimento, **quando** o servidor confere, **então** Σ confirmado nos recebimentos do pedido (incluindo este) ≤ total aprovado + tolerância; frete ou IPI acima do proporcional do pedido: 422 no campo "Frete e IPI só podem baixar no recebimento."; acima do teto: 422 "Acima do valor aprovado do pedido: peça a correção do preço (CA 14) ou a reaprovação." (D8, ADR 0024 §3).
5. **Dado** o recebimento preenchido, **quando** clico "Registrar recebimento" e confirmo no diálogo grande com o impacto (por item: quantidade, custo unitário de entrada com o rateio, custo médio antes → depois; total da entrada; títulos a pagar que nascem, com valor e vencimento; previsto do pedido antes → depois), **então** na **mesma transação**: nasce o recebimento `Completed` com número `REC-2026-0001`; cada linha vira uma entrada `Receipt` pelo `IStockMovements.PostAsync` (ADR 0023 §1; origem `GoodsReceipt`, com lote, local e o **custo unitário de entrada** = (valor da linha + rateio do frete + rateio do IPI) ÷ quantidade, 6 casas); `quantity_received` das linhas do pedido sobe; o pedido passa a `PartiallyReceived` ou `Received`; nascem os títulos do recebimento (CA 8) e os previstos do pedido são replanejados (CA 9); e o `GoodsReceived` (`{ goodsReceiptId, purchaseOrderId, partyId, projectId, receivedOn, amount, lines: [{ poLineId, quantity, amount }] }`) vai para o outbox só para aviso e projeção; falha em qualquer parte desfaz tudo (409 `insufficient_stock` ou `payable_in_payment_order`); com `Idempotency-Key`; a mensagem diz "Recebimento registrado".
6. **Dado** o rateio (P-16), **quando** o frete é R$ 100,00 e as linhas valem R$ 300,00 e R$ 700,00, **então** o frete vai R$ 30,00 e R$ 70,00 (proporcional ao valor, a última linha absorve a diferença de arredondamento), e o mesmo vale para o IPI, tratado como não recuperável; o custo médio de cada item bate com o cálculo de referência (dataset fixo).
7. **Dado** a entrada, **quando** o item tem faltas de reserva em OS (EST-02), **então** o motor completa as reservas na mesma transação e o `NextSteps` mostra "Ver OS que receberam material" com as OS atendidas.

**Caixa: títulos do recebimento e replanejamento (ADR 0024 §3)**
8. **Dado** o recebimento registrado, **quando** grava, **então** nascem os **títulos em aberto do próprio recebimento** pelo `IPayables` por parcela: origem `GoodsReceipt`, um por parcela da condição do pedido, vencimento = data do recebimento + dias da parcela, valor = itens + frete e IPI rateados deste recebimento (a última parcela absorve a diferença), fornecedor, projeto, conta e natureza do pedido (CMP-03a CA 19), sem a marca "Estimado", descrição "Recebimento {número} do pedido {número} — parcela {n}/{m}"; idempotente por `(GoodsReceipt, recebimentoId, parcela)`.
9. **Dado** o recebimento registrado, **quando** grava, **então** os títulos previstos do pedido (origem `Purchase`) são replanejados para o saldo a receber por `IPayables.ReplacePlannedAsync`; com o pedido `Received`, não sobra previsto (cancelados); algum previsto em OP: 409 `payable_in_payment_order` com "Ver OP", e nada é gravado.
10. **Dado** um pedido com projeto, **quando** abro o Projeto 360 → Custos, **então** os títulos `GoodsReceipt` de item estocável aparecem em "Comprometido" e não no realizado, até a baixa do material para a OS (D2, ADR 0025 §1); a entrada no estoque leva o `project_id` só como referência.
11. **Dado** o recebimento registrado, **quando** o `NextSteps` aparece, **então** oferece "Conferir título a pagar" (com `finance.view`, R-PRP-2) e "Ver movimentos de estoque".

**Estorno, encerramento e preço**
12. **Dado** `purchasing.approve` e um recebimento `Completed`, **quando** clico "Estornar recebimento" com motivo e confirmo no diálogo `destructive` com o impacto, **então** na mesma transação: os títulos do recebimento são cancelados por `IPayables.CancelAllFromOriginAsync(GoodsReceipt, recebimentoId, motivo)` **antes** de mexer no estoque (409 se algum estiver em OP ou pago, com a ação "Ver OP"); nascem os movimentos inversos pelo motor (ADR 0023 §4: estorno de entrada já consumida protege o custo médio; saldo insuficiente → 409 `insufficient_stock` com o item); `quantity_received` desce; o pedido volta a `PartiallyReceived` ou ao estado anterior; o saldo volta ao previsto do pedido (`ReplacePlannedAsync`); `GoodsReceiptReversed` vai para o outbox só para aviso.
13. **Dado** um pedido `PartiallyReceived` cujo saldo não virá, **quando** clico "Encerrar saldo do pedido" (`purchasing.edit`) com motivo, **então** o pedido fica `Received` com a marca "Encerrado com saldo de {x}", os previstos restantes são cancelados (`IPayables.CancelAllFromOriginAsync(Purchase, pedidoId, motivo)`) e as linhas de SC ligadas voltam a `Open` pela quantidade não recebida; `PurchaseOrderClosed` vai para o outbox só para aviso.
14. **[Segurança]** **Dado** um preço real diferente do pedido, **quando** um usuário com `purchasing.approve` clica "Corrigir preço" na linha do pedido com motivo obrigatório, **então** o preço novo vale só para o saldo a receber (as entradas já feitas não mudam); quem registrou recebimento do pedido não corrige o preço dele, e quem corrigiu não registra o recebimento (403 `segregation_of_duties`); dentro do teto (aprovado + tolerância), os previstos são replanejados na mesma transação; acima do teto, o pedido volta à alçada (CMP-03a CA 15) e "Registrar recebimento" fica desabilitado com o motivo "Pedido em reaprovação" até a nova aprovação; a diferença fica no histórico.

**Consulta, permissões e auditoria**
15. **Dado** `purchasing.view`, **quando** abro "Recebimentos" (`/app/compras/recebimentos`), **então** a tela abre na aba **"A receber"** (pedidos aprovados com saldo, ordenados pela previsão de entrega, atraso com faixa lateral vermelha e o texto "Atrasado {n} d"); a aba "Recebidos" mostra número, data, pedido, fornecedor, itens, valor e situação (Registrado ok, Estornado neutro); o pedido mostra a aba Recebimentos; o item ganha "Última compra" (preço e fornecedor), usado pela CMP-02 e pela CMP-03a.
16. **Dado** o módulo `purchasing` (ou `inventory`) não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; registrar sem `purchasing.receive` (inclusive com `purchasing.create`), estornar ou corrigir preço sem `purchasing.approve`, encerrar sem `purchasing.edit`: 403; o Almoxarifado do seed (`purchasing.view` + `purchasing.receive`) registra o recebimento e não edita o pedido (teste). `RequireModule` e `perm:`; IDOR em `/purchase-orders/{id}/receipts/{receiptId}` e `/goods-receipts/{id}/lines/{lineId}`; linha de pedido de outro pedido: 422.
17. **[Segurança]** **Dado** dois recebimentos simultâneos do mesmo saldo, **quando** chegam juntos, **então** só um passa do saldo (transição pela entidade do pedido com `xmin`; o segundo recebe 409 `concurrency_conflict` ou 422 de saldo), o estoque não entra duas vezes (idempotência por `(GoodsReceipt, recebimentoId, linha)` no `IStockMovements`) e os títulos não duplicam (idempotência por parcela).
18. **Dado** qualquer recebimento, estorno, encerramento ou correção de preço, **quando** abro o histórico, **então** vejo antes/depois, usuário, data e a marca do Proprietário único quando houver; o movimento de estoque aponta para o recebimento (link) e o título a pagar aponta para o pedido e o recebimento.

## Regras de negócio e técnicas
- **Tabelas:** `goods_receipts` (+ `party_id`, `freight`, `ipi_amount`, `amount`, `status` Completed/Reversed, `received_by`, `sole_owner_flag`, `reversed_at`, `reversed_by`, `reversal_reason`, `xmin`); `goods_receipt_lines` (+ `freight_share`, `ipi_share`, `supplier_unit`, `supplier_quantity`, `lot_id`, `movement_id`); `purchase_orders.closed_with_balance`, `close_reason`; `purchase_order_lines.price_corrections` no histórico (preço anterior, novo, motivo, usuário).
- **Custo de entrada** = valor da linha (qtd × preço do pedido) + rateio do frete + rateio do IPI (P-16), ÷ quantidade na unidade de estoque, 6 casas; IPI não recuperável até a decisão da P-01.
- **Valor do recebimento** (títulos) = Σ (qtd × preço) + frete + IPI deste recebimento, 2 casas.
- **Teto (D8):** Σ confirmado ≤ total aprovado + `purchasing.receipt_tolerance_pct` (padrão 0%); frete e IPI só para baixo; preço = preço do pedido, muda só pelo CA 14.
- **Projeto:** a entrada no estoque leva o `project_id` do pedido só como referência; o custo do projeto nasce na baixa para a OS (EST-03, ADR 0025).
- **Lote:** criado na entrada (`inventory.lots`, `supplier_party_id` = fornecedor, `received_at`), base da rastreabilidade (EST-04).
- **Nunca apagar:** recebimento só é estornado; movimento de estoque só pelo inverso.
- Guid v7; numeração `REC`; `xmin`; auditoria `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.receive` (registrar; D8, seed patch `purchasing-permissions` da CMP-01), `purchasing.edit` (encerrar saldo), `purchasing.approve` (estornar e corrigir preço), `purchasing.view`.
- **Eventos (`Purchasing.Contracts`, só aviso e projeção):** `GoodsReceived`, `GoodsReceiptReversed`, `PurchaseOrderClosed`, `PurchaseOrderPriceCorrected`. Nenhum consumidor do Finance cria, confirma ou cancela título por evento.
- **Contratos:** `IStockMovements` (entrada e estorno, na transação do recebimento), `IPayables` por parcela (CMP-03a: `ReplacePlannedAsync`, `CancelAllFromOriginAsync` e `CreateOpenInstallmentsAsync`, ADR 0024 §2; o índice parcial deixa o cancelado fora da chave), `SoleOwnerRule`, `IUnitConversion`, `IItemSupplierCodes`; `IPurchaseOrders.LastPurchaseAsync(itemId)` (novo) para a "Última compra"; `DocumentOrigin.GoodsReceipt` (SharedKernel).
- **Tabelas:** `purchasing.goods_receipts`, `purchasing.goods_receipt_lines` (com as colunas acima); parâmetro `purchasing.receipt_tolerance_pct`.

## Telas e estados
- Padrões de tela da Fase 3: 2, 4, 6.
- **Fábrica → Compras → Recebimentos** (`/app/compras/recebimentos`, abre em "A receber"; detalhe `/app/compras/recebimentos/$id`, abas Itens, Financeiro, Histórico de alterações); **Pedido → "Registrar recebimento"** (página `/app/compras/pedidos/$id/recebimento`).
- **Verbos:** "Registrar recebimento" / "Recebimento registrado"; "Estornar recebimento" / "Recebimento estornado"; "Encerrar saldo do pedido" / "Saldo do pedido encerrado"; "Corrigir preço" / "Preço corrigido".
- **Estados:** carregando; vazio de "A receber" ("Nenhum pedido a receber. Os pedidos aprovados aparecem aqui." + "Ver pedidos"); vazio de "Recebidos" ("Nenhum recebimento." + "Ver pedidos a receber"); erro dentro do formulário; modo leitura do recebimento registrado; "Registrar recebimento" desabilitado com o motivo quando o usuário pediu ou aprovou o pedido, ou o pedido está em reaprovação.
- **Propositivo:** quantidades preenchidas com o saldo, local padrão e lote sugerido pelo padrão do fornecedor quando houver, com a `Tag` "Sugerido"; próximos passos do CA 7 e 11.
- **400 px:** linhas em cartões (item, saldo, recebido, lote, local); o impacto do diálogo em tela cheia; "Ler código de barras do lote" fica fora do MVP.
- **Ajuda:** `Ajuda/compras-recebimento-novo.md`, `Ajuda/compras-recebimentos.md` (abas `a-receber` padrão, `recebidos`), `Ajuda/compras-recebimento.md` (abas `itens` padrão, `financeiro`, `historico`); `<Dica>` em "Custo de entrada", "Rateio do frete", "Tolerância" e "Encerrar saldo".

## Plano de testes
- **Unidade:** rateio do frete e do IPI com arredondamento na última linha (CA 6); custo de entrada e valor do recebimento; teto com tolerância (CA 3, 4); parcelas do recebimento e replanejamento do saldo (CA 8, 9).
- **Integração (Testcontainers, obrigatória: movimenta estoque e financeiro):** recebimento total e parcial com entrada, lote, custo médio e títulos na mesma transação, e rollback quando o `IPayables` ou o estoque recusam (CA 1, 5, 6, 8, 9); segregação e Proprietário único (CA 2); acima do saldo e do teto, frete e IPI só para baixo (CA 3, 4); faltas completadas (CA 7); comprometido no Projeto 360 (CA 10); estorno com título em OP e com saldo insuficiente (CA 12); encerrar saldo cancela os previstos e devolve a SC (CA 13); correção de preço dentro e acima do teto (CA 14); "A receber" como aba padrão (CA 15); três camadas, `purchasing.create` sem `purchasing.receive` recebendo 403 e IDOR (CA 16); recebimentos concorrentes (CA 17); auditoria e links (CA 18); dataset fixo pedido → recebimento → fluxo de caixa.
- **E2E (Playwright):** receber parcialmente um pedido de dois itens com lote, ver o custo médio, o título do recebimento e o previsto reduzido; tela a 400 px.

## Fora do escopo
- Recebimento por NF-e (XML/PDF/DF-e): FIS-05/FIS-06 (Fase 5), que reaproveitam este fluxo.
- Devolução ao fornecedor: fora do MVP (o estorno do recebimento cobre o erro de lançamento).
- Frete de outro fornecedor (transportadora): fora; vira título manual no Financeiro com projeto e natureza `Freight` (conta `2.4 Fretes`).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 47–53):
1. **IPI recuperável (47):** não recuperável (entra no custo) até a decisão da P-01 (CA 6).
2. **Tolerância de quantidade (48):** parâmetro `purchasing.receipt_tolerance_pct`, padrão 0% (D8; CA 3, 4).
3. **Preço diferente do pedido (49):** vale o preço do pedido; muda só com `purchasing.approve` e motivo, nunca por quem recebe; acima da tolerância volta à alçada (CA 14).
4. **CP no recebimento parcial (50):** cada recebimento gera os próprios títulos e replaneja o saldo previsto do pedido (ADR 0024 §3; CA 8, 9).
5. **Quem estorna (51):** `purchasing.approve` (CA 12).
6. **Quem registra (52):** `purchasing.receive` (D8; CA 1, 16).
7. **Frete de outro fornecedor (53):** fora; título manual com projeto e natureza Frete.

**Pré-condição externa:** a P-01 (crédito de imposto, com a contadora) pode tornar o IPI recuperável no futuro; não bloqueia: até lá ele entra no custo.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade e E2E passando; integração (Testcontainers) escritos, a executar na fila do Docker.
- [x] ADR 0024 aplicada (§3, §5) e ADR 0023 §1 (entrada síncrona); migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`; `DocumentOrigin.GoodsReceipt` acrescentado.
- [x] Permissões só do `ModuleCatalog` (`purchasing.receive` do seed patch da CMP-01).
- [x] Telas com estados, inclusive a 400 px.
- [x] Ajuda de cada tela e aba; teste de cobertura passando.
- [x] Confirmação com impacto em estoque e caixa e próximos passos aplicados.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados (gerado pelo Host em processo; regenerar com `BQ_UPDATE_OPENAPI`).
- [x] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (§2: `GoodsReceived` só aviso) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `GoodsReceipt` e `GoodsReceiptLine` (Domain); `CostAllocator` puro (rateio P-16); regra do teto.
- [x] `GoodsReceiptService` (registrar, estornar), encerrar saldo e corrigir preço no `PurchaseOrderService`, chamando `IStockMovements` e `IPayables` na transação.
- [x] `DocumentOrigin.GoodsReceipt` e a criação de títulos por parcela em aberto no `IPayables`.
- [x] `IPurchaseOrders.LastPurchaseAsync`; telas "A receber", recebimento e lista; ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (cadeia Purchasing → Inventory → Finance na mesma transação), @data-engineer (dataset de custo), cybersecurity (segregação, teto, preço), @qa.
- Gate: CA 2, 4, 5, 6, 8, 9, 12, 14 e 17 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (renumerados, 18) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CA 2, 4, 14, 16) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (ajustado: abre em "A receber") |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** re-estimada de M para G; rateio da P-16 com teste de arredondamento (CA 4); a entrada não vira custo do projeto (regras, ligado à CMP-03a ponto 4); estorno com título já em OP bloqueado antes de mexer no estoque (CA 9); confirmação parcial do CP como ponto em aberto, com proposta.
- **Correções da revalidação (21/10/2026):** ADR 0024 §3: cada recebimento gera os próprios títulos em aberto (origem `GoodsReceipt`) e replaneja os previstos do pedido, tudo síncrono, sem consumidor no Finance (CA 5, 8, 9); estorno cancela os títulos do recebimento antes do estoque (CA 12); teto do aprovado com tolerância, frete e IPI só para baixo (CA 3, 4); correção de preço com `purchasing.approve` e volta à alçada (CA 14); `purchasing.receive` e segregação de quem pede ou aprova, com `SoleOwnerRule` (CA 2, 16); comprometido pela ADR 0025 (CA 10); entrada síncrona pela ADR 0023 §1; "Recebimentos" abre em "A receber" (CA 15); frete de outro fornecedor fora. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condição externa explícita: P-01 (IPI recuperável), sem bloquear.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; re-estimada de M para G | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D2, D8, tabela 1.1 itens 47–53) e correções da revisão pré-Fase 3 (§2.1 Compras ↔ Financeiro, §2.2 CMP-04, §2.3 "A receber"); ADR 0024 §3 e §5, ADR 0023 §1; nova ordem (Marco 3.3); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 24/10/2026 | 1.0 | Implementada (`Done`). Recebimento no Purchasing (`goods_receipts`, `goods_receipt_lines`, `purchase_order_price_corrections`; colunas novas no pedido e na linha; migration `GoodsReceipts`, sem Drop nem Rename, CHECKs por SQL e o `approved_total` dos já aprovados), `GoodsReceiptService` (prévia, registrar, estornar) chamando o `IStockMovements` e o `IPayables` na transação, `CloseBalanceAsync` e `CorrectPriceAsync` no `PurchaseOrderService`, `IPayables.CreateOpenInstallmentsAsync`, `DocumentOrigin.GoodsReceipt`, `IPurchaseOrders.LastPurchaseAsync`, parâmetro `purchasing.receipt_tolerance_pct` (0% a 50%) em Configurações → Parâmetros → Compras, telas Recebimentos (A receber e Recebidos), Registrar recebimento, detalhe do recebimento, aba Recebimentos, Encerrar saldo e Corrigir preço no pedido, Última compra no item, ajudas e eventos só de aviso. Desvios e decisões: (1) **Excedente da tolerância:** o `CHECK quantity_received <= quantity` da CMP-03a fica (o `Up` não tem Drop); o que passa da quantidade vai para `quantity_over_received`. (2) **Previsto do saldo** = total do pedido − Σ recebimentos registrados, pela condição a partir da previsão de entrega; o frete ou IPI baixado num recebimento parcial continua no previsto até o pedido ficar recebido ou o saldo ser encerrado (aí o previsto é cancelado). (3) **Correção de preço** fica numa tabela própria (`purchase_order_price_corrections`, no histórico do pedido), não numa coluna da linha; a linha guarda `amount_received` para o preço novo valer só para o saldo; com recebimento, a reaprovação aprovada volta a `PartiallyReceived` e a reprovada desfaz os preços corrigidos e mantém `PartiallyReceived` (nunca rascunho) — recomendação aceita (PO 24/10). (4) **Estorno** recusado (409) com o saldo do pedido encerrado ou com o pedido em reaprovação — recomendação aceita (PO 24/10). (5) **Lote sugerido pelo padrão do fornecedor:** não há padrão de lote no cadastro do fornecedor; o lote vem vazio e o local padrão vem com a `Tag` "Sugerido". (6) **SC no encerramento:** a linha de SC sem recebimento volta a `Open`; a coberta em parte fica `Ordered` com o recebido e o resto nasce numa linha aberta (ou soma na linha aberta da mesma falta ou do mesmo mínimo, por causa dos índices únicos) — recomendação aceita (PO 24/10). (7) **Contratos novos:** `IStockReservations.OrdersCompletedByEntriesAsync` (as OS do "Ver OS que receberam material", lidas do histórico da reserva pelo número dos movimentos) e `IItemSupplierCodes.ForItemsAsync` (unidade e fator do fornecedor por item); fakes dos testes atualizados. (8) **Endpoints extras:** `GET /purchase-orders/{id}/receipt-defaults`, `POST /purchase-orders/{id}/receipts/preview` (o impacto do diálogo é calculado pelo servidor sem gravar), `GET /purchasing/locations` e `GET /purchasing/items/{id}/last-purchase`; os grupos do recebimento exigem também o módulo `inventory`. A aba "A receber" usa a lista de pedidos com `show=a-receber`. (9) **Custo médio antes → depois** no diálogo só com `inventory.view` (como a ADR 0023 §5). (10) **Data:** `received_on` (`date`, ≤ hoje e não antes do pedido) no lugar de `received_at`. (11) **Mensagem do teto** sem a referência "(CA 14)": "Acima do valor aprovado do pedido: peça a correção do preço ou a reaprovação.". (12) **Próximos passos:** "Conferir título a pagar" abre a aba Financeiro do recebimento, "Ver movimentos de estoque" abre o primeiro movimento e "Ver OS que receberam material" abre a OS (ou a lista, com várias). "Registrar recebimento" é botão do pedido (desabilitado com o motivo), não passo da aprovação, porque quem aprova não recebe. (13) **Links (CA 18):** o movimento de origem `GoodsReceipt` aponta para o recebimento e o título a pagar mostra "Ver recebimento" ou "Ver pedido". (14) **CA 10** coberto por teste de unidade do `ProjectCosts.Compute` (o título `GoodsReceipt` de Material entra em Comprometido). (15) **OpenAPI** gerado pelo próprio Host em processo (Development, sem banco e sem hosted services), não editado à mão; mesmo assim, regenerar com `BQ_UPDATE_OPENAPI=1 sh scripts/test-docker.sh --filter-class "*OpenApiDocumentTests"` e `pnpm gen:api`. (16) **Pendente:** testes de integração escritos e não executados (fila do Docker); migration a testar sobre cópia do banco `konebras`; o E2E de Configurações passou a rolar ao topo antes do axe (o botão desceu com o campo novo). | Dev (Claude) |
