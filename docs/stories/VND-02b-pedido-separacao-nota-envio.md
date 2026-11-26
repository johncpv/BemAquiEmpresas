# VND-02b — Pedido de venda: separação, NF-e, envio e entrega

| Campo | Valor |
|---|---|
| ID | VND-02b |
| Título | Depois do pagamento: lista de separação, rascunho da NF-e modelo 55 com "Emitir nota" humano, envio (com "Enviar sem nota" por configuração), entrega e cancelamento antes da nota |
| Tamanho | M |
| Status | Draft (26/11/2026) |
| Requisitos | [VND-02a](VND-02a-pedido-de-venda-nucleo.md) · [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §1 · FIS-03a/03b · ADR 0023 |
| Dependências | VND-02a, FIS-03a (Done) |
| Bloqueia | MKT-00, ECM-04 |
| Ordem | 1b (F1 — Base no ERP, logo depois da VND-02a) |

## User story
Como **dono ou vendedor**, quero **separar o pedido pago, emitir a nota e informar o envio e a entrega**, para **acompanhar cada pedido até o cliente e ter a nota certa da venda**.

## Contexto
- A VND-02a leva o pedido até `Paid` e converte em venda. Esta story cobre `Paid → Picked → Invoiced → Shipped → Delivered`.
- **NF-e nunca é automática na v1:** o Fiscal prepara o rascunho e uma pessoa clica "Emitir nota". A emissão automática para volume de marketplace é decidida no MKT-00.

## Critérios de aceite

### Separação
1. **Dado** um pedido `Paid`, **quando** aciono "Separar" (`sales.edit`), **então** vejo a lista de separação (produto, variação, quantidade, local), posso imprimi-la (QuestPDF) e confirmo "Separado"; o pedido passa a `Picked` e publica `OrderPicked`.
2. **Dado** uma linha com "Reserva com falta" (venda do PDV sobre a reserva, VND-02a CA 14), **quando** separo, **então** a linha mostra a falta e o pedido pago tem prioridade na próxima entrada; "Separado" fica desabilitado com o motivo até haver saldo.

### NF-e modelo 55
3. **Dado** o `OrderPaid` e o módulo `fiscal` liberado, **quando** o Fiscal reage, **então** cria **só o rascunho** da NF-e a partir da venda (`ISaleLookup`, `IParties.FiscalInfoAsync`), com `Idempotency-Key` `nfe-sale-{saleId}` e índice único parcial de nota ativa por venda; **nunca emite sozinho**.
4. **Dado** um pedido `Picked` com o rascunho pronto, **quando** quem tem `fiscal.create` clica "Emitir nota", **então** segue o fluxo `Draft → Processing → Authorized` da FIS-03a com as mesmas pré-condições (regra fiscal por item, sem DIFAL, cliente com documento e IBGE); enquanto a SEFAZ responde, o pedido mostra "Nota em processamento".
5. **Dado** a nota autorizada, **quando** o `InvoiceAuthorized` (com `SaleId`, campo novo) chega, **então** o pedido passa a `Invoiced`, publica `OrderInvoiced` e mostra número, chave e "Baixar DANFE"; rejeitada, fica `Picked` com a faixa "Nota rejeitada", o motivo e "Corrigir nota".
6. **Dado** a NF-e cancelada (`InvoiceCancelled` com `SaleId`, FIS-03b), **quando** chega, **então** o pedido volta de `Invoiced` a `Picked`.
7. **[Segurança]** **Dado** a NF-e de uma venda, **quando** o DRE e o faturamento são montados, **então** ela fica fora da receita por nota do `IOutgoingInvoices.RevenueAsync` (a receita vem do `ISalesRevenue`) e não baixa estoque (a baixa foi na venda).

### Envio e entrega
8. **Dado** um pedido `Invoiced`, **quando** aciono "Informar envio" com a forma (entrega própria, transportadora, correio) e o código de rastreio opcional, **então** passa a `Shipped` e publica `OrderShipped`; "Confirmar entrega" leva a `Delivered` e publica `OrderDelivered`; "Entregar no balcão" leva direto de `Invoiced` a `Delivered`.
9. **Dado** o parâmetro do tenant `sales.order_ship_without_invoice` (**padrão desligado**, para quem não emite NF-e), **quando** está ligado, **então** "Enviar sem nota" e "Entregar sem nota" aparecem no pedido `Picked` com motivo obrigatório, gravado na auditoria; desligado, `Picked` só avança pela nota, e sem o módulo `fiscal` o pedido mostra "Libere o Fiscal ou ative 'Enviar sem nota' em Configurações → Parâmetros.".

### Cancelamento
10. **[Segurança]** **Dado** um pedido `Picked`, **quando** quem tem `sales.approve` aciona "Cancelar pedido e estornar venda", **então** vale a mesma regra do `Paid` (VND-02a CA 26: "Já devolvi o valor ao cliente", devolução contra o título, DRE e caixa coerentes) e o rascunho da nota é descartado; em `Invoiced` em diante, é recusado com "Cancele a NF-e {número} antes." (FIS-03b); depois da entrega, é devolução (fora de escopo).

### Eventos
11. **Dado** `OrderPicked`, `OrderInvoiced`, `OrderShipped` e `OrderDelivered`, **quando** acontecem, **então** vão ao outbox na mesma transação, sem dado pessoal, como na VND-02a CA 27; a INT-02 traduz para `sales_order.*`.

## Regras de negócio e técnicas
- **Máquina (complemento da VND-02a):** `Paid ──Separar──> Picked ──NF-e autorizada──> Invoiced ──Informar envio──> Shipped ──Confirmar entrega──> Delivered`; `Invoiced ──Entregar no balcão──> Delivered`; `Invoiced ──NF-e cancelada──> Picked`; com `sales.order_ship_without_invoice`: `Picked ──Enviar sem nota──> Shipped` e `Picked ──Entregar sem nota──> Delivered`; `Picked ──Cancelar pedido e estornar venda──> Cancelled`.
- **Tabelas:** `sales.orders` ganha `shipping_method`, `tracking_code`, `shipped_without_invoice_reason`; `fiscal.outgoing_invoices.sale_id`. Migration só aditiva.
- **Contratos:** `IOutgoingInvoices.BySaleAsync` (novo); `InvoiceAuthorized`/`InvoiceCancelled.SaleId` (aditivo). Eventos novos: `OrderPicked`, `OrderInvoiced`, `OrderShipped`, `OrderDelivered`. Consumidos: `InvoiceAuthorized`, `InvoiceCancelled`, `InvoiceRejected`.
- **Permissões (existentes):** `sales.edit` (separar, envio, entrega), `sales.approve` (cancelar), `fiscal.create` (emitir nota).

## Telas e estados
- **Lista (VND-02a):** os grupos "Para separar · Para emitir nota · Para enviar" passam a valer.
- **Pedido:** abas novas **Nota** (situação, "Nota em processamento", DANFE, faixa de rejeição com "Corrigir nota") e **Entrega**; próximo passo em destaque: "Separar", "Emitir nota", "Informar envio", "Confirmar entrega".
- **Verbos:** "Separar" / "Separado"; "Emitir nota" / "Nota enviada"; "Informar envio" / "Envio informado"; "Confirmar entrega" / "Entrega confirmada"; "Enviar sem nota" / "Envio informado sem nota".
- **Propositivo:** separado, oferece "Emitir nota" com o rascunho pronto; autorizada, "Informar envio".
- **Configurações → Parâmetros → Pedidos de venda:** "Enviar sem nota" (desligado), com `<Dica>` "Só para quem não emite NF-e. Cada envio sem nota pede o motivo.".
- **Ajuda:** `Ajuda/pedido-nota.md` e `Ajuda/pedido-entrega.md` (abas do pedido); atualizar `Ajuda/pedidos.md`.

## Tarefas
- [ ] Sales: transições, separação (PDF), envio, entrega, consumidores de `InvoiceAuthorized`/`InvoiceCancelled`/`InvoiceRejected`; parâmetro `sales.order_ship_without_invoice`.
- [ ] Fiscal: rascunho da NF-e pelo `OrderPaid`, `sale_id`, `SaleId` nos eventos, `BySaleAsync`, fora da receita por nota.
- [ ] Endpoints `/{id}/pick`, `/ship`, `/deliver`, `/picking-list.pdf`; OpenAPI e Orval.
- [ ] Front: abas Nota e Entrega, diálogos, parâmetro; Vitest.
- [ ] Integração (Testcontainers + emissor falso): `Paid → Delivered`; nota rejeitada e cancelada; envio sem nota (ligado e desligado); cancelamento em `Picked` e recusa em `Invoiced`; DRE sem receita dobrada (CA 7).

## Dev Notes
- **Fiscal:** `src/Modules/Fiscal/BemAqui.Fiscal.Contracts/OutgoingInvoiceContracts.cs`; rascunho hoje nasce do romaneio em `src/Modules/Fiscal/BemAqui.Fiscal.Application/Invoices/OutgoingInvoiceService.cs`.
- Pendências do ECM-04 (lacuna 25 e dívida E14 do EmissorFiscal): conferir antes de emitir NF-e de pedido para tenant novo.

## Fora de escopo
- Emissão automática da NF-e (MKT-00). Envio parcial e pedido com várias notas. Frete, etiqueta e Correios (ECM-04, MKT-01). Devolução depois da entrega.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 26/11/2026 | 0.1 | Recomendação aceita (PO 24/10), revisões de 26/11/2026: separada da VND-02; NF-e só por "Emitir nota"; "Enviar sem nota" por parâmetro do tenant (desligado). | Claude (planejamento) |
