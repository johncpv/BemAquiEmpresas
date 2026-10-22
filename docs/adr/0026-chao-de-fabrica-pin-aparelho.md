# ADR 0026: Chão de fábrica: aparelho registrado e PIN

- Status: Aceito (21/10/2026). O PO (John) aprovou as decisões D5 (tablet do chão de fábrica: PIN, aparelho e sem rede), D6 (qualidade × PIN) e D17 (d) (prazo absoluto da sessão), e a seção 2.1 "Chão de fábrica" da revisão pré-Fase 3 (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`).
- Contexto: a PRD-03b pedia PIN no tablet, sessão curta e fila de apontamentos sem rede. A revisão marcou a story NO-GO em arquitetura e em segurança: o PIN cria uma rota anônima nova, o refresh atual (7 dias rotativo, ADR 0009) não tem prazo absoluto nem vínculo com o aparelho, a lista do que a sessão por PIN pode chamar não estava fechada, e a fila sem rede trazia regras de relógio e de validade que dependem de um uso que o piloto ainda não mostrou. A PRD-03b também proibia PIN para quem tem `*.approve`, mas a fila de qualidade exigia `production.approve`: o inspetor nunca veria a fila no tablet.
- Complementa as ADRs 0008 (identidade no token, adendo `amr` de 21/10/2026), 0009 (rotas anônimas, adendo de 21/10/2026), 0011 (identity enxuto: tokens opacos, bloqueio) e 0003 (ação `production.approve_quality`, adendo de 21/10/2026).

## Decisão

### 1. Aparelho registrado
- `identity.shopfloor_devices` (`name`, `secret_hash`, `secret_rotated_at`, `created_by`, `created_at`, `last_seen_at`, `expires_at`, `revoked_at`, `revoked_by`, `theme`).
- Registrar exige login completo de quem tem `production.edit` + `shopfloor.create`; o aparelho recebe um segredo opaco de 32 bytes no cookie `__Host-bq_dev` (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`); no banco fica só o hash.
- **Segredo rotativo:** a cada login por PIN o servidor emite um segredo novo e guarda o hash; o segredo anterior vale só até a resposta chegar (uma reapresentação do anterior revoga o aparelho, como a detecção de reuso do refresh).
- **Validade:** 180 dias a partir do registro; **revogado sozinho depois de 30 dias sem uso** (job diário).
- **Teto:** até 10 aparelhos ativos por tenant (parâmetro `security.max_shopfloor_devices`).
- Cada registro notifica o Proprietário. Revogam: quem tem `settings.edit` ou `production.approve`; o próprio usuário apaga o próprio PIN.
- **O aparelho revogado é conferido a cada request** da sessão por PIN (não só no próximo login).
- Tema do `/fab` guardado por aparelho, padrão automático (ponto 95).

### 2. PIN
- 6 dígitos, com hash PBKDF2 (`PasswordHasher`), em `identity.user_pins` (`user_id`, `device_id`, `pin_hash`, `failed_count`, `locked_until`, `created_at`), único por `(user_id, device_id)`; PIN óbvio recusado.
- Criar PIN só depois do login completo (e-mail, senha e 2FA quando exigido) **num aparelho registrado**. **O login completo feito no tablet** (registrar aparelho, criar PIN) **não deixa sessão persistente**: termina ao fechar o fluxo, sem refresh.
- Bloqueio: 5 erros seguidos no par usuário + aparelho bloqueiam por 15 min; o 10º erro em 24 h apaga o PIN; **limite de erros por aparelho** (20 em 15 min, somando todos os usuários) bloqueia a entrada por PIN no aparelho por 15 min e avisa o Proprietário.
- **Perfil sensível reconferido a cada login por PIN:** ganhar qualquer `*.approve`, `finance.pay`, `hr.view_sensitive`, `identity.edit` ou `settings.edit` apaga o PIN; a exceção é a **`production.approve_quality`** (D6), que pode ser usada por PIN.
- Trocar o vínculo usuário ↔ funcionário (`IUserEmployeeLink`) apaga o PIN e revoga as sessões por PIN do usuário.
- Lista de nomes no `/fab/entrar`: primeiro nome e inicial, **só com aparelho registrado válido** (sem o cookie, a lista não existe).

### 3. Sessão por PIN
- Access token com **`amr = ["pin"]`** (ADR 0008, adendo).
- Refresh ligado ao aparelho: `identity.refresh_tokens` ganha `session_kind` (`Full`/`Pin`), `device_id` (anulável) e `absolute_expires_at`.
- **Sessão por PIN:** 12 h absolutas e **não renováveis** além disso; saída depois de **15 min sem toque**, com aviso de 60 s ("Você vai sair em 60 s. Tocar para continuar."); revogada com o aparelho, com a troca de senha, com o security stamp ou com a perda de `shopfloor.create`.
- **Prazo absoluto também na sessão completa (D17 d):** a família de refresh da sessão completa ganha `absolute_expires_at` = início da família + 30 dias (parâmetro `security.session_absolute_days`, padrão 30); depois disso, novo login. A renovação de 7 dias continua como está dentro desse prazo.

### 4. Rota anônima e lista permitida
- **`POST /api/v1/auth/pin`** é anônima (ADR 0009, adendo) **só** com o cookie do aparelho válido: sem ele responde 404 (como rota inexistente); exige `Origin` do tenant e `X-CSRF: 1`; entra no rate limit do login (ADR 0010 item 6); resposta e tempo uniformes (ADR 0011). `GET /api/v1/auth/pin/users` (lista de nomes) segue a mesma regra.
- **Lista permitida da sessão por PIN** (atributo `[PinSessionAllowed]` no endpoint, conferido por um filtro do Host; tudo o que não está na lista responde 403 `pin_session_scope`), com **teste de arquitetura** que reprova endpoint novo marcado sem revisão:
  - `/api/v1/shopfloor/*` (fila, etapa, iniciar, pausar, retomar, finalizar, checklist, buscar OS pelo número, documentos da OS pelo endpoint que confere a fila do operador);
  - pedido de material do operador (EST-03);
  - fila e decisão de qualidade com `production.approve_quality` (PRD-05);
  - `/api/v1/me`, `/api/v1/me/permissions`, `/api/v1/auth/logout`, ajuda contextual;
  - `ProductionHub`, grupo do chão de fábrica, com `shopfloor.view`.
- O **service worker nunca guarda `/api/*`** (só o shell do `/fab`).

### 5. Operador sem e-mail
- Agora: um e-mail da empresa por operador (apelido no domínio `konebras.com.br`), sem mudar código.
- "Usuário só de chão de fábrica, sem e-mail" só com ADR nova, se os apelidos não servirem.

### 6. Funcionamento sem rede: adiado
- **Fora da PRD-03b.** Volta **depois do piloto, se o Wi-Fi falhar**, numa story própria, com as regras já levantadas: fila no IndexedDB por usuário e aparelho, `Idempotency-Key` por ação, instante do aparelho aceito até 2 min no futuro e nunca antes do início da sessão de origem, validade de 72 h, faixa fixa "Sem conexão".
- Até lá, sem rede o `/fab` mostra "Sem conexão. Esta ação precisa de rede." com "Tentar de novo".

## Consequências
- **PRD-03b** (replanejada como "App do operador: aparelho registrado e PIN", arquivo `docs/stories/PRD-03b-pwa-pin-aparelho.md`, que substitui o `PRD-03b-pwa-pin-offline.md`) implementa as seções 1 a 5 (sem a 6) e o prazo absoluto da sessão completa.
- **PRD-05** usa a `production.approve_quality` por PIN (aprovar e reprovar a qualidade exigem essa ação; a Supervisão a recebe pelo seed patch `production-quality`); a conclusão da OS e a liberação da parcela continuam com `production.approve` e login completo (PRD-06a1).
- **PRD-02** fecha a conexão do hub quando a permissão ou o security stamp mudam.
- `docs/03-modelo-dados.md` registra `shopfloor_devices`, `user_pins` e as colunas novas de `refresh_tokens`.
- Registrado em `06-pendencias-riscos.md` o adiamento do sem rede, com a condição (P-23), e o operador sem e-mail (P-24).

## Adendo 21/10/2026 (revisão pré-Fases 4 a 8, decisão D15 do PO): PIN no "Meu ponto"
- **Lista permitida da sessão por PIN** (§4) ganha os endpoints da marcação do próprio funcionário (ADR 0035): `GET /api/v1/me/hr/time-clock` (situação e próxima marcação sugerida) e `POST /api/v1/me/hr/time-entries` (marcar), com `[PinSessionAllowed]`. "Meu espelho", "Meus holerites" e todo o resto do `hr` continuam **fora** da lista (exigem login completo).
- **Marcar encerra a sessão por PIN no servidor:** na mesma transação da marcação, a sessão por PIN e a família de refresh dela são revogadas; o access token deixa de valer no próximo request (a revogação entra no cache da sessão conferido a cada request). O tablet volta à lista de nomes depois do comprovante.
- A marcação pela sessão por PIN grava `source = Pin` e o `device_id` do aparelho registrado; o aparelho registrado nunca pede geolocalização.
- Sem mudança na regra de perfil sensível: ganhar `hr.view_sensitive` apaga o PIN (§2).
- O teste de arquitetura do `[PinSessionAllowed]` passa a conter os dois endpoints, com a revisão registrada.

## Adendo 22/10/2026 (PRD-05): qualidade no tablet
- A fila e a decisão de qualidade ficam em `/api/v1/production/quality/*` (`GetQualityQueue`, `GetQualityStep`, `MarkQualityChecklist`, `StartQualityInspection`, `ApproveQuality`, `RejectQuality`), num arquivo próprio (`ProductionEndpoints.Quality.cs`), todos com `production.approve_quality` e nada mais; um teste de arquitetura garante que a ação só abre esses endpoints. A PRD-03b põe só eles (e o upload de anexo da NC, `UploadNonConformityAttachment`) na lista permitida da sessão por PIN.
- No front, `/fab/qualidade` e `/fab/qualidade/$stepId` usam o layout do `/fab`, mas conferem o módulo `production` (não o `shopfloor`): o inspetor não precisa do app do operador. Quem tem a ação vê a entrada "Qualidade" no topo da fila do operador.
