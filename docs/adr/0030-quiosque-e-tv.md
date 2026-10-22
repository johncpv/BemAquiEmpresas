# ADR 0030: Quiosque e Painel da TV

- Status: Aceito (21/10/2026). O PO (John) aprovou a decisão D11 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`), os pontos 44 a 50 da tabela 3.1 e os achados da seção 4.3 (PNL-01a) e da seção 4.4 (padrão 4, kit TV).
- Contexto: o `02-arquitetura` previa `/tv/{token}`, com o token no caminho, e o front já tem a rota `/tv/$token` com o `TvLayout` vazio. O log do nginx (`bqe_masked`, ADR 0015) grava o caminho, então o token iria para o log; iria também para o histórico do navegador e para o `Referer`. A revisão pré-Fase 0 já pedia cookie no lugar do token na URL. Faltavam a revogação (o módulo `kiosk` só tem `view` e `create`), o prazo da sessão, a regra do hub e a leitura a distância: o protótipo usava fonte de backoffice numa TV vista a metros de distância.
- Complementa as ADRs 0009 (rotas anônimas, adendo de 21/10/2026 da revisão pré-Fases 4 a 8), 0011 (tokens opacos), 0012 (`OpaqueToken`), 0003 (adendo de 21/10/2026: `kiosk.delete`) e 0019 (§12).

## Decisão

### 1. Token no fragmento, trocado por cookie
- O link da tela é **`https://{host}/tv#{token}`**: o fragmento não é enviado ao servidor nem gravado no log do nginx.
- O front lê o fragmento, apaga-o da barra (`history.replaceState`) e faz `POST /api/v1/kiosk/session` com o token no corpo; a página `/tv` tem `Referrer-Policy: no-referrer`.
- O servidor confere o hash no banco do tenant do host (sem índice de quiosque no catálogo), a situação da tela, o módulo `kiosk` liberado, o tenant ativo e a rede permitida, e responde com o cookie **`__Host-bq_kiosk`** (opaco, 32 bytes, só o hash no banco; `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`).
- A rota do front passa de `/tv/$token` para **`/tv`**. Antes de trocar, conferir se há linhas em `identity.kiosk_tokens` (se houver, revogar e gerar de novo).
- Token de 256 bits, guardado como SHA-256, com os 6 primeiros caracteres para identificar; o link aparece uma única vez. **Pareamento de uso único:** o token vira uma sessão e não abre outra (para outra TV, gerar link novo); teto de 1 sessão ativa por tela.
- O endpoint de sessão responde 401 com tempo uniforme e mensagem única ("Link inválido ou desligado."), com rate limit por política nomeada (10 por minuto por IP e tenant); nenhum log grava token, cookie ou hash (canário).

### 2. Sessão e esquema `Kiosk`
- `identity.kiosk_sessions` (hash, `kiosk_id`, criada, vista, IP, revogada). Vence com **30 dias sem uso** e **180 dias absolutos**; depois, novo link.
- Esquema de autenticação **`Kiosk`**: o principal tem só `tid`, `kiosk_id` e o escopo (linha, visões). É aceito **apenas** em `GET /api/v1/kiosk/*` e no hub `/hubs/kiosk`; fora disso responde 403, e método diferente de GET em `/api/v1/kiosk/*` responde 405 (teste de arquitetura). Nenhum endpoint de usuário aceita o esquema `Kiosk`.
- O escopo do token é aplicado no servidor; filtros enviados pelo cliente são ignorados. `GET /api/v1/kiosk/board/orders/{id}` de OS fora do escopo responde 404.
- Respostas com `Cache-Control: no-store`; o service worker **não guarda `/tv` nem `/api/v1/kiosk`**.
- Revalidação da sessão a cada 30 s (request e hub), com cache invalidado na revogação: tela revogada, tenant suspenso ou módulo não liberado derrubam a TV em menos de 1 minuto.

### 3. Hub do quiosque
- **`KioskHub`** (`/hubs/kiosk`): grupos `{tenantId}:production` e `{tenantId}:kiosk:{kioskId}` **derivados das claims**; o hub não tem método para entrar em grupo nem método de cliente para servidor. Recebe `OrderBoardChanged` (só o id) pelo `IProductionBoardNotifier` e as mensagens `KioskConfigChanged` e `KioskRevoked`.
- O hub fecha a conexão quando a sessão é revogada ou deixa de valer.

### 4. Permissões
- `kiosk.view` lista as telas; `kiosk.create` cria e edita e **exige também `production.view`** (quem não vê a produção não a publica numa TV); **`kiosk.delete`** (ação padrão nova, ADR 0003) revoga.
- Cada tela criada, editada ou revogada notifica o Proprietário e é auditada sem o token nem o hash.

### 5. Conteúdo e leitura a distância (kit TV)
- **O cliente nunca aparece** na TV: só o nome curto do projeto. Operador pelo **primeiro nome e inicial**. Sem dinheiro e sem dado pessoal (o DTO do quiosque tem allowlist, teste de arquitetura).
- Tamanhos mínimos: **32 px** nos títulos, **28 px** nas linhas, **48 px** em contagens e relógio. Cartão com até 4 linhas, sem o produto; até 3 cartões por coluna, com "+{n} OS".
- Sempre tema escuro, sem dock; estados "Ao vivo", "Reconectando…" e "Atualizado às {hh:mm}"; `StageIndicator` com `size="tv"`.
- As 3 visões (PNL-01b), pulando "Atrasos" quando vazia.
- **Prévia no backoffice** em `/app/painel-tv/$id/previa`, com a sessão do usuário (`kiosk.view` + `production.view`), sem abrir sessão de quiosque.

## Consequências
- **PNL-01a** implementa as seções 1 a 4 e o adendo à ADR 0009; **PNL-01b** implementa a seção 5 e a prévia; o kit TV (D18) fica pronto antes do Marco 4.4.
- `docs/02-arquitetura.md` §2.1, §2.2 e §4 passam a usar `/tv#token`, sem índice de quiosque no catálogo.
- `docs/03-modelo-dados.md` registra `kiosk_tokens` (+ `token_prefix`, `allowed_networks`, `created_by`, `revoked_by`, `xmin`) e `kiosk_sessions`.

## Alternativas consideradas
- **Token no caminho (`/tv/{token}`):** rejeitada; vai para o log, o histórico e o `Referer`.
- **Token na query string:** rejeitada pelos mesmos motivos.
- **Login de usuário na TV:** rejeitada; a TV fica ligada dias sem ninguém, e uma sessão de usuário nela teria permissões demais.
