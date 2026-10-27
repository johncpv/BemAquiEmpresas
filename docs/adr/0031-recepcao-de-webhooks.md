# ADR 0031: Recepção de webhooks

- Status: Aceito (21/10/2026). O PO (John) aprovou a decisão D13 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`), os pontos 1 a 5 da tabela 3.2 e os achados das seções 4.2 e 4.3 para a FND-16.
- Contexto: a FND-16 cria a única porta de entrada anônima para provedores externos (EmissorFiscal na FIS-01, Inter na BNK-02 e, depois, o WhatsApp). A story gravava o evento recebido e depois enfileirava o processamento, em dois passos: uma queda entre eles deixaria o evento `Received` para sempre. Não estava decidido o host da rota, como ela escapa do middleware de tenant por host, quanto tempo o corpo fica guardado (ele tem dado pessoal), nem como trocar o segredo sem perder avisos. O nginx da VPS não faz mTLS por location, e o Inter assina com mTLS, não com HMAC.
- Complementa as ADRs 0006 (outbox e inbox), 0007 (segredos, adendo de 21/10/2026), 0009 (rotas anônimas, adendo de 21/10/2026), 0015 (log `bqe_masked` só com `$uri`), 0018 (retenção) e 0019 (§12 jobs idempotentes).

## Decisão

### 1. Rota e tenant
- **`POST /webhooks/{provider}/{tenantKey}`**, fora de `/api/v1` (a URL fica cadastrada no provedor e não muda com a versão da API), sem cookie, sem CSRF, sem usuário. Outros métodos: 405, com uma exceção: o **GET de verificação** do provedor que o declara no registro (`SupportsVerificationGet`). O `whatsapp` (ADR 0041, WPP-01) precisa dele: a Meta confirma a URL com `GET ?hub.mode=subscribe&hub.verify_token=…&hub.challenge=…`; o verify token é guardado só como hash e comparado em tempo constante, a resposta é o `hub.challenge` em `text/plain` e nada é gravado em `webhook_events`; token errado devolve a resposta uniforme de recusa.
- **`tenantKey` opaco** (aleatório, guardado no catálogo), nunca o nome nem o host do tenant. O tenant é resolvido pela chave, só se estiver `Active`.
- Atende no **host da plataforma** (ADR 0005 D2), com a location `/webhooks/` **fora da allowlist de IP** do painel da plataforma. O EmissorFiscal também chega por ali, pela URL pública (ADR 0027, adendo de 27/10/2026, que tirou o host interno da API e a rede `bqe-fiscal`).
- **`/webhooks/*` fica fora do middleware de tenant por host**, com teste: a rota nunca resolve tenant pelo `Host` e nenhuma outra rota resolve tenant pelo `tenantKey`.
- Todo provedor do `WebhookProviderRegistry` declara um **validador de origem** (HMAC com timestamp, token na URL ou outro); o teste de arquitetura reprova provedor sem validador. Rate limit por política nomeada, por provedor e tenant.

### 2. Validação por provedor
- **EmissorFiscal:** HMAC-SHA256 do corpo cru com timestamp (janela de 5 min), comparação em tempo constante.
- **Inter (ADR 0032):** sem mTLS no nginx, a origem é conferida por **token `?t=`** na URL (só o hash guardado; o log `bqe_masked` grava só `$uri`, então a query não vai para o log), e **nenhuma baixa acontece pelo corpo**: o handler reconsulta a cobrança no Inter por mTLS antes de qualquer efeito.
- O corpo do webhook **nunca é fonte de verdade** em nenhum provedor: ele só dispara a reconsulta.

### 3. Atomicidade com o outbox
- A recepção grava **na mesma transação** a linha em `shared.webhook_events` e a mensagem do outbox que leva ao handler do provedor; o handler é consumidor com inbox (ADR 0006), então o efeito acontece no máximo uma vez.
- Reserva: um **varredor** recoloca no outbox os eventos `Received` há mais de 5 min sem mensagem processada.
- Resposta ao provedor só depois do commit: 200/202 para aceito ou duplicado, 401 para assinatura inválida (sem detalhe), 404 para tenant ou provedor desconhecido.

### 4. Deduplicação
- `shared.webhook_events` (banco do tenant): `id` (v7), `provider`, `external_id`, `body_sha256`, `event_type`, `received_at`, `payload` (`bytea` ou `text`, guardado como chegou, `[PersonalData]`, anulável depois do expurgo), `status` (`Received`/`Processed`/`Ignored`/`Failed`/`DeadLettered`), `attempts`, `last_error`, `processed_at`, `xmin`.
- Único em `(provider, external_id)` quando o provedor manda id de entrega, e **também em `(provider, body_sha256)`** (o Inter pode reenviar sem id estável); chave composta do provedor quando necessário (Inter: cobrança, situação, data).

### 5. Retenção e acesso ao corpo
- O **corpo é guardado 90 dias** e depois apagado (a linha fica, sem `payload`), por job diário idempotente por tenant; nunca além do necessário. A ADR 0037 inclui o `webhook_events` no inventário de dado pessoal.
- A tela "Eventos recebidos" lista com `fiscal.view` ou `finance.view` (conforme o provedor) e mostra o corpo, **mascarado**, só com `fiscal.approve` ou `finance.approve`, com registro `SensitiveRead`.

### 6. Rotação do segredo
- Rotação com **24 h de sobreposição**: o segredo antigo continua aceito por 24 h depois da troca, e **todo uso do antigo gera alerta** (FND-12) com o provedor e o tenant; depois de 24 h, só o novo.
- Segredos cifrados com `SecretKind.WebhookSecret` (ADR 0007, adendo); o token do Inter guarda só o hash (`webhook_token_hash`, `previous_webhook_token_hash`).

### 7. Segurança
- Teste com dois tenants: um evento de um tenant nunca chega ao outro, nem com `tenantKey` trocado.
- Teste de canário no log da aplicação **e no `error_log` do nginx**: nem corpo, nem token, nem assinatura aparecem.

## Consequências
- **FND-16** implementa as seções 1 a 7 com o provedor de teste `test`; **FIS-01** registra o `emissor-fiscal`; **BNK-02** registra o `inter`; **WPP-01** registra o provedor do WhatsApp, se o BSP usar webhook de situação.
- Adendo de 21/10/2026 à ADR 0007 (segredos novos) e à ADR 0009 (rota anônima `/webhooks/*`).
- `docs/02-arquitetura.md` deixa de citar o tenant pelo nome na URL do webhook; `docs/03-modelo-dados.md` registra `shared.webhook_events`.
- O expurgo do corpo apaga dado de propósito e está coberto por esta ADR (não é migration destrutiva).

## Adendo 25/10/2026 (implementação da FND-16; recomendações aceitas pelo PO em 24/10/2026)
- **Respostas:** 202 para evento aceito, 200 para entrega repetida, **401 sem corpo para toda recusa** (chave inexistente, tenant suspenso, provedor desconhecido, origem inválida), 403 sem corpo no GET de verificação recusado, 413/415/429 antes de ler o corpo e de abrir o banco do tenant, 503 em falha do banco ou schema defasado. Substitui o "404 para tenant ou provedor desconhecido" da §3 (sem enumeração).
- **Anti-replay:** o HMAC cobre `{timestamp}.{corpo}` quando o provedor declara o cabeçalho do timestamp (janela de 5 min), além da deduplicação por id e por SHA-256.
- **Provedor de teste só nos testes:** o build publicado não registra provedor nesta story; o `emissor-fiscal` (FIS-01) e o `inter` (BNK-02) são os primeiros.
- **Credencial:** `shared.webhook_tokens` guarda, por provedor e escopo, o hash do token ou o segredo HMAC cifrado (`webhook-secret`), com o anterior até `previous_valid_until`. Validar exige ler a credencial antes da gravação; a gravação continua uma só (evento + outbox).
- **Rate limit:** política `webhooks` por IP real e provedor (60/min); o abuso por tenant é coberto pelo alerta de 20 recusas em 5 min por provedor e tenant.
- **2FA de novo:** contrato `IStepUpTwoFactor` (BuildingBlocks), implementado pelo Identity, com o código do aplicativo no corpo da ação.
