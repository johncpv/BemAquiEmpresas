# FND-16 — Recepção de webhooks: rota pública, validação, idempotência e processamento por fila

| Campo | Valor |
|---|---|
| ID | FND-16 |
| Título | Mecanismo único para receber webhooks de provedores externos (EmissorFiscal, Banco Inter e os futuros): rota pública por provedor e tenant, validação da origem (assinatura HMAC ou segredo na URL), limite de tamanho e de taxa, registro do evento cru com deduplicação (`shared.webhook_events`), resposta rápida e processamento assíncrono por `TenantJob`, sem nunca registrar segredo nem corpo em log |
| Tamanho | M (nova, Fase 5: a revisão pré-Fase 0 §2.1 pediu `webhook_events` e a §3 os cuidados do webhook; nenhuma story anterior criou a recepção) |
| Status | Draft |
| Requisitos | R-FIN-7 (baixa automática por webhook) · R-FIS-3 (retorno assíncrono da autorização) · PRD §6 Segurança (OWASP, segredos por tenant) · PRD §6 Observabilidade (alerta de falha em integrações) · R-SEC-5 |
| Dependências | FND-02 (resolução de tenant e catálogo), FND-07 (outbox, inbox, `TenantJob<T>`), FND-11 (`ISecretProtector`, `Secret`), FND-12 (logs mascarados e alertas), FND-10b (vhost e log `bqe_masked`) |
| Bloqueia | FIS-01 (webhook do EmissorFiscal), BNK-02 (webhook do Inter) |
| Ordem de execução | 1 (Marco 5.1 "Base das integrações") |

## User story
Como **proprietário da plataforma (PO)**, quero **que todo aviso que chega de um sistema externo passe por uma porta única, que confere a origem, guarda o evento uma vez só e responde rápido**, para **nenhuma nota ou pagamento ser baixado duas vezes, nem por um aviso falso, e nenhum segredo vazar em log**.

## Contexto e referências
- `02-arquitetura` §2.1: "Webhooks e jobs não têm subdomínio: webhooks levam o tenant na URL (`/webhooks/inter/{tenantKey}`) + validação de assinatura; jobs Hangfire recebem `tenantId` como parâmetro e abrem o escopo do tenant."
- CLAUDE.md, controle de acesso: "Sem rota anônima, exceto … webhooks (assinatura validada)".
- ADR 0005 D2: o host da plataforma (`plataforma.bemaquiapp.com.br`) atende super admin, Hangfire e webhooks; ADR 0018 adendo: o painel da plataforma está **sem allowlist de IP** (se o PO ligar a allowlist, os webhooks precisam de exceção: Pontos em aberto 2).
- Revisão pré-Fase 0 §2.1 (`webhook_events` para idempotência) e §3 ("Webhook do Inter: o webhook só dispara uma reconsulta da cobrança via mTLS, nunca a baixa direta; URL com segredo; chave única para idempotência").
- ADR 0006 (outbox, inbox, `TenantJob<T>`, filas `default`, `email` e `fiscal`), ADR 0007 (segredo write-only, propósito por tenant e tipo, comparação em tempo constante), ADR 0015 (log `bqe_masked` grava só `$uri`, sem query string), ADR 0019 §12 (job idempotente com testes de execução dupla e de dois tenants).
- EmissorFiscal, `docs/integracao/README.md` §6: o webhook de saída dele assina o corpo cru com HMAC-SHA256 (`X-BemAqui-Signature: sha256=…`), manda `X-BemAqui-Event` e `X-BemAqui-Delivery-Id`, espera 2xx em até 15 s, reentrega em falha e desativa a assinatura depois de 10 falhas seguidas; `http` só em endereço de rede interna.
- EmissorFiscal ADR 003: "validar assinatura, idempotência por evento, aceitar antes de processar, nunca confiar no valor do corpo, registrar o evento cru".

## Critérios de aceite
**Rota e resolução do tenant**
1. **Dado** um provedor registrado no `WebhookProviderRegistry` (nesta story, só o provedor de teste `test`; o `emissor-fiscal` entra na FIS-01 e o `inter` na BNK-02), **quando** chega `POST /webhooks/{provider}/{tenantKey}`, **então** o tenant é resolvido pela chave no catálogo (não pelo host), a requisição é aceita só se o tenant estiver `Active` e só pelo método POST; qualquer outro método responde 405.
2. **[Segurança]** **Dado** um `tenantKey` inexistente, um tenant suspenso, um provedor desconhecido ou uma validação de origem que falha, **quando** a requisição chega, **então** a resposta é a mesma (401 com corpo vazio, sem dizer qual parte falhou, para não revelar quais tenants existem), o evento não é gravado no tenant e o contador `webhook_rejected{provider}` sobe; a partir de 20 recusas em 5 minutos do mesmo provedor e tenant, sai o alerta `webhook_rejections` pelo caminho da FND-12 (uma vez por janela).
3. **[Segurança]** **Dado** a rota de webhook, **quando** a lista de rotas anônimas é conferida pelo teste de arquitetura, **então** `/webhooks/{provider}/{tenantKey}` é a única rota anônima nova, e todo provedor registrado declara um validador de origem (o registro sem validador reprova o teste).

**Validação da origem**
4. **[Segurança]** **Dado** um provedor com assinatura HMAC (`HmacSha256Validator`, com o nome do cabeçalho e o prefixo configuráveis por provedor), **quando** o corpo chega, **então** a assinatura é calculada sobre os **bytes crus** do corpo, antes de qualquer desserialização, com o segredo do tenant decifrado só nesse momento (`SecretKind.WebhookSecret`, proposta), e comparada em tempo constante (`CryptographicOperations.FixedTimeEquals`); corpo alterado em 1 byte é recusado (CA 2).
5. **[Segurança]** **Dado** um provedor que não assina o corpo (`UrlTokenValidator`), **quando** o corpo chega, **então** o token vai na **query string** (`?t=`), nunca no caminho (o log `bqe_masked` grava o caminho e descarta a query, ADR 0015), é comparado com o hash SHA-256 guardado (o token em claro nunca é gravado) em tempo constante, e o processamento **nunca confia no conteúdo**: o consumidor do provedor tem de reconsultar a fonte antes de qualquer efeito (regra para a BNK-02).
6. **[Segurança]** **Dado** a troca do segredo de um provedor (rotação), **quando** o tenant gera um segredo novo, **então** o anterior continua aceito por uma janela de sobreposição (proposta: 24 h, Pontos em aberto 3) e depois é recusado; a troca é auditada sem o valor (`[ProtectedSecret]`: só "alterado"), notificada aos membros do Proprietário, e o segredo novo aparece **uma única vez** na tela de quem o gerou.
7. **[Segurança]** **Dado** um corpo acima de 64 KB, um `Content-Type` diferente de `application/json` (ou do declarado pelo provedor) ou mais de 60 requisições por minuto do mesmo IP para o mesmo provedor, **quando** a requisição chega, **então** responde 413, 415 ou 429 (com `Retry-After`) antes de ler o corpo inteiro e antes de abrir o banco do tenant.

**Registro e idempotência**
8. **Dado** uma requisição válida, **quando** é aceita, **então** grava em `shared.webhook_events` do banco do tenant: `provider`, `external_id` (o id de entrega do provedor, ex.: `X-BemAqui-Delivery-Id`; sem ele, o SHA-256 do corpo), `event_type`, `received_at`, `payload` (o corpo cru, `[PersonalData]`), `status` `Received`, e enfileira o `ProcessWebhookEventJob` (`TenantJob`, argumentos só com `tenantId` e o id do evento) na fila do provedor; responde **200 em até 2 s** (p95 medido em teste), sem esperar o processamento.
9. **Dado** a mesma entrega repetida (mesmo `provider` e `external_id`), **quando** chega de novo, **então** o índice único `(provider, external_id)` impede o segundo registro, a resposta é 200 (o provedor não deve reentregar) e nenhum job novo é enfileirado.
10. **Dado** o `ProcessWebhookEventJob`, **quando** roda, **então** chama o `IWebhookHandler` do provedor numa transação própria que grava o efeito e marca o evento `Processed` (ou `Ignored`, com o motivo, para tipo de evento que o handler não trata); exceção marca `Failed` com o erro (sem dado pessoal) e segue o backoff do Hangfire; depois de 10 tentativas, o evento fica `DeadLettered`, aparece no painel da plataforma e gera notificação a quem tem a permissão de edição do módulo dono (`fiscal.edit` ou `finance.edit`), com a ação "Reprocessar" (ADR 0019 §5).
11. **Dado** o job executado duas vezes para o mesmo evento, ou dois tenants recebendo eventos ao mesmo tempo, **quando** roda, **então** o efeito acontece uma vez só (inbox por handler e transição `Received → Processed` pela entidade com `xmin`) e um tenant nunca vê o evento do outro (testes obrigatórios da ADR 0019 §12).

**Log, retenção e auditoria**
12. **[Segurança]** **Dado** qualquer webhook, **quando** é recebido, recusado ou processado, **então** o log estruturado traz só provedor, tenant, id do evento, tipo, resultado e duração; **nunca** o corpo, os cabeçalhos de assinatura, o token da query nem o segredo (teste de canário: grava valores conhecidos no corpo, no token e no segredo e confere que o sink de log e o `audit.audit_log` não os contêm).
13. **Dado** eventos `Processed` ou `Ignored` com mais de 90 dias (proposta, Pontos em aberto 4), **quando** o recorrente global `webhook-events-purge` roda (diário, 03:00 de São Paulo), **então** o `payload` é apagado e a linha fica (provedor, id, tipo, datas, situação), para a deduplicação continuar valendo; eventos `Failed` e `DeadLettered` não são expurgados.
14. **Dado** o módulo dono do provedor, **quando** um usuário com a permissão de visualização dele (`fiscal.view` ou `finance.view`) abre "Configurações → Integrações → Eventos recebidos", **então** vê a lista (provedor, tipo, recebido em, situação, tentativas, erro) **sem o corpo**; o corpo só abre com a ação de aprovação do módulo dono (`fiscal.approve`, que nasce na FIS-02a, e até lá só o Proprietário; ou `finance.approve`), com `Cache-Control: no-store`, e a leitura fica registrada como `SensitiveRead` (ADR 0016).

## Regras de negócio e técnicas
- **Rota:** `POST /webhooks/{provider}/{tenantKey}` fora de `/api/v1` (não versionada, porque a URL fica cadastrada no provedor), mapeada no Host, sem autenticação de usuário, sem CSRF, sem cookies. Atende no host da plataforma e no host interno da API (`http://bqe-<ambiente>-api:8080`, para o EmissorFiscal na rede Docker; ver FIS-01).
- **`WebhookProviderRegistry`** (BuildingBlocks): por provedor, o módulo dono, o validador de origem (`HmacSha256Validator` | `UrlTokenValidator`), o cabeçalho do id de entrega, o `Content-Type`, a fila do Hangfire e o `IWebhookHandler` (implementado no módulo dono, que lê o segredo pelo próprio Contracts).
- **`shared.webhook_events`** (proposta, banco do tenant): `id` (v7), `provider`, `external_id`, `event_type`, `received_at`, `payload` (jsonb, `[PersonalData]`, anulável depois do expurgo), `status` (`Received`/`Processed`/`Ignored`/`Failed`/`DeadLettered`), `attempts`, `last_error`, `processed_at`, `xmin`; índice único `(provider, external_id)`; índice parcial `(status) WHERE status IN ('Received','Failed')`.
- **Segredo do webhook:** `SecretKind.WebhookSecret` (nome fixo `webhook-secret`), entra no teste golden das cadeias de propósito; adendo à ADR 0007 (que já prevê `inter-credentials`, `inter-certificate` e `fiscal-certificate`). Hash do token de URL guardado como SHA-256 (o token tem 256 bits, aleatório).
- **Nunca confiar no corpo:** o handler valida o tipo, a versão e os ids pelo esquema; valores de dinheiro e situação vindos do corpo só servem para disparar reconsulta (BNK-02) ou são conferidos com o documento local (FIS-01).
- **Tempo:** a rota responde sem abrir transação longa; o `SaveChanges` do evento é o único acesso ao banco antes da resposta.

## Permissões, eventos e tabelas
- **Permissões:** rota anônima validada; tela "Eventos recebidos" com `fiscal.view`/`finance.view` (lista) e `fiscal.approve`/`finance.approve` (corpo). Nenhuma ação nova no `ModuleCatalog`.
- **Eventos:** nenhum evento de domínio novo; o handler de cada provedor publica os dele.
- **Tabelas:** `shared.webhook_events` (nova, proposta). **Segredos:** `SecretKind.WebhookSecret` (proposta, adendo à ADR 0007).
- **Jobs:** `ProcessWebhookEventJob` (`TenantJob`), recorrente global `webhook-events-purge`.

## Telas e estados
- **Configurações → Integrações → Eventos recebidos** (`/app/configuracoes/integracoes/eventos`): h1 "Avisos recebidos de sistemas externos."; DataTable com provedor, tipo, recebido em, situação (Recebido info, Processado ok, Ignorado neutro, Falhou alerta, Parado crítico), tentativas e erro; filtros por provedor, situação e período na URL; ação "Reprocessar" (para `DeadLettered`, com a permissão de edição do dono) e "Ver conteúdo" (CA 14).
- **Estados:** carregando; vazio ("Nenhum aviso recebido ainda. Os avisos aparecem aqui quando a nota fiscal ou o banco responderem."); erro com "Tentar de novo"; sem permissão do corpo, o botão "Ver conteúdo" some.
- **400 px:** provedor, tipo, recebido em e situação sempre visíveis; tentativas e erro na linha expansível.
- **Ajuda:** `Ajuda/configuracoes-integracoes-eventos.md`; `<Dica>` em "Parado" ("O aviso falhou 10 vezes. Corrija a causa e clique em Reprocessar.").

## Plano de testes
- **Unidade:** `HmacSha256Validator` (corpo alterado, cabeçalho ausente, prefixo errado, tempo constante via `FixedTimeEquals`); `UrlTokenValidator`; janela de sobreposição da rotação (CA 6).
- **Integração (Testcontainers):** tenant inexistente, suspenso, provedor desconhecido e assinatura errada com a mesma resposta (CA 2); 413/415/429 antes de abrir o banco (CA 7); registro e resposta em até 2 s com o processamento depois (CA 8); entrega repetida (CA 9); handler que lança exceção até `DeadLettered`, notificação e "Reprocessar" (CA 10); execução dupla e dois tenants (CA 11); canário de log e auditoria (CA 12); expurgo mantendo a deduplicação (CA 13); corpo só com a ação de aprovação e `SensitiveRead` (CA 14).
- **Arquitetura:** única rota anônima nova; todo provedor com validador; handler só no módulo dono.
- **E2E (Playwright):** abrir "Eventos recebidos" com eventos do provedor de teste, filtrar e reprocessar um parado.

## Fora do escopo
- Handlers do EmissorFiscal (FIS-01) e do Inter (BNK-02); cadastro do webhook no provedor (cada story).
- mTLS de cliente no nginx (o Inter apresentar certificado ao chamar o webhook): Pontos em aberto 1.
- Webhook de saída do BemAqui para terceiros: fora do MVP.

## Pontos em aberto
1. **mTLS do webhook do Inter** (arquiteto/PO): validar o certificado de cliente do Inter no `stack-nginx` compartilhado exige `ssl_verify_client` num vhost ou location próprio. Proposta: não exigir no MVP; a segurança vem do token na URL e da reconsulta obrigatória via mTLS de saída (BNK-02), como recomendou a revisão pré-Fase 0 §3.
2. **Host do webhook** (PO): proposta: host da plataforma (ADR 0005 D2). Se a allowlist de IP do painel for ligada (ADR 0018 adendo), a location `/webhooks/` precisa ficar fora dela.
3. **Janela de sobreposição na rotação do segredo** (PO/segurança): proposta 24 h.
4. **Retenção do corpo do webhook** (PO, LGPD): proposta 90 dias; o corpo do Inter traz nome e documento do pagador.
5. **Novo `SecretKind`** (arquiteto): `WebhookSecret` exige adendo à ADR 0007 e ao teste golden.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras` (ADR 0020).
- [ ] Nenhuma permissão fora do `ModuleCatalog`; adendo à ADR 0007 registrado.
- [ ] Tela com estados de carregando, vazio e erro.
- [ ] Ajuda da tela escrita; teste de cobertura passando.
- [ ] Alertas acionáveis (dead-letter com "Reprocessar").
- [ ] Auditoria ligada (rotação de segredo, reprocessamento, leitura do corpo).
- [ ] OpenAPI e cliente Orval regenerados (a rota de webhook fica fora do OpenAPI do front).
- [ ] `docs/03-modelo-dados.md` e `docs/02-arquitetura.md` §2.1 atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `WebhookProviderRegistry`, `IWebhookOriginValidator` (`HmacSha256Validator`, `UrlTokenValidator`), `IWebhookHandler` (BuildingBlocks).
- [ ] Endpoint no Host com limite de corpo, `Content-Type` e rate limit por IP e provedor antes de ler o corpo.
- [ ] `WebhookEvent` (entidade), `ProcessWebhookEventJob`, recorrente `webhook-events-purge`, alerta `webhook_rejections`.
- [ ] `SecretKind.WebhookSecret` com propósito fixo e teste golden; tela "Eventos recebidos" e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (rota anônima, assinatura, tempo constante, enumeração de tenant, log), @qa.
- Gate: CA 2, 4, 5, 9, 11 e 12 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções da validação cruzada:** `fiscal.approve` só existe a partir da FIS-02a; até lá, o corpo dos eventos do Fiscal abre só para o Proprietário (CA 14).
- **Correções feitas na validação:** token do provedor sem assinatura movido do caminho para a query string, porque o log `bqe_masked` grava o caminho (CA 5); resposta única para tenant inexistente e assinatura errada (CA 2); corpo do evento atrás da ação de aprovação, com `SensitiveRead` (CA 14).
- **Pré-condições para `Ready`:** pontos em aberto 2 e 5.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (nova, Fase 5) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
