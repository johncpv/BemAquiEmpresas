# INT-04 — Ajustes da base para o Conecta: avisos, segredo, dono do pedido, escopos novos, pareamento e prova de identidade

| Campo | Valor |
|---|---|
| ID | INT-04 |
| Título | Correções e acréscimos no ERP achados na revisão da F2: `resource` dos avisos alinhado ao `integration-v1` (com `GET /invoices/{id}` novo e `party_id` onde faltava), cabeçalho `BemAqui-Connection`, `webhook_secret` reentregável até a confirmação, pedido do aplicativo pertencendo a **aplicativo + tenant** (não à conexão), escopos `quotes.create` e `conecta.send_whatsapp` com o aviso `whatsapp_message.requested` numa reautorização única, código de pareamento no "Conectar", origem do telefone do cliente e código de identidade de 6 dígitos, e o atendente identificado na auditoria |
| Tamanho | M |
| Status | Done (28/11/2026) |
| Requisitos | [ADR 0053](../adr/0053-aplicativos-conectados.md) (adendo §5, §13 e §14 novo) · [INT-01](INT-01-aplicativos-conectados.md) · [INT-02](INT-02-webhooks-de-saida.md) · [INT-03](INT-03-api-integracao-v1.md) · [VND-02a](VND-02a-pedido-de-venda-nucleo.md) · decisões do PO de 27/11/2026 (1 a 4) |
| Dependências | INT-01, INT-02, INT-03, VND-02a (Done) |
| Bloqueia | ATD-01 (todas as ATD) |
| Repositórios | **ERP** |
| Ordem | 0 (F2, antes da ATD-01) |

## User story
Como **time das integrações**, quero **corrigir na base do ERP o que a revisão da F2 achou**, para **o Conecta consumir avisos e rotas sem adivinhar caminho, sem perder o segredo numa resposta perdida, sem deixar pedido órfão ao reconectar e sem confundir quem é o cliente**.

## Contexto (conferido no código em 27/11/2026)
- `OutboundWebhookCatalog` emite `resource` `/api/v1/integration/sales-orders/{id}`, `/billing-slips/{id}` e `/invoices/{id}`, que **não existem** no `integration-v1` (lá: `/orders/{id}`, `/charges/{id}` pelo **título**, e só `/invoices/{id}/danfe`); `billing_slip.*` leva o id do **boleto**; `billing_slip.cancelled`, `invoice.cancelled` e `invoice.rejected` não levam `party_id`.
- O campo `tenant` do aviso é o **slug** (`tenant.Key`), não o host.
- `OAuthTokenService` chama `TakePendingSecretAsync` depois de emitir o par novo, e o método **zera** o pendente: se a resposta se perder, o `bqr_` repetido em 10 s devolve par novo **sem** o segredo.
- O pedido grava `SourceAppId = grant.ConnectionId` (`IntegrationOrderEndpoints`): ações, limite de 5 em aberto, `externalId` e idempotência ficam presos à **conexão**; reconectar deixa os pedidos do Conecta sem dono.
- Não há módulo `whatsapp` no `ModuleCatalog`.

## Critérios de aceite

### Avisos
1. **Dado** o catálogo v1, **quando** um aviso sai, **então** `resource` é uma rota que existe no `integration-v1`: `sales_order.*` → `/api/v1/integration/orders/{id}`; `billing_slip.*` → `/api/v1/integration/charges/{receivable_id}` (o título; `data.id` continua o do boleto); `invoice.*` → `/api/v1/integration/invoices/{id}` (rota nova, CA 2); `app.disconnected` e `ping` → `/api/v1/integration/connection`. Teste: para cada tipo, o `resource` casa com um caminho do `openapi-integration-v1.json`.
2. **Dado** `fiscal.view`, **quando** o aplicativo chama `GET /api/v1/integration/invoices/{id}`, **então** recebe o mesmo DTO da nota do pedido (tipo, situação, série, número, chave, data, `party_id`, `sales_order_id` quando houver, link do DANFE), só de nota de produção e não descartada; outra empresa, inexistente ou descartada: 404.
3. **Dado** `billing_slip.cancelled`, `invoice.cancelled` e `invoice.rejected`, **quando** o tradutor monta o aviso, **então** inclui `party_id` lido do título ou da nota pelo contrato do módulo dono (campo novo, aditivo, contrato `1`); sem destinatário (nota rejeitada antes de ter um), o campo vai nulo.
4. **Dado** qualquer aviso, **quando** sai, **então** leva o cabeçalho **`BemAqui-Connection: <connection_id>`** (fora da assinatura, que segue sobre `{timestamp}.{corpo}`), para o recebedor achar o segredo sem ler corpo não autenticado; a ajuda e o contrato da ATD-01 dizem que `tenant` é o **identificador da empresa (slug)**, estável, e não o host.

### Segredo reentregável
5. **[Segurança]** **Dado** um `webhook_secret` pendente (conexão nova ou troca), **quando** o ERP responde ao `/oauth/token`, **então** o segredo vai em **toda** resposta de troca e de renovação **até** o aplicativo fazer uma renovação com o `bqr_` que veio **na mesma resposta** que levou o segredo (prova de que gravou aquela resposta); só então o pendente é baixado. A repetição do `bqr_` dentro dos 10 s de tolerância também devolve o segredo. Teste: resposta perdida na troca e na renovação, segredo recuperado na repetição.

### Dono do pedido (decisão do PO 3)
6. **[Segurança]** **Dado** um pedido criado por aplicativo, **quando** é gravado, **então** `source_app_id` = o **aplicativo** (`connected_apps.id`) e `source_connection_id` = a conexão que criou (coluna nova, só para trilha); ações (`confirm`, `charge`, `cancel`), o limite de 5 em aberto por cliente, a unicidade `(origin, source_app_id, external_id)` e o escopo da `Idempotency-Key` (`tid + aplicativo + rota + hash do corpo`) passam a ser por **aplicativo + tenant**; reconectar ou autorizar de novo não deixa pedido órfão nem perde a idempotência. Migration aditiva: cria `source_connection_id`, copia o valor atual e troca `source_app_id` pelo aplicativo da conexão. Teste: pedido criado antes de reconectar é confirmado e cobrado depois.

### Escopos novos numa reautorização única
7. **Dado** o adendo à ADR 0053 §5, **quando** entra, **então** `ConnectedAppScopes.Conecta` ganha **`quotes.create`** ("Abrir pedidos de orçamento") e **`conecta.send_whatsapp`** ("Mandar avisos do ERP pelo WhatsApp do Conecta"; ação específica nova do módulo `conecta`, ADR 0003, porque não há módulo `whatsapp`); o catálogo de avisos ganha **`whatsapp_message.requested`** (escopo `conecta.send_whatsapp`, `data = {id, template, not_after}`).
8. **Dado** uma conexão ativa cuja concessão não tem um escopo que hoje seria concedido (ou cuja inscrição não tem um tipo de aviso que a concessão permitiria), **quando** o Proprietário abre Configurações → Integrações → Conecta, **então** vê "O Conecta pode ganhar: Abrir pedidos de orçamento · Mandar avisos pelo WhatsApp" com **"Autorizar de novo"** (fluxo da INT-01; a conexão nova nasce com os escopos e a inscrição com os tipos novos, numa vez só).

### Pareamento (decisão do PO 2)
9. **Dado** "Conectar" no ERP, **quando** o Proprietário clica, **então** o diálogo avisa "Você vai confirmar no Conecta com um usuário administrador." e pede o **código de pareamento** gerado no Conecta (Configurações → Integrações → BemAqui Empresas, ATD-01); o ERP só confere o formato (`XXXX-XXXX`, letras e números sem ambíguos) e o leva à `StartUrl` como `pairing=`; sem código, "Conectar" fica desabilitado com a `<Dica>` "Peça o código a quem administra o Conecta".
10. **Dado** o retorno do Conecta depois do consentimento, **quando** o navegador volta a `/app/configuracoes/integracoes/conecta?conectado=1`, **então** a página mostra "Conectado à conta {account_label} do Conecta" e o próximo passo ("Abra uma conversa no Conecta: o painel do cliente mostra pedidos e cobranças").

### Origem do telefone e código de identidade (decisão do PO 1)
11. **[Segurança]** **Dado** um telefone ou e-mail gravado por aplicativo (`POST /customers` ou `PATCH /customers/{id}`), **quando** é salvo, **então** o contato guarda a origem "aplicativo" até uma pessoa confirmar no ERP; `GET /customers?phone=` devolve `phoneSetByApp: true` nesse caso (o Conecta não aceita esse telefone como prova).
12. **[Segurança]** **Dado** `parties.view`, **quando** o aplicativo chama `POST /api/v1/integration/customers/{id}/identity-challenges` com `{channel: "email"|"phone"}` e `Idempotency-Key`, **então** o ERP manda um código de **6 dígitos** (válido por 10 min) **ao e-mail ou telefone que já está no cadastro e não veio de aplicativo** (e-mail pelo `IEmailSender`; telefone pela mensagem de autenticação da ATD-06a, quando houver), responde `{challengeId, channel, maskedDestination}` (`m***@g***.com`, `(**) *****-4321`) e nunca o destino completo; sem destino válido, 422 `no_verified_contact`; limite de 3 desafios por cliente por hora e 20 por conexão por hora.
13. **[Segurança]** **Dado** `POST /api/v1/integration/customers/{id}/identity-challenges/{challengeId}/verify` com `{code}`, **quando** o código confere (tempo constante, no máximo 5 tentativas, depois o desafio morre), **então** responde `{verified: true}`; errado, `{verified: false, attemptsLeft}`; vencido, 410; cada desafio e verificação fica na auditoria do cliente.

### Atendente na auditoria
14. **Dado** o cabeçalho opcional **`BemAqui-On-Behalf-Of`** (`<id opaco do atendente no Conecta>;<primeiro nome>`, até 80 caracteres, sanitizado), **quando** o aplicativo escreve ou lê dado auditado, **então** a trilha mostra "Integração Conecta (autorizada por Fulano) · atendente {nome}"; o cabeçalho **não** muda permissão nenhuma.

## O que muda (ERP)
- `OutboundWebhookCatalog` (resources e `party_id`), `OutboundWebhookDelivery` (cabeçalho), `OutboundSubscriptionService` (pendente até a prova), `OAuthTokenService` (segredo também na tolerância de 10 s).
- `IntegrationOrderEndpoints` e `SalesOrderService` (dono = aplicativo), `IdempotencyFilter.Scope` (aplicativo), migration aditiva em `sales.orders`.
- Rotas novas: `GET /integration/invoices/{id}`, `POST /integration/customers/{id}/identity-challenges`, `POST …/{challengeId}/verify`; campo `phoneSetByApp`; origem do contato em `parties` (migration aditiva).
- `ConnectedAppScopes.Conecta`, `ModuleCatalog` (`conecta.send_whatsapp`), catálogo de avisos, "O Conecta pode ganhar" e "Autorizar de novo", pareamento no "Conectar", `?conectado=1`; auditoria com o atendente.
- Adendos: ADR 0053 §5 (escopos novos), §13 (idempotência por aplicativo), §14 novo (pareamento, `BemAqui-Connection`, `BemAqui-On-Behalf-Of`, prova de identidade); `openapi-integration-v1.json` (aditivo, teste de contrato); ajudas `configuracoes-integracoes-conecta.md`, `configuracoes-integracoes-eventos-enviados.md` (cabeçalho, `tenant` é o slug, `resource`).

## Plano de testes
- **Unidade:** `resource` de cada tipo casa com o contrato (CA 1); formato do pareamento (CA 9); máscara do destino (CA 12).
- **Integração (Testcontainers):** `GET /invoices/{id}` com IDOR (CA 2); `party_id` nos três tipos (CA 3); cabeçalho presente e fora da assinatura (CA 4); segredo perdido na troca e na renovação e recuperado (CA 5); pedido de antes da reconexão confirmado depois, e a mesma chave depois de reconectar (CA 6); reautorização única com escopos e aviso novos (CA 7, 8); `phoneSetByApp` (CA 11); desafio, limite, 5 tentativas e vencido (CA 12, 13); trilha com o atendente (CA 14).

## Tarefas
- [x] Avisos (CA 1 a 4) e segredo (CA 5).
- [x] Dono do pedido e idempotência (CA 6), com migration.
- [x] Escopos, aviso novo, "Autorizar de novo" e pareamento (CA 7 a 10).
- [x] Origem do telefone e código de identidade (CA 11 a 13); atendente na trilha (CA 14).
- [x] Adendos de ADR, OpenAPI, ajudas.

## Dev Notes
- `src/BuildingBlocks/BemAqui.BuildingBlocks/Webhooks/OutboundWebhookCatalog.cs`, `OutboundWebhookDelivery.cs` (linha do `Build` com `tenant.Key`), `OutboundSubscriptionService.cs` (`TakePendingSecretAsync`), `src/Modules/Identity/BemAqui.Identity.Application/ConnectedApps/OAuthTokenService.cs`, `src/Modules/Sales/BemAqui.Sales.Endpoints/IntegrationOrderEndpoints.cs` (`new SalesOrderActor(origin, grant.GrantorId, grant.ConnectionId, grant.AppName)`), `src/Modules/Sales/BemAqui.Sales.Application/SalesOrderService.cs`, `src/BuildingBlocks/BemAqui.BuildingBlocks/Web/IdempotencyFilter.cs`, `src/BuildingBlocks/BemAqui.BuildingBlocks/Modules/ConnectedAppScopes.cs`, `src/BuildingBlocks/BemAqui.BuildingBlocks/Integration/IntegrationApi.cs` (lista de códigos).

## Fora do escopo
- O lado do Conecta (ATD-01 em diante); a rota de pedido de orçamento (ATD-03b) e as rotas de mensagens do WhatsApp (ATD-06a), que usam os escopos criados aqui.

## Pontos em aberto (para o PO)
1. **Nome do escopo do WhatsApp:** `conecta.send_whatsapp` (ação do módulo `conecta`) em vez de `whatsapp.send`, porque não existe módulo `whatsapp` e criar um só para o escopo mudaria menu e liberação. **Recomendação:** `conecta.send_whatsapp`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story criada a partir das revisões de 27/11/2026 da F2 (arquitetura, segurança, UX) e das decisões do PO; cada achado conferido no código do ERP. | Claude (planejamento) |
| 28/11/2026 | 1.0 | Implementada (ERP). Migration aditiva `ConnectedAppsInt04`: `sales.orders.source_connection_id` (cópia da conexão e troca de `source_app_id` pelo aplicativo; pedidos com o mesmo `externalId` em conexões diferentes ficam, os mais antigos, na conexão, sem perda de dado), `identity.app_tokens.webhook_secret_version`, `shared.outbound_webhook_subscriptions.secret_version`, `parties.parties.email_set_by_app`/`phone_set_by_app` e `parties.identity_challenges`. Rotas novas: `GET /integration/invoices/{id}`, `POST /integration/customers/{id}/identity-challenges` e `…/{challengeId}/verify`, `POST /parties/{id}/contact-confirmation` (botão "Confirmar contato" no cadastro). Ação nova `conecta.send_whatsapp` (sem seed patch). Decisões sem o PO (a opção mais segura): (1) o código de pareamento é obrigatório em "Conectar" e opcional em "Autorizar de novo" (já existe conexão ativa ou suspensa); (2) `phone` no código de identidade responde 422 `channel_unavailable` até a ATD-06a; (3) o `resource` de `whatsapp_message.requested` é `/integration/whatsapp-messages/{id}`, rota da ATD-06a (o teste do CA 1 exclui só esse tipo; nenhum aviso dele sai antes); (4) a troca do código não pode ser repetida (código de uso único), então na conexão nova o segredo fica pendente até a primeira renovação com o `bqr_` da troca; a prova vale só para a versão atual do segredo; (5) "O Conecta pode ganhar" lista só o que hoje seria concedido (permissões atuais de quem autorizou); (6) o contato só perde a marca de aplicativo por "Confirmar contato" ou quando uma pessoa digita outro valor; (7) o código de identidade é guardado só como HMAC com sal, confere só pelo mesmo aplicativo, e no `email_logs` o código é apagado depois do envio (`EmailSecrets`); (8) do `BemAqui-On-Behalf-Of` fica na trilha só o primeiro nome sanitizado (até 40 caracteres), não o id; (9) `GET /invoices/{id}` não exige destinatário (a nota rejeitada sem destinatário também é lida); `sales_order_id` vai nulo até a VND-02b. | Claude (dev) |
