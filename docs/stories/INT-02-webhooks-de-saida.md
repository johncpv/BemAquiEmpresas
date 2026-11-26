# INT-02 — Webhooks de saída: inscrição por aplicativo conectado, assinatura HMAC, nova tentativa e registro de entregas

| Campo | Valor |
|---|---|
| ID | INT-02 |
| Título | O ERP avisa os aplicativos conectados: inscrição criada na conexão com a lista de eventos, corpo mínimo com o endereço para consultar de novo, assinatura HMAC-SHA256 sobre `{timestamp}.{corpo}` com segredo rotativo (24 h de sobreposição), entrega pelo Hangfire com espera crescente, parada e "Reenviar", pausa automática de endpoint que falha, proteção contra SSRF e a aba "Enviados" em Configurações → Integrações → Eventos |
| Tamanho | M |
| Status | Draft |
| Requisitos | ADR 0052 §1 (INT-02) · ADR 0006 (outbox e inbox) · ADR 0007 (segredos; tipo novo) · ADR 0031 (§5 retenção de 90 dias, §6 rotação de 24 h, espelhados na saída) · ADR 0019 §12 (jobs idempotentes) · ADR 0037 (inventário de dado pessoal) · R-SEC-5 |
| Dependências | INT-01 (conexões, `conecta.view`, eventos `AppConnected`/`AppConnectionRevoked`), FND-16 (aba "Eventos recebidos"), FND-12 (alertas), VND-02 (eventos do pedido de venda) |
| Bloqueia | ATD-01 (recebimento de eventos no Conecta), ATD-05 (avisos automáticos), API-01 |
| Ordem de execução | 3 (F1 — Base no ERP) |

## User story
Como **Proprietário que conectou o Conecta**, quero **que o ERP avise o Conecta na hora em que o pedido é pago, a nota sai ou o boleto vence**, para **o cliente receber o aviso no WhatsApp sem ninguém da equipe lembrar de mandar**.

## Contexto e referências
- ADR 0052 §1: "inscrição por evento, HMAC-SHA256 sobre `{timestamp}.{corpo}`, nova tentativa pelo Hangfire, registro de entregas e segredo com rotação de 24 h". Plano §3.2.
- Hoje o outbox é só interno (`shared.outbox_messages`, consumidores com inbox) e a única tela de webhooks é a de entrada (FND-16, ADR 0031). Esta story é o espelho de saída: mesma retenção, mesma rotação e mesmo padrão de tela.
- **Eventos internos não saem crus:** cada módulo dono traduz o seu evento de contrato para um evento público, mínimo e estável (`tipo` com versão de contrato), e o aplicativo consulta o recurso de novo pela API (INT-03). Nunca vai custo, imposto, margem nem dado pessoal no corpo.

## Critérios de aceite
**Inscrição**
1. **Dado** uma conexão nova do Conecta (`AppConnected`, INT-01), **quando** o consumidor da inscrição processa, **então** nasce a inscrição (`shared.outbound_webhook_subscriptions`) com a URL da configuração `Integrations:Conecta:WebhookUrl`, os eventos de `Integrations:Conecta:DefaultEvents` **limitados aos escopos concedidos** (CA 8) e um segredo de 32 bytes cifrado com o `SecretKind` novo `OutboundWebhookSecret` (propósito `outbound-webhook-secret` + id da inscrição); o segredo é entregue **uma única vez** ao aplicativo no campo `webhook_secret` da resposta do `POST /api/v1/oauth/token` da troca do código (canal servidor a servidor, `no-store`) e nunca volta numa resposta, tela ou log.
2. **Dado** o aplicativo com `conecta.view`, **quando** chama `GET /api/v1/integration/webhooks/subscription` ou `PUT` com a lista de eventos, **então** vê e troca **só os eventos** (tipos desconhecidos ou sem o escopo: 400 com a lista); a URL só muda para outra cujo host esteja em `Integrations:Conecta:WebhookAllowedHosts` (CA 17); mudança auditada com `Origin` "Integração Conecta".
3. **Dado** a conexão revogada ou suspensa (INT-01), **quando** o evento chega, **então** a inscrição vai a `Ended` (revogada) ou `Held` (suspensa); antes de encerrar, sai um último evento `app.disconnected` assinado com o segredo atual (uma série de tentativas, CA 10), e as entregas pendentes viram `Skipped` "Conexão encerrada".

**Corpo e assinatura**
4. **Dado** qualquer evento entregue, **quando** sai, **então** o corpo é JSON UTF-8:
   `{"id":"<uuid v7>","type":"invoice.authorized","contract":1,"occurred_at":"2026-12-03T14:05:00Z","tenant":"<host primário do tenant>","connection_id":"<id>","data":{…mínimo…},"resource":"https://<host>/api/v1/integration/invoices/<id>"}`; `occurred_at` em UTC; `data` só com ids, número legível, situação, valor e datas (tabela do catálogo, Regras); **nenhum** nome, documento, telefone, e-mail, endereço, custo, imposto ou margem (teste por tipo com lista de campos proibidos).
5. **[Segurança]** **Dado** o corpo serializado uma vez (os mesmos bytes em todas as tentativas), **quando** a entrega sai, **então** leva os cabeçalhos `BemAqui-Event-Id`, `BemAqui-Event-Type`, `BemAqui-Timestamp` (segundos Unix da tentativa) e `BemAqui-Signature: v1=<hex do HMAC-SHA256 de "{timestamp}.{corpo}">`; durante a sobreposição de rotação vão as duas assinaturas, `v1=<nova>,v1=<antiga>`; `User-Agent: BemAqui-Webhooks/1`. A documentação para o recebedor (ATD-01) pede janela de 5 min e comparação em tempo constante, como a ADR 0031 faz na entrada.
6. **[Segurança]** **Dado** o aplicativo pedindo um segredo novo (`POST /api/v1/integration/webhooks/secret/rotate`, `conecta.view`, rate limit 5/dia por inscrição), **quando** o ERP gera, **então** devolve o segredo novo uma vez, guarda o anterior cifrado com `previous_valid_until` = agora + 24 h e assina com os dois até lá; depois, só o novo; rotação auditada (sem valor) e notificada aos Proprietários. Comprometimento do lado do ERP se resolve com "Desconectar" (INT-01 CA 18).

**Origem dos eventos e idempotência**
7. **Dado** um evento de contrato do catálogo (Regras) gravado no outbox pelo módulo dono, **quando** o despachante entrega ao tradutor daquele módulo (`IIntegrationEventConsumer<T>`, com inbox), **então** o tradutor grava **na mesma transação** um `shared.outbound_webhook_events` (id v7, tipo, escopo exigido, corpo) e uma entrega `Pending` por inscrição ativa que assina o tipo; reentrega da mesma mensagem não duplica (inbox por handler e mensagem); após o commit, enfileira o job de entrega; um varredor recoloca entregas `Pending` paradas há mais de 5 min (como o da ADR 0031 §3).
8. **[Segurança]** **Dado** uma entrega, **quando** o job vai enviar, **então** reavalia o acesso: conexão `Active`, módulo `conecta` e o módulo do evento liberados, e o escopo exigido nas **permissões efetivas atuais** da conexão (concessão ∩ autorizador, INT-01 CA 13); sem isso, `Skipped` com o motivo ("Sem o escopo fiscal.view") e nada sai.
9. **Dado** o mesmo evento enviado mais de uma vez (nova tentativa ou "Reenviar"), **quando** chega ao aplicativo, **então** leva **o mesmo `id` e o mesmo corpo** (só o timestamp e a assinatura mudam); a ordem **não é garantida**: o aplicativo usa o `id` para não processar duas vezes e o `occurred_at` mais a consulta ao `resource` para saber o estado atual (documentado na ajuda e no contrato da ATD-01).

**Entrega, nova tentativa e parada**
10. **Dado** um envio, **quando** o recebedor responde 2xx em até 10 s, **então** `Delivered` com código, duração e hora; qualquer outra resposta, 3xx (redirecionamento **não é seguido**), *timeout* (conexão 5 s, total 10 s) ou erro de rede é falha: nova tentativa em 1 min, 5 min, 30 min, 2 h, 6 h, 12 h e 24 h (8 tentativas, ±10% de variação); 429 e 503 com `Retry-After` respeitam o pedido até 1 h; depois da 8ª, `DeadLettered`.
11. **Dado** uma entrega `DeadLettered` ou `Delivered`, **quando** alguém com `settings.edit` clica "Reenviar" na aba "Enviados", **então** uma nova série de tentativas começa com o mesmo `id` e o mesmo corpo (se ainda guardado, CA 15); auditado.
12. **Dado** uma inscrição sem nenhuma entrega com sucesso há 72 h **e** com 20 entregas seguidas em falha, **quando** a próxima falha, **então** a inscrição vai a `Paused` ("Pausada: o Conecta não respondeu desde dd/mm hh:mm"), as entregas novas ficam `Held` (guardadas, sem tentar), sai o alerta `outbound_webhooks` (FND-12, `IntegrationHealthTracker`) e a notificação aos Proprietários com o link para a aba; "Reativar" (`settings.edit`) envia as retidas dos últimos 7 dias, e as mais antigas vão a `DeadLettered`.
13. **Dado** a aba "Enviados", **quando** alguém com `settings.edit` clica "Enviar teste", **então** sai um evento `ping` (corpo sem dado de negócio) pela mesma esteira, e o resultado aparece na lista em segundos.
14. **Dado** o envio por inscrição, **quando** há fila, **então** no máximo 4 entregas simultâneas por inscrição e 32 por instância; jobs na fila `default` do Hangfire, sempre com `tenantId` (`TenantJob`), idempotentes pela entrega (a transição de estado usa `xmin`: duas execuções da mesma tentativa enviam uma vez; teste de execução dupla).

**Registro, retenção e LGPD**
15. **Dado** as entregas, **quando** passam 90 dias, **então** o job diário por tenant apaga o corpo de eventos com todas as entregas finais (`Delivered`, `Skipped`, `DeadLettered`) e a linha fica (tipo, ids, datas, situação, código), como na entrada (ADR 0031 §5); "Reenviar" de evento sem corpo fica desabilitado com a `<Dica>` "O conteúdo foi apagado depois de 90 dias".
16. **Dado** a aba "Enviados", **quando** abro um evento, **então** vejo o corpo (já sem dado pessoal, CA 4) e cada tentativa: hora, código HTTP, duração e o erro resumido (até 500 caracteres, **sem** o corpo da resposta do recebedor, que é lido até 4 KB e descartado).

**SSRF e rede**
17. **[Segurança]** **Dado** uma URL de inscrição, **quando** é gravada e **a cada conexão**, **então**: só `https`, porta 443, sem usuário e senha na URL, host na lista da configuração do aplicativo (primeira parte: `WebhookAllowedHosts`); o `HttpClient` nomeado `outbound-webhooks` confere o **IP de fato conectado** (`SocketsHttpHandler.ConnectCallback`) e recusa loopback, RFC 1918, link-local (169.254.0.0/16, inclusive metadados), CGNAT 100.64.0.0/10, `0.0.0.0/8`, multicast, `::1`, `fc00::/7` e `fe80::/10` (vale contra DNS *rebinding*); sem cookies, sem proxy com credencial, `AllowAutoRedirect = false`. Rede privada só com `Integrations:OutboundWebhooks:AllowPrivateNetworks` em `Development` e nos testes.
18. **[Segurança]** **Dado** qualquer entrega, **quando** algo vai para o log, **então** nem o segredo, nem a assinatura, nem o corpo aparecem (teste de canário).

**Permissões e telas**
19. **Dado** o módulo `conecta` não liberado, **quando** abro a aba "Enviados" ou chamo as rotas, **então** 403 `module_not_released` (rotas de aplicativo: INT-01 CA 13); ver a aba exige `conecta.view`; "Reenviar", "Enviar teste", "Reativar": `settings.edit`; IDOR: entrega de outro tenant não existe no banco, e id inexistente responde 404.

## Regras de negócio e técnicas
- **Catálogo de eventos v1** (contrato `1`; campo novo é compatível, remover ou mudar sentido exige contrato `2`):

| Tipo público | Origem (contrato) | Escopo | `data` |
|---|---|---|---|
| `sales_order.created`, `.paid`, `.invoiced`, `.shipped`, `.delivered`, `.cancelled` | eventos do pedido da VND-02 (nomes definidos lá) | `sales.view` | `id`, `number`, `status`, `total`, `party_id` |
| `sale.completed`, `sale.cancelled` | `SaleCompleted`, `SaleCancelled` (Sales) | `sales.view` | `id`, `number`, `channel`, `total`, `business_date` |
| `billing_slip.issued`, `billing_slip.cancelled` | `BillingSlipIssued`, `BillingSlipCancelled` (Finance) | `finance.view` | `id`, `receivable_id`, `amount`, `due_date`, `party_id` / `id`, `reason`, `expired` |
| `billing_slip.paid` | **novo** `BillingSlipPaid` de contrato (Finance.Contracts), publicado no outbox na mesma transação da baixa da BNK-02 | `finance.view` | `id`, `receivable_id`, `paid_amount`, `credit_date` |
| `receivable.settled`, `receivable.settlement_reversed` | `ReceivableSettled` (`IsReversal`) | `finance.view` | `receivable_id`, `settled_on`, `principal`, `party_id` |
| `invoice.authorized`, `invoice.cancelled`, `invoice.rejected` | `InvoiceAuthorized`, `InvoiceCancelled`, `InvoiceRejected` (Fiscal) | `fiscal.view` | `id`, `type`, `issued_on`/`cancelled_on`, `total`, `party_id` (nunca `Taxes` nem `CostTaxAmount`) |
| `app.disconnected`, `ping` | sistema | — | `connection_id` |

- **Tradução no módulo dono:** cada módulo registra o tradutor no próprio `Application` (ex.: `FiscalOutboundWebhooks : IIntegrationEventConsumer<InvoiceAuthorized>`) e chama o contrato novo `IOutboundWebhookPublisher` (BuildingBlocks). O BuildingBlocks não referencia módulo nenhum (ADR 0006, teste de arquitetura).
- **Acesso da conexão:** contrato novo `IConnectedAppAccess` (BuildingBlocks/Identity, implementado pelo Identity) devolve situação e escopos efetivos da conexão; usado pelo job a cada entrega (CA 8).
- **Situações:** inscrição `Active` → `Paused` (CA 12) → `Active` (Reativar); `Held` (conexão suspensa) → `Active` (nova conexão cria inscrição nova; a suspensa vai a `Ended`); `Ended` é final. Entrega `Pending` → `Retrying` → `Delivered` | `DeadLettered`; `Held`; `Skipped`.
- **Ordem e tamanho:** sem garantia de ordem (CA 9); corpo de no máximo 64 KB (evento maior é erro de programação, teste por tipo).

## Permissões, eventos e tabelas
- **Permissões:** `conecta.view` (aba "Enviados"; rotas de aplicativo da inscrição e da rotação), `settings.edit` (Reenviar, Enviar teste, Reativar). Nenhuma ação nova.
- **Eventos:** consome `AppConnected`, `AppConnectionRevoked`, `AppConnectionSuspended` (INT-01) e os do catálogo; **novo** `BillingSlipPaid` de contrato no Finance (a BNK-02 continua com o efeito interno síncrono; só passa a publicar também no outbox).
- **Tabelas (banco do tenant, schema `shared`, novas):** `outbound_webhook_subscriptions` (`connection_id`, `client_id`, `url`, `events text[]`, `status`, `secret` cifrado, `previous_secret` cifrado, `previous_valid_until`, `consecutive_failures`, `last_success_at`, `paused_at`, `xmin`), `IAuditable` sem o segredo; `outbound_webhook_events` (`id`, `type`, `contract`, `required_scope`, `occurred_at`, `payload` anulável, `payload_purged_at`); `outbound_webhook_deliveries` (`event_id`, `subscription_id`, `status`, `attempts`, `next_attempt_at`, `last_status_code`, `last_duration_ms`, `last_error`, `delivered_at`, `xmin`; único em `(event_id, subscription_id)`); `outbound_webhook_attempts` (`delivery_id`, `at`, `status_code`, `duration_ms`, `error`).
- **Segredo:** `SecretKind.OutboundWebhookSecret` → `outbound-webhook-secret` (adendo à ADR 0007; teste golden atualizado). Não reaproveita o `WebhookSecret` da entrada: propósitos separados.
- **Jobs:** `OutboundWebhookDeliveryJob` (`TenantJob<OutboundWebhookDeliveryArgs(DeliveryId)>`), varredor `outbound-webhooks-sweep` (recorrente global, enfileira por tenant com inscrição ativa), `OutboundWebhookPurgeJob` (diário, `[PersonalDataPurge]` por coerência com a entrada, mesmo sem dado pessoal no corpo).

## Telas e estados
- **Configurações → Integrações → Eventos** (`/app/configuracoes/integracoes/eventos`): o título passa de "Eventos recebidos" a "Eventos", com as abas **"Recebidos"** (a tela de hoje, sem mudança de regra) e **"Enviados"** (`?aba=enviados`, só com `conecta.view`).
- **Aba "Enviados":** filtros por aplicativo, tipo, situação e período; colunas aplicativo, tipo, criado em, situação (chip: Entregue ok, Tentando de novo alerta, Parado crítico, Retido informativo, Ignorado informativo), tentativas, último código e próxima tentativa; painel do evento (CA 16) com "Reenviar"; cabeçalho com a situação da inscrição, "Enviar teste" e, pausada, a faixa vermelha "Envio pausado desde dd/mm: o Conecta não respondeu" e "Reativar". O filtro "Parados" abre sozinho quando há algum (como na entrada).
- **Estados:** carregando; vazio "Nenhum evento enviado ainda. Os avisos aparecem aqui quando um pedido, boleto ou nota mudar." com "Enviar teste"; sem conexão: "Conecte o Conecta para enviar eventos" com o link para a página do Conecta; erro com "Tentar de novo".
- **Página do Conecta** (INT-01) ganha o resumo "Eventos: última entrega dd/mm hh:mm · {n} parados", com link para a aba.
- **Verbos:** "Reenviar" → "Reenviado"; "Enviar teste" → "Teste enviado"; "Reativar" → "Envio reativado".
- **400 px:** tipo, situação e hora visíveis; painel em tela cheia.
- **Ajuda:** nova `Ajuda/configuracoes-integracoes-eventos-enviados.md` (`aba: integracoes-eventos-enviados`, `modulo: conecta`), com o formato do corpo, a assinatura e a regra "use o id e consulte de novo"; `configuracoes-integracoes-eventos.md` ajusta o título e cita a aba nova; `<Dica>` em "Retido" e "Ignorado".

## Plano de testes
- **Unidade:** assinatura com vetor fixo (uma e duas `v1`); agenda de tentativas e `Retry-After`; validação de URL e de IP (tabela de faixas); corpo de cada tipo sem campo proibido (CA 4).
- **Integração (Testcontainers + recebedor falso local com `AllowPrivateNetworks`):** inscrição na conexão e segredo uma vez no token (CA 1); evento do Fiscal até `Delivered` com assinatura válida (CA 5, 7); reentrega da mensagem sem duplicar (CA 7); perda de escopo → `Skipped` (CA 8); falhas até `DeadLettered` e "Reenviar" com o mesmo id (CA 10, 11); pausa automática e "Reativar" com retidas (CA 12); rotação com duas assinaturas e fim da sobreposição (CA 6); revogação com `app.disconnected` (CA 3); execução dupla do job (CA 14); expurgo (CA 15); DNS para IP privado recusado no `ConnectCallback` (CA 17); canário no log (CA 18); dois tenants.
- **Front (Vitest):** aba "Enviados" nos estados vazio, pausado e com parados.

## Tarefas
- [ ] Adendo à ADR 0007 (`outbound-webhook-secret`) e à ADR 0031 (seção "Saída", espelho da retenção e da rotação); `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (situações da inscrição e da entrega).
- [ ] BuildingBlocks/Webhooks: `OutboundWebhookEntities.cs`, `IOutboundWebhookPublisher`, `OutboundWebhookSigner`, `OutboundUrlGuard` + `HttpClient` `outbound-webhooks` com `ConnectCallback`, `OutboundWebhookDeliveryJob`, varredor e expurgo; `SecretKind.OutboundWebhookSecret` e o golden.
- [ ] BuildingBlocks/Identity: `IConnectedAppAccess`; implementação no Identity. Consumidor de `AppConnected`/`AppConnectionRevoked`/`AppConnectionSuspended`; `webhook_secret` na resposta da troca do código (INT-01).
- [ ] Tradutores no `Application` de Sales, Finance e Fiscal; `BillingSlipPaid` de contrato publicado pela baixa da BNK-02; tradutores do pedido de venda quando a VND-02 estiver na main.
- [ ] Rotas: `GET`/`PUT /api/v1/integration/webhooks/subscription`, `POST /api/v1/integration/webhooks/secret/rotate` (aplicativo, `conecta.view`); `GET /api/v1/outbound-webhooks/deliveries`, `GET .../deliveries/{id}`, `POST .../deliveries/{id}/resend`, `POST .../subscriptions/{id}/test`, `POST .../subscriptions/{id}/resume` (pessoa); rate limit `webhook-secret-rotate`; OpenAPI e Orval.
- [ ] Front: abas em `IntegrationEventsPage.tsx`, `IntegrationsNav` ("Eventos"), ajuda nova e a ajustada.

## Dev Notes
- **Outbox, inbox e despacho:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Messaging/OutboxMessage.cs` (`InboxMessage` único por mensagem e handler), `IIntegrationEventConsumer.cs` (roda na transação do inbox), `OutboxDispatcher.cs`, `OutboxProcessor.cs`, `OutboxOptions.cs` (backoff base × 2^(n−1): **não** reusar para a entrega HTTP, que tem agenda própria e longa), `DeadLetters.cs`.
- **Espelho da entrada:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Webhooks/WebhookEntities.cs` (`WebhookEvent`, `WebhookCredential.Overlap` de 24 h), `WebhookMaintenance.cs` (`WebhookEventsPurgeJob` de 90 dias, molde do expurgo), `WebhookProcessing.cs` (`WebhookEventJobArgs`), `WebhookEventsAdmin.cs` (lista, filtros, contagem de parados, reprocessar com auditoria) e `WebhookCredentials.cs` (segredo atual e anterior).
- **Jobs:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Jobs/TenantJob.cs` e `GlobalRecurringJob.cs`; após o commit, `IAfterCommit` (`src/BuildingBlocks/BemAqui.BuildingBlocks/Persistence/AfterCommit.cs`).
- **Segredos:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Security/SecretKind.cs` e `SecretProtector.cs` (`Purposes`).
- **Alertas:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Observability/IntegrationHealth.cs` (`IntegrationHealthTracker`).
- **HTTP de saída já existente (molde de cliente nomeado e teto):** `src/BuildingBlocks/BemAqui.BuildingBlocks/Lookups/PublicDataHttp.cs` e `LookupGuard.cs`; `src/Modules/Fiscal/BemAqui.Fiscal.Infrastructure/Emissor/EmissorFiscalHttpEmitter.cs`.
- **Eventos de origem:** `src/Modules/Fiscal/BemAqui.Fiscal.Contracts/OutgoingInvoiceContracts.cs`, `src/Modules/Finance/BemAqui.Finance.Contracts/BillingSlipContracts.cs`, `ReceivablesContracts.cs`, `src/Modules/Sales/BemAqui.Sales.Contracts/SalesContracts.cs`.
- **Front:** `src/Web/src/pages/settings/integrations/IntegrationEventsPage.tsx`, `events.ts` e `events.test.ts`; ajuda atual em `Ajuda/configuracoes-integracoes-eventos.md`.
- **Mesma VPS:** o Conecta e o ERP ficam no mesmo servidor; a URL do Conecta resolve para o IP público, que passa pelo guarda. Conferir no SAAS/produção se o contêiner alcança o próprio IP público (*hairpin*); se não, a saída é a lista de hosts da configuração com rota pelo nginx, **nunca** liberar rede privada em produção.
- **Testes:** `--filter-class "*OutboundWebhook*"`; carga (milhares de entregas) só com `[Trait(LoadTest.Trait, LoadTest.Value)]`.

## Fora do escopo
- Inscrição criada pelo Proprietário para URL própria e webhooks de aplicativos de terceiros: API-01 (o guarda de SSRF já fica pronto).
- Recebimento e conferência da assinatura no Conecta: ATD-01. Eventos de projeto e de orçamento da fábrica: ATD-03/ATD-05.
- Garantia de ordem por recurso e reenvio em lote por período.

## Pontos em aberto (para o PO)
1. Publicar `BillingSlipPaid` no outbox (a BNK-02 decidiu o evento como interno e síncrono; a story mantém isso e só acrescenta a publicação).
2. Pausa automática em 72 h sem sucesso e 20 falhas seguidas, com retidas reenviadas até 7 dias: confirmar os números.
3. Rotação só a pedido do aplicativo (CA 6); o ERP não troca o segredo sozinho. Precisa de rotação periódica obrigatória?

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 26/11/2026 | 0.1 | Story detalhada a partir da ADR 0052 e do plano de integrações | River (SM) |
