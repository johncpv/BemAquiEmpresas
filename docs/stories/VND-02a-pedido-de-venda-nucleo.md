# VND-02a — Pedido de venda (núcleo): rascunho, reserva, cobrança Pix/boleto do Inter, pagamento e conversão em venda

| Campo | Valor |
|---|---|
| ID | VND-02a |
| Título | Pedido de venda no módulo `sales`: rascunho, confirmação com reserva de estoque, cobrança por boleto com Pix do Inter (bolepix existente), pagamento pelo webhook ou registrado à mão, conversão na venda da VND-01, cancelamento e eventos para as integrações |
| Tamanho | G− |
| Status | Done (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §1 e §3 · [ADR 0053](../adr/0053-aplicativos-conectados.md) · [Plano de integrações](../integracoes/00-plano-integracoes.md) §3.3 e §6 (F1) · [VND-01](../PDV/stories/VND-01-nucleo-vendas-sem-caixa.md) · [BNK-01b](BNK-01b-bolepix-emissao-cancelamento.md) · [BNK-02](BNK-02-webhook-pagamento-baixa.md) · ADR 0023 (motor de estoque) · ADR 0046 (exceção manual) |
| Dependências | VND-01 (Done), CAT-01, BNK-01b e BNK-02 (Done) |
| Bloqueia | VND-02b, INT-03, MKT-00, ATD-03; absorve o núcleo do ECM-04 e a reserva do ECM-02 (seção "Relação com o ECM") |
| Ordem | 1 (F1 — Base no ERP) |

## User story
Como **dono ou vendedor**, quero **registrar um pedido (pelo telefone, pelo Conecta, por um marketplace ou por outro sistema), segurar o estoque e cobrar por Pix ou boleto**, para **não vender a mesma peça duas vezes, saber o que já foi pago e ver custo, margem e caixa do pedido como de qualquer outra venda**.

## Contexto
- A VND-01 registra a **venda concluída** (`ISaleIngestion`, `IOrderPayments`, `SaleCompleted`). Não existe pedido em aberto. O plano de integrações (§2) e a ADR 0052 fazem do pedido de venda a base comum do Conecta, dos marketplaces e da API.
- **O pedido não duplica a venda.** Enquanto não é pago, é só pedido (estoque reservado, nenhum efeito financeiro além do título de cobrança). Pago, vira uma venda da VND-01 pelo caminho de sempre: a venda baixa o estoque (consumindo a reserva), gera a receita e o Fiscal reage.
- **Cobrança:** o boleto com Pix do Inter (bolepix) já existe e nasce de um **título a receber** (BNK-01b). O pedido ganha um título próprio, contra o cliente, e o bolepix sai dele. O Pix é o copia-e-cola e o QR do próprio bolepix; o Pix imediato (`cob`) fica fora.
- **Separação, nota, envio e entrega** ficam na [VND-02b](VND-02b-pedido-separacao-nota-envio.md). A F1 só vai à produção com as duas.
- **Fábrica:** o `sales` fica fora do perfil `factory` e é ligado por empresa por exceção manual (ADR 0046) quando a fábrica vende peça de prateleira (ADR 0052 §3). O pedido de orçamento pelo Conecta é a ATD-03.

## Critérios de aceite

### Pedido, origem e idempotência
1. **Dado** `sales.create`, **quando** crio um pedido (`POST /api/v1/sales/orders`) com cliente (terceiro com papel `Customer`), linhas (produto vendável do catálogo, quantidade, desconto), **então** ele nasce `Draft` com número `PV-2026-0001` (serviço de numeração, tipo `sales_order`), o preço de cada linha vem da tabela padrão (`ISellableProducts`), e o total é calculado no servidor (`decimal(18,2)`; quantidade `decimal(18,4)`).
2. **[Segurança]** **Dado** um pedido gravado, **quando** a origem é definida, **então** `origin` (`Backoffice`, `Conecta`, `Marketplace` ou `Api`; `Storefront` na Fase 14), `source_app_id` e `created_by` vêm **só do principal autenticado** (sessão de pessoa: `Backoffice` e a pessoa; token de aplicativo: a origem do aplicativo, a conexão e quem autorizou, ADR 0053), nunca do corpo; `(origin, source_app_id, external_id)` é único: o mesmo `externalId` com o mesmo corpo devolve o pedido existente; com outro corpo, 409 `external_id_conflict` (nunca devolve outro pedido).
3. **[Segurança]** **Dado** um `POST` sem `Idempotency-Key`, **quando** chega, **então** 400 `idempotency_key_required`; para pessoa vale o `IdempotencyFilter` de hoje (mesma chave e corpo devolvem o original; outro corpo, 422 `idempotency_key_reused`). Para **principal de aplicativo**, o escopo da chave é `tid + conexão + rota + hash do corpo` (não o autorizador, que é o mesmo em várias conexões) e a mesma chave com outro corpo responde **409** `idempotency_key_reused`; a resposta guardada nunca sai para outra conexão.
4. **Dado** um pedido criado por aplicativo, **quando** é gravado, **então** fica `Draft` com o rótulo "Pedido do Conecta: confirme com o cliente" (ou "Pedido de {aplicativo}") e **não reserva estoque nem gera cobrança** sozinho (a IA sugere, não decide).
5. **[Segurança]** **Dado** um aplicativo criando ou editando um pedido, **quando** manda preço unitário, **então** é ignorado (preço sempre da tabela); desconto acima de `sales.app_discount_limit_pct` (parâmetro novo, **padrão 0%**) é aceito só como rascunho com o chip "Desconto para aprovar", e a confirmação pelo aplicativo responde 403 `approval_required`; quem tem `sales.approve` confirma no ERP.

### Estados e transições
6. **Dado** a máquina de estados abaixo, **quando** uma transição não listada é pedida, **então** 409 `invalid_order_transition` com "Este pedido está {estado} e não pode {ação}." e nada muda; toda transição grava quem, quando, a origem e o motivo, sobre o `xmin` do pedido (duas ações juntas: só uma passa, a outra 409 com "Recarregar").

```
Draft (Rascunho | Pedido do Conecta: confirme com o cliente)
  ──Confirmar pedido (reserva tudo ou nada)──> Open (Aberto)
Open ──Gerar cobrança──> AwaitingPayment (Aguardando pagamento)
Open | AwaitingPayment ──Registrar pagamento (sales.approve) | baixa do título (BNK-02)──> Paid (Pago) ──> vira venda (VND-01)
AwaitingPayment ──cobrança cancelada ou vencida, sem pagamento──> Open
Draft | Open | AwaitingPayment ──Cancelar pedido | prazo vencido──> Cancelled
Paid ──Cancelar pedido e estornar venda (sales.approve)──> Cancelled
Paid ──> Picked, Invoiced, Shipped, Delivered (VND-02b)
```

7. **Dado** um desconto de linha ou do pedido acima de `sales.order_discount_limit_pct` (parâmetro novo, padrão 10%), **quando** uma pessoa confirma, **então** só quem tem `sales.approve` confirma; os demais veem "Desconto acima do limite: só quem aprova em Vendas pode confirmar." e a API responde 403 `approval_required`.
8. **Dado** um pedido `Draft` ou `Open`, **quando** edito linhas ou cliente (`sales.edit`), **então** o total é recalculado; no `Open`, a reserva acompanha (sobe tudo ou nada, desce liberando); de `AwaitingPayment` em diante, linhas e cliente ficam travados ("Cancele a cobrança para alterar o pedido.").

### Reserva de estoque
9. **Dado** "Confirmar pedido", **quando** o servidor processa, **então** reserva cada linha no local de estoque do pedido (padrão: o local padrão da loja), **tudo ou nada**, na mesma transação; o disponível (físico − reservado) do balcão, da API e dos canais passa a descontar a reserva; faltando saldo, 409 `insufficient_stock` com "Só há {n} {unidade} disponíveis de {produto}." por linha e nada é reservado.
10. **Dado** dois pedidos disputando a última unidade, **quando** confirmam ao mesmo tempo, **então** só um reserva (teste de concorrência, trava por item em ordem crescente de `item_id`, ADR 0023).
11. **Dado** a reserva genérica, **quando** a migration roda, **então** `inventory.reservations` ganha `source_type`/`source_id` e `expires_at`, `production_order_id` vira anulável, um `CHECK` exige **exatamente uma origem** (`production_order_id` ou `source_type`+`source_id`) e um índice único parcial novo cobre `(source_type, source_id, item_id, location_id) WHERE source_type IS NOT NULL`; o índice de hoje `ux_reservations_order_item_location` é recriado como parcial `WHERE production_order_id IS NOT NULL` (mesmas colunas: nenhuma mudança para a OS; hoje ele não é parcial e, com `NULL`, deixaria de proteger só por acaso do `NULLS DISTINCT`). Os testes da EST-02 seguem verdes e duas reservas de pedidos diferentes no mesmo item e local convivem.
12. **Dado** prazos de reserva, **quando** o job `sales-order-expiry` roda (a cada 15 min, `TenantJob` com `tenantId`), **então** cancela com o motivo "Prazo de pagamento vencido", libera a reserva e cancela título e boleto (BNK-01b CA 17): pedido de pessoa `Open` sem cobrança há mais de `sales.order_hold_hours` (padrão 48 h); pedido de aplicativo `Open` sem cobrança há mais de `sales.app_order_hold_hours` (padrão **2 h**); com cobrança emitida, a reserva vale até o vencimento + 1 dia útil de compensação (para aplicativo, vencimento de no máximo 3 dias); **pedido `Paid` não expira** (`expires_at` limpo). O vendedor recebe o aviso com "Recriar pedido".
13. **[Segurança]** **Dado** um aplicativo, **quando** cria ou confirma pedidos, **então** valem os limites contra reserva abusiva: quantidade por linha até `sales.app_max_line_qty` e por pedido até `sales.app_max_order_qty` (422 `app_quantity_limit`); no máximo 5 pedidos em aberto (`Draft`, `Open`, `AwaitingPayment`) por cliente e aplicativo (409 `too_many_open_orders`); confirmar pelo aplicativo só com cliente cobrável (documento e endereço completos; senão 422 `customer_not_billable`); acima de `sales.app_unpaid_alert_count` (padrão 10) pedidos do aplicativo com reserva e sem pagamento, os Proprietários recebem o alerta com o link para a lista filtrada.
14. **Dado** o PDV vendendo a mercadoria reservada, **quando** o disponível já está reservado para um pedido, **então** o caixa não é barrado (regra do ECM-02 e da PP-02): o saldo pode ficar negativo com o alerta da PDV-05; o pedido pago tem prioridade na saída e mostra "Reserva com falta" até a reposição.

### Cobrança (boleto com Pix do Inter)
15. **Dado** um pedido `Open`, a integração Inter ativa e o cliente cobrável, **quando** abro "Gerar cobrança" (`sales.edit`), **então** o diálogo já vem preenchido (valor = total, vencimento `sales.order_charge_days`, padrão 1 dia útil, e-mail do cliente); ao confirmar, o Finance cria **um título a receber de origem `SalesOrder`** contra o cliente e pede o bolepix pelo fluxo da BNK-01b (`Requested → Issued`, multa e juros de Configurações → Cobrança); o pedido vai a `AwaitingPayment` e mostra linha digitável, Pix copia-e-cola, QR, "Copiar Pix", "Compartilhar boleto" e "Baixar PDF" quando o boleto fica `Issued`.
16. **Dado** a forma de pagamento do boleto, **quando** configuro `sales.order_bank_slip_payment_method_id`, **então** só aceita uma forma do tipo novo **`PaymentMethodType.BankSlip`** ("Boleto com Pix"), distinto de `Pix`; sem ela, "Gerar cobrança" mostra "Configure a forma de pagamento do boleto em Configurações → Parâmetros.".
17. **Dado** cliente sem documento ou sem endereço de cobrança, **quando** abro "Gerar cobrança", **então** o diálogo mostra o que falta e "Completar cadastro" abre o cadastro do cliente num **diálogo lateral** (só os campos que faltam, `parties.edit`); salvo, volta à cobrança já preenchida; **nenhuma cobrança sai sem endereço** (regra do banco, BNK-01b CA 10); "Registrar pagamento" continua disponível.
18. **Dado** o boleto pago (webhook com reconsulta ou conferência diária, BNK-02), **quando** o título do pedido é baixado e o `ReceivableSettled` chega ao Sales, **então** o pedido passa a `Paid` uma vez só (consumidor idempotente) e publica `OrderPaid`; pagamento com diferença segue a conferência da BNK-02 (`PaidPendingReview`) e o pedido fica `AwaitingPayment` com o chip "Pagamento em conferência".
19. **Dado** o pagamento de um pedido já `Cancelled` (ou de um boleto já cancelado), **quando** a baixa chega, **então** o pedido não muda sozinho: ganha o alerta "Pago depois de cancelado" para quem tem `sales.approve`, com "Reabrir pedido" (reserva de novo; sem saldo, recusa) ou "Devolver o valor" (registro do estorno feito fora); nada é estornado sozinho.
20. **[Segurança]** **Dado** "Registrar pagamento" (`sales.approve`, dinheiro, maquininha ou Pix recebido fora), **quando** havia boleto do pedido, **então** o ERP **reconsulta o boleto no Inter antes de cancelá-lo**: pago lá, o registro é recusado com "Este boleto já foi pago em {data}. O pedido fica pago pela baixa do banco."; não pago, cancela boleto e título e o pedido passa a `Paid` pelo `IOrderPayments` com o `ManualPaymentProvider`; se a baixa do banco chegar mesmo assim, segue o caminho do CA 19. Tudo sobre o `xmin` do pedido.
21. **Dado** a baixa do título (`ReceivableSettled` → `Paid`) e um "Cancelar pedido" ao mesmo tempo, **quando** os dois processam, **então** só um vence (teste de concorrência): pago primeiro, o cancelamento responde 409 com "Recarregar"; cancelado primeiro, a baixa cai no CA 19.

### Conversão em venda (sem duplicar a VND-01)
22. **Dado** um pedido que chega a `Paid`, **quando** o Sales processa, **então** grava a venda pelo mesmo serviço da `ISaleIngestion` (na mesma transação): `SaleId` = o reservado na confirmação, canal `Online`, `SourceType = "SalesOrder"`, `SourceId` = o pedido, as mesmas linhas, o cliente e o pagamento capturado; o `SaleStockConsumer` baixa o estoque **consumindo a reserva** (`StockPosting.ReservationSourceType/Id`), com custo médio na linha, e é idempotente pelo `StockPosting` (índice de idempotência do movimento): reentrega do `SaleCompleted` não desconta a reserva duas vezes.
23. **[Segurança]** **Dado** a venda vinda de um pedido pago por boleto, **quando** o `SaleReceivablesConsumer` recompõe o dia, **então** o pagamento com `ReceivableId` (campo novo em `SalePaymentInput`) **não gera outro recebível**, e o DRE conta a receita uma vez só, pelo `ISalesRevenue`, com o título `SalesOrder` fora da receita por título (testes de integração dos dois lados).
24. **Dado** o mesmo `ReceivableSettled` entregue duas vezes, **quando** processo, **então** há uma venda só (idempotência pelo `SaleId`, VND-01 CA 4).

### Cancelamento
25. **Dado** "Cancelar pedido" em `Draft`, `Open` ou `AwaitingPayment` (`sales.edit`, motivo obrigatório), **quando** confirmo, **então** a reserva é liberada, título e boleto são cancelados e `OrderCancelled` é publicado.
26. **[Segurança]** **Dado** um pedido `Paid`, **quando** quem tem `sales.approve` aciona "Cancelar pedido e estornar venda", **então** exige a caixa "Já devolvi o valor ao cliente" marcada e o motivo; a venda é cancelada pelo `ISaleIngestion.CancelAsync` (`SaleCancelled` estorna a baixa de estoque); pago por boleto, o Finance **registra a devolução contra o título `SalesOrder`** (saída na conta do recebimento, título encerrado como devolvido); pago à mão, o recebível sai pela recomposição da VND-01. **Teste de integração:** depois do cancelamento, a receita do pedido no DRE é zero, o fluxo de caixa mostra a entrada e a devolução (líquido zero) e não sobra título em aberto.

### Eventos, acesso e auditoria
27. **Dado** cada evento do pedido (`OrderCreated`, `OrderConfirmed`, `OrderPaid`, `OrderCancelled`; os da separação em diante na VND-02b), **quando** acontece, **então** vai ao outbox na mesma transação, **sem dado pessoal** (ids, número, origem, `external_id`, estado, total e data); o consumidor relê pelo `ISalesOrderLookup` (novo). A INT-02 traduz para `sales_order.*`.
28. **[Segurança]** **Dado** o módulo `sales` não liberado, **quando** chamo qualquer rota do pedido, **então** 403 `module_not_released` e a tela `ModuleNotReleased`; sem grupo, 403 `forbidden`; IDOR: pedido de outro tenant ou id inexistente responde 404 igual.
29. **Dado** qualquer transição, mudança de linha, desconto aprovado, cobrança, pagamento ou cancelamento, **quando** abro a trilha (auditoria), **então** vejo o que houve, quem (ou "Integração {app} (autorizada por {pessoa})"), quando, antes e depois (`IAuditable`); a **linha do tempo** do pedido (estados com data, em linguagem simples) é um componente separado da trilha.

## Regras de negócio e técnicas
- **Pedido é documento comercial:** nunca é apagado; só cancelado. Depois de `Paid`, o documento com movimento é a venda.
- **Preço congelado na confirmação:** lido na criação e relido na confirmação; mudou, o diálogo mostra o novo e pede confirmação (ECM-02 CA 5). A versão da tabela fica na linha (`price_list_version`).
- **Reserva genérica:** contrato novo `ISalesReservations` (`ReserveAsync` tudo ou nada, `ReleaseAsync`, idempotentes, na transação de quem chama); a baixa consome a reserva por `StockPosting.ReservationSourceType/Id`, e o motor desce `quantity_reserved` junto com o físico, no mesmo movimento. O ECM-02 reusa o contrato.
- **Título `SalesOrder`:** `DocumentOrigin.SalesOrder` (novo); contrato `IOrderCharges` em `Finance.Contracts` (`CreateAsync`, `IssueSlipAsync`, `RecheckSlipAsync`, `CancelAsync`, `RefundAsync`, `ChargeAsync`) sobre o `BillingSlipService`. Um título por pedido (índice único parcial de título ativo por pedido). Fora da régua de cobrança e da receita por título.
- **Canal:** a venda do pedido usa `Online` em todas as origens; a origem fica no pedido. MKT-00 decide canal próprio do marketplace.
- **Parâmetros novos (Configurações → Parâmetros → Pedidos de venda):** `sales.order_discount_limit_pct` (10%), `sales.app_discount_limit_pct` (0%), `sales.order_hold_hours` (48 h), `sales.app_order_hold_hours` (2 h), `sales.order_charge_days` (1 dia útil; aplicativo até 3), `sales.app_max_line_qty`, `sales.app_max_order_qty`, `sales.app_unpaid_alert_count` (10), `sales.order_bank_slip_payment_method_id`.
- **Datas:** transições em `timestamptz` (UTC), exibidas em `America/Sao_Paulo`; vencimento é `date`.

## Permissões, eventos e tabelas
- **Permissões (existentes, nenhuma nova):** `sales.view`, `sales.create` (criar e confirmar), `sales.edit` (editar, gerar cobrança, cancelar antes do pagamento), `sales.approve` (desconto acima do limite, registrar pagamento, cancelar depois do pagamento, reabrir pago depois de cancelado), `sales.view_cost`, `sales.view_customer_document`; "Completar cadastro": `parties.edit`.
- **Eventos novos (`Sales.Contracts`):** `OrderCreated`, `OrderConfirmed`, `OrderPaid`, `OrderCancelled`. **Consumido:** `ReceivableSettled`.
- **Contratos novos:** `ISalesOrderLookup`, `ISalesReservations`, `IOrderCharges`. **Alterados (aditivos):** `SalePaymentInput.ReceivableId`, `StockPosting.Reservation*`, `DocumentOrigin.SalesOrder`, `PaymentMethodType.BankSlip`; `IdempotencyFilter.Scope` com a conexão para principal de aplicativo.
- **Tabelas:** `sales.orders` (`number`, `status`, `origin`, `source_app_id`, `external_id`, `created_by`, `customer_id`, `store_id`, `stock_location_id`, `sale_id`, `receivable_id`, `subtotal`, `discount_total`, `total`, `hold_until`, `cancel_reason`, `paid_after_cancel`, `xmin`), `sales.order_lines`, `sales.order_transitions`; `inventory.reservations` (colunas, `CHECK` e índices do CA 11). Migration só aditiva.
- **Jobs:** `sales-order-expiry` (15 min, por tenant).

## Telas e estados
- **Menu:** "Pedidos" (`/app/pedidos`) fica na área **Loja** nos perfis de varejo, loja virtual e serviços; na **fábrica com exceção manual**, em **Comercial**. Rótulos por `useTerm`/`BusinessVocabulary` (sem chave nova se o teto de 30 não comportar; usar a chave de "pedido" existente, se houver).
- **Lista de pedidos:** agrupada pelo **próximo passo**: "Para confirmar · Para cobrar · Aguardando pagamento · Para separar · Para emitir nota · Para enviar" (os três últimos com a VND-02b), com contagem; filtros por origem, cliente, período e "Parados há mais de {n} dias"; atrasado com faixa lateral âmbar ou vermelha. A 400 px, **cartões** (número, cliente, total, chip e próximo passo). Vazio: "Nenhum pedido ainda." com "Novo pedido"; carregando (esqueleto); erro "Não foi possível carregar os pedidos." com "Tentar de novo"; sem permissão: tela padrão.
- **Novo pedido:** cliente (busca com "Cadastrar cliente"), linhas pela consulta rápida (GTIN, nome) com o disponível de cada uma, desconto, total; local de estoque padrão já sugerido.
- **Pedido:** cabeçalho com o indicador de etapa e o **próximo passo em destaque** (a 400 px, botão fixo no rodapé); **faixa de problema** com a ação quando houver ("Reserva com falta", "Pagamento em conferência", "Pago depois de cancelado", "Desconto para aprovar"); abas **Itens**, **Cobrança** (linha digitável, "Copiar Pix" com `aria-live` "Copiado", "Compartilhar boleto", situação), **Linha do tempo** e **Trilha**. Link para a venda depois de `Paid`. Estados: 404 "Pedido não encontrado." com "Voltar para pedidos"; "Reservando…" durante a confirmação; conflito de edição com "Recarregar".
- **Verbos:** "Confirmar pedido" / "Pedido confirmado"; "Gerar cobrança" / "Cobrança gerada"; "Registrar pagamento" / "Pagamento registrado"; "Cancelar pedido" / "Pedido cancelado"; "Cancelar pedido e estornar venda" / "Pedido cancelado e venda estornada" (`ConfirmDialog` `destructive` com o impacto: reserva liberada, boleto cancelado, venda estornada).
- **Propositivo:** confirmado, oferece "Gerar cobrança" preenchida; pago, "Separar" (VND-02b); card no Início "Pedidos aguardando ação".
- **Acessibilidade:** chips com texto (nunca só cor), foco no primeiro campo dos diálogos, contraste dos tokens.
- **Ajuda (novas):** `Ajuda/pedidos.md`, `Ajuda/pedido-novo.md`, `Ajuda/pedido.md` (abas `itens`, `cobranca`, `linha-do-tempo`, `trilha` em arquivos próprios); atualizar `Ajuda/financeiro-receber-vendas.md` (origem "Pedido de venda") e `Ajuda/configuracoes-parametros*.md`. `<Dica>` em "Reservado", "Pedido do Conecta: confirme com o cliente" e "Pago depois de cancelado".

## Relação com o ECM
- **ECM-04** reusa este pedido (origem `Storefront`); fica com o que é só da loja virtual (frete, etiqueta, página pública, e-mail por estado). Propõe-se substituir `storefront.online_orders` por `sales.orders` quando a Fase 14 abrir.
- **ECM-02** reusa `ISalesReservations` com validade curta do carrinho. **ECM-03** (Pix dinâmico e cartão) entra pelo `IOrderPayments`.

## Tarefas
- [x] Estoque: colunas, `CHECK` e índices de `inventory.reservations` (CA 11), `ISalesReservations`, consumo da reserva no `StockPosting`; teste de concorrência na última unidade e de reentrega.
- [x] Sales Domain: `SalesOrder`, `SalesOrderLine`, máquina de estados com teste de unidade de todas as transições.
- [x] Sales Application: `SalesOrderService` (criar, confirmar, editar, cobrar, registrar pagamento, cancelar), limites de aplicativo (CA 13), conversão em venda, `ISalesOrderLookup`, consumidor de `ReceivableSettled`, job `sales-order-expiry`.
- [x] Finance: `DocumentOrigin.SalesOrder`, `PaymentMethodType.BankSlip`, `IOrderCharges` (com reconsulta e devolução); `SaleReceivablesConsumer` ignorando pagamento com `ReceivableId`; receita por título sem `SalesOrder`.
- [x] BuildingBlocks: `IdempotencyFilter.Scope` com `tid + conexão` para principal de aplicativo e 409 na divergência.
- [x] Endpoints `/api/v1/sales/orders` (+ `/{id}/confirm`, `/charge`, `/payments`, `/cancel`, `/reopen`), `RequireModule` e `RequirePermission`; OpenAPI e Orval.
- [x] Front: lista, novo, detalhe, diálogos (cobrança, completar cadastro, cancelar), card do Início, parâmetros; Vitest dos estados.
- [x] Ajudas; `docs/04-fluxos-estados.md` (máquina do pedido) e `docs/PDV/03-modelo-dados.md` (`sales.orders`).
- [x] Integração (Testcontainers + fake do Inter): `Draft → Paid` por boleto e à mão; expiração (pessoa e aplicativo); pago depois de cancelado; corrida pagamento × cancelamento (CA 21); cancelamento depois do pagamento com DRE e caixa (CA 26); recebível sem duplicar (CA 23); idempotência (CA 2, 3, 24).

## Dev Notes
- **Venda:** `src/Modules/Sales/BemAqui.Sales.Contracts/SalesContracts.cs`; `src/Modules/Sales/BemAqui.Sales.Application/SaleService.cs`, `SaleService.Orders.cs`, `PaymentProviders.cs` (`ManualPaymentProvider`), `SaleStockConsumer.cs`; rotas em `src/Modules/Sales/BemAqui.Sales.Endpoints/SalesEndpoints.cs`.
- **Estoque:** `src/Modules/Inventory/BemAqui.Inventory.Contracts/Stock.cs` e `Reservations.cs`; índice atual em `src/Modules/Inventory/BemAqui.Inventory.Infrastructure/ReservationConfigurations.cs` (`ux_reservations_order_item_location`, não parcial); idempotência do movimento em `StockConfigurations.cs` (`source_type, source_id, leg, type`).
- **Financeiro:** `src/Modules/Finance/BemAqui.Finance.Application/SaleReceivablesConsumer.cs` (recompõe por `ISaleLookup.CapturedPaymentsAsync`; filtrar o pagamento com `ReceivableId`); `Billing/BillingSlipService.Commands.cs`, `BillingSlipReactions.cs`, `BankPaymentService.cs`; `src/Modules/Finance/BemAqui.Finance.Contracts/PaymentMethodContracts.cs` (`PaymentMethodType`); `src/BuildingBlocks/BemAqui.SharedKernel/DocumentOrigin.cs`.
- **Idempotência:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Web/IdempotencyFilter.cs` (`Scope` usa `ICurrentUser.UserId`, que para aplicativo é o autorizador: por isso o escopo por conexão).
- **Terceiros:** `src/Modules/Parties/BemAqui.Parties.Contracts/PartiesContracts.cs`.
- Ajudas de referência: `Ajuda/loja-vendas.md`, `Ajuda/loja-venda.md`, `Ajuda/loja-venda-trilha.md`.

## Fora de escopo
- Separação, NF-e, envio e entrega (VND-02b). Pedido de orçamento da fábrica (ATD-03).
- Pix imediato (`cob`) e cartão por gateway (PP-21, ECM-03). Frete e etiqueta (ECM-04, MKT-01). Devolução depois da entrega (PDV-06, ECM-05). Avisos ao cliente por estado (ATD-05, ECM-04).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 26/11/2026 | 0.1 | Story escrita a partir da ADR 0052 e do plano de integrações (como VND-02). | Claude (planejamento) |
| 26/11/2026 | 0.2 | Recomendação aceita (PO 24/10), revisões de 26/11/2026: VND-02 dividida em 02a e 02b; `BankSlip`; completar cadastro; menu por perfil; limites e origem do aplicativo; idempotência por conexão; reserva genérica com índice parcial e `CHECK`; corrida pagamento × cancelamento; devolução no cancelamento depois do pagamento; telas por próximo passo. | Claude (planejamento) |
| 27/11/2026 | 1.0 | Implementada (branch `feat/vnd-02a`). Backend: `sales.orders`/`order_lines`/`order_transitions`/`order_parameters`, `SalesOrderService` (criar, editar, confirmar com reserva tudo ou nada, cobrar, registrar pagamento com reconsulta, cancelar, cancelar o pago com estorno e devolução, reabrir e devolver o pago depois de cancelado), consumidores de `ReceivableSettled` e `BillingSlipCancelled`, job `sales-order-expiry` (15 min, `TenantJob`), `ISalesOrderLookup`; Estoque: reserva genérica (`ISalesReservations`, colunas, `CHECK`, índice da OS recriado como parcial, filtro do modelo para a OS não ver as reservas de documento) e consumo pela baixa (`StockPosting.ReservationSource*`); Financeiro: `DocumentOrigin.SalesOrder`, `PaymentMethodType.BankSlip`, `IOrderCharges` (`OrderChargeService`), `SaleReceivablesConsumer` sem o pagamento com `ReceivableId`, receita por título sem `SalesOrder`, devolução no título (`refunded_at`); `IdempotencyFilter.Scope` por conexão (claim `conn`, 409 na divergência). Front: Loja → Pedidos (lista por próximo passo, novo, detalhe com Itens, Cobrança, Linha do tempo e Trilha, diálogos), card do Início e a seção de Parâmetros. Migration `SalesOrdersVnd02a` (aditiva). Correção junto: o `BillingSlipIssued` não estava registrado no outbox (a mensagem ficava com erro). | Claude (dev) |

### Desvios e decisões (registrados na implementação)
- **Rota:** `/app/loja/pedidos` (aba "Pedidos" do módulo Vendas, na área Loja), em vez de `/app/pedidos`; a colocação em **Comercial** para a fábrica com exceção manual fica para quando a primeira fábrica ligar o módulo (o menu segue a área do módulo).
- **`IOrderCharges`:** `CreateAsync` e `IssueSlipAsync` foram unidos em `ChargeAsync` (cria ou reaproveita o título ativo e pede o boleto, idempotente pelo pedido); `ReadinessAsync` e `FindAsync` a mais para o diálogo e a aba Cobrança.
- **"Completar cadastro":** abre o cadastro do cliente (Cadastros → Terceiros) em vez do diálogo lateral só com os campos que faltam; o diálogo volta preenchido ao reabrir.
- **Aba Cobrança:** linha digitável, Pix copia-e-cola, "Copiar Pix" (`aria-live` "Copiado") e "Compartilhar boleto"; o PDF continua no título do Financeiro (BNK-01b).
- **Edição de itens na tela:** a API (`PUT /sales/orders/{id}`, CA 8) está pronta e testada pela regra; a tela de edição fica para a VND-02b. Enquanto isso, "Recriar pedido" (`?de=`).
- **CA 14:** o caixa nunca consome o reservado: a venda do PDV sem disponível cai na falta da PDV-05 (Estoque → Divergências) e a reserva do pedido fica intacta; "Reserva com falta" aparece se o reservado ficar abaixo da quantidade (ex.: inventário).
- **Dias úteis:** sábado e domingo não contam; feriado não (sem calendário de feriados no Sales nesta versão).
- **Padrões novos:** `sales.app_max_line_qty` = 50 e `sales.app_max_order_qty` = 200 (a story não fixava); recomendação aceita (PO 24/10).
- **Preço mudado na confirmação:** 409 `price_changed` com o preço novo; o diálogo confirma de novo com `acceptNewPrices`.
- **Rota de aplicativo:** as regras do principal de aplicativo (origem, limites, desconto, cliente cobrável, id externo, escopo da idempotência) estão no serviço e testadas com o ator de aplicativo; a rota HTTP é da INT-03.
- **E2E:** não escrito nesta story (E2E é por marco de feature, CLAUDE.md).
