# INT-02 — Avisos de saída (webhooks): inscrição na conexão, assinatura HMAC, nova tentativa e registro de entregas

| Campo | Valor |
|---|---|
| ID | INT-02 |
| Título | O ERP avisa os aplicativos conectados: inscrição criada na troca do código (mesma transação), corpo mínimo com o endereço para consultar de novo, assinatura HMAC-SHA256 sobre `{timestamp}.{corpo}` com segredo rotativo, entrega pelo Hangfire com espera crescente, `DeadLettered` e "Reenviar", proteção contra SSRF, resumo de saúde na página do Conecta e o registro detalhado em "Avançado" |
| Tamanho | M |
| Status | Draft |
| Requisitos | [ADR 0053](../adr/0053-aplicativos-conectados.md) · ADR 0052 §1 · ADR 0006 (outbox e inbox) · ADR 0007 (adendo 26/11/2026, `OutboundWebhookSecret`) · ADR 0031 (§5 retenção, §6 rotação) · ADR 0019 §12 · ADR 0037 · R-SEC-5 |
| Dependências | INT-01 (conexões, `conecta.view`/`conecta.edit`), FND-16 (aba "Eventos recebidos"), FND-12 (alertas), VND-02a e VND-02b (eventos do pedido) |
| Bloqueia | ATD-01, ATD-05, API-01 |
| Ordem de execução | 3 (F1 — Base no ERP) |

## User story
Como **Proprietário que conectou o Conecta**, quero **que o ERP avise o Conecta na hora em que o pedido é pago, a nota sai ou o boleto vence**, para **o cliente receber o aviso no WhatsApp sem ninguém da equipe lembrar de mandar**.

## Contexto e referências
- ADR 0052 §1 e plano §3.2. Hoje o outbox é só interno e a única tela de webhooks é a de entrada (FND-16, ADR 0031). Esta story é o espelho de saída.
- **Eventos internos não saem crus:** cada módulo dono traduz o seu evento para um evento público, mínimo e estável, e o aplicativo consulta o recurso de novo pela API (INT-03). Nunca vai custo, imposto, margem nem dado pessoal no corpo.
- Na interface, a palavra é **"avisos"**; "webhook" e "evento" ficam na ajuda técnica e na visão "Avançado".

## Critérios de aceite
**Inscrição**
1. **[Segurança]** **Dado** a troca do código de uma conexão nova (INT-01 CA 11), **quando** o Identity grava a conexão, **então** chama o contrato `IOutboundSubscriptions` (BuildingBlocks, implementado onde ficam as tabelas `shared.outbound_webhook_*`) **na mesma transação**: nasce a inscrição com a URL `Integrations:Conecta:WebhookUrl`, os eventos de `Integrations:Conecta:DefaultEvents` limitados à concessão e um segredo de 32 bytes cifrado com `SecretKind.OutboundWebhookSecret`; o segredo volta **uma única vez**, no campo `webhook_secret` da resposta do `/oauth/token` (servidor a servidor, `no-store`), e nunca numa tela ou log. Documentado no contrato da ATD-01: **perdido o segredo, só a troca (CA 6) recupera**. Falha ao criar a inscrição desfaz a conexão.
2. **Dado** uma conexão substituída (INT-01 CA 12), **quando** a troca acontece, **então** a ordem é: conexão nova → inscrição nova → inscrição anterior `Ended` (entregas pendentes dela viram `Skipped` "Conexão substituída").
3. **Dado** a conexão revogada ou suspensa, **quando** o `AppConnectionRevoked` ou o `AppConnectionSuspended` chega, **então** o consumidor **só encerra ou suspende**: a inscrição vai a `Ended` (revogada, depois de um último `app.disconnected` assinado com o segredo atual, uma série de tentativas) ou `Held` (suspensa: entregas novas ficam `Held`, sem tentar); pendentes de inscrição encerrada viram `Skipped` "Conexão encerrada".
4. **Dado** o aplicativo, **quando** chama `GET /api/v1/integration/webhooks/subscription` (rota da própria conexão, ADR 0053), **então** vê URL, eventos e situação; mudar eventos ou URL é **só no ERP**, por quem tem **`conecta.edit`**, com a URL nova restrita a `Integrations:Conecta:WebhookAllowedHosts` (CA 16) e tipos fora da concessão recusados com a lista; mudança auditada.

**Corpo e assinatura**
5. **Dado** qualquer aviso entregue, **quando** sai, **então** o corpo é JSON UTF-8 `{"id","type","contract":1,"occurred_at","tenant","connection_id","data":{…mínimo…},"resource"}`, `occurred_at` em UTC, `data` só com ids, número legível, situação, valor e datas (catálogo, Regras); **nenhum** nome, documento, telefone, e-mail, endereço, custo, imposto ou margem (teste por tipo com lista de campos proibidos); cabeçalhos `BemAqui-Event-Id`, `BemAqui-Event-Type`, `BemAqui-Timestamp` e `BemAqui-Signature: v1=<hex do HMAC-SHA256 de "{timestamp}.{corpo}">` (na sobreposição, `v1=<nova>,v1=<antiga>`); `User-Agent: BemAqui-Webhooks/1`; os mesmos bytes do corpo em todas as tentativas. A documentação pede janela de 5 min e comparação em tempo constante.
6. **[Segurança]** **Dado** "Trocar segredo" no ERP (`conecta.edit`), **quando** confirmo, **então** o ERP gera o segredo novo e o entrega ao aplicativo **uma vez**, no campo `webhook_secret` da próxima resposta do `/oauth/token` (renovação, a cada até 15 min); o anterior vale por mais 24 h (as duas assinaturas vão juntas); com **"Invalidar o anterior agora"** (`immediate=true`, para suspeita de vazamento) o anterior para na hora e as entregas falham e tentam de novo até o aplicativo renovar; troca auditada (sem valor), notificada aos Proprietários e limitada a 5 por dia por inscrição.

**Origem dos eventos e idempotência**
7. **Dado** um evento de contrato do catálogo gravado no outbox pelo módulo dono, **quando** o tradutor daquele módulo (`IIntegrationEventConsumer<T>`, com inbox) processa, **então** grava **na mesma transação** o evento público e uma entrega `Pending` por inscrição ativa que assina o tipo; reentrega da mesma mensagem não duplica; após o commit, enfileira o job; um varredor recoloca `Pending` paradas há mais de 5 min.
8. **[Segurança]** **Dado** uma entrega, **quando** o job vai enviar, **então** reavalia: conexão `Active`, módulos `conecta` e do evento liberados, escopo exigido nas **permissões efetivas atuais** da conexão (`IConnectedAppAccess`); sem isso, `Skipped` com o motivo ("Sem permissão para ver notas") e nada sai.
9. **Dado** o mesmo aviso enviado mais de uma vez, **quando** chega, **então** leva **o mesmo `id` e o mesmo corpo**; a ordem **não é garantida**: o aplicativo usa o `id` para não processar duas vezes e o `resource` para saber o estado atual (ajuda e contrato da ATD-01).

**Entrega, nova tentativa e parada**
10. **Dado** um envio, **quando** o recebedor responde 2xx em até 10 s, **então** `Delivered`; qualquer outra resposta, 3xx (não seguido), *timeout* (conexão 5 s, total 10 s) ou erro de rede é falha: novas tentativas em 1 min, 5 min, 30 min, 2 h, 6 h, 12 h e 24 h (8 no total, ±10%); 429 e 503 com `Retry-After` respeitados até 1 h; depois da 8ª, `DeadLettered`.
11. **Dado** uma entrega `DeadLettered` ou `Delivered`, **quando** quem tem `conecta.edit` clica "Reenviar", **então** uma nova série começa com o mesmo `id` e corpo (se ainda guardado, CA 14); auditado.
12. **Dado** "Enviar teste" (`conecta.edit`), **quando** aciono, **então** sai um `ping` pela mesma esteira e o resultado aparece em segundos.
13. **Dado** fila, **quando** há muitas entregas, **então** no máximo 4 simultâneas por inscrição e 32 por instância; jobs `TenantJob` com `tenantId`, idempotentes pela entrega (transição com `xmin`; teste de execução dupla).

**Registro, retenção e LGPD**
14. **Dado** as entregas, **quando** passam 90 dias, **então** o job diário apaga o corpo dos eventos com todas as entregas finais e a linha fica (tipo, ids, datas, situação, código), como na entrada; "Reenviar" sem corpo fica desabilitado com a `<Dica>` "O conteúdo foi apagado depois de 90 dias".
15. **Dado** o registro detalhado ("Avançado"), **quando** abro um aviso, **então** vejo o corpo e cada tentativa: hora, código HTTP, duração e o erro resumido (até 500 caracteres, **sem** o corpo da resposta, lido até 4 KB e descartado).

**SSRF e rede**
16. **[Segurança]** **Dado** uma URL de inscrição, **quando** é gravada e **a cada conexão**, **então**: só `https`, porta 443, sem usuário e senha, host em `WebhookAllowedHosts` (lista preferida); o `HttpClient` `outbound-webhooks` confere o **IP de fato conectado** (`SocketsHttpHandler.ConnectCallback`), normaliza com `MapToIPv4` e recusa loopback, RFC 1918, link-local 169.254.0.0/16, CGNAT 100.64.0.0/10, `0.0.0.0/8`, `192.0.0.0/24`, `198.18.0.0/15`, `240.0.0.0/4`, multicast, `::1`, `::ffff:0:0/96` (IPv4 mapeado), NAT64 `64:ff9b::/96`, 6to4 `2002::/16`, `fc00::/7` e `fe80::/10`; validação TLS ligada com SNI = host; sem cookies, sem proxy com credencial, `AllowAutoRedirect = false`. Rede privada só com `Integrations:OutboundWebhooks:AllowPrivateNetworks` em `Development` e nos testes.
17. **[Segurança]** **Dado** qualquer entrega, **quando** algo vai para o log, **então** nem segredo, nem assinatura, nem corpo aparecem (canário).

**Permissões e telas**
18. **Dado** o módulo `conecta` não liberado, **quando** abro as telas ou chamo as rotas, **então** 403 `module_not_released`; ver o resumo e o registro: `conecta.view`; "Reenviar", "Enviar teste", mudar avisos e "Trocar segredo": `conecta.edit`; IDOR: entrega de outro tenant ou id inexistente responde 404.
19. **Dado** a página do Conecta (INT-01), **quando** abro, **então** vejo o **resumo de saúde**: "Os avisos estão chegando ao Conecta" (chip ok) ou "O Conecta não está recebendo os avisos desde dd/mm" (chip crítico) com "Reenviar" os parados; há `DeadLettered`, sai também o alerta `outbound_webhooks` (FND-12) aos Proprietários com o link.

## Regras de negócio e técnicas
- **Catálogo de avisos v1** (contrato `1`; campo novo é compatível; remover ou mudar sentido exige contrato `2`):

| Tipo público | Origem (contrato) | Escopo | `data` |
|---|---|---|---|
| `sales_order.created`, `.confirmed`, `.paid`, `.cancelled` | `OrderCreated`, `OrderConfirmed`, `OrderPaid`, `OrderCancelled` (VND-02a) | `sales.view` | `id`, `number`, `status`, `total`, `party_id` |
| `sales_order.picked`, `.invoiced`, `.shipped`, `.delivered` | `OrderPicked`, `OrderInvoiced`, `OrderShipped`, `OrderDelivered` (VND-02b) | `sales.view` | idem |
| `billing_slip.issued` | `BillingSlipIssued` | `finance.view` | `id`, `receivable_id`, `amount`, `due_date`, `party_id` |
| `billing_slip.cancelled` | `BillingSlipCancelled` (inclui vencido, `expired: true`) | `finance.view` | `id`, `receivable_id`, `reason`, `expired` |
| `billing_slip.paid` | **novo** `BillingSlipPaid` (Finance.Contracts), publicado no outbox na mesma transação da baixa da BNK-02 (o efeito interno não muda) | `finance.view` | `id`, `receivable_id`, `paid_amount`, `credit_date` |
| `invoice.authorized`, `invoice.cancelled`, `invoice.rejected` | `InvoiceAuthorized`, `InvoiceCancelled`, `InvoiceRejected` (Fiscal) | `fiscal.view` | `id`, `type`, `issued_on`/`cancelled_on`, `total`, `party_id` (nunca `Taxes` nem `CostTaxAmount`) |
| `app.disconnected`, `ping` | sistema | — | `connection_id` |

- `sale.*` e `receivable.*` **não** entram na v1.
- **Tradução no módulo dono** (`Application` de Sales, Finance, Fiscal) pelo `IOutboundWebhookPublisher` (BuildingBlocks); o BuildingBlocks não referencia módulo (ADR 0006).
- **Contratos novos (BuildingBlocks):** `IOutboundSubscriptions` (criar, encerrar, suspender; chamado pelo Identity na troca do código), `IOutboundWebhookPublisher`, `IConnectedAppAccess` (implementado pelo Identity).
- **Situações:** inscrição `Active` → `Held` (conexão suspensa) → `Ended` (conexão nova cria inscrição nova); `Ended` é final. Entrega `Pending` → `Retrying` → `Delivered` | `DeadLettered`; `Held`; `Skipped`.
- **Ordem e tamanho:** sem garantia de ordem; corpo até 64 KB (teste por tipo).

## Permissões, eventos e tabelas
- **Permissões:** `conecta.view` (resumo e registro), `conecta.edit` (mudar avisos, Reenviar, Enviar teste, Trocar segredo); ações do módulo `conecta` criadas na INT-01.
- **Eventos:** consome `AppConnectionRevoked` e `AppConnectionSuspended` e os do catálogo; **novo** `BillingSlipPaid` de contrato.
- **Tabelas (schema `shared`, novas):** `outbound_webhook_subscriptions` (`connection_id`, `client_id`, `url`, `events text[]`, `status`, `secret` cifrado, `previous_secret` cifrado, `previous_valid_until`, `pending_secret_delivery`, `last_success_at`, `xmin`), `IAuditable` sem o segredo; `outbound_webhook_events`; `outbound_webhook_deliveries` (único em `(event_id, subscription_id)`); `outbound_webhook_attempts`.
- **Segredo:** `SecretKind.OutboundWebhookSecret` → `outbound-webhook-secret` (adendo de 26/11/2026 à ADR 0007; teste golden).
- **Jobs:** `OutboundWebhookDeliveryJob`, varredor `outbound-webhooks-sweep`, `OutboundWebhookPurgeJob` (diário).

## Telas e estados
- **Página do Conecta (INT-01):** resumo de saúde (CA 19) e "Avançado" com o registro detalhado.
- **Avançado** (`/app/configuracoes/integracoes/eventos?aba=enviados`): a tela de eventos passa a ter as abas "Recebidos" (a de hoje) e "Enviados" (só com `conecta.view`); filtros por tipo, situação e período; chips Entregue (ok), Tentando de novo (alerta), Parado (crítico), Retido e Ignorado (informativos), sempre com texto; painel do aviso (CA 15) com "Reenviar"; "Enviar teste" e "Trocar segredo" no cabeçalho. O filtro "Parados" abre sozinho quando há algum.
- **Estados:** carregando; vazio "Nenhum aviso enviado ainda. Eles aparecem aqui quando um pedido, boleto ou nota mudar." com "Enviar teste"; sem conexão "Conecte o Conecta para enviar avisos" com o link; erro com "Tentar de novo".
- **Verbos:** "Reenviar" → "Reenviado"; "Enviar teste" → "Teste enviado"; "Trocar segredo" → "Segredo trocado".
- **400 px:** tipo, situação e hora visíveis; painel em tela cheia.
- **Ajuda:** nova `Ajuda/configuracoes-integracoes-eventos-enviados.md` (`aba: integracoes-eventos-enviados`, `modulo: conecta`) com o formato, a assinatura, "use o id e consulte de novo" e "perdeu o segredo: troque"; ajustar `configuracoes-integracoes-eventos.md`; `<Dica>` em "Retido" e "Ignorado".

## Plano de testes
- **Unidade:** assinatura com vetor fixo (uma e duas `v1`); agenda e `Retry-After`; validação de URL e de IP (tabela de faixas do CA 16, inclusive IPv4 mapeado, NAT64 e 6to4); corpo sem campo proibido.
- **Integração (Testcontainers + recebedor falso com `AllowPrivateNetworks`):** inscrição na mesma transação e segredo uma vez (CA 1); falha da inscrição desfaz a conexão; substituição na ordem (CA 2); evento do Fiscal até `Delivered` (CA 5, 7); reentrega sem duplicar (CA 7); perda de escopo → `Skipped` (CA 8); `DeadLettered` e "Reenviar" (CA 10, 11); troca de segredo com e sem `immediate` (CA 6); revogação com `app.disconnected` (CA 3); execução dupla (CA 13); expurgo (CA 14); DNS para IP privado e `::ffff:127.0.0.1` recusados (CA 16); canário (CA 17); dois tenants.
- **Front (Vitest):** resumo de saúde nos dois estados; aba "Enviados" vazia e com parados.

## Tarefas
- [ ] Adendo à ADR 0031 (seção "Saída"); `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md`.
- [ ] BuildingBlocks/Webhooks: entidades, `IOutboundSubscriptions` e implementação, `IOutboundWebhookPublisher`, `OutboundWebhookSigner`, `OutboundUrlGuard` + `HttpClient` com `ConnectCallback`, job, varredor, expurgo; `SecretKind.OutboundWebhookSecret` e golden.
- [ ] Identity: chamada a `IOutboundSubscriptions` no `OAuthTokenService`; `webhook_secret` na resposta da troca e da renovação pendente; `IConnectedAppAccess`.
- [ ] Tradutores em Sales, Finance e Fiscal; `BillingSlipPaid` publicado pela baixa da BNK-02.
- [ ] Rotas: `GET /api/v1/integration/webhooks/subscription` (aplicativo); `GET /api/v1/outbound-webhooks/deliveries`, `GET .../deliveries/{id}`, `POST .../deliveries/{id}/resend`, `PUT .../subscriptions/{id}`, `POST .../subscriptions/{id}/test`, `POST .../subscriptions/{id}/secret/rotate` (pessoa, `conecta.edit`); OpenAPI e Orval.
- [ ] Front: resumo na página do Conecta, abas em `IntegrationEventsPage.tsx`, ajudas.

## Dev Notes
- **Outbox e inbox:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Messaging/OutboxMessage.cs`, `IIntegrationEventConsumer.cs`, `OutboxDispatcher.cs`, `OutboxProcessor.cs`, `OutboxOptions.cs` (não reusar o backoff para HTTP), `DeadLetters.cs`.
- **Espelho da entrada:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Webhooks/WebhookEntities.cs`, `WebhookMaintenance.cs`, `WebhookProcessing.cs`, `WebhookEventsAdmin.cs`, `WebhookCredentials.cs`.
- **Jobs:** `Jobs/TenantJob.cs`, `GlobalRecurringJob.cs`; `Persistence/AfterCommit.cs`.
- **Segredos:** `Security/SecretKind.cs`, `SecretProtector.cs`. **Alertas:** `Observability/IntegrationHealth.cs`.
- **HTTP de saída:** `Lookups/PublicDataHttp.cs`, `LookupGuard.cs`; `src/Modules/Fiscal/BemAqui.Fiscal.Infrastructure/Emissor/EmissorFiscalHttpEmitter.cs`.
- **Eventos de origem:** `src/Modules/Finance/BemAqui.Finance.Contracts/BillingSlipContracts.cs` (`BillingSlipIssued`, `BillingSlipCancelled` com `Expired`; `BillingSlipPaid` não existe ainda), `src/Modules/Fiscal/BemAqui.Fiscal.Contracts/OutgoingInvoiceContracts.cs`, `src/Modules/Sales/BemAqui.Sales.Contracts/SalesContracts.cs`.
- **Mesma VPS:** conferir o *hairpin* do contêiner para o IP público; se não alcançar, rota pelo nginx com a lista de hosts, **nunca** rede privada em produção.
- **Testes:** `--filter-class "*OutboundWebhook*"`; carga com `[Trait(LoadTest.Trait, LoadTest.Value)]`.

## Fora do escopo
- **Pausa automática** (72 h sem sucesso e 20 falhas seguidas) e **"Reativar" com retenção de 7 dias**: story de acompanhamento depois da F1. Na v1 ficam novas tentativas, `DeadLettered`, "Reenviar" e troca de segredo.
- Avisos de `sale.*` e `receivable.*`; inscrição para URL própria e aplicativos de terceiros (API-01).
- Recebimento no Conecta (ATD-01). Avisos de projeto e orçamento (ATD-03/ATD-05). Garantia de ordem e reenvio em lote.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 26/11/2026 | 0.1 | Story detalhada a partir da ADR 0052 e do plano de integrações | River (SM) |
| 26/11/2026 | 0.2 | Recomendação aceita (PO 24/10), revisões de 26/11/2026: inscrição síncrona pela `IOutboundSubscriptions`; `conecta.edit`; troca de segredo pelo ERP com `immediate`; catálogo v1 sem `sale.*` e `receivable.*`; SSRF ampliado (IPv4 mapeado, NAT64, 6to4, faixas reservadas, TLS e SNI); pausa automática para depois; resumo de saúde e "Avançado"; "avisos" na interface. | Claude (planejamento) |
