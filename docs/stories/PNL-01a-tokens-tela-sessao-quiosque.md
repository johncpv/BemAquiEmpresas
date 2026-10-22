# PNL-01a — Tokens de tela, sessão de quiosque e tempo real da TV

| Campo | Valor |
|---|---|
| ID | PNL-01a |
| Título | Cadastro das telas de TV com token próprio (256 bits, guardado só como hash, mostrado uma vez, de uso único), link `/tv#token` trocado por uma sessão de quiosque no cookie `__Host-bq_kiosk` (30 dias sem uso, 180 dias absolutos), esquema de autenticação só de leitura e restrito ao painel, hub de tempo real com o grupo derivado das claims, revogação com `kiosk.delete` que derruba a tela em menos de 1 minuto, aviso ao Proprietário e o kit base da superfície `/tv` (ADR 0030) |
| Tamanho | G (quebra da PNL-01: tokens, sessão, autenticação, tempo real e o kit base da TV; as visões e a prévia ficam na PNL-01b) |
| Status | Ready |
| Requisitos | R-SEC-6 (token de quiosque por tela, revogável, somente leitura, escopo de painel) · PRD §5.14 (Painel de produção: tempo real via SignalR; uma tela por linha ou geral, filtro na configuração do token; modo escuro e fonte grande) · PRD §2 (Tela de quiosque: painel sem login) · PRD §6 (Segurança) · R-SEC-5 |
| Dependências | PRD-02 (`ProductionHub`, `IProductionBoardNotifier`, mensagem `OrderBoardChanged`), PLN-01b (sinal `ScheduleChanged`), FND-02 (SignalR com o tenant, `PingHub`), FND-04 (sessões e cookies `__Host-`), FND-09b (superfície `/tv` sem dock), FND-12 (logs e alertas), ADR 0015 (log do nginx mascarado) |
| Bloqueia | PNL-01b (visões do painel e prévia) |
| Ordem de execução | 11 (Marco 4.4 "A fábrica na parede") |

## User story
Como **supervisor de produção**, quero **cadastrar cada TV da fábrica com um link próprio, que abre o painel sem login e que eu posso desligar a qualquer momento**, para **deixar o painel sempre ligado sem expor senha de ninguém nem o resto do sistema**.

## Contexto e referências
- PRD R-SEC-6; §2 ("Tela de quiosque: TV da fábrica — painel de produção sem login (token de tela)"); §5.14.
- CLAUDE.md: "Sem rota anônima, exceto login, convite, recuperação de senha, **quiosque (token de tela)**, webhooks…"; "Painel da TV (fonte grande, sem login) têm layout próprio, sem o dock"; "O Painel da TV é sempre escuro".
- **ADR 0030** (quiosque e TV, D11): link `/tv#token` (o fragmento não vai ao servidor; `history.replaceState` e `Referrer-Policy: no-referrer`) trocado pelo cookie `__Host-bq_kiosk`; sessão de 30 dias sem uso e 180 dias absolutos; esquema `Kiosk`, hub do quiosque e revogação; `kiosk.delete` no catálogo; criar exige também `production.view`; aviso ao Proprietário; leitura a distância (32/28/48 px); cliente nunca aparece; prévia no backoffice. **Adendos de 21/10/2026:** ADR 0003 (`kiosk.delete`), ADR 0009 (rota anônima de troca do token por cookie).
- `02-arquitetura` §4 ("Painel TV (`/tv/{token}`)…", corrigido para `/tv#token` junto com a ADR 0030) e §5 ("Token de quiosque: aleatório (256 bits), guardado com hash, escopo somente leitura do painel, revogável, IP opcional"); §2.1 (grupos SignalR `{tenantId}:{painel}`; o índice de quiosque no catálogo foi removido pela FND-02).
- Revisão pré-Fase 0 §3 (Quiosque, A/S): "cookie no lugar do token na URL; o hub deriva o grupo das claims; revalidação em até 60 s". ADR 0015: o log `bqe_masked` do nginx grava o caminho (`$uri`) — um token no caminho iria para o log.
- `03-modelo-dados`: `identity.kiosk_tokens` (name, token_hash, scope (painel + filtros JSON), last_seen_at, revoked_at). **Código atual:** não há entidade nem tabela `kiosk_tokens` (só o módulo no catálogo); o front tem a rota `/tv/$token` com `TvLayout` (esqueleto) e `Ajuda/tv.md` ("Nesta fase o painel ainda é só a estrutura").
- `ModuleCatalog`: módulo `kiosk` ("Painel da TV", área Telas dedicadas, ações `view` e `create`); `GroupSeeder`: Supervisão de produção tem `kiosk.view` e `kiosk.create` e `production.view`. `modules.ts`: slug `painel-tv`.
- Backlog (CA da PNL-01): "token revogado derruba a tela em < 1 min; atualização < 2 s após apontamento".
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D11, D18 (kit TV antes do Marco 4.4); tabela 3.1, pontos 44 a 47; §4.2 (conferir linhas em `kiosk_tokens` antes de trocar a rota); §4.3 (PNL-01a: pareamento de uso único ou teto de sessões; `no-store`; service worker sem `/tv` nem `/api/v1/kiosk`; principal de quiosque com 403 fora do GET do painel; grupos SignalR pelas claims); §4.4 padrão 4 (kit TV).

## Critérios de aceite
**Catálogo e cadastro das telas**
1. **Dado** o `ModuleCatalog`, **quando** a migration e o seed desta story rodam, **então** o módulo `kiosk` passa a declarar a ação padrão **`kiosk.delete`** ("Revogar uma tela: desliga a TV na hora.", ADR 0003 adendo); o Proprietário recebe a ação; o seed patch `kiosk-delete` dá a ação ao grupo Supervisão de produção do seed, uma vez, com o `perm_version` incrementado (ADR 0016); a concessão segue a delegação da ADR 0008.
2. **Dado** `kiosk.view`, **quando** abro "Painel da TV" (`/app/painel-tv`), **então** vejo as telas cadastradas com nome, linha (ou "Todas"), visões, rotação, criado por e quando, último acesso ("Visto há {n} min" ou "Nunca conectada"), sessões ativas, IPs permitidos e situação (Aguardando conexão, Ativa, Revogada).
3. **Dado** `kiosk.create` **e** `production.view`, **quando** clico "Nova tela", informo nome (único entre as ativas), linha (todas ou uma das linhas dos centros de trabalho), visões e tempo de rotação (PNL-01b; padrão: todas as visões, 20 s, de 10 a 120 s) e, opcionalmente, as redes permitidas (CIDR, até 5), e confirmo, **então** o sistema gera um token aleatório de 256 bits, grava só o SHA-256 e os 6 primeiros caracteres (para identificar), e mostra **uma única vez** o link `https://{host}/tv#{token}` com "Copiar link" e a orientação "Abra este link no navegador da TV. Ele vale para uma conexão só e não aparece de novo."; a criação é auditada sem o token e o Proprietário recebe a notificação "Nova tela de TV {nome} criada por {usuário}."; sem `production.view`, 403 e o botão some.
4. **Dado** uma tela, **quando** edito nome, linha, visões, rotação ou redes (`kiosk.create` e `production.view`), **então** grava com auditoria, o Proprietário recebe "Tela de TV {nome} alterada por {usuário}." e as TVs conectadas recebem a configuração nova em até 1 min (`KioskConfigChanged`), sem trocar o token.

**Sessão de quiosque**
5. **Dado** a TV abre `/tv#{token}`, **quando** o front carrega, **então** lê o token do fragmento (que o navegador não envia ao servidor), apaga o fragmento da barra (`history.replaceState`), a página leva `Referrer-Policy: no-referrer`, e faz `POST /api/v1/kiosk/session` com o token no corpo; o servidor confere o hash no banco do tenant do host, a situação da tela, o módulo `kiosk` liberado, o tenant ativo e a rede permitida, e responde com o cookie `__Host-bq_kiosk` (opaco, 32 bytes, só o hash no banco; `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`), sem devolver dado do tenant além do nome da tela; a rota do front passa a ser só `/tv`.
6. **[Segurança]** **Dado** o token de uma tela, **quando** ele abre uma sessão, **então** fica **consumido** (pareamento de uso único): uma segunda troca com o mesmo token responde 401 "Link inválido ou desligado." e o Proprietário recebe "Tentaram usar de novo o link da tela {nome}."; para conectar outra vez (TV nova, cookie apagado), quem tem `kiosk.create` e `production.view` clica "Gerar novo link", que troca o token e derruba as sessões anteriores da tela; cada tela tem no máximo **1 sessão ativa** (ADR 0030 §1).
7. **Dado** a TV reiniciou, **quando** abre `/tv`, **então** o cookie de sessão abre o painel sem o link; a sessão vence depois de **30 dias sem uso** ou **180 dias** desde o pareamento, o que vier primeiro, e a TV mostra "Esta tela precisa ser conectada de novo. Peça um novo link a quem cuida do painel."
8. **[Segurança]** **Dado** o endpoint de sessão, **quando** recebe tokens errados, **então** responde 401 com tempo uniforme e mensagem única ("Link inválido ou desligado."), tem rate limit pela política nomeada `kiosk-session` (10 tentativas por minuto por IP e tenant), e nenhum log grava o token, o cookie ou o hash (teste de canário no log da aplicação, na auditoria e no `access_log`/`error_log` do nginx).

**Autenticação só de leitura**
9. **[Segurança]** **Dado** o esquema de autenticação `Kiosk` (novo), **quando** um request chega com o cookie `__Host-bq_kiosk`, **então** o principal tem só `tid`, `kiosk_id` e o escopo (linha, visões); ele é aceito **apenas** em `GET /api/v1/kiosk/*` e no hub `/hubs/kiosk`; em qualquer outro endpoint o principal de quiosque recebe **403**, e qualquer método diferente de GET em `/api/v1/kiosk/*` responde 405 (teste de arquitetura: endpoint de quiosque é GET e declara a policy `kiosk`; nenhum endpoint de usuário aceita o esquema `Kiosk`).
10. **[Segurança]** **Dado** o escopo do token, **quando** a TV pede dados, **então** o servidor aplica a linha e as visões do token e ignora qualquer filtro enviado pelo cliente; um token de outro tenant não abre sessão no host deste (o hash não existe no banco dele) e o cookie `__Host-` não é enviado a outro host (teste com dois tenants).
11. **[Segurança]** **Dado** as respostas de `/tv` e de `/api/v1/kiosk/*`, **quando** saem, **então** levam `Cache-Control: no-store`, e o service worker do PWA (vite-plugin-pwa) não guarda `/tv` nem `/api/v1/kiosk/*` (teste da configuração do service worker).

**Tempo real**
12. **Dado** o hub `KioskHub` (`/hubs/kiosk`, novo), **quando** a TV conecta com a sessão, **então** entra nos grupos `{tenantId}:production` e `{tenantId}:kiosk:{kioskId}` derivados das claims (o hub não tem método para entrar em grupo, nem nenhum método de cliente → servidor), e passa a receber o `OrderBoardChanged` enviado pelo `IProductionBoardNotifier` da PRD-02 (o notificador envia para os dois hubs), o `ScheduleChanged` da PLN-01b e as mensagens de controle `KioskConfigChanged` e `KioskRevoked`.
13. **Dado** uma mudança no quadro, **quando** a transação do Production faz commit, **então** a TV recebe a mensagem (só o id) e recarrega o cartão pela API de quiosque; a medição do teste mostra menos de 2 s do commit à resposta da API na TV (PRD §6).

**Revogação**
14. **Dado** `kiosk.delete`, **quando** clico "Revogar" numa tela e confirmo ("Revogar tela" / "Tela revogada"), **então** a tela fica `Revoked` (quem e quando, auditado), as sessões dela são invalidadas, o servidor envia `KioskRevoked` ao grupo da tela e **fecha as conexões**; a TV mostra "Esta tela foi desligada por {nome da empresa}." em **menos de 1 minuto** em todos os casos (com a conexão aberta: na hora; sem a conexão: na próxima revalidação, que acontece a cada 30 s em cada request e a cada 30 s no hub; teste com a conexão caída); o Proprietário recebe "Tela de TV {nome} revogada por {usuário}."; sem `kiosk.delete`, 403 e o botão some.
15. **Dado** o tenant suspenso ou o módulo `kiosk` deixa de ser liberado, **quando** a TV faz o próximo request ou a próxima revalidação, **então** a sessão é recusada (403 `module_not_released` ou tenant suspenso), a conexão do hub é fechada e a TV mostra "Painel indisponível nesta versão do sistema." em menos de 1 minuto.
16. **Dado** redes permitidas na tela, **quando** um request vem de outro IP (IP real pelo cabeçalho do proxy confiável, `KnownProxies` só com o `stack-nginx`), **então** 403 "Esta tela só funciona na rede da fábrica." e a sessão não é criada nem usada.

**Troca da rota e kit base da TV**
17. **Dado** o deploy desta story, **quando** a migration roda, **então** ela confere se existe `identity.kiosk_tokens` com linhas (tela criada com o link antigo `/tv/{token}`): havendo, revoga todas, registra a revogação na auditoria ("Rota da TV trocada para /tv#token") e avisa o Proprietário para gerar links novos; a rota antiga `/tv/$token` deixa de existir no front e qualquer `/tv/*` mostra "Este link antigo foi desligado. Peça um novo link a quem cuida do painel." sem chamar a API.
18. **Dado** a superfície `/tv`, **quando** abre, **então** é sempre escura (ignora o tema do perfil), sem dock nem barra do backoffice, com o **kit TV base**: tokens tipográficos de leitura a distância (`--tv-title` 32 px, `--tv-line` 28 px, `--tv-count` 48 px, escalados pela altura da tela), o cabeçalho (nome da linha, relógio de 48 px, estado **"Ao vivo" / "Reconectando…" / "Atualizado às {hh:mm}"**), o `StageIndicator` com `size="tv"`, o botão "Tela cheia" e a tela mantida acesa (Wake Lock, quando o navegador permitir); sem conexão, mostra "Reconectando…" e mantém os dados com "Atualizado às {hh:mm}", com recarga a cada 60 s como reserva (ADR 0009).
19. **Dado** a TV conectada, **quando** usa o painel, **então** `last_seen_at` da tela e da sessão é atualizado no máximo 1 vez por minuto, e a lista do CA 2 mostra "Visto há {n} min".
20. **Dado** o módulo `kiosk` não liberado, **quando** um usuário abre `/app/painel-tv`, **então** a tela `ModuleNotReleased` e a API 403 `module_not_released`; sem grupo, 403 `forbidden`; criar, editar ou gerar novo link sem `kiosk.create` e `production.view`, ou revogar sem `kiosk.delete`, 403 e o botão some. `RequireModule("kiosk")` e `perm:` nos endpoints de gestão.

## Regras de negócio e técnicas
- **Dono:** o Identity (tabelas `identity.kiosk_tokens` e `identity.kiosk_sessions`; módulo de catálogo `kiosk` sem projeto próprio, como o `shopfloor`); os dados do painel vêm do Production pelos endpoints `/api/v1/kiosk/*` (PNL-01b).
- **Token** (ponto 44): `OpaqueToken` (BuildingBlocks.Security, ADR 0012) de 32 bytes, base64url; comparação pelo hash; uso único; nunca em query string nem no caminho; o link só existe na tela de criação e na de "Gerar novo link".
- **Sessão** (ponto 46): hash do cookie, `kiosk_id`, criada em, vista em, IP, vence em (30 dias sem uso, 180 absolutos), revogada em; cache da validação por 30 s, invalidado na revogação.
- **Permissões** (ponto 45): `kiosk.view` (lista), `kiosk.create` + `production.view` (criar, editar, gerar novo link), `kiosk.delete` (revogar).
- **Sem dado pessoal nem dinheiro** no que a TV recebe (PNL-01b define o DTO).
- Guid v7; auditoria `IAuditable` na tela (sem o token nem o hash); `xmin`.

## Permissões, eventos e tabelas
- **Permissões:** `kiosk.view`, `kiosk.create` (com `production.view`), `kiosk.delete` (nova no catálogo, seed patch `kiosk-delete`); policy `kiosk` (principal de quiosque) nos endpoints do painel.
- **Eventos:** nenhum no outbox; mensagens SignalR `OrderBoardChanged`, `ScheduleChanged`, `KioskConfigChanged`, `KioskRevoked` no `KioskHub`.
- **Contratos:** `IProductionBoardNotifier` (PRD-02) passa a enviar também ao `KioskHub`.
- **Tabelas:** `identity.kiosk_tokens` (03) + `token_prefix`, `allowed_networks`, `consumed_at`, `created_by`, `created_at`, `revoked_by`, `xmin`; `scope` com linha, visões e rotação; `identity.kiosk_sessions` (nova: `token_hash`, `kiosk_id`, `created_at`, `last_seen_at`, `expires_at`, `absolute_expires_at`, `ip`, `revoked_at`).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 4 (kit TV: tema escuro, 32/28/48 px, "Ao vivo / Reconectando… / Atualizado às", `StageIndicator` com `size="tv"`; os cartões de 4 linhas, as colunas de 3 cartões e a prévia são da PNL-01b) e 7 (revogação com confirmação e impacto: "A TV {nome} desliga em até 1 minuto").
- **Telas dedicadas → Painel da TV** (`/app/painel-tv`): lista, "Nova tela" (painel lateral), diálogo do link mostrado uma vez, "Gerar novo link", "Revogar" (estilo `destructive`).
- **Superfície `/tv`**: estados "Conectando…", "Link inválido ou desligado." (com a orientação de pedir um novo link), "Este link antigo foi desligado", "Esta tela foi desligada", "Painel indisponível", "Reconectando…", e o painel (PNL-01b).
- **Componentes (D18):** kit TV base (tokens tipográficos da TV, cabeçalho com relógio e estado da conexão, `StageIndicator size="tv"`).
- **Propositivo:** depois de criar, `NextSteps` "Copiar link" e "Ver prévia" (PNL-01b); tela "Aguardando conexão" há mais de 1 dia mostra a dica "Abra o link na TV"; tela com sessão vencida oferece "Gerar novo link".
- **400 px:** a lista vira cartões (nome, linha, situação, último acesso) com botões de 44 px; a superfície `/tv` não é pensada para celular, mas não quebra (uma coluna).
- **Ajuda:** `Ajuda/painel-tv.md` (rota `/app/painel-tv`: criar, copiar o link de uso único, gerar novo link, revogar, redes) e atualizar `Ajuda/tv.md` (rota `/tv`); `<Dica>` em "Redes permitidas", "Gerar novo link" e "Revogar".

## Plano de testes
- **Unidade:** geração e hash do token, validação de CIDR, escopo, vencimento de 30/180 dias (CA 3, 7, 10, 16).
- **Integração (Testcontainers, obrigatória):** ação `kiosk.delete` e seed patch uma vez só (CA 1); criar com e sem `production.view`, link uma vez, aviso ao Proprietário (CA 3); sessão pelo token no corpo, cookie e uso único (CA 5, 6); vencimento (CA 7); rate limit e canário de log (CA 8); principal de quiosque com 403 fora de `/api/v1/kiosk/*` e 405 em métodos não GET (CA 9); dois tenants (CA 10); `no-store` (CA 11); hub com grupos das claims e sem métodos (CA 12); push depois do commit em menos de 2 s (CA 13); revogação com a conexão aberta e caída em menos de 1 min, com `kiosk.delete` (CA 14); tenant suspenso e módulo não liberado (CA 15); IP fora da rede (CA 16); migration com linhas antigas em `kiosk_tokens` (CA 17); três camadas da gestão (CA 20).
- **Arquitetura:** endpoints de quiosque só GET com a policy `kiosk`; nenhum endpoint de usuário aceita o esquema `Kiosk`; o hub não tem método público além da conexão.
- **Front (Vitest):** service worker sem `/tv` nem `/api/v1/kiosk` (CA 11); kit TV base (CA 18).
- **E2E (Playwright):** criar a tela, abrir o link numa nova página, ver o painel base, conferir que a URL final não tem o token e que o segundo uso do link é recusado, revogar e ver a tela desligar.

## Fora do escopo
- As visões do painel (quadro por etapa, fila e previsões, atrasos), a rotação, os cartões e a prévia no backoffice: PNL-01b.
- Pareamento por código curto digitado na TV, controle remoto, várias telas por token.
- Alerta de TV desconectada (monitoramento): quando o PO pedir.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 44–47):
1. `/tv#token` trocado pelo cookie `__Host-bq_kiosk`, com a rota `/tv` (D11, ADR 0030; CA 5, 17).
2. `kiosk.delete` no catálogo para revogar; criar e editar exigem `kiosk.create` e também `production.view` (D11; CA 1, 3, 14, 20).
3. Sessão de 30 dias sem uso e 180 dias absolutos (D11; CA 7).
4. O `02-arquitetura` §2.1 e §4 é corrigido junto (`/tv#token`, sem índice de quiosque no catálogo), pela ADR 0030 (DoD).

Decisões da revisão aplicadas: D11 e ADR 0030 (aviso ao Proprietário, CA 3, 6, 14); D18 (kit TV base nesta story, CA 18); §4.2 (conferir `kiosk_tokens` antes de trocar a rota, CA 17); §4.3 (pareamento de uso único, CA 6; `no-store` e service worker, CA 11; 403 fora do GET do painel, CA 9; conexão fechada na revogação, CA 14, 15; canário também no log do nginx, CA 8); adendos das ADRs 0003 e 0009.

**Pré-condição externa:** nenhuma (a TV é da Konebras, P-12 decidida).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dois tenants, revogação com conexão caída), arquitetura, front (Vitest) e E2E passando.
- [ ] Migration de `kiosk_tokens` e `kiosk_sessions` (não destrutiva), com a conferência das linhas antigas; seed patch `kiosk-delete` (ADR 0003 adendo aplicado).
- [ ] Permissões só do `ModuleCatalog`; rota anônima só a de sessão de quiosque (lista de rotas anônimas da ADR 0009 atualizada).
- [ ] Telas com estados (gestão e TV) e 400 px; kit TV base com tokens e sem cor literal.
- [ ] Ajudas `painel-tv` e `tv`; teste de cobertura passando.
- [ ] Auditoria ligada; canário de log sem token.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/02-arquitetura.md` (§2.1 e §4) e `docs/03-modelo-dados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `kiosk.delete` no `ModuleCatalog` e seed patch; entidades e migration (com a conferência das linhas antigas).
- [ ] Serviço de telas (criar, editar, gerar novo link, revogar) e endpoints `/api/v1/kiosk-screens`; avisos ao Proprietário.
- [ ] `POST /api/v1/kiosk/session`, esquema `Kiosk`, policy, uso único, validação com cache e revalidação.
- [ ] `KioskHub`, notificador estendido, `KioskRevoked`/`KioskConfigChanged`.
- [ ] Superfície `/tv` (fragmento, cookie, estados, tela cheia, Wake Lock), kit TV base, service worker; tela de gestão e ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (esquema de autenticação e hub), cybersecurity (token, sessão, rota anônima, isolamento), @qa, design (kit TV).
- Gate: CA 5, 6, 8, 9, 10, 14 e 17 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: PLN-01b pelo `ScheduleChanged`) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** o token em `/tv/{token}` iria para o log do nginx (ADR 0015) e contrariava a recomendação da revisão pré-Fase 0 §3: virou ponto em aberto com proposta (`/tv#token` trocado por cookie); principal de quiosque só em GET do painel, com teste de arquitetura; revogação em menos de 1 min mesmo com a conexão caída.
- **Correções da revalidação (21/10/2026):** pontos 44 a 47 decididos (`/tv#token` com cookie e `no-referrer`, CA 5; `kiosk.delete` com seed patch e `production.view` para criar, CA 1, 3, 20; 30/180 dias, CA 7; `02-arquitetura` corrigido junto); pareamento de uso único com "Gerar novo link" e aviso ao Proprietário (§4.3, D11; CA 3, 6, 14); 403 para o principal de quiosque fora do painel (CA 9); `no-store` e service worker (CA 11); conexão fechada na revogação e na suspensão (CA 14, 15); conferência das linhas antigas de `kiosk_tokens` na troca da rota (§4.2, CA 17); kit TV base com 32/28/48 px e `StageIndicator size="tv"` (D18, padrão 4, CA 18); `KnownProxies` só com o `stack-nginx` (D19); CAs renumerados (20).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; quebra da PNL-01 (01a/01b) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D11, D18, D19, tabela 3.1 pontos 44–47, §4.2, §4.3, §4.4 padrões 4 e 7; ADR 0030; adendos 0003 e 0009); ordem mantida (Marco 4.4, ordem 11); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
