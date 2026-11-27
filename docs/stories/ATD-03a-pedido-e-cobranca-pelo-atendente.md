# ATD-03a — Criar pedido e mandar a cobrança pelo atendente

| Campo | Valor |
|---|---|
| ID | ATD-03a |
| Título | Na conversa, o atendente busca produto com preço e disponível, cadastra o cliente no ERP se faltar, monta o pedido (rascunho no ERP com `Idempotency-Key` e `externalId` do Conecta), confirma com o cliente (reserva), gera a cobrança (boleto com Pix do Inter) e manda linha digitável, Pix e PDF na conversa assim que o boleto sai; cancela antes do pagamento; o pedido fica ligado à conversa e ao negócio do funil |
| Tamanho | M |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §1, §3 e §4 · [ADR 0053](../adr/0053-aplicativos-conectados.md) §13 · [VND-02a](VND-02a-pedido-de-venda-nucleo.md) (CA 2 a 5, 9, 12, 13, 15, 25) · [INT-03](INT-03-api-integracao-v1.md) (CA 4 a 6, 10 a 16) · [INT-02](INT-02-webhooks-de-saida.md) (catálogo) |
| Dependências | ATD-01, ATD-02 (vínculo e telefone conferido); VND-02a e INT-03 (Done); Inter ativo no tenant do ERP (BNK-01b) e `sales.order_bank_slip_payment_method_id` configurado; módulo `sales` liberado no tenant (na fábrica, por exceção manual, ADR 0052 §3) |
| Bloqueia | ATD-04, ATD-05 (vínculo pedido ↔ conversa) |
| Repositórios | **Conecta** (`bemaqui-atende-api`, `bemaqui-atende-web`, `bemaqui-conecta`). ERP: nenhuma mudança |
| Ordem | 3 (F2) |

## User story
Como **atendente do Conecta**, quero **montar o pedido na conversa, confirmar com o cliente e mandar o Pix ou o boleto ali mesmo**, para **fechar a venda no WhatsApp com o estoque segurado e o pagamento caindo direto no ERP**.

## Contexto
- **Ajuste de escopo:** o título do backlog juntava "criar pedido e mandar a cobrança" e "pedido de orçamento para fábrica". A leitura mostrou que o segundo exige **mudança no ERP** (rota nova e o escopo `quotes.create`, que a ADR 0053 §5 deixou fora da v1, e por isso **nova autorização** de cada conexão), enquanto o primeiro usa só o contrato `integration-v1` pronto. Separadas, a ATD-03a entra só no Conecta e a [ATD-03b](ATD-03b-pedido-de-orcamento-fabrica.md) leva a mudança do ERP com a sua ADR.
- O pedido de aplicativo nasce `Draft` "Pedido do Conecta: confirme com o cliente", **não reserva nem cobra sozinho**; confirmar reserva tudo ou nada; o pedido de aplicativo `Open` sem cobrança expira em 2 h (`sales.app_order_hold_hours`); a cobrança do aplicativo vence em no máximo 3 dias; preço vem sempre da tabela; desconto acima de `sales.app_discount_limit_pct` (padrão 0%) só é confirmado no ERP (VND-02a CA 4, 5, 12, 13).
- Hoje o Conecta não tem catálogo, carrinho nem pedido; o que mais se parece é o `Deal` do funil (`Deal`: contato, etapa, valor, `open|won|lost`).

## Critérios de aceite

### Produto e cliente
1. **Dado** `catalog.view`, **quando** o atendente abre "Novo pedido" na conversa e digita na busca, **então** vê `GET /products?q=&page=&pageSize=20`: código, descrição, unidade, preço e, com `inventory.view`, "Disponível: {n}" (sem `inventory.view`, a linha mostra "Disponível: consulte o ERP"); sem custo nem margem.
2. **Dado** o contato sem vínculo com o ERP (ATD-02 CA 1), **quando** o atendente clica "Cadastrar no ERP", **então** o formulário vem preenchido com nome e telefone da conversa e pede tipo, CPF ou CNPJ, e-mail e endereço (CEP preenche o resto); `POST /customers` com `Idempotency-Key`; criado, o vínculo nasce com o telefone conferido; 409 `party_exists` mostra "Este documento já está no ERP" e leva à busca por documento (ATD-02 CA 2), sem criar outro.
3. **Dado** o cliente sem `canBill`, **quando** o pedido vai ser cobrado, **então** o Conecta mostra o que falta ("Falta o endereço completo para emitir o boleto") e "Completar no ERP" (só preenche campo vazio, `PATCH /customers/{id}`); campo já preenchido diferente só muda no ERP (422 `field_change_requires_backoffice`: "Esse dado só é trocado no ERP.").

### Rascunho e confirmação
4. **[Segurança]** **Dado** o carrinho com linhas (produto e quantidade), **quando** o atendente clica "Criar pedido", **então** o Conecta grava antes o `ErpOrderDraft` (conversa, contato, atendente, linhas, `externalId` = `atd-<id do rascunho>`, `Idempotency-Key` nova) e chama `POST /orders` com `customerId`, linhas, `externalId` e observação; **nunca manda preço** (o ERP ignora) e não manda desconto na v1 (ponto em aberto 1); a resposta (`Draft`, número `PV-…`, total) liga o pedido à conversa (`ErpOrderLink`).
5. **Dado** uma falha de rede no `POST /orders`, **quando** o atendente tenta de novo, **então** a nova chamada usa **a mesma chave e o mesmo corpo** e recebe o mesmo pedido; mudou o carrinho depois de uma falha, o Conecta gera chave nova **e** `externalId` novo (409 `idempotency_key_reused` ou `external_id_conflict` nunca é tratado como sucesso).
6. **Dado** o pedido `Draft`, **quando** o atendente clica "Mandar resumo", **então** sai na conversa o resumo (itens, quantidades, total, "Confirma o pedido?") como mensagem do atendente, e o pedido mostra "Aguardando o sim do cliente".
7. **Dado** o cliente confirmou na conversa, **quando** o atendente clica "Confirmar pedido", **então** o Conecta chama `POST /orders/{id}/confirm` com `version`; a resposta `Open` mostra "Estoque reservado por 2 h: gere a cobrança"; os erros viram texto e ação: 409 `insufficient_stock` ("Só há {n} {unidade} de {produto}", com "Ajustar quantidades", que cancela e recria o rascunho); 409 `price_changed` (mostra o preço novo; "Confirmar com o preço novo" manda `acceptNewPrices = true` só depois de o atendente avisar o cliente); 403 `approval_required` ("Desconto para aprovar no ERP"); 409 `too_many_open_orders` ("Este cliente já tem 5 pedidos em aberto pelo Conecta"); 422 `customer_not_billable` (CA 3); 422 `app_quantity_limit`; 409 de versão com "Recarregar".

### Cobrança
8. **Dado** um pedido `Open`, **quando** o atendente clica "Gerar cobrança", **então** o diálogo vem preenchido com o vencimento padrão (1 dia útil, no máximo 3) e o `POST /orders/{id}/charge` sai com `Idempotency-Key`; a resposta 202 (`Requested`) mostra "Gerando o boleto no banco…"; o pedido vai a "Aguardando pagamento".
9. **Dado** o aviso `billing_slip.issued` (ATD-01) do título do pedido (`receivable_id` igual ao da cobrança do pedido, lido em `GET /orders/{id}`), **quando** chega, **então** o Conecta lê `GET /charges/{id}?customerId=` e, se o atendente marcou "Mandar sozinho quando sair" (padrão ligado), manda na conversa a linha digitável, o Pix copia-e-cola e o PDF (como na 2ª via da ATD-02 CA 9 e 14); se o boleto não sair em 2 min, o pedido mostra "O banco está demorando" com "Ver no ERP"; `billing_slip.cancelled` mostra "Cobrança cancelada" com o motivo e "Gerar de novo".
10. **Dado** `sales_order.paid`, **quando** chega, **então** o pedido na conversa mostra "Pago" (o aviso ao cliente é da ATD-05).
11. **Dado** um pedido `Draft`, `Open` ou `AwaitingPayment`, **quando** o atendente clica "Cancelar pedido" e informa o motivo, **então** o Conecta chama `POST /orders/{id}/cancel`; em `Paid` em diante, 409 `order_cancel_requires_backoffice` vira "Este pedido já foi pago. O cancelamento é feito no ERP.".
12. **Dado** o pedido de aplicativo que expirou no ERP (2 h sem cobrança), **quando** chega `sales_order.cancelled`, **então** a conversa mostra "Pedido cancelado: prazo da reserva vencido" com "Recriar pedido" (carrinho preenchido).

### Funil, vínculo e auditoria
13. **Dado** um pedido criado pelo Conecta, **quando** nasce, **então** o `ErpOrderLink` guarda conversa, contato, atendente, número, situação e o negócio (`Deal`) aberto mais recente do contato (ou cria um no estágio padrão, com título "PV-…" e valor = total); mover o negócio por evento é da ATD-05.
14. **Dado** qualquer ação de pedido, **quando** é feita, **então** o Conecta grava no `erp_access_log` (ATD-02) o atendente, a ação, o pedido e a data; no ERP, a trilha mostra "Integração Conecta (autorizada por Fulano)" (o ERP não sabe qual atendente: por isso o registro do Conecta e a observação do pedido com "Atendente: {primeiro nome}").
15. **Dado** os papéis do Conecta, **quando** um usuário usa o pedido, **então** criar, confirmar, gerar cobrança e cancelar: `agent`, `supervisor` e `admin` com acesso à conversa; a conta pode exigir `supervisor` para "Cancelar pedido" (padrão: não).

## Regras de negócio e técnicas
- **Rotas do ERP (contrato `integration-v1`):** `GET /products`, `GET /products/{id}` (`catalog.view`; `available` só com `inventory.view`); `POST /customers` (`parties.create`), `PATCH /customers/{id}` (`parties.edit`); `POST /orders` e `POST /orders/{id}/confirm` (`sales.create`); `POST /orders/{id}/charge` e `POST /orders/{id}/cancel` (`sales.edit`); `GET /orders/{id}` (`sales.view`); `GET /charges/{id}`, `GET /charges/{id}/pdf` (`finance.view`).
- **Idempotência:** `Idempotency-Key` em **todo** `POST` (`/customers`, `/orders`, `/confirm`, `/charge`, `/cancel`), gerada e gravada no Conecta antes da chamada, reaproveitada só com o mesmo corpo; `externalId` único por rascunho.
- **Avisos consumidos:** `sales_order.created|confirmed|paid|cancelled`, `billing_slip.issued|paid|cancelled`. A ordem não é garantida: o Conecta relê `GET /orders/{id}` antes de mudar a tela.
- **Dinheiro:** valores exibidos como vieram do ERP (2 casas); o Conecta não calcula total nem desconto.
- **Pedido de outro canal:** pedidos criados no ERP para um cliente vinculado aparecem no painel (ATD-02), mas só os criados pelo Conecta têm as ações desta story (o ERP já recusa ação em pedido de outra conexão com 404, INT-03 Change Log item 4).

## O que muda em cada repositório
**Conecta (`bemaqui-atende-api`):** `ErpOrderDraft` e `ErpOrderLink` (`ITenantEntity`, `erp_order_id`, `number`, `status`, `conversation_id`, `contact_id`, `deal_id`, `created_by_user_id`, `external_id`, `charge_id`, `idempotency_keys`); migration aditiva; comandos MediatR "Criar pedido", "Confirmar", "Gerar cobrança", "Cancelar", "Mandar resumo", "Mandar cobrança"; reação a `billing_slip.issued` e `sales_order.*` no `ProcessErpEventJob`.
**Conecta (`bemaqui-atende-web`):** "Novo pedido" na conversa (busca, carrinho, cliente), cartão do pedido na conversa com o próximo passo.
**Conecta (`bemaqui-conecta`, app):** o mesmo fluxo em folha deslizante na conversa.
**ERP:** nenhuma mudança.

## Telas e estados (Conecta)
- **Novo pedido:** busca com carregando, vazio ("Nenhum produto com esse nome"), erro; carrinho com total do ERP; "Criar pedido" desabilitado enquanto envia.
- **Cartão do pedido na conversa:** indicador do próximo passo ("Mandar resumo" → "Confirmar pedido" → "Gerar cobrança" → "Aguardando pagamento" → "Pago"), faixa de problema com a ação (CA 7 e 9), "Ver no ERP" (link para o pedido no ERP, que pede login de pessoa).
- **Verbos:** "Criar pedido" → "Pedido criado"; "Confirmar pedido" → "Pedido confirmado"; "Gerar cobrança" → "Cobrança gerada"; "Cancelar pedido" → "Pedido cancelado".
- **Propositivo:** criado, oferece "Mandar resumo"; confirmado, "Gerar cobrança"; boleto emitido, manda sozinho (CA 9).

## Plano de testes
- **Unidade:** máquina do cartão (próximo passo por situação); chave e `externalId` (mesmo corpo reaproveita, corpo novo gera novos).
- **Integração (Conecta, ERP falso):** fluxo `Draft → Open → AwaitingPayment → Paid` com os avisos fora de ordem (CA 4 a 10); falha de rede e repetição sem pedido duplicado (CA 5); cada erro do CA 7; boleto que demora e boleto cancelado (CA 9); expiração (CA 12); cancelamento depois do pago (CA 11); negócio criado ou reaproveitado (CA 13).
- **Manual (piloto):** pedido real no ERP SaaS com Inter em homologação até o boleto; conferir no ERP a reserva, o título `SalesOrder` e a trilha "Integração Conecta".

## Tarefas
- [ ] Conecta API: entidades, comandos e reações aos avisos.
- [ ] Conecta web e app: novo pedido e cartão na conversa.
- [ ] Manual do usuário do Conecta: "Vender pelo ERP na conversa".
- [ ] Fluxo do repositório do Conecta.

## Dev Notes
- **Conecta:** `Domain/Entities/Deal.cs`, `Application/Features/Pipeline/Commands/MoveDealCommand.cs` (dispara `deal_stage_changed`), `Infrastructure/Automations/AutomationEngine.cs` (`create_deal`), `Infrastructure/Jobs/YampiPaymentPollingJob.cs` e `Infrastructure/Channels/YampiOrderService.cs` (precedente do Yampi: aqui não há consulta periódica, os avisos do ERP substituem).
- **ERP:** VND-02a Change Log ("Preço mudado na confirmação: 409 `price_changed`"; `sales.app_max_line_qty` 50 e `sales.app_max_order_qty` 200); INT-03 Change Log itens 2 a 4 (chave obrigatória também em `/customers` e `/charge`; `version` opcional; ações só nos pedidos da própria conexão).

## Fora do escopo
- Pedido de orçamento da fábrica (ATD-03b); ferramentas da IA (ATD-04); avisos automáticos ao cliente e funil por evento (ATD-05).
- Cartão de crédito (PP-21), Pix imediato (`cob`), frete e entrega (VND-02b, ECM-04), edição das linhas de um pedido já criado (cancela e recria na v1).

## Pontos em aberto (para o PO)
1. **Desconto pelo atendente:** com `sales.app_discount_limit_pct` em 0%, qualquer desconto fica "para aprovar" no ERP. **Recomendação:** v1 sem campo de desconto no Conecta; quando o tenant subir o limite, a ATD seguinte libera o campo até o limite.
2. **"Mandar sozinho quando sair"** ligado por padrão: **recomendação:** sim, porque o atendente já pediu a cobrança naquela conversa; o envio aparece na conversa como mensagem do atendente que gerou.
3. **Cancelar pelo `agent`:** **recomendação:** permitido antes do pagamento (o ERP limita o resto), com a opção de exigir `supervisor`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir da VND-02a e da INT-03 implementadas; ATD-03 dividida em 03a (só Conecta) e 03b (pedido de orçamento, com mudança no ERP). | Claude (planejamento) |
