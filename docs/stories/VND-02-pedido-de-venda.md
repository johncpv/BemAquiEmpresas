# VND-02 — Pedido de venda: estados, reserva de estoque, cobrança Pix/boleto pelo Inter, conversão em venda e NF-e

| Campo | Valor |
|---|---|
| ID | VND-02 |
| Título | Pedido de venda no módulo `sales`: rascunho, confirmação com reserva de estoque, cobrança por boleto com Pix do Inter (bolepix existente), baixa pelo webhook, conversão na venda da VND-01, NF-e modelo 55, separação, envio, entrega e cancelamento, com eventos para as integrações |
| Tamanho | G (proposta de quebra em VND-02a e VND-02b, seção "Quebra proposta") |
| Status | Draft (26/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §1 e §3 · [Plano de integrações](../integracoes/00-plano-integracoes.md) §3.3 e §6 (F1) · [VND-01](../PDV/stories/VND-01-nucleo-vendas-sem-caixa.md) · [BNK-01b](BNK-01b-bolepix-emissao-cancelamento.md) · [BNK-02](BNK-02-webhook-pagamento-baixa.md) · FIS-03a/03b · ADR 0023 (motor de estoque) · ADR 0046 (exceção manual) |
| Dependências | VND-01 (Done), CAT-01, BNK-01b e BNK-02 (Done), FIS-03a (Done) |
| Bloqueia | INT-03, MKT-00, ATD-03; absorve o núcleo do ECM-04 e a reserva do ECM-02 (seção "Relação com o ECM-02, ECM-03 e ECM-04") |
| Ordem | 1 (F1 — Base no ERP) |

## User story
Como **dono ou vendedor**, quero **registrar um pedido (pelo telefone, pelo Conecta, por um marketplace ou por outro sistema), segurar o estoque, cobrar por Pix ou boleto e acompanhar até a entrega**, para **não vender a mesma peça duas vezes, saber o que já foi pago e ver custo, margem e caixa do pedido como de qualquer outra venda**.

## Contexto
- A VND-01 registra a **venda concluída** (`ISaleIngestion`, `IOrderPayments`, `SaleCompleted`). Não existe pedido em aberto: aguardando pagamento, separação, envio. O plano de integrações (§2) e a ADR 0052 fazem do pedido de venda a base comum do Conecta, dos marketplaces e da API.
- **O pedido não duplica a venda.** Enquanto não é pago, ele é só pedido (estoque reservado, nenhum efeito financeiro). Quando é pago, vira uma venda da VND-01 pelo mesmo caminho de sempre: a venda baixa o estoque (consumindo a reserva), gera a receita e o Fiscal reage.
- **Cobrança:** o boleto com Pix do Inter já existe, mas nasce de um **título a receber** (BNK-01b). O pedido ganha um título próprio, contra o cliente, e o bolepix sai dele. O Pix é o copia-e-cola e o QR do próprio bolepix (pago na hora); o Pix imediato (`cob`) não existe hoje e fica fora (seção "Fora de escopo").
- **Fábrica:** o `sales` fica fora do perfil `factory` (VND-01) e é ligado por empresa no painel de plataforma, por exceção manual (ADR 0046), quando a fábrica vende peça de prateleira (ADR 0052 §3). Nada é criado só para a fábrica. O **pedido de orçamento** da fábrica pelo Conecta **não é desta story** (ATD-03, rascunho no Comercial).

## Critérios de aceite

### Pedido, origem e idempotência
1. **Dado** `sales.create`, **quando** crio um pedido (`POST /api/v1/sales/orders`) com cliente (terceiro com papel `Customer`), linhas (produto vendável do catálogo, quantidade, desconto) e origem, **então** ele nasce `Draft` com número `PV-2026-0001` (serviço de numeração, tipo `sales_order`), o preço de cada linha vem da tabela padrão (`ISellableProducts`), e o total é calculado no servidor (`decimal(18,2)`; quantidade `decimal(18,4)`).
2. **Dado** a origem do pedido, **quando** é gravada, **então** é uma de `Backoffice`, `Conecta`, `Marketplace` ou `Api` (`Storefront` entra com a Fase 14), com `source_app_id` (aplicativo conectado da INT-01, quando houver) e `external_id` (o id no sistema de origem); `(origin, source_app_id, external_id)` é único: o mesmo pedido externo nunca entra duas vezes.
3. **[Segurança]** **Dado** um `POST` sem `Idempotency-Key`, **quando** chega, **então** 400 `idempotency_key_required`; a mesma chave com o mesmo corpo devolve o pedido original sem gravar de novo; com outro corpo, 422 (filtro `IdempotencyFilter`, FND-09e).
4. **Dado** um pedido de origem `Conecta` ou `Api` criado por aplicativo, **quando** é gravado, **então** fica `Draft` com o rótulo "Aguardando confirmação do cliente" e **não reserva estoque nem gera cobrança** sozinho (a IA sugere, não decide, plano §3.3).
5. **[Segurança]** **Dado** um aplicativo conectado criando ou editando um pedido, **quando** manda preço unitário, **então** é ignorado: o preço é sempre o da tabela; desconto acima do limite (CA 7) é recusado com 422 `discount_above_limit`.

### Estados e transições
6. **Dado** a máquina de estados abaixo, **quando** uma transição não listada é pedida, **então** 409 `invalid_order_transition` com a mensagem "Este pedido está {estado} e não pode {ação}." e nada muda; toda transição grava quem, quando, de onde (origem) e o motivo, sobre o `xmin` do pedido (duas ações juntas: só uma passa, a outra 409 com "Recarregar").

```
Draft (Rascunho | Aguardando confirmação do cliente)
  ──Confirmar pedido (reserva tudo ou nada)──> Open (Aberto)
Open ──Gerar cobrança──> AwaitingPayment (Aguardando pagamento)
Open | AwaitingPayment ──Registrar pagamento (manual, sales.approve) | baixa do título (BNK-02)──> Paid (Pago) ──> vira venda (VND-01)
AwaitingPayment ──cobrança cancelada ou expirada, sem pagamento──> Open
Paid ──Separar──> Picked (Separado)
Picked ──NF-e autorizada (InvoiceAuthorized com SaleId)──> Invoiced (Faturado)
Invoiced ──NF-e cancelada (InvoiceCancelled com SaleId)──> Picked
Invoiced ──Informar envio──> Shipped (Enviado) ──Confirmar entrega──> Delivered (Entregue)
Invoiced ──Entregar no balcão (retirada)──> Delivered
Draft | Open | AwaitingPayment ──Cancelar pedido | prazo vencido──> Cancelled
Paid | Picked ──Cancelar pedido (sales.approve, "Devolvi o valor ao cliente")──> Cancelled (estorna a venda)
```

7. **Dado** um desconto de linha ou do pedido acima de `sales.order_discount_limit_pct` (parâmetro novo, padrão 10%), **quando** confirmo, **então** só quem tem `sales.approve` confirma; os demais veem "Desconto acima do limite: só quem aprova em Vendas pode confirmar." e a API responde 403 `approval_required`.
8. **Dado** um pedido `Draft` ou `Open`, **quando** edito linhas ou cliente (`sales.edit`), **então** o total é recalculado; no `Open`, a reserva acompanha (sobe tudo ou nada, desce liberando); em `AwaitingPayment` em diante, linhas e cliente ficam travados ("Cancele a cobrança para alterar o pedido.").

### Reserva de estoque
9. **Dado** "Confirmar pedido", **quando** o servidor processa, **então** reserva cada linha no local de estoque do pedido (padrão: o local padrão da loja), **tudo ou nada**, na mesma transação, e o disponível (físico − reservado) do balcão, da API e dos canais passa a descontar a reserva; faltando saldo, 409 `insufficient_stock` com "Só há {n} {unidade} disponíveis de {produto}." por linha e nada é reservado.
10. **Dado** dois pedidos disputando a última unidade, **quando** confirmam ao mesmo tempo, **então** só um reserva (teste de concorrência, trava por item em ordem crescente de `item_id`, ADR 0023).
11. **Dado** um pedido `Open` sem cobrança há mais de `sales.order_hold_hours` (parâmetro novo, padrão 48 h) ou `AwaitingPayment` com a cobrança vencida (vencimento + 1 dia útil de compensação), **quando** o job `sales-order-expiry` roda (a cada 15 min, `TenantJob` com `tenantId`), **então** o pedido fica `Cancelled` com o motivo "Prazo de pagamento vencido", a reserva é liberada e o título do pedido é cancelado (o que cancela o boleto no Inter, BNK-01b CA 17); o vendedor recebe o aviso com "Recriar pedido".
12. **Dado** o PDV vendendo a mercadoria que está na prateleira, **quando** o disponível já está reservado para um pedido, **então** o caixa não é barrado (regra do ECM-02 e da PP-02); a falta resultante segue a PDV-05 e o pedido mostra "Reserva com falta" na separação.

### Cobrança (boleto com Pix do Inter)
13. **Dado** um pedido `Open` de cliente com CPF/CNPJ e endereço de cobrança completos, e a integração Inter ativa, **quando** aciono "Gerar cobrança" (`sales.edit`) e confirmo no diálogo (valor = total do pedido, vencimento sugerido `sales.order_charge_days`, padrão 1 dia útil, e o e-mail que vai receber), **então** o Finance cria **um título a receber de origem `SalesOrder`** (nova) contra o cliente, com o total do pedido, e pede o bolepix por ele, pelo fluxo da BNK-01b (`Requested → Issued`, multa e juros de Configurações → Cobrança, sem desconto editável); o pedido passa a `AwaitingPayment` e mostra linha digitável, Pix copia-e-cola, QR e "Baixar PDF" quando o boleto fica `Issued`.
14. **Dado** cliente sem documento ou sem endereço de cobrança, **quando** abro "Gerar cobrança", **então** as pré-condições aparecem antes de confirmar, com "Completar cadastro do cliente", e a ação fica desabilitada com o motivo (regra do banco, BNK-01b CA 10); "Registrar pagamento" continua disponível.
15. **Dado** o boleto pago (webhook com reconsulta ou conferência diária, BNK-02), **quando** o título do pedido é baixado e o `ReceivableSettled` chega ao Sales, **então** o pedido passa a `Paid` uma vez só (consumidor idempotente) e publica `OrderPaid`; pagamento com diferença segue a conferência humana da BNK-02 (`PaidPendingReview`) e o pedido fica `AwaitingPayment` com o chip "Pagamento em conferência".
16. **Dado** o pagamento de um pedido já `Cancelled` (boleto pago durante o cancelamento), **quando** a baixa chega, **então** o pedido não muda sozinho: ganha o alerta "Pago depois de cancelado" para quem tem `sales.approve`, com "Reabrir pedido" (reserva de novo; sem saldo, recusa) ou "Devolver o valor" (registro do estorno feito fora); nada é estornado sozinho.
17. **Dado** "Registrar pagamento" (`sales.approve`, para dinheiro, maquininha ou Pix recebido fora), **quando** confirmo forma e valor iguais ao saldo do pedido, **então** o pedido passa a `Paid` pelo `IOrderPayments` com o `ManualPaymentProvider` (capturado, atestado por quem registrou); se havia título e boleto do pedido, o boleto é cancelado e o título também (a receita vem da venda, CA 19).

### Conversão em venda (sem duplicar a VND-01)
18. **Dado** um pedido que chega a `Paid`, **quando** o Sales processa, **então** grava a venda pelo mesmo serviço da `ISaleIngestion` (na mesma transação): `SaleId` = o reservado na confirmação, canal `Online`, `SourceType = "SalesOrder"`, `SourceId` = o pedido, as mesmas linhas, o cliente e o pagamento capturado; o `SaleCompleted` vai ao outbox e o `SaleStockConsumer` baixa o estoque **consumindo a reserva do pedido** (não o disponível), com custo médio na linha.
19. **[Segurança]** **Dado** a venda vinda de um pedido pago por boleto, **quando** o consumidor de recebíveis da VND-01 recompõe o dia, **então** o pagamento ligado ao título do pedido (`ReceivableId` no pagamento, campo novo) **não gera outro recebível**, e o DRE conta a receita uma vez só: pelo `ISalesRevenue`, com o título `SalesOrder` fora da receita por título (testes de integração dos dois lados).
20. **Dado** o mesmo `ReceivableSettled` entregue duas vezes, **quando** processo, **então** há uma venda só (idempotência pelo `SaleId`, VND-01 CA 4).

### NF-e modelo 55
21. **Dado** o `OrderPaid` e o módulo `fiscal` liberado, **quando** o Fiscal reage, **então** cria o **rascunho** da NF-e a partir da venda (`ISaleLookup` e `IParties.FiscalInfoAsync`), com o `Idempotency-Key` `nfe-sale-{saleId}` e índice único parcial de nota ativa por venda; a emissão **sempre pede confirmação** (o sistema sugere, não decide): "Emitir nota" no pedido `Picked` (`fiscal.create`), pelo fluxo `Draft → Processing → Authorized` da FIS-03a, com as mesmas pré-condições (regra fiscal por item, sem DIFAL, cliente com documento e IBGE).
22. **Dado** a nota autorizada, **quando** o `InvoiceAuthorized` (com `SaleId`, campo novo) chega, **então** o pedido passa a `Invoiced`, publica `OrderInvoiced` e mostra número, chave e "Baixar DANFE"; rejeitada, o pedido fica `Picked` com o chip "Nota rejeitada", o motivo e "Corrigir nota".
23. **[Segurança]** **Dado** a NF-e de uma venda, **quando** o DRE e o faturamento são montados, **então** ela fica fora da receita por nota do `IOutgoingInvoices.RevenueAsync` (a receita da venda já vem do `ISalesRevenue`) e não baixa estoque (a baixa foi na venda).

### Separação, envio, entrega e cancelamento
24. **Dado** um pedido `Paid`, **quando** aciono "Separar" (`sales.edit`), **então** vejo a lista de separação (produto, variação, quantidade, local), posso imprimi-la (QuestPDF) e confirmo "Separado"; o pedido passa a `Picked`.
25. **Dado** um pedido `Invoiced`, **quando** aciono "Informar envio" com a forma (entrega própria, transportadora, correio) e o código de rastreio opcional, **então** passa a `Shipped` e publica `OrderShipped`; "Confirmar entrega" leva a `Delivered` e publica `OrderDelivered`; "Entregar no balcão" leva direto de `Invoiced` a `Delivered`.
26. **Dado** "Cancelar pedido" em `Draft`, `Open` ou `AwaitingPayment` (`sales.edit`, motivo obrigatório), **quando** confirmo, **então** a reserva é liberada, o título e o boleto do pedido são cancelados e `OrderCancelled` é publicado.
27. **[Segurança]** **Dado** "Cancelar pedido" em `Paid` ou `Picked`, **quando** confirmo, **então** só com `sales.approve` e a confirmação "Devolvi o valor ao cliente" (estorno fora, como na VND-01); a venda é cancelada pelo `ISaleIngestion.CancelAsync` (`SaleCancelled` estorna a baixa e refaz o recebível) e o pedido fica `Cancelled`. Em `Invoiced` em diante, o cancelamento é recusado com "Cancele a NF-e {número} antes." (FIS-03b); depois da entrega, é devolução (fora de escopo).

### Eventos, acesso e auditoria
28. **Dado** cada evento do pedido (`OrderCreated`, `OrderPaid`, `OrderInvoiced`, `OrderShipped`, `OrderDelivered`, `OrderCancelled`), **quando** acontece, **então** vai ao outbox na mesma transação, **sem dado pessoal** (ids, número, origem, `external_id`, estado, total e data); o consumidor relê pelo `ISalesOrderLookup` (novo). A INT-02 entrega esses eventos aos aplicativos inscritos.
29. **[Segurança]** **Dado** o módulo `sales` não liberado (ex.: perfil `factory` sem a exceção manual), **quando** chamo qualquer rota do pedido, **então** 403 `module_not_released` e a tela `ModuleNotReleased`; sem grupo, 403 `forbidden`; IDOR: pedido de outro tenant ou id inexistente responde 404 igual.
30. **Dado** qualquer transição, mudança de linha, desconto aprovado, cobrança, pagamento registrado ou cancelamento, **quando** abro a trilha do pedido, **então** vejo o que houve, quem (ou "Integração {app} (autorizada por {pessoa})"), quando, antes e depois (`IAuditable`).

## Regras de negócio e técnicas
- **Pedido é documento comercial:** nunca é apagado; só cancelado. Depois de `Paid`, o documento com movimento é a venda (VND-01).
- **Preço congelado na confirmação:** o preço da linha é lido da tabela na criação e relido na confirmação; mudou, o diálogo mostra o novo e pede confirmação (padrão do ECM-02 CA 5). A versão da tabela fica na linha (`price_list_version`).
- **Reserva genérica no Estoque (nova):** hoje `inventory.reservations` é só de OS (`production_order_id`, EST-02). Ganha `source_type`/`source_id` (com `production_order_id` anulável) e `expires_at`, e o contrato novo `ISalesReservations` (`ReserveAsync` tudo ou nada, `ReleaseAsync`, idempotentes, na transação de quem chama). A baixa consome a reserva por um campo novo opcional no `StockPosting` (`ReservationSourceType`/`ReservationSourceId`): o motor desce `quantity_reserved` junto com o físico. O ECM-02 (carrinho) reusa o mesmo contrato.
- **Título `SalesOrder`:** `DocumentOrigin.SalesOrder` (novo) no SharedKernel; contrato novo `IOrderCharges` em `Finance.Contracts` (`CreateAsync(orderId, partyId, amount, dueDate)`, `IssueSlipAsync`, `CancelAsync`, `ChargeAsync` para ler), que reusa o `BillingSlipService` por dentro. Um título por pedido (`origin_id` = pedido, índice único parcial de título ativo por pedido). Fora da régua de cobrança e da receita por título.
- **Forma de pagamento do boleto:** parâmetro novo `sales.order_bank_slip_payment_method_id` (a forma "Boleto/Pix Inter" do PAG-01); sem ele configurado, "Gerar cobrança" mostra "Configure a forma de pagamento do boleto em Configurações → Parâmetros." (ver Pontos em aberto 2).
- **Canal:** a venda do pedido usa o canal `Online` em todas as origens; a origem fica no pedido e na trilha. MKT-00 decide se o marketplace ganha canal próprio para margem por canal.
- **Datas:** transições em `timestamptz` (UTC), exibidas em `America/Sao_Paulo`; vencimento é `date`.

## Permissões, eventos e tabelas
- **Permissões (existentes, nenhuma nova):** `sales.view` (lista, detalhe, trilha), `sales.create` (criar e confirmar), `sales.edit` (editar rascunho e aberto, gerar cobrança, separar, informar envio, entregar, cancelar antes do pagamento), `sales.approve` (desconto acima do limite, registrar pagamento, cancelar depois do pagamento, reabrir pedido pago depois de cancelado), `sales.view_cost` (custo e margem do pedido), `sales.view_customer_document` (documento inteiro do cliente). Emitir a nota: `fiscal.create`; ver o boleto: `finance.view` não é exigido no pedido (o pedido mostra só a própria cobrança).
- **Eventos novos (`Sales.Contracts`):** `OrderCreated`, `OrderPaid`, `OrderInvoiced`, `OrderShipped`, `OrderDelivered`, `OrderCancelled`. **Consumidos:** `ReceivableSettled` (Finance), `InvoiceAuthorized` e `InvoiceCancelled` com `SaleId` novo (Fiscal), `InvoiceRejected`.
- **Contratos novos:** `ISalesOrderLookup` (Sales), `ISalesReservations` (Inventory), `IOrderCharges` (Finance), `IOutgoingInvoices.BySaleAsync` (Fiscal). **Alterados (só aditivos):** `SalePaymentInput.ReceivableId`, `StockPosting.Reservation*`, `InvoiceAuthorized/InvoiceCancelled.SaleId`, `DocumentOrigin.SalesOrder`.
- **Tabelas:** `sales.orders` (`number`, `status`, `origin`, `source_app_id`, `external_id`, `customer_id`, `store_id`, `stock_location_id`, `sale_id`, `receivable_id`, `subtotal`, `discount_total`, `total`, `hold_until`, `cancel_reason`, `paid_after_cancel`, `shipping_method`, `tracking_code`, `xmin`), `sales.order_lines`, `sales.order_transitions`; `inventory.reservations` (colunas novas); `fiscal.outgoing_invoices.sale_id`; `finance.parameters`/`sales` parâmetros novos. Migration só aditiva.
- **Jobs:** `sales-order-expiry` (15 min, por tenant).
- **Auditoria:** pedido e linhas `IAuditable`; desconto aprovado, pagamento registrado e cancelamento com motivo.

## Telas e estados
- **Loja → Pedidos** (`/app/loja/pedidos`): lista com filtros por estado (chips com contagem), origem, cliente, período e "Parados há mais de {n} dias"; colunas número, data, cliente, origem, total, estado (chip semântico) e há quanto tempo no estado; atrasado com faixa lateral âmbar ou vermelha. Vazio: "Nenhum pedido ainda." com "Novo pedido". Carregando (esqueleto) e erro ("Não foi possível carregar os pedidos." com "Tentar de novo").
- **Novo pedido** (`/app/loja/pedidos/novo`): cliente (busca com "Cadastrar cliente"), linhas pela consulta rápida do catálogo (GTIN, nome), desconto, total; já sugere o local de estoque padrão e mostra o disponível de cada linha.
- **Pedido** (`/app/loja/pedidos/$id`): cabeçalho com o indicador de etapa (display de andar) e o **próximo passo em destaque** ("Confirmar pedido", "Gerar cobrança", "Separar", "Emitir nota", "Informar envio", "Confirmar entrega"); abas **Itens**, **Cobrança** (boleto, linha digitável e Pix com "Copiar", estado do pagamento), **Nota** (situação, DANFE), **Entrega** e **Trilha**. Link para a venda depois de `Paid`.
- **Verbos e confirmação:** "Confirmar pedido" / "Pedido confirmado"; "Gerar cobrança" / "Cobrança gerada"; "Registrar pagamento" / "Pagamento registrado"; "Separar" / "Separado"; "Emitir nota" / "Nota enviada"; "Informar envio" / "Envio informado"; "Confirmar entrega" / "Entrega confirmada"; "Cancelar pedido" / "Pedido cancelado" (`ConfirmDialog` `destructive`, com o impacto: reserva liberada, boleto cancelado, venda estornada).
- **Propositivo:** depois de confirmar, oferece "Gerar cobrança"; depois do pagamento, "Separar"; depois de separar, "Emitir nota" com o rascunho pronto; card no Início "Pedidos aguardando ação".
- **Configurações → Parâmetros**, seção "Pedidos de venda": limite de desconto, horas de reserva sem cobrança, dias do vencimento da cobrança e a forma de pagamento do boleto.
- **Ajuda (novas):** `Ajuda/loja-pedidos.md`, `Ajuda/loja-pedido-novo.md`, `Ajuda/loja-pedido.md` (abas `itens`, `cobranca`, `nota`, `entrega`, `trilha` em arquivos próprios, ex.: `Ajuda/loja-pedido-cobranca.md`); atualizar `Ajuda/financeiro-receber-vendas.md` (título de origem "Pedido de venda"). `<Dica>` em "Reservado", "Aguardando confirmação do cliente" e "Pago depois de cancelado".

## Quebra proposta (se o G não couber)
- **VND-02a — Núcleo do pedido (G−):** CA 1 a 12, 17 a 20, 24 a 30 sem a nota (`Picked → Shipped` só depois da 02b): modelo, estados, reserva genérica no Estoque, pagamento manual, conversão em venda, eventos, telas e ajuda.
- **VND-02b — Cobrança Inter e NF-e (M):** CA 13 a 16, 21 a 23 e a volta `Invoiced → Picked`: título `SalesOrder`, `IOrderCharges`, bolepix, baixa pelo webhook, pago depois de cancelado, rascunho e emissão da NF-e pela venda e a exclusão da receita por nota.
- A F1 só vai à produção com as duas (o `sales` segue desligado para a Konebras até a exceção manual).

## Relação com o ECM-02, ECM-03 e ECM-04
- **ECM-04** passa a reusar este pedido: a tela "Pedidos online" vira o filtro de origem `Storefront` da lista de pedidos, e o ECM-04 fica com o que é só da loja virtual (frete e etiqueta, falta na separação com aviso ao comprador, página pública do pedido, e-mail por estado). Os estados `Paid → Picking → Invoiced → Shipped → Delivered` do `storefront.online_orders` (03-modelo-dados do PDV, §11) são os deste pedido; propõe-se substituir `online_orders` por `sales.orders` com a origem `Storefront` quando a Fase 14 abrir.
- **ECM-02** reusa a reserva genérica (`ISalesReservations`) com validade curta do carrinho.
- **ECM-03** (Pix dinâmico e cartão por gateway) continua valendo para a loja virtual; o pedido aceita outras formas pelo `IOrderPayments` quando ela vier.

## Tarefas
- [ ] Estoque: colunas novas em `inventory.reservations`, `ISalesReservations` e consumo da reserva no `StockPosting`; teste de concorrência na última unidade.
- [ ] Sales Domain: `SalesOrder`, `SalesOrderLine`, máquina de estados com teste de unidade de todas as transições (válidas e recusadas).
- [ ] Sales Application: `SalesOrderService` (criar com idempotência, confirmar, editar, cancelar, separar, envio, entrega, registrar pagamento), conversão em venda pelo serviço da ingestão, `ISalesOrderLookup`, consumidores de `ReceivableSettled`, `InvoiceAuthorized`, `InvoiceCancelled`, `InvoiceRejected`; job `sales-order-expiry`.
- [ ] Finance: `DocumentOrigin.SalesOrder`, `IOrderCharges` sobre o `BillingSlipService`; `SaleReceivablesConsumer` ignorando pagamento com `ReceivableId`; receita por título sem `SalesOrder`.
- [ ] Fiscal: rascunho da NF-e a partir da venda (consumidor de `OrderPaid`), `sale_id` na nota, `SaleId` nos eventos, `BySaleAsync`, fora da receita por nota.
- [ ] Endpoints `/api/v1/sales/orders` (+ `/{id}/confirm`, `/charge`, `/payments`, `/pick`, `/ship`, `/deliver`, `/cancel`, `/reopen`, `/picking-list.pdf`), com `RequireModule` e `RequirePermission`; OpenAPI e Orval.
- [ ] Front: lista, novo pedido, detalhe com abas, diálogos, card do Início, parâmetros; Vitest dos estados e dos diálogos.
- [ ] Ajudas e `docs/04-fluxos-estados.md` (máquina do pedido), `docs/PDV/03-modelo-dados.md` (`sales.orders`) e backlog.
- [ ] Integração (Testcontainers + fake do Inter + emissor falso): ponta a ponta `Draft → Delivered`; expiração; pago depois de cancelado; cancelamento depois do pagamento; recebível e DRE sem duplicar (CA 19, 23); idempotência (CA 3, 20); seed patch nenhum.

## Dev Notes
- **Venda:** `src/Modules/Sales/BemAqui.Sales.Contracts/SalesContracts.cs` (`ISaleIngestion`, `IOrderPayments`, `OrderSale`, `SalePaymentInput`, `SaleCompleted`, `SaleCancelled`, `ISaleLookup`); `src/Modules/Sales/BemAqui.Sales.Application/SaleService.cs` e `SaleService.Orders.cs` (o `PayAsync` já cria a venda no primeiro pagamento), `PaymentProviders.cs` (`ManualPaymentProvider`), `SaleStockConsumer.cs`; rotas em `src/Modules/Sales/BemAqui.Sales.Endpoints/SalesEndpoints.cs` (grupo `/sales`, `RequireModule`, `RequirePermission`).
- **Catálogo:** `src/Modules/Catalog/BemAqui.Catalog.Contracts/CatalogContracts.cs` (`ISellableProducts`, `ICatalogLookup`, `IPriceLists`).
- **Estoque:** `src/Modules/Inventory/BemAqui.Inventory.Contracts/Stock.cs` (`IStockMovements`, `StockPosting`, `StockSources.Sale`, `IStockBalances`/`StockAvailability` com `Reserved` e `Available`) e `Reservations.cs` (`IStockReservations`, hoje só de OS).
- **Financeiro:** `src/Modules/Finance/BemAqui.Finance.Contracts/ReceivablesContracts.cs` (`ReceivableSettled`), `SaleReceivableContracts.cs` (`ISaleReceivables`); `src/Modules/Finance/BemAqui.Finance.Application/Billing/BillingSlipService.Commands.cs` (`IssueAsync`, `CancelAsync`), `BillingSlipReactions.cs` (cancelamento automático), `BankPaymentService.cs` (`ApplyAsync`, BNK-02); `src/BuildingBlocks/BemAqui.SharedKernel/DocumentOrigin.cs`.
- **Fiscal:** `src/Modules/Fiscal/BemAqui.Fiscal.Contracts/OutgoingInvoiceContracts.cs` (`InvoiceAuthorized`, `InvoiceCancelled`, `InvoiceRejected`, `IOutgoingInvoices`); rascunho hoje nasce do romaneio em `src/Modules/Fiscal/BemAqui.Fiscal.Application/Invoices/OutgoingInvoiceService.cs`.
- **Terceiros:** `src/Modules/Parties/BemAqui.Parties.Contracts/PartiesContracts.cs` (`IParties.FindAsync`, `HasRoleAsync`, `DefaultAddressAsync`, `FiscalInfoAsync`).
- **Base:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Web/IdempotencyFilter.cs`, `Persistence/Numbering`, `Messaging/OutboxEventPublisher.cs`, `Paging/PageRequest.cs`; módulo `sales` em `src/BuildingBlocks/BemAqui.BuildingBlocks/Modules/ModuleCatalog.cs`.
- Ajudas existentes de referência: `Ajuda/loja-vendas.md`, `Ajuda/loja-venda.md`, `Ajuda/loja-venda-trilha.md`.

## Fora de escopo
- Pedido de orçamento da fábrica pelo Conecta (ATD-03, rascunho no Comercial).
- Pix imediato pela API Pix do Inter (`cob`) e cartão por gateway (PP-21, ECM-03).
- Frete calculado, etiqueta de transportadora e Correios (ECM-04, MKT-01 Mercado Envios, Melhor Envio).
- Devolução e troca depois da entrega (PDV-06 no balcão, ECM-05 no online).
- Envio parcial e pedido com várias notas.
- Avisos ao cliente por WhatsApp e e-mail por estado (ATD-05 e ECM-04).

## Pontos em aberto (para o PO)
1. **Emissão automática da NF-e** ao separar (volume de marketplace): a regra do produto exige confirmação para efeito fiscal; propõe-se manter o clique e avaliar no MKT-00.
2. **Forma de pagamento "Boleto/Pix"**: o PAG-01 não tem o tipo boleto (`PaymentMethodKind`: Cash, Pix, Debit, Credit, CreditInstallments, Voucher, StoreCredit, OnAccount). Usar `Pix` ou criar `BankSlip`?
3. **Tenant sem NF-e** (ex.: MEI vendendo a pessoa física): permitir `Picked → Shipped` sem passar por `Invoiced` quando o `fiscal` não está liberado?
4. **Cliente consumidor sem endereço** (comum no Conecta): o bolepix exige documento e endereço; sem eles, só pagamento registrado até existir o Pix imediato.
5. **Menu na fábrica:** com a exceção manual, "Pedidos" aparece na área "Loja" (hoje só do PDV) ou em "Comercial"?
6. **Pendências do ECM-04** (lacuna 25 e dívida E14 do EmissorFiscal): confirmar se já foram resolvidas antes de emitir NF-e de pedido para um tenant novo.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 26/11/2026 | 0.1 | Story escrita a partir da ADR 0052 e do plano de integrações, conferida contra o código (Sales, Inventory, Finance, Fiscal, Catalog, Parties). | Claude (planejamento) |
