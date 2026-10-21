# ADR 0024: Compras e Financeiro

- Status: Aceito (21/10/2026). O PO (John) aprovou a revisão pré-Fase 3 (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): a seção 2.1 "Compras ↔ Financeiro" entra como está, com a decisão D8 (segregação em compras) e os pontos 38 a 53 da tabela 1.1.
- Contexto: a revisão marcou a CMP-03a e a CMP-04 como NO-GO. O `IPayables` de hoje é idempotente por `(origin, originId)` e cria **um** título por origem, mas o pedido de compra tem uma parcela por vencimento da condição de pagamento. O motor de alçadas (`ApprovalResolver`, FIN-03b) é interno do Finance e só conhece a OP. O pedido publicaria `PurchaseOrderApproved` para o Finance criar o previsto, o que deixa a previsão de caixa à mercê da ordem do outbox. E `purchasing.create` cobria pedir e receber, e o recebimento podia subir o valor acima do aprovado.
- Complementa as ADRs 0003 (ação `purchasing.receive`, adendo de 21/10/2026), 0006, 0019 (§1, §6 alçada viável, §8 título a pagar) e 0023 (motor de estoque). O custo do projeto (comprometido × realizado) está na ADR 0025.

## Decisão

### 1. Chamada síncrona
- O Purchasing chama o `IPayables` **na própria transação** do pedido e do recebimento, como a COM-06 faz com o título do instalador. `PurchaseOrderApproved` e `GoodsReceived` continuam publicados, só para aviso e projeção (notificação, fluxo de caixa ao vivo, custo comprometido do projeto).

### 2. `IPayables` por parcela
- `finance.payables` ganha **`installment`** (`smallint`, 1 para os títulos de hoje). O índice único passa a ser **`(origin, origin_id, installment)`**, criado **antes** de remover o índice atual `(origin, origin_id)` (migração não destrutiva, a versão anterior continua funcionando).
- Conta do plano e natureza (`CostType`) viram **parâmetros** da chamada (hoje são fixos por origem). Padrão do pedido: conta 2.1 Materiais, natureza `Material`; frete na conta 2.4 e natureza `Freight` (a confirmar com a contadora, sem bloquear).
- Métodos novos em `Finance.Contracts`:
  - `CreatePlannedInstallmentsAsync(origin, originId, partyId, projectId, accountId, costType, installments[(installment, amount, estimatedDueDate)], description)`: cria os títulos `Planned` com a marca "Estimado"; idempotente por parcela;
  - `ReplacePlannedAsync(origin, originId, installments[…])`: substitui os títulos `Planned` ainda não confirmados da origem (reaprovação, replanejamento do saldo); recusa 409 `payable_in_payment_order` se algum estiver em OP;
  - `CancelAllFromOriginAsync(origin, originId, reason)`: cancela todos os títulos em aberto da origem; 409 se algum estiver em OP ou pago;
  - `CreateOpenInstallmentsAsync(origin, originId, partyId, projectId, accountId, costType, installments[(installment, amount, dueDate)], description)`: cria os títulos já confirmados (`Open`) por parcela, usados pelos títulos do recebimento (§3); idempotente por parcela.
- **Título cancelado não ocupa a chave:** o índice único `(origin, origin_id, installment)` é parcial (`WHERE status <> 'Cancelled'`), para o `ReplacePlannedAsync` recriar os previstos depois do estorno de um recebimento; o reenvio do mesmo comando continua protegido pelo `Idempotency-Key` do documento (ADR 0019 §3).
- Frete e IPI do fornecedor entram nas mesmas parcelas do pedido (o custo já os rateia, P-16); a conta 2.4 e a natureza `Freight` ficam para o título manual de frete de outro fornecedor.
- Os métodos de hoje (`CreateFromOriginAsync`, `ConfirmAsync`, `UpdatePlannedAsync`, `CancelFromOriginAsync`) continuam valendo com `installment = 1`.

### 3. Títulos do recebimento e replanejamento
- **Cada recebimento gera os próprios títulos em aberto** (origem `GoodsReceipt`, um por parcela da condição do pedido, vencimento a partir da data do recebimento), pelo valor recebido (itens + frete e IPI rateados).
- Os títulos previstos do pedido (origem `Purchase`) são **replanejados para o saldo a receber** pelo `ReplacePlannedAsync`; com o pedido totalmente recebido ou com o saldo encerrado, são cancelados.
- O **estorno do recebimento** cancela os títulos dele (`CancelAllFromOriginAsync`; 409 se algum estiver em OP ou pago, com a ação "Ver OP") e devolve o saldo ao previsto do pedido.
- **Teto (D8):** Σ confirmado nos recebimentos ≤ total aprovado do pedido + tolerância (parâmetro `purchasing.receipt_tolerance_pct`, padrão 0%). Frete e IPI só podem baixar no recebimento; qualquer aumento acima do teto volta o pedido à alçada (o recebimento fica pendente até a reaprovação).
- Preço do recebimento = preço do pedido; muda só com `purchasing.approve` e motivo, nunca por quem recebe.

### 4. `IApprovalRouting`
- Contrato novo em `Finance.Contracts`, implementado sobre o `ApprovalResolver` da FIN-03b:
  - `ResolveAsync(documentType, referenceAmount, accountId, requesterId)` devolve os níveis (regra, aprovadores elegíveis, aviso de fracionamento) ou 422 `approval_not_viable` (ADR 0019 §6);
  - `EligibleAsync(documentType, level, ruleId, requesterId)` devolve quem pode decidir agora.
- **Mapa tipo de documento → permissão do aprovador** fica no Finance: `PaymentOrder` → `finance.approve`; `PurchaseOrder` → `purchasing.approve`. Quem tem `finance.approve` sem `purchasing.approve` recebe 403 no pedido (teste).
- **Valor de referência contra fracionamento** vem do chamador: o Purchasing soma os pedidos do mesmo fornecedor nos últimos 30 dias (aprovados ou pendentes) e passa o maior entre esse total e o pedido.
- O **Purchasing guarda os próprios níveis e aprovações** (`purchase_order_levels`, `purchase_order_approvals`), com histórico imutável; o Finance só resolve a regra.
- Regra padrão do tipo `PurchaseOrder` ("Proprietário aprova tudo"), própria, no seed das `approval_rules`.
- **Proprietário único:** o helper comum `SoleOwnerRule` (BuildingBlocks, o mesmo da ADR 0023 §5) decide a autoaprovação, com a marca e a notificação.
- **Reprovação** volta o pedido a rascunho, com o histórico imutável; o reenvio resolve a alçada de novo. Editar pedido aprovado ou enviado volta à aprovação e exige reenvio ao fornecedor.

### 5. Segregação em compras (D8)
- Ação nova **`purchasing.receive`** (ADR 0003, adendo de 21/10/2026). O grupo Almoxarifado do seed recebe `purchasing.view` + `purchasing.receive`, sem `purchasing.edit` (seed patch `purchasing-permissions`).
- **Quem pediu ou aprovou o pedido não registra o recebimento** dele (exceção do Proprietário único, com a marca).
- **Cotação:** sem mínimo de cotações; a aprovação mostra o aviso "1 cotação" quando houver só uma. Acima de **R$ 5.000,00** (parâmetro `purchasing.quote_attachment_amount`), a resposta do fornecedor exige o anexo da proposta (dono `Rfq`). Fica registrado quem lançou cada resposta.

## Consequências
- **CMP-03a** implementa o `IApprovalRouting`, o `installment` e os métodos novos do `IPayables`, com teste de regressão dos títulos de origem já existentes (COM-06, FIN-02) sobre cópia do banco do tenant `konebras`.
- **CMP-04** implementa os títulos do recebimento, o replanejamento, o teto e a segregação.
- **CMP-02** implementa o anexo obrigatório acima de R$ 5.000,00 e o aviso "1 cotação".
- **FIN-05** (fluxo de caixa) passa a ler os previstos por parcela sem mudança de tela.
- `DocumentOrigin` (SharedKernel): o `Purchase` que já existe passa a ser o título previsto do **pedido** e entra o valor novo **`GoodsReceipt`** (títulos do recebimento). O tipo de documento `PurchaseOrder` do motor de alçadas (`approval_rules.document_type`) já existe e não muda.
