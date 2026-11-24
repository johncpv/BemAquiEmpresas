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

## Nota de aplicação 22/10/2026 (PRD-03b)
- **Conferência a cada request** sem claim nova: o `PinSessionScopeMiddleware` (Host, logo depois da autenticação) usa o cookie `__Host-bq_dev` e a família por PIN viva de (usuário, aparelho); depois, só os endpoints com `[PinSessionAllowed]`. O token leva `amr` em toda sessão (`pwd`, `otp`, `pin`).
- **Segredo anterior:** aceito dentro da tolerância do refresh (`Auth:RefreshReuseTolerance`); fora dela, revoga o aparelho.
- **GET `/api/v1/auth/pin/users`:** o navegador não manda `Origin` num GET da mesma origem; vale o `X-CSRF: 1`, com `Origin` igual ao host e `Sec-Fetch-Site: same-origin` quando vierem.
- **Login completo no tablet:** sem refresh quando a tela pede (`ephemeral`) ou quando o navegador já tem o cookie do aparelho; registrar o aparelho revoga o refresh que houver.
- **Refresh preso ao host** em que a sessão nasceu, nas duas sessões.
- Desvios e detalhes na Change Log da PRD-03b.

## Adendo 18/11/2026 (PDV-01, decisões PP-11 e PP-12 do PO): aparelho com tipo
- `identity.shopfloor_devices` ganha o tipo (`kind`: `Factory`, `Register`), a loja, o caixa, a época do caixa, a chave pública e a última sincronização ([ADR 0048](0048-aparelho-do-pdv-e-esquema-device.md) §1). Os aparelhos de antes ficam `Factory` e nada muda para eles: o `/fab`, o PIN, o segredo rotativo e o teto de 10 continuam valendo só para o tipo Fábrica.
- O caixa do PDV tem cookie próprio (`__Host-bq_pos`), chave ECDSA e o esquema `Device`; o segredo dele gira na sincronização, não no PIN (ADR 0048 §2).
- **Exceção nomeada ao §2 (PP-11):** o supervisor de balcão pode ter PIN no aparelho de caixa mesmo com `*.approve`, só para cancelar venda e NFC-e e dar desconto acima do limite, com motivo e aviso ao Proprietário (ADR 0048 §5). Fica ao lado da exceção da `production.approve_quality`.
- **§5 (operador sem e-mail):** no varejo, o operador sem e-mail é o usuário de operação da [ADR 0049](0049-usuario-de-operacao.md).

## Adendo 24/11/2026 (OFF-03, PP-10, PP-22 e PP-39): PIN e supervisor sem rede no caixa do PDV

Revoga, só para o aparelho do tipo Caixa, o "§6. Funcionamento sem rede: adiado". O desenho abaixo passa pela revisão de segurança antes do deploy.

- **Verificador (CA 1):** o servidor não conhece o PIN, só o hash PBKDF2 (formato V3 do `PasswordHasher`: algoritmo, iterações, sal e a chave derivada). Na descida do catálogo, para cada operador da loja (PIN) e cada supervisor com código sem internet, vão os parâmetros PBKDF2 e o verificador `HMAC-SHA256(chave derivada, "bq-pos-offline-verifier|v1|tipo|aparelho|pessoa|nonce")`, com nonce novo a cada descida. O PIN e a chave derivada nunca descem. O que desceu fica em `identity.device_pin_verifiers` (aparelho, pessoa, tipo, impressão digital SHA-256 do hash, quando).
- **No aparelho:** a lista fica no `LocalStore` cifrada com AES-GCM por uma chave não extraível própria (`verifier-key`), separada da chave ECDSA que assina os lotes e da chave do documento do consumidor; some com a chave do aparelho. O segredo digitado passa pela mesma PBKDF2 (WebCrypto) e só vive em memória.
- **Prova (CA 2, 4, 7 e 15):** a entrada (`pos.auth`) e a aprovação (`pos.approval`) levam `HMAC-SHA256(chave derivada, mensagem)` com o aparelho, as pessoas, os valores e o instante. Quem tem só o verificador não forja a prova; o servidor refaz com o hash atual. Segredo trocado, pessoa desativada ou fora da loja não conferem: a entrada fica marcada ("Acesso sem rede") e o que a pessoa fez fica na quarentena.
- **PP-39:** o operador usa o mesmo PIN (quebrar o verificador dá o PIN, que ele já tem); o supervisor usa um **código sem internet** separado, que nunca vale com rede, então quebrar o verificador de um supervisor rende só aprovações sem rede dentro dos limites da loja (PP-22).
- **Tentativas (CA 3):** 5 erros seguidos bloqueiam a pessoa naquele aparelho até a próxima sincronização; o bloqueio sobe e vai para a auditoria (`PosPinOfflineLocked`). Risco aceito (RP-08): com o aparelho na mão, 10⁶ tentativas da PBKDF2 quebram o código; por isso os limites e a reconferência.
- **Janela (CA 9, 10 e 14):** conta do último horário recebido do servidor; o "agora" do aparelho é o maior entre o relógio, esse horário e o maior horário já visto. Relógio antes do piso: registra a divergência e não abre turno novo até sincronizar.
