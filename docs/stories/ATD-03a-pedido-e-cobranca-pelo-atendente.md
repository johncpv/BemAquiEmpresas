# ATD-03a — Criar pedido e mandar a cobrança pelo atendente

| Campo | Valor |
|---|---|
| ID | ATD-03a |
| Título | Na conversa, o atendente busca produto com preço e disponível, cadastra o cliente com o mínimo, monta o pedido (rascunho no BemAqui Empresas com `Idempotency-Key` e `externalId`), manda o resumo, marca "Cliente confirmou" (sugerido, nunca automático), confirma (reserva), gera a cobrança e manda linha digitável, Pix e PDF com prévia; rascunho parado é cancelado sozinho; um job reconcilia os pedidos em aberto; o pedido é do Conecta da empresa e sobrevive a uma reconexão |
| Tamanho | M |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §1, §3 e §4 · [ADR 0053](../adr/0053-aplicativos-conectados.md) §13 · [VND-02a](VND-02a-pedido-de-venda-nucleo.md) (CA 2 a 5, 9, 12, 13, 15, 25) · [INT-03](INT-03-api-integracao-v1.md) (CA 4 a 6, 10 a 16) · [INT-04](INT-04-ajustes-avisos-e-conexao.md) (CA 1, 6, 11) · decisões do PO de 27/11/2026 (3) |
| Dependências | ATD-01, ATD-02, INT-04; VND-02a e INT-03 (Done); Inter ativo e `sales.order_bank_slip_payment_method_id` no tenant; módulo `sales` liberado (na fábrica, por exceção manual) |
| Bloqueia | ATD-03b, ATD-04, ATD-05a |
| Repositórios | **Conecta** (`bemaqui-atende-api`, `bemaqui-atende-web`, `bemaqui-conecta`). ERP: nada além da INT-04 |
| Ordem | 3 (F2) |

## User story
Como **atendente do Conecta**, quero **montar o pedido na conversa, confirmar com o cliente e mandar o Pix ou o boleto ali mesmo**, para **fechar a venda no WhatsApp com o estoque segurado e o pagamento caindo direto no BemAqui Empresas**.

## Contexto
- A ATD-03 original foi dividida: esta parte usa só o `integration-v1`; o pedido de orçamento da fábrica ([ATD-03b](ATD-03b-pedido-de-orcamento-fabrica.md)) muda o ERP.
- Pedido de aplicativo: `Draft` "Pedido do Conecta: confirme com o cliente", sem reserva nem cobrança sozinho; confirmar reserva tudo ou nada; `Open` sem cobrança expira em 2 h; cobrança do aplicativo vence em até 3 dias; preço da tabela; desconto acima de 0% só confirma no ERP; limite de 5 pedidos em aberto por cliente (VND-02a CA 4, 5, 12, 13). Com a INT-04, o pedido pertence ao **aplicativo + empresa**: reconectar não deixa pedido órfão nem perde a idempotência.
- O Conecta não tem catálogo, carrinho nem pedido; o mais próximo é o `Deal` do funil.

## Critérios de aceite

### Produto e cliente
1. **Dado** `catalog.view`, **quando** o atendente abre "Novo pedido" e busca, **então** vê `GET /products?q=&pageSize=20`: código, descrição, unidade, preço e, com `inventory.view`, "Disponível: {n}" (sem ele, "Disponível: consulte o BemAqui Empresas"); sem custo nem margem.
2. **Dado** o cliente não encontrado (ATD-02 CA 1), **quando** o atendente clica "Cadastrar no BemAqui Empresas", **então** o formulário pede **só o mínimo**: nome e telefone (preenchidos da conversa), tipo e CPF/CNPJ; `POST /customers` com `Idempotency-Key`; criado, o cliente fica "identificado pela conversa" (ATD-02 CA 4); 409 `party_exists` mostra "Este CPF/CNPJ já está no BemAqui Empresas" e leva à busca e à confirmação de identidade (ATD-02 CA 2 e 3), sem criar outro.
3. **Dado** o cliente sem `canBill` na hora de cobrar, **quando** o atendente clica "Gerar cobrança", **então** o Conecta diz o que falta ("Para o boleto, falta o endereço completo") com **"Pedir o endereço ao cliente"** (manda a pergunta na conversa, com prévia) e "Preencher endereço" (só campo vazio, `PATCH /customers/{id}`); campo já preenchido diferente: "Esse dado só é trocado no BemAqui Empresas.".

### Rascunho, resumo e confirmação
4. **[Segurança]** **Dado** o carrinho, **quando** o atendente clica "Criar pedido", **então** o Conecta grava antes o `ErpOrderDraft` (conversa, contato, atendente, linhas, `externalId` `atd-<id>`, `Idempotency-Key`) e chama `POST /orders` com `customerId`, linhas, `externalId` e observação "Atendente: {primeiro nome}", com `BemAqui-On-Behalf-Of`; nunca manda preço nem desconto; a resposta (`Draft`, número `PV-…`, total) cria o `ErpOrderLink` com a conversa.
5. **Dado** falha de rede no `POST /orders`, **quando** o atendente tenta de novo, **então** a nova chamada usa a mesma chave e o mesmo corpo e recebe o mesmo pedido; mudou o carrinho, gera chave e `externalId` novos; `idempotency_in_progress` espera e repete (ATD-01 CA 13); 409 `idempotency_key_reused`/`external_id_conflict` nunca é tratado como sucesso.
6. **Dado** o pedido no cartão da conversa, **quando** o atendente olha, **então** há **um botão principal que muda por etapa** — "Mandar resumo" → "Confirmar pedido" → "Gerar cobrança" — e **todo botão que envia algo ao cliente abre a prévia** do que ele vai receber; "Mandar resumo" manda itens, quantidades, total do ERP e "Confirma o pedido?".
7. **Dado** o resumo enviado, **quando** o cliente responde, **então** o Conecta **sugere** "Cliente confirmou?" quando a resposta parece um sim, mas **nunca marca sozinho**: o atendente clica "Cliente confirmou" (grava a mensagem do cliente que confirmou) e só então "Confirmar pedido" fica liberado.
8. **Dado** "Confirmar pedido", **quando** o Conecta chama `POST /orders/{id}/confirm` com `version`, **então** `Open` mostra "Estoque reservado por 2 h: gere a cobrança"; erros em português com ação: `insufficient_stock` → "Só temos {n} {unidade} de {produto}. Oferecer essa quantidade?" (recria o rascunho com a quantidade disponível, nova chave); `price_changed` → "O preço mudou: era R$ X, agora R$ Y. Avise o cliente antes." e "Confirmar com o preço novo" (`acceptNewPrices`) só depois de "Avisei o cliente"; `approval_required` → "Desconto para aprovar no BemAqui Empresas"; `too_many_open_orders` → "Este cliente já tem 5 pedidos em aberto pelo Conecta" com **"Cancelar pedidos parados"** (lista os rascunhos e abertos dele, para cancelar com motivo); `customer_not_billable` → CA 3; `app_quantity_limit`; `concurrency_conflict` → "O pedido mudou. Recarregar.".

### Cobrança
9. **Dado** um pedido `Open`, **quando** o atendente clica "Gerar cobrança", **então** o diálogo traz o vencimento padrão (1 dia útil, até 3) e `POST /orders/{id}/charge` sai com `Idempotency-Key`; 202 mostra "Gerando o boleto no banco…" e o pedido vai a "Aguardando pagamento"; `bank_slip_method_missing` mostra o texto da ATD-01 CA 13. Opção da conta: exigir `supervisor` para cobrança acima de R$ {valor} (padrão desligada).
10. **[Segurança]** **Dado** o aviso `billing_slip.issued` do título do pedido (ATD-01; `resource` = `/charges/{receivable_id}`, INT-04 CA 1), **quando** chega, **então** o cartão mostra "Boleto pronto" com **"Mandar cobrança"** (prévia: valor, vencimento, linha digitável, Pix, PDF); a opção "Mandar sozinho quando sair" existe por conversa e vem **desligada**; quando ligada, o job que manda **reconfere o destino na hora** (ATD-02 CA 11) e, divergindo, não manda e avisa o atendente. A mensagem é sensível (ATD-02 CA 16). Boleto que não sai em 2 min: "O banco está demorando" com "Ver no BemAqui Empresas"; `billing_slip.cancelled`: "Cobrança cancelada: {motivo}" com "Gerar de novo".
11. **Dado** `sales_order.paid`, **quando** chega, **então** o cartão mostra "Pago" (o aviso ao cliente é da ATD-05a).
12. **Dado** um pedido `Draft`, `Open` ou `AwaitingPayment`, **quando** o atendente clica "Cancelar pedido" com motivo, **então** `POST /orders/{id}/cancel`; depois de pago, "Este pedido já foi pago. O cancelamento é feito no BemAqui Empresas.". Opção da conta: exigir `supervisor` para cancelar (padrão desligada).

### Pedidos parados e reconciliação
13. **Dado** um rascunho do Conecta sem "Cliente confirmou" há mais de `ErpOrders:DraftTtlHours` (padrão **24 h**), **quando** o job `ErpDraftExpiryJob` roda (a cada hora, por tenant), **então** cancela no ERP com o motivo "Rascunho sem confirmação do cliente" e o cartão mostra "Pedido cancelado: o cliente não confirmou" com "Recriar pedido".
14. **Dado** o pedido de aplicativo que expirou no ERP (2 h sem cobrança), **quando** chega `sales_order.cancelled`, **então** o cartão mostra "Pedido cancelado: prazo da reserva vencido" com "Recriar pedido" (carrinho preenchido).
15. **Dado** `ErpOrderLink` em `Draft`, `Open` ou `AwaitingPayment`, **quando** o job `ErpOrderReconcileJob` roda (a cada **10 min** e logo depois de uma reconexão), **então** relê `GET /orders/{id}` e aplica a situação atual, idempotente por **(pedido, situação)**: um aviso perdido não deixa o cartão parado, e a mudança passa pelas mesmas reações dos avisos (ATD-05a).

### Funil, registro e permissões
16. **Dado** um pedido criado pelo Conecta, **quando** nasce, **então** o `ErpOrderLink` guarda o negócio (`Deal`) aberto mais recente do contato ou cria um no estágio padrão ("PV-…", valor = total); mover por evento é da ATD-05b.
17. **Dado** qualquer ação de pedido, **quando** feita, **então** vai ao `erp_access_log` (ATD-02) com atendente, ação, pedido e data.
18. **Dado** os papéis, **quando** usam o pedido, **então** criar, confirmar, cobrar e cancelar: `agent`, `supervisor`, `admin` com acesso à conversa, respeitando as opções dos CA 9 e 12; quem não pode vê "Peça a um supervisor para…".

## Regras de negócio e técnicas
- **Rotas do ERP:** `GET /products`, `GET /products/{id}` (`catalog.view`, `inventory.view`); `POST /customers` (`parties.create`), `PATCH /customers/{id}` (`parties.edit`); `POST /orders`, `POST /orders/{id}/confirm` (`sales.create`); `POST /orders/{id}/charge`, `POST /orders/{id}/cancel` (`sales.edit`); `GET /orders/{id}` (`sales.view`); `GET /charges/{id}`, `GET /charges/{id}/pdf` (`finance.view`).
- **Idempotência:** `Idempotency-Key` em todo `POST`, gravada antes; `externalId` único por rascunho; o ERP escopa por aplicativo + empresa (INT-04 CA 6).
- **Avisos usados:** `sales_order.created|confirmed|paid|cancelled`, `billing_slip.issued|paid|cancelled`; sempre relendo `GET /orders/{id}`.
- **Pedidos feitos fora do Conecta** aparecem no painel, mas só os do Conecta têm ações (o ERP responde 404 para os demais).

## O que muda em cada repositório
**Conecta (`bemaqui-atende-api`):** `ErpOrderDraft`, `ErpOrderLink` (`erp_order_id`, `number`, `status`, `conversation_id`, `contact_id`, `deal_id`, `created_by_user_id`, `external_id`, `charge_id`, `customer_confirmed_message_id`, `auto_send_charge`); migration aditiva; comandos (criar, mandar resumo, cliente confirmou, confirmar, cobrar, mandar cobrança, cancelar, cancelar parados); `ErpDraftExpiryJob`, `ErpOrderReconcileJob`; reações aos avisos.
**Conecta (`bemaqui-atende-web`):** "Novo pedido" (busca, carrinho, cadastro mínimo) e cartão do pedido com botão por etapa e prévias.
**Conecta (`bemaqui-conecta`, app):** carrinho em **folha própria com o total sempre à vista**; confirmações que mandam algo ao cliente em folha com a prévia; botões de 56 px.

## Telas e estados (Conecta)
- **Novo pedido:** busca (carregando, "Nenhum produto com esse nome", erro); carrinho com o total do BemAqui Empresas; "Criar pedido" desabilitado enquanto envia.
- **Cartão:** etapa atual, botão principal, faixa de problema com a ação (CA 8 a 10, 13, 14), "Ver no BemAqui Empresas".
- **Verbos:** "Criar pedido" → "Pedido criado"; "Mandar resumo" → "Resumo enviado"; "Cliente confirmou" → "Confirmação registrada"; "Confirmar pedido" → "Pedido confirmado"; "Gerar cobrança" → "Cobrança gerada"; "Mandar cobrança" → "Cobrança enviada"; "Cancelar pedido" → "Pedido cancelado".

## Plano de testes
- **Unidade:** botão por etapa; chave e `externalId`; sugestão de "sim" sem marcar sozinho; tradução dos erros.
- **Integração (Conecta, ERP falso):** `Draft → Open → AwaitingPayment → Paid` com avisos fora de ordem e um aviso perdido recuperado pela reconciliação; falha de rede sem duplicar e `idempotency_in_progress`; cada erro do CA 8; "Cancelar pedidos parados"; rascunho vencido em 24 h; expiração de 2 h; "Mandar sozinho" com telefone trocado no meio (não manda); pedido criado antes de reconectar segue confirmável depois (INT-04); dois tenants.

## Tarefas
- [ ] Conecta API: entidades, comandos, jobs, reações.
- [ ] Conecta web e app: novo pedido, carrinho, cartão.
- [ ] Manual do usuário: "Vender pelo BemAqui Empresas na conversa".
- [ ] Fluxo do repositório do Conecta.

## Dev Notes
- **Conecta:** `Domain/Entities/Deal.cs`, `Application/Features/Pipeline/Commands/MoveDealCommand.cs`, `Infrastructure/Automations/AutomationEngine.cs` (`create_deal`), `Infrastructure/Jobs/YampiPaymentPollingJob.cs` (precedente; aqui avisos + reconciliação).
- **ERP:** VND-02a Change Log (`price_changed`, limites 50/200); INT-03 Change Log itens 2 a 4; INT-04 CA 6.

## Fora do escopo
- Orçamento (ATD-03b); IA (ATD-04); avisos e funil (ATD-05a/b); cartão (PP-21), Pix `cob`, frete (VND-02b, ECM-04); editar linhas de pedido criado (cancela e recria).

## Pontos em aberto
Decididos (PO, 27/11/2026): sem desconto no Conecta na v1; "Mandar sozinho" desligado por padrão; cancelar antes do pagamento liberado ao `agent`, com opção de exigir `supervisor`; pedido do aplicativo + empresa (decisão 3). Nada pendente.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir da VND-02a e da INT-03; ATD-03 dividida em 03a e 03b. | Claude (planejamento) |
| 27/11/2026 | 0.2 | Revisões de 27/11/2026: arquitetura, segurança, UX; decisões do PO. Rascunho parado cancelado em 24 h; "Cancelar pedidos parados"; reconciliação a cada 10 min e na reconexão; "Mandar sozinho" desligado e destino reconferido no job; botão por etapa com prévia; "Cliente confirmou" nunca automático; erros traduzidos; cadastro mínimo e "Pedir o endereço ao cliente"; opções de exigir supervisor; folhas no app; pedido do aplicativo + empresa (INT-04). | Claude (planejamento) |
