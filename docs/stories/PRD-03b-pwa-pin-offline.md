# PRD-03b — App do operador: acesso rápido por PIN e funcionamento sem rede

| Campo | Valor |
|---|---|
| ID | PRD-03b |
| Título | PWA instalável no tablet; dispositivo da fábrica registrado pelo supervisor; acesso rápido por PIN (opcional, depois de um login completo do usuário naquele dispositivo), com bloqueio por tentativas e sessão curta; fila de apontamentos guardada no aparelho quando a rede cai e sincronizada ao voltar, sem duplicar |
| Tamanho | M (quebra da PRD-03) |
| Status | Draft |
| Requisitos | R-PRD-3 (apontamento no PWA) · PRD §6 (Usabilidade: tablet de entrada; Segurança: OWASP, rate limit) · R-SEC-1 (bloqueio após tentativas) · R-SEC-3 (sessão) · R-SEC-7 (sessões visíveis e revogáveis, D) · R-SEC-5 |
| Dependências | PRD-03a (fila e apontamento), FND-04 (login, refresh rotativo, security stamp), FND-05 (2FA), FND-06c (permissão efetiva), FND-12 (rate limit e alertas) |
| Bloqueia | — |
| Ordem de execução | 16 (Marco 3.4 "Fábrica rodando") |

## User story
Como **operador**, quero **entrar no tablet da fábrica com um PIN curto e continuar apontando quando o Wi-Fi cair**, e como **proprietário**, quero **que o PIN só funcione no aparelho da fábrica que o supervisor liberou**, para **apontar ser rápido sem abrir uma porta de entrada para qualquer um**.

## Contexto e referências
- Backlog PRD-03: "Login rápido (PIN opcional após login completo no dispositivo)… tolerância a queda de rede"; CA "apontamento offline sincroniza ao reconectar sem duplicar; botões ≥ 56px; funciona em tablet Android de entrada".
- `02-arquitetura` §4 ("Chão de fábrica PWA (`/fab`) — … modo offline tolerante (fila de apontamentos guardada localmente e sincronizada)"); §5 (JWT 15 min, refresh 7 dias rotativo em cookie httpOnly, detecção de reuso).
- ADR 0011 (identity enxuto: tokens opacos de 32 bytes guardados como hash, bloqueio de 15 min depois de 5 erros, anti-replay atômico); ADR 0008 (JWT só com identidade; security stamp a cada request); ADR 0009 (cookie `__Host-bq_rt`, `X-CSRF`); ADR 0010 item 5/6 (rate limit).
- CLAUDE.md: "Sem rota anônima, exceto login, convite, recuperação de senha, quiosque (token de tela), webhooks…": o PIN é uma forma de **login** e só vale com o dispositivo registrado; o token de tela (quiosque) é do painel da TV (PNL-01), não do PWA do operador.
- `06` P-12 (tablet Android de entrada). ADR 0019 §3 (idempotência).
- `CLAUDE.md` Stack: `vite-plugin-pwa` (ainda não instalado no `src/Web`).

## Critérios de aceite
**PWA instalável**
1. **Dado** o `/fab`, **quando** aberto no Chrome do Android, **então** o app é instalável (manifesto com nome "BemAqui Fábrica", ícones, `display: standalone`, `start_url: /fab`, tema escuro por padrão) e o service worker do `vite-plugin-pwa` guarda o shell do `/fab` (HTML, JS, CSS, fontes) para abrir sem rede; o `/app` (backoffice) não é cacheado para uso offline.

**Dispositivo registrado**
2. **Dado** um usuário com `production.edit` + `shopfloor.create` logado no tablet, **quando** abre "Registrar este aparelho da fábrica", dá um nome (até 40, ex.: "Tablet Linha 1") e confirma, **então** nasce o registro do dispositivo (proposta: `identity.shopfloor_devices` com nome, hash do segredo, `created_by`, `last_seen_at`, `revoked_at`), e o aparelho guarda um segredo opaco de 32 bytes num cookie `__Host-bq_dev` (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`), que vale por 180 dias (proposta); no banco fica só o hash.
3. **Dado** a lista "Aparelhos da fábrica" (Configurações → Segurança, com `settings.view`; revogar com `settings.edit` ou `production.approve`, proposta), **quando** revogo um aparelho, **então** o PIN deixa de funcionar nele no próximo acesso e as sessões abertas por PIN nele são revogadas (R-SEC-7).

**PIN**
4. **Dado** um operador que fez o login completo (e-mail e senha, e o 2FA quando exigido) **num dispositivo registrado**, **quando** escolhe "Criar PIN para este aparelho" e digita duas vezes um PIN de 6 dígitos (proposta), **então** o PIN é guardado com hash (PBKDF2, `PasswordHasher`) e vinculado ao par usuário + dispositivo; PIN óbvio (000000, 123456, data de nascimento quando conhecida) é recusado.
5. **Dado** o tablet registrado, **quando** abro `/fab/entrar`, **então** vejo os operadores que têm PIN neste aparelho (só primeiro nome e inicial; proposta) e, ao tocar no nome e digitar o PIN, a sessão abre; o `POST /api/v1/auth/pin` é anônimo **só** com o cookie do dispositivo válido (sem ele, 404, como rota inexistente), exige `Origin` do tenant e `X-CSRF: 1`, e entra no rate limit do login (ADR 0010 item 6).
6. **[Segurança]** **Dado** 5 PINs errados seguidos para o mesmo usuário no mesmo aparelho, **quando** acontece, **então** o PIN daquele par fica bloqueado por 15 minutos (resposta e tempo uniformes, ADR 0011), e o 10º erro em 24 h apaga o PIN e exige novo login completo; os erros vão para a auditoria e contam para o alerta de abuso (FND-12).
7. **[Segurança]** **Dado** a sessão aberta por PIN, **quando** emitida, **então** o access token tem `amr` com `pin` e a sessão só autoriza os endpoints do `shopfloor` (e o pedido de material da EST-03): qualquer endpoint do backoffice responde 403 `pin_session_scope`; o refresh dessa sessão vale até 12 h (um turno, proposta) e é revogado com o dispositivo, com a troca de senha ou com o security stamp; o usuário sai sozinho depois de 15 min sem toque (proposta), voltando à lista de nomes.
8. **[Segurança]** **Dado** um usuário que tem `*.approve`, `finance.pay`, `hr.view_sensitive`, `identity.edit` ou `settings.edit`, **quando** tenta criar PIN, **então** 403 "Seu perfil não pode usar PIN." (ações sensíveis nunca por PIN; o supervisor usa o login completo).
9. **Dado** um usuário desativado, um funcionário desligado ou afastado, ou um usuário que perdeu `shopfloor.create`, **quando** tenta entrar por PIN, **então** é recusado no próximo acesso (security stamp e permissão efetiva conferidos a cada request).

**Sem rede**
10. **Dado** a rede caiu com a etapa aberta no PWA, **quando** o operador toca "Iniciar", "Pausar", "Retomar", "Finalizar" ou marca item do checklist, **então** a ação vai para uma fila local (IndexedDB, por usuário e dispositivo) com o instante do toque (relógio do aparelho), um `Idempotency-Key` (UUID) e a ordem; a tela mostra o efeito na hora com o chip "Sem conexão · {n} apontamentos aguardando" e o cronômetro continua localmente.
11. **Dado** a rede voltou, **quando** o PWA sincroniza (na reconexão e a cada 30 s), **então** envia a fila em ordem, um a um; cada ação usa o `Idempotency-Key` e o instante informado (`occurred_at`), e o servidor aceita o instante do aparelho se estiver entre o último evento aceito da etapa e agora + 2 min (proposta); fora disso grava o instante do servidor com a marca "Horário ajustado na sincronização"; nada duplica (teste: reenviar a fila inteira duas vezes).
12. **Dado** um conflito na sincronização (a etapa foi concluída pelo supervisor, outro operador finalizou, a OS foi cancelada, o funcionário já tem outro apontamento aberto), **quando** o servidor recusa (409/422), **então** a ação sai da fila com o motivo, o PWA mostra "1 apontamento não entrou: {motivo}. Avise o supervisor." e o supervisor recebe a notificação com link para corrigir o apontamento (PRD-03a CA 14); nada fica preso na fila.
13. **Dado** a fila local, **quando** o operador sai (ou a sessão por PIN expira) com itens pendentes, **então** os itens continuam guardados e são enviados na próxima sessão **do mesmo usuário**; outro operador no mesmo tablet não vê nem envia a fila de outro; itens com mais de 72 h (proposta) são descartados com aviso ao supervisor.
14. **Dado** o PWA sem rede, **quando** o operador tenta ações que dependem do servidor (abrir uma etapa que não estava carregada, pedir material, entrar por PIN), **então** vê "Sem conexão. Esta ação precisa de rede." com alvo grande para "Tentar de novo".

**Permissões e auditoria**
15. **Dado** o módulo `shopfloor` não liberado, **quando** chamo o PIN ou a sincronização, **então** 403 `module_not_released`; sem `shopfloor.create`, 403 `forbidden`; registrar aparelho sem `production.edit` + `shopfloor.create`: 403.
16. **Dado** registro e revogação de aparelho, criação e bloqueio de PIN, entradas por PIN e ações sincronizadas com horário ajustado, **quando** consulto a auditoria, **então** vejo quem, quando, qual aparelho e o IP, sem o PIN nem o segredo (teste de canário: o PIN e o segredo não aparecem em `audit.audit_log` nem no log).

## Regras de negócio e técnicas
- **Tabelas** (propostas): `identity.shopfloor_devices` (`name`, `secret_hash`, `created_by`, `created_at`, `last_seen_at`, `revoked_at`, `revoked_by`); `identity.user_pins` (`user_id`, `device_id`, `pin_hash`, `failed_count`, `locked_until`, `created_at`), único por `(user_id, device_id)`. O Identity é dono (autenticação).
- **Escopo da sessão por PIN:** claim `amr = pin`; um filtro do Host nega o que não é `shopfloor` (lista permitida nos endpoints, conferida por teste de arquitetura).
- **Fila local:** só ações de apontamento e checklist; cada ação com `Idempotency-Key`, `occurred_at`, `stepId`; o servidor guarda o resultado em `shared.idempotency_records` (FND-09e).
- **Nada de dado sensível no aparelho:** o IndexedDB guarda só ids, instantes e o texto do motivo de pausa; o access token fica em memória, como no backoffice (o mutator `http.ts`).
- Guid v7; auditoria `IAuditable` em dispositivo e PIN (sem o hash no diff, `[ProtectedSecret]`).

## Permissões, eventos e tabelas
- **Permissões:** `shopfloor.create` (PIN e sincronização), `production.edit` + `shopfloor.create` (registrar aparelho), `settings.view`/`settings.edit` (lista de aparelhos; revogar também com `production.approve`, proposta).
- **Eventos:** nenhum novo (as ações sincronizadas publicam os eventos da PRD-03a).
- **Tabelas:** `identity.shopfloor_devices`, `identity.user_pins` (propostas).

## Telas e estados
- **`/fab/entrar`** (lista de nomes e teclado numérico grande, alvos ≥ 56 px), **`/fab`** com o chip de conexão, **Configurações → Segurança → Aparelhos da fábrica**.
- **Verbos:** "Registrar este aparelho" / "Aparelho registrado"; "Criar PIN" / "PIN criado"; "Revogar aparelho" / "Aparelho revogado".
- **Estados:** sem aparelho registrado ("Este aparelho não está registrado. Entre com e-mail e senha."); PIN bloqueado ("PIN bloqueado por 15 minutos. Entre com e-mail e senha ou espere."); sem conexão (CA 10, 14); sincronizando ("Enviando {n} apontamentos…"); conflito (CA 12).
- **Propositivo:** depois do login completo num aparelho registrado, o PWA oferece "Criar PIN para entrar mais rápido"; o último operador do aparelho aparece primeiro na lista.
- **Ajuda:** `Ajuda/fab-entrar.md` (rota `/fab/entrar`), atualização de `Ajuda/fab.md` ("Sem conexão") e de `Ajuda/configuracoes-seguranca.md` ("Aparelhos da fábrica"); `<Dica>` em "PIN".

## Plano de testes
- **Unidade:** regras do PIN (formato, óbvios, bloqueio); janela do instante sincronizado (CA 11).
- **Integração (Testcontainers):** registro e revogação de aparelho com cookie `__Host-` (CA 2, 3); criar PIN só depois do login completo e recusa para perfis sensíveis (CA 4, 8); PIN sem cookie do aparelho → 404, com `Origin`/`X-CSRF` e rate limit (CA 5); bloqueio e apagamento (CA 6); escopo da sessão por PIN em todos os endpoints do backoffice (teste parametrizado) e expiração (CA 7); desativado, desligado e sem permissão (CA 9); sincronização idempotente com reenvio duplo, instante dentro e fora da janela, conflitos (CA 11, 12); três camadas (CA 15); canário de auditoria e log (CA 16).
- **E2E (Playwright):** instalar o PWA (manifesto válido), registrar o aparelho, criar PIN, entrar por PIN; derrubar a rede (`context.setOffline(true)`), apontar, voltar a rede e ver a sincronização sem duplicar; dois operadores no mesmo aparelho sem ver a fila do outro.
- **Manual:** tablet Android de entrada com Wi-Fi intermitente, registrado na story.

## Fora do escopo
- Token de quiosque e painel da TV: PNL-01 (Fase 4).
- Offline do backoffice e de outras telas do PWA além do apontamento.
- Biometria e crachá NFC: fora do MVP.

## Pontos em aberto
1. **Formato e duração** (PO/segurança): propostas do CA 4 e 7 (PIN de 6 dígitos, sessão por PIN de até 12 h com saída depois de 15 min sem toque, aparelho válido por 180 dias).
2. **Lista de nomes no aparelho** (PO/LGPD): proposta: primeiro nome e inicial dos operadores com PIN no aparelho; alternativa: o operador digita a matrícula.
3. **Operador sem e-mail** (PO): o usuário nasce por convite de e-mail (FND-06c) e o operador pode não ter e-mail. Proposta: fora desta story (o operador recebe um e-mail da empresa ou o supervisor aponta por ele); alternativa: usuário sem e-mail criado pelo supervisor, com primeiro acesso pelo próprio aparelho — mudaria a FND-06c e pede ADR.
4. **Quem revoga aparelho** (PO): proposta: `settings.edit` ou `production.approve`.
5. **Janela do horário do aparelho e validade da fila** (PO): propostas do CA 11 e 13 (até 2 min no futuro; fila descartada depois de 72 h).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), E2E offline e o teste manual no tablet passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; rota do PIN documentada como exceção de login (só com aparelho registrado).
- [ ] Telas com estados (sem conexão, sincronizando, conflito, bloqueado), alvos ≥ 56 px.
- [ ] Ajudas novas e atualizadas; teste de cobertura passando.
- [ ] Auditoria ligada, com canário sem PIN nem segredo.
- [ ] OpenAPI e cliente Orval regenerados; `vite-plugin-pwa` no `package.json` com a varredura de vulnerabilidades da FND-01.
- [ ] ADR do ponto 3 se o PO escolher a alternativa; `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ShopfloorDevice` e `UserPin` no Identity; `PinLoginService` com o mesmo tratamento de tempo e bloqueio do login.
- [ ] Filtro de escopo `amr = pin` no Host; teste de arquitetura da lista permitida.
- [ ] `vite-plugin-pwa` (manifesto, service worker só para `/fab`); fila local em IndexedDB com sincronização ordenada.
- [ ] `occurred_at` aceito nos endpoints do `shopfloor`, com a janela de validação.
- [ ] Telas `/fab/entrar` e Aparelhos da fábrica; ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (PIN, cookie do aparelho, escopo da sessão, rota anônima), @ux-design-expert (teclado de PIN, estados offline), @qa.
- Gate: CA 5, 6, 7, 8, 11 e 12 obrigatórios.

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

- **Correções feitas na validação:** o PIN é login (exceção de rota anônima já prevista no CLAUDE.md), só com o aparelho registrado, e não confundido com o token de quiosque; sessão por PIN limitada ao `shopfloor` (CA 7) e proibida para perfis com ações reservadas (CA 8); fila local por usuário (CA 13); operador sem e-mail como ponto em aberto (3), sem decidir.
- **Pré-condições para `Ready`:** pontos em aberto 1, 2 e 3.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PRD-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
