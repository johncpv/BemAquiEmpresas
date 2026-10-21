# PNL-01a — Tokens de tela, sessão de quiosque e tempo real da TV

| Campo | Valor |
|---|---|
| ID | PNL-01a |
| Título | Cadastro das telas de TV com token próprio (256 bits, guardado só como hash, mostrado uma vez), troca do token por uma sessão de quiosque em cookie (o token nunca vai na URL que chega ao servidor), esquema de autenticação só de leitura e restrito ao painel, hub de tempo real com o grupo derivado das claims, revogação que derruba a tela em menos de 1 minuto e o layout base da superfície `/tv` |
| Tamanho | G (quebra da PNL-01: tokens, sessão, autenticação e tempo real; as visões do painel ficam na PNL-01b) |
| Status | Draft |
| Requisitos | R-SEC-6 (token de quiosque por tela, revogável, somente leitura, escopo de painel) · PRD §5.14 (Painel de produção: tempo real via SignalR; uma tela por linha ou geral, filtro na configuração do token; modo escuro e fonte grande) · PRD §2 (Tela de quiosque: painel sem login) · PRD §6 (Segurança) · R-SEC-5 |
| Dependências | PRD-02 (`ProductionHub`, `IProductionBoardNotifier`, mensagem `OrderBoardChanged`), FND-02 (SignalR com o tenant, `PingHub`), FND-04 (sessões e cookies `__Host-`), FND-09b (superfície `/tv` sem dock), FND-12 (logs e alertas), ADR 0015 (log do nginx mascarado) |
| Bloqueia | PNL-01b (visões do painel) |
| Ordem de execução | 11 (Marco 4.4 "A fábrica na parede") |

## User story
Como **supervisor de produção**, quero **cadastrar cada TV da fábrica com um link próprio, que abre o painel sem login e que eu posso desligar a qualquer momento**, para **deixar o painel sempre ligado sem expor senha de ninguém nem o resto do sistema**.

## Contexto e referências
- PRD R-SEC-6; §2 ("Tela de quiosque: TV da fábrica — painel de produção sem login (token de tela)"); §5.14.
- CLAUDE.md: "Sem rota anônima, exceto login, convite, recuperação de senha, **quiosque (token de tela)**, webhooks…"; "Painel da TV (fonte grande, sem login) têm layout próprio, sem o dock"; "O Painel da TV é sempre escuro".
- `02-arquitetura` §4 ("Painel TV (`/tv/{token}`) — sem login, SignalR, rotação de visões") e §5 ("Token de quiosque: aleatório (256 bits), guardado com hash, escopo somente leitura do painel, revogável, IP opcional"); §2.1 (grupos SignalR `{tenantId}:{painel}`).
- **Revisão pré-Fase 0 §3 (Quiosque, A/S):** "cookie `__Host-kiosk` no lugar do token na URL; o hub deriva o grupo das claims; revalidação em até 60 s". ADR 0015: o log `bqe_masked` do nginx grava o caminho (`$uri`) — um token no caminho `/tv/{token}` iria para o log.
- `03-modelo-dados`: `identity.kiosk_tokens` (name, token_hash, scope (painel + filtros JSON), last_seen_at, revoked_at). FND-02 removeu o `kiosk_token_index` do catálogo (o tenant vem do host).
- `ModuleCatalog`: módulo `kiosk` ("Painel da TV", área Telas dedicadas, ações `view` e `create`: "Criar = gerar o token de uma tela"); `GroupSeeder`: Supervisão de produção tem `kiosk.view` e `kiosk.create`. `modules.ts`: slug `painel-tv`. Front: rota `/tv/$token` com `TvLayout` (esqueleto) e `Ajuda/tv.md` ("Nesta fase o painel ainda é só a estrutura").
- Backlog (CA da PNL-01): "token revogado derruba a tela em < 1 min; atualização < 2 s após apontamento".

## Critérios de aceite
**Cadastro das telas**
1. **Dado** `kiosk.view`, **quando** abro "Painel da TV" (`/app/painel-tv`), **então** vejo as telas cadastradas com nome, linha (ou "Todas"), visões, rotação, criado por e quando, último acesso ("Visto há {n} min" ou "Nunca conectada"), IPs permitidos e situação (Ativa, Revogada).
2. **Dado** `kiosk.create`, **quando** clico "Nova tela", informo nome (único entre as ativas), linha (todas ou uma das linhas dos centros de trabalho), visões e tempo de rotação (PNL-01b; padrão: todas as visões, 20 s, de 10 a 120 s) e, opcionalmente, as redes permitidas (CIDR, até 5), e confirmo, **então** o sistema gera um token aleatório de 256 bits, grava só o SHA-256 e os 6 primeiros caracteres (para identificar), e mostra **uma única vez** o link `https://{host}/tv#{token}` com "Copiar link" e a orientação "Abra este link no navegador da TV. Ele não aparece de novo."; a criação é auditada sem o token.
3. **Dado** uma tela, **quando** edito nome, linha, visões, rotação ou redes (`kiosk.create`, proposta, Pontos em aberto 2), **então** grava com auditoria e as TVs conectadas recebem a configuração nova em até 1 min, sem trocar o token.

**Sessão de quiosque**
4. **Dado** a TV abre `/tv#{token}`, **quando** o front carrega, **então** lê o token do fragmento (que o navegador não envia ao servidor), apaga o fragmento da barra (`history.replaceState`) e faz `POST /api/v1/kiosk/session` com o token no corpo; o servidor confere o hash no banco do tenant do host, a situação da tela, o módulo `kiosk` liberado, o tenant ativo e a rede permitida, e responde com o cookie `__Host-bq_kiosk` (opaco, 32 bytes, só o hash no banco; `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`), sem devolver dado do tenant além do nome da tela (proposta: o caminho muda de `/tv/$token` para `/tv`, Pontos em aberto 1).
5. **Dado** a TV reiniciou, **quando** abre `/tv`, **então** o cookie de sessão abre o painel sem o link; a sessão vence depois de 30 dias sem acesso (proposta, Pontos em aberto 3) e a TV mostra "Esta tela precisa ser conectada de novo. Peça um novo link a quem cuida do painel."
6. **[Segurança]** **Dado** o endpoint de sessão, **quando** recebe tokens errados, **então** responde 401 com tempo uniforme e mensagem única ("Link inválido ou desligado."), tem rate limit próprio de 10 tentativas por minuto por IP e tenant, e nenhum log grava o token, o cookie ou o hash (teste de canário no log e na auditoria).

**Autenticação só de leitura**
7. **[Segurança]** **Dado** o esquema de autenticação `Kiosk` (novo), **quando** um request chega com o cookie `__Host-bq_kiosk`, **então** o principal tem só `tid`, `kiosk_id` e o escopo (linha, visões); ele é aceito **apenas** em `GET /api/v1/kiosk/*` e no hub `/hubs/kiosk`; qualquer outro endpoint recusa o principal de quiosque (401), e qualquer método diferente de GET em `/api/v1/kiosk/*` responde 405 (teste de arquitetura: endpoint de quiosque é GET e declara a policy `kiosk`; nenhum endpoint de usuário aceita o esquema `Kiosk`).
8. **[Segurança]** **Dado** o escopo do token, **quando** a TV pede dados, **então** o servidor aplica a linha e as visões do token e ignora qualquer filtro enviado pelo cliente; um token de outro tenant não abre sessão no host deste (o hash não existe no banco dele) e o cookie `__Host-` não é enviado a outro host (teste com dois tenants).

**Tempo real**
9. **Dado** o hub `KioskHub` (`/hubs/kiosk`, novo), **quando** a TV conecta com a sessão, **então** entra nos grupos `{tenantId}:production` e `{tenantId}:kiosk:{kioskId}` derivados das claims (o hub não tem método para entrar em grupo, nem nenhum método de cliente → servidor), e passa a receber o `OrderBoardChanged` enviado pelo `IProductionBoardNotifier` da PRD-02 (o notificador envia para os dois hubs) e as mensagens de controle `KioskConfigChanged` e `KioskRevoked`.
10. **Dado** uma mudança no quadro, **quando** a transação do Production faz commit, **então** a TV recebe a mensagem (só o id) e recarrega o cartão pela API de quiosque; a medição do teste mostra menos de 2 s do commit à resposta da API na TV (PRD §6).

**Revogação**
11. **Dado** `kiosk.create`, **quando** clico "Revogar" numa tela e confirmo ("Revogar tela" / "Tela revogada"), **então** a tela fica `Revoked` (quem e quando, auditado), as sessões dela são invalidadas, o servidor envia `KioskRevoked` ao grupo da tela e derruba as conexões; a TV mostra "Esta tela foi desligada por {nome da empresa}." em **menos de 1 minuto** em todos os casos (com a conexão aberta: na hora; sem a conexão: na próxima revalidação, que acontece a cada 30 s em cada request e a cada 30 s no hub; teste com a conexão caída).
12. **Dado** o tenant suspenso ou o módulo `kiosk` deixa de ser liberado, **quando** a TV faz o próximo request ou a próxima revalidação, **então** a sessão é recusada (403 `module_not_released` ou tenant suspenso) e a TV mostra "Painel indisponível nesta versão do sistema." em menos de 1 minuto.
13. **Dado** redes permitidas na tela, **quando** um request vem de outro IP (IP real pelo cabeçalho do proxy confiável, como no rate limit do login), **então** 403 "Esta tela só funciona na rede da fábrica." e a sessão não é criada nem usada.

**Layout base e uso**
14. **Dado** a superfície `/tv`, **quando** abre, **então** é sempre escura (ignora o tema do perfil), sem dock nem barra do backoffice, com o cabeçalho (nome da linha, relógio grande, estado "Ao vivo"/"Reconectando…"), o botão "Tela cheia" e a tela mantida acesa (Wake Lock, quando o navegador permitir); sem conexão, mostra "Reconectando…" e mantém os dados com "Atualizado às {hh:mm}", com recarga a cada 60 s como reserva (ADR 0009).
15. **Dado** a TV conectada, **quando** usa o painel, **então** `last_seen_at` da tela é atualizado no máximo 1 vez por minuto, e a lista do CA 1 mostra "Visto há {n} min".
16. **Dado** o módulo `kiosk` não liberado, **quando** um usuário abre `/app/painel-tv`, **então** a tela `ModuleNotReleased` e a API 403 `module_not_released`; sem grupo, 403 `forbidden`; criar, editar ou revogar sem `kiosk.create`, 403 e o botão some. `RequireModule("kiosk")` e `perm:` nos endpoints de gestão.

## Regras de negócio e técnicas
- **Dono:** o Identity (tabela `identity.kiosk_tokens` do 03; módulo de catálogo `kiosk` sem projeto próprio, como o `shopfloor`); os dados do painel vêm do Production pelos endpoints `/api/v1/kiosk/*` (PNL-01b).
- **Token:** `OpaqueToken` (BuildingBlocks.Security, ADR 0012) de 32 bytes, base64url; comparação pelo hash; nunca em query string nem no caminho; o link só existe na tela de criação.
- **Sessão:** `identity.kiosk_sessions` (proposta) com hash, `kiosk_id`, criada em, vista em, IP, revogada em; cache da validação por 30 s, invalidado na revogação.
- **Sem dado pessoal nem dinheiro** no que a TV recebe (PNL-01b define o DTO).
- Guid v7; auditoria `IAuditable` na tela (sem o token nem o hash); `xmin`.

## Permissões, eventos e tabelas
- **Permissões:** `kiosk.view` (lista), `kiosk.create` (criar, editar e revogar; proposta, Pontos em aberto 2); policy `kiosk` (principal de quiosque) nos endpoints do painel.
- **Eventos:** nenhum no outbox; mensagens SignalR `OrderBoardChanged`, `KioskConfigChanged`, `KioskRevoked` no `KioskHub`.
- **Contratos:** `IProductionBoardNotifier` (PRD-02) passa a enviar também ao `KioskHub`.
- **Tabelas:** `identity.kiosk_tokens` (03) + `token_prefix`, `allowed_networks`, `created_by`, `created_at`, `revoked_by`, `xmin` (**propostas**); `scope` com linha, visões e rotação; `identity.kiosk_sessions` (**proposta**).

## Telas e estados
- **Telas dedicadas → Painel da TV** (`/app/painel-tv`): lista, "Nova tela" (painel lateral), diálogo do link mostrado uma vez, "Revogar" (estilo `destructive`).
- **Superfície `/tv`**: estados "Conectando…", "Link inválido ou desligado." (com a orientação de pedir um novo link), "Esta tela foi desligada", "Painel indisponível", "Reconectando…", e o painel (PNL-01b).
- **Propositivo:** depois de criar, `NextSteps` "Copiar link" e "Abrir em nova aba para testar"; tela "Nunca conectada" há mais de 1 dia mostra a dica "Abra o link na TV".
- **400 px:** a lista vira cartões (nome, linha, situação, último acesso); a superfície `/tv` não é pensada para celular, mas não quebra (uma coluna).
- **Ajuda:** `Ajuda/painel-tv.md` (rota `/app/painel-tv`: criar, copiar o link, revogar, redes) e atualizar `Ajuda/tv.md` (rota `/tv`); `<Dica>` em "Redes permitidas" e "Revogar".

## Plano de testes
- **Unidade:** geração e hash do token, validação de CIDR, escopo (CA 2, 8, 13).
- **Integração (Testcontainers, obrigatória):** criar e mostrar o link uma vez (CA 2); sessão pelo token no corpo, cookie e rate limit (CA 4–6); principal de quiosque recusado fora de `/api/v1/kiosk/*` e métodos não GET (CA 7); dois tenants (CA 8); hub com grupos das claims e sem métodos (CA 9); push depois do commit em menos de 2 s (CA 10); revogação com a conexão aberta e caída em menos de 1 min (CA 11); tenant suspenso e módulo não liberado (CA 12); IP fora da rede (CA 13); canário de log sem token (CA 6); três camadas da gestão (CA 16).
- **Arquitetura:** endpoints de quiosque só GET com a policy `kiosk`; nenhum endpoint de usuário aceita o esquema `Kiosk`; o hub não tem método público além da conexão.
- **E2E (Playwright):** criar a tela, abrir o link numa nova página, ver o painel base, revogar e ver a tela desligar; conferir que a URL final não tem o token.

## Fora do escopo
- As visões do painel (quadro por etapa, fila e previsões, atrasos) e a rotação: PNL-01b.
- Pareamento por código curto digitado na TV (alternativa ao link), controle remoto, várias telas por token.
- Alerta de TV desconectada (monitoramento): quando o PO pedir.

## Pontos em aberto
1. **Caminho da TV** (PO/arquiteto): o `02-arquitetura` §4 e a rota atual usam `/tv/{token}`, mas o token no caminho vai para o log do nginx (ADR 0015) e para o histórico do navegador; a revisão pré-Fase 0 recomendou o cookie. Proposta: link `/tv#{token}` trocado por cookie de sessão, com a rota `/tv`; atualizar o `02` §4 e a ajuda `tv.md`. Alternativa: manter `/tv/{token}` e excluir `/tv/` do log no vhost.
2. **Quem edita e revoga** (PO): o catálogo do `kiosk` só tem `view` e `create`. Proposta: `kiosk.create` cria, edita e revoga. Alternativa: acrescentar `edit` e `delete` ao `kiosk` no `ModuleCatalog` (com seed patch para a Supervisão).
3. **Duração da sessão da TV** (PO): proposta: 30 dias sem acesso; enquanto a TV estiver ligada, não vence.
4. **Doc desatualizado** (arquiteto): o `02-arquitetura` §2.1 ainda cita "tokens de quiosque (índice)" no catálogo, que a FND-02 removeu; corrigir junto com esta story.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dois tenants, revogação com conexão caída), arquitetura e E2E passando.
- [ ] Migration de `kiosk_tokens` (colunas novas) e `kiosk_sessions` (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; rota anônima só a de sessão de quiosque (lista de rotas anônimas da ADR 0009 atualizada).
- [ ] Telas com estados (gestão e TV) e 400 px.
- [ ] Ajudas `painel-tv` e `tv`; teste de cobertura passando.
- [ ] Auditoria ligada; canário de log sem token.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/02-arquitetura.md` (§2.1 e §4), `docs/03-modelo-dados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades e migration; serviço de telas (criar, editar, revogar) e endpoints `/api/v1/kiosk-screens`.
- [ ] `POST /api/v1/kiosk/session`, esquema `Kiosk`, policy, validação com cache e revalidação.
- [ ] `KioskHub`, notificador estendido, `KioskRevoked`/`KioskConfigChanged`.
- [ ] Superfície `/tv` (fragmento, cookie, estados, tela cheia, Wake Lock); tela de gestão e ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (esquema de autenticação e hub), cybersecurity (token, sessão, rota anônima, isolamento), @qa, design (TV escura).
- Gate: CA 4, 6, 7, 8 e 11 obrigatórios.

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

- **Correções feitas na validação:** o token em `/tv/{token}` iria para o log do nginx (ADR 0015) e contraria a recomendação da revisão pré-Fase 0 §3 (cookie): virou ponto em aberto 1 com proposta (`/tv#token` trocado por cookie), sem mudar o `02` por conta própria; principal de quiosque só em GET do painel, com teste de arquitetura (CA 7); revogação em menos de 1 min mesmo com a conexão caída (CA 11).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; quebra da PNL-01 (01a/01b) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
