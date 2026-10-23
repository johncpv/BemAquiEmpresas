# PRD-03b — App do operador: aparelho registrado e PIN

| Campo | Valor |
|---|---|
| ID | PRD-03b |
| Título | PWA instalável no tablet (service worker só do shell do `/fab`); aparelho da fábrica registrado, com segredo rotativo, validade e teto; acesso rápido por PIN de 6 dígitos (depois de um login completo naquele aparelho), com bloqueio por tentativas, sessão por PIN de 12 h absoluta e lista permitida de endpoints; prazo absoluto também na sessão completa; tema do `/fab` por aparelho |
| Tamanho | M (quebra da PRD-03; replanejada pela D5: o funcionamento sem rede foi adiado) |
| Status | Done |
| Requisitos | R-PRD-3 (apontamento no PWA) · PRD §6 (Usabilidade: tablet de entrada; Segurança: OWASP, rate limit) · R-SEC-1 (bloqueio após tentativas) · R-SEC-3 (sessão) · R-SEC-7 (sessões visíveis e revogáveis, D) · R-SEC-5 |
| Dependências | PRD-03a, PRD-05; **ADR 0026** (pré-condição, já aceita) |
| Bloqueia | — |
| Ordem de execução | 9 (Marco 3.1 "Fábrica visível") |

## User story
Como **operador**, quero **entrar no tablet da fábrica com um PIN curto**, e como **proprietário**, quero **que o PIN só funcione no aparelho da fábrica que foi liberado, por pouco tempo e só para apontar**, para **apontar ser rápido sem abrir uma porta de entrada para qualquer um**.

## Contexto e referências
- Backlog PRD-03: "Login rápido (PIN opcional após login completo no dispositivo)… tolerância a queda de rede"; CA "botões ≥ 56px; funciona em tablet Android de entrada". A tolerância a queda de rede foi adiada (D5, ADR 0026 §6).
- `02-arquitetura` §4 ("Chão de fábrica PWA (`/fab`)"); §5 (JWT 15 min, refresh 7 dias rotativo em cookie httpOnly, detecção de reuso).
- **Revisão pré-Fase 3** (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): D5 (PIN, sessão, aparelho, operador sem e-mail; sem rede depois do piloto), D6 (qualidade × PIN), D17 (d) (prazo absoluto da sessão), tabela 1.1 itens 68 a 72 e 95, §2.1 "Chão de fábrica", §2.2 PRD-03b, §2.3 padrão 1.
- **ADR 0026** (chão de fábrica: aparelho registrado e PIN, seções 1 a 5; a seção 6 é o adiamento do sem rede); ADR 0008 (JWT só com identidade; adendo de 21/10/2026: `amr` e perfil sensível); ADR 0009 (cookie `__Host-bq_rt`, `X-CSRF`; adendo de 21/10/2026: rota anônima do PIN); ADR 0003 (adendo de 21/10/2026: `production.approve_quality`); ADR 0011 (tokens opacos guardados como hash, bloqueio de 15 min depois de 5 erros, anti-replay atômico); ADR 0010 item 5/6 (rate limit).
- CLAUDE.md: "Sem rota anônima, exceto login…": o PIN é uma forma de **login** e só vale com o aparelho registrado; o token de tela (quiosque) é do painel da TV (PNL-01), não do PWA do operador.
- `06` P-12 (tablet Android de entrada).
- `CLAUDE.md` Stack: `vite-plugin-pwa` (ainda não instalado no `src/Web`).
- Kit "chão de fábrica" da PRD-03a (padrão 1).

## Critérios de aceite
**PWA instalável e sem rede**
1. **Dado** o `/fab`, **quando** aberto no Chrome do Android, **então** o app é instalável (manifesto com nome "BemAqui Fábrica", ícones, `display: standalone`, `start_url: /fab`) e o service worker do `vite-plugin-pwa` guarda só o shell do `/fab` (HTML, JS, CSS, fontes); o `/app` (backoffice) não é cacheado.
2. **[Segurança]** **Dado** o service worker, **quando** qualquer chamada a `/api/*` passa por ele, **então** ela nunca é guardada em cache (teste da configuração do `vite-plugin-pwa` e E2E que confere o Cache Storage depois de usar o `/fab`).
3. **Dado** o `/fab` sem rede, **quando** o operador tenta qualquer ação (abrir a fila ou uma etapa, iniciar, pausar, retomar, finalizar, entrar por PIN), **então** vê "Sem conexão. Esta ação precisa de rede." com o botão grande "Tentar de novo" (kit da PRD-03a); nada fica guardado para enviar depois (ADR 0026 §6).

**Aparelho registrado**
4. **Dado** um usuário com `production.edit` + `shopfloor.create` que fez o login completo no tablet, **quando** abre "Registrar este aparelho da fábrica", dá um nome (até 40, ex.: "Tablet Linha 1") e confirma, **então** nasce o registro em `identity.shopfloor_devices` (ADR 0026 §1), o aparelho recebe um segredo opaco de 32 bytes no cookie `__Host-bq_dev` (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`), que vale 180 dias a partir do registro (`expires_at`); no banco fica só o hash; todos os membros do Proprietário recebem a notificação "Aparelho {nome} registrado por {usuário}.".
5. **[Segurança]** **Dado** o teto de aparelhos ativos (10 por tenant, parâmetro `security.max_shopfloor_devices`), **quando** tento registrar mais um, **então** 409 "Limite de {n} aparelhos ativos. Revogue um antes de registrar outro.".
6. **[Segurança]** **Dado** o login completo feito no tablet para registrar o aparelho ou criar PIN, **quando** o fluxo termina, **então** não fica sessão persistente: nenhum refresh é emitido para essa sessão e ela acaba ao fechar o fluxo (ADR 0026 §2).
7. **[Segurança]** **Dado** um login por PIN, **quando** o servidor aceita, **então** emite um segredo novo para o aparelho (rotativo) e guarda só o hash; o segredo anterior vale só até a resposta chegar, e reapresentar o anterior revoga o aparelho, como a detecção de reuso do refresh (ADR 0026 §1).
8. **Dado** um aparelho sem uso há 30 dias (`last_seen_at`) ou com os 180 dias vencidos, **quando** o job diário (Hangfire, um por tenant, com `tenantId`) roda, **então** o aparelho fica revogado e as sessões por PIN dele são revogadas; o tablet passa a mostrar "Este aparelho não está registrado. Entre com e-mail e senha.".
9. **Dado** a lista "Aparelhos da fábrica" (Configurações → Segurança, com `settings.view`), **quando** quem tem `settings.edit` ou `production.approve` revoga um aparelho, **então** o PIN deixa de funcionar nele e as sessões abertas por PIN nele são revogadas (R-SEC-7); todos os membros do Proprietário recebem a notificação "Aparelho {nome} revogado por {usuário}."; o próprio usuário apaga o próprio PIN em "Apagar meu PIN deste aparelho" no `/fab` ou em Meu perfil → Segurança.
10. **[Segurança]** **Dado** uma sessão por PIN, **quando** chega qualquer request, **então** o servidor confere se o aparelho dela continua válido (não revogado, não vencido); aparelho revogado responde 401 e o tablet volta a "Este aparelho não está registrado." (não só no próximo login).
11. **Dado** o tema do `/fab` (ponto 95), **quando** alguém troca o tema no cabeçalho do `/fab` (Automático, Claro, Escuro), **então** ele fica guardado no aparelho registrado (`shopfloor_devices.theme`), com padrão Automático, e vale para todos os operadores daquele tablet.

**PIN**
12. **Dado** um operador que fez o login completo (e-mail e senha, e o 2FA quando exigido) **num aparelho registrado**, **quando** escolhe "Criar PIN para este aparelho" e digita duas vezes um PIN de 6 dígitos, **então** o PIN é guardado com hash (PBKDF2, `PasswordHasher`) em `identity.user_pins`, vinculado ao par usuário + aparelho; PIN óbvio (000000, 123456, sequências, data de nascimento quando conhecida) é recusado.
13. **Dado** o tablet registrado, **quando** abro `/fab/entrar`, **então** vejo os operadores que têm PIN neste aparelho (só primeiro nome e inicial, pelo `GET /api/v1/auth/pin/users`) e, ao tocar no nome e digitar o PIN no teclado numérico grande, a sessão abre; `POST /api/v1/auth/pin` e `GET /api/v1/auth/pin/users` são anônimos **só** com o cookie do aparelho válido (sem ele, 404, como rota inexistente), exigem `Origin` do tenant e `X-CSRF: 1`, entram no rate limit do login (ADR 0010 item 6) e têm resposta e tempo uniformes (ADR 0011). Na `/fab/entrar`, a ajuda abre sozinha na primeira visita (padrão 1).
14. **[Segurança]** **Dado** PINs errados, **quando** são 5 seguidos para o mesmo usuário no mesmo aparelho, **então** o PIN daquele par fica bloqueado por 15 minutos; o 10º erro em 24 h apaga o PIN e exige novo login completo; **e** 20 erros em 15 min no mesmo aparelho, somando todos os usuários, bloqueiam a entrada por PIN no aparelho por 15 min e notificam o Proprietário; os erros vão para a auditoria e contam para o alerta de abuso (FND-12).
15. **[Segurança]** **Dado** a sessão aberta por PIN, **quando** emitida, **então** o access token tem `amr = ["pin"]` (ADR 0008, adendo) e o refresh fica ligado ao aparelho (`identity.refresh_tokens` com `session_kind = Pin`, `device_id` e `absolute_expires_at`); a sessão dura 12 h absolutas e **não é renovável** além disso; depois de 15 min sem toque, o `/fab` mostra "Você vai sair em 60 s. Tocar para continuar." e, sem toque, sai e volta à lista de nomes; a sessão é revogada com o aparelho, com a troca de senha, com o security stamp ou com a perda de `shopfloor.create`.
16. **[Segurança]** **Dado** a lista permitida da sessão por PIN (ADR 0026 §4), **quando** a sessão chama um endpoint, **então** só passam os marcados com `[PinSessionAllowed]`: `/api/v1/shopfloor/*` (fila, etapa, iniciar, pausar, retomar, sair, finalizar, checklist, buscar OS pelo número, documentos da OS pelo endpoint que confere a fila), o pedido de material do operador (EST-03, quando entrar), a fila e a decisão de qualidade com `production.approve_quality` (PRD-05), `/api/v1/me`, `/api/v1/me/permissions`, `/api/v1/auth/logout`, a ajuda contextual e o `ProductionHub` (grupo do chão de fábrica, com `shopfloor.view`); qualquer outro responde 403 `pin_session_scope`; o teste de arquitetura reprova endpoint novo com `[PinSessionAllowed]` fora da lista revisada.
17. **[Segurança]** **Dado** um usuário que tem `*.approve` (exceto `production.approve_quality`), `finance.pay`, `hr.view_sensitive`, `identity.edit` ou `settings.edit`, **quando** tenta criar PIN, **então** 403 "Seu perfil não pode usar PIN."; **e**, a cada login por PIN, o perfil é reconferido: se o usuário ganhou uma dessas ações, o PIN é apagado e a entrada é recusada com "Seu perfil mudou e não pode usar PIN. Entre com e-mail e senha.".
18. **Dado** um inspetor com `production.approve_quality` (D6), **quando** cria PIN e entra por PIN, **então** é aceito e vê a fila de qualidade da PRD-05; a conclusão da OS e a liberação da parcela continuam com `production.approve` e login completo (PRD-06a1).
19. **[Segurança]** **Dado** uma troca do vínculo usuário ↔ funcionário (`IUserEmployeeLink`), **quando** grava, **então** o PIN do usuário em todos os aparelhos é apagado e as sessões por PIN dele são revogadas.
20. **Dado** um usuário desativado, um funcionário desligado ou afastado (`IEmployees.Status`), ou um usuário que perdeu `shopfloor.create`, **quando** tenta entrar por PIN ou usa uma sessão por PIN aberta, **então** é recusado no próximo request (security stamp e permissão efetiva conferidos a cada request).

**Sessão completa**
21. **[Segurança]** **Dado** a sessão completa (e-mail e senha), **quando** a família de refresh passa do prazo absoluto (início da família + 30 dias, parâmetro `security.session_absolute_days`), **então** a renovação responde 401 e o usuário faz novo login; dentro do prazo, a renovação de 7 dias continua como está (ADR 0026 §3, D17 d). As famílias abertas antes do deploy contam o prazo a partir do início delas.

**Permissões e auditoria**
22. **Dado** o módulo `shopfloor` não liberado, **quando** chamo o PIN ou registro aparelho, **então** 403 `module_not_released`; sem `shopfloor.create`, 403 `forbidden`; registrar aparelho sem `production.edit` + `shopfloor.create`: 403; revogar sem `settings.edit` nem `production.approve`: 403.
23. **Dado** registro, rotação com reuso e revogação de aparelho, criação, apagamento e bloqueio de PIN e entradas por PIN, **quando** consulto a auditoria, **então** vejo quem, quando, qual aparelho e o IP, sem o PIN nem o segredo (teste de canário: o PIN e o segredo não aparecem em `audit.audit_log` nem no log).

## Regras de negócio e técnicas
- **Tabelas** (ADR 0026): `identity.shopfloor_devices` (`name`, `secret_hash`, `secret_rotated_at`, `created_by`, `created_at`, `last_seen_at`, `expires_at`, `revoked_at`, `revoked_by`, `theme`); `identity.user_pins` (`user_id`, `device_id`, `pin_hash`, `failed_count`, `locked_until`, `created_at`), único por `(user_id, device_id)`; `identity.refresh_tokens` + `session_kind` (`Full`/`Pin`), `device_id` (anulável), `absolute_expires_at`. O Identity é dono (autenticação).
- **Valores (D5):** PIN de 6 dígitos (PBKDF2); sessão por PIN de 12 h absoluta e não renovável; saída depois de 15 min sem toque, com aviso de 60 s; aparelho válido por 180 dias e revogado depois de 30 dias sem uso; teto de 10 aparelhos; 5 erros → 15 min; 10º erro em 24 h apaga o PIN; 20 erros por aparelho em 15 min → 15 min; sessão completa com prazo absoluto de 30 dias.
- **Escopo da sessão por PIN:** claim `amr = pin`; filtro do Host com a lista `[PinSessionAllowed]`, conferida por teste de arquitetura; aparelho e perfil conferidos a cada request.
- **Nada de dado sensível no aparelho:** o access token fica em memória, como no backoffice (o mutator `http.ts`); o service worker guarda só o shell.
- Guid v7; auditoria `IAuditable` em aparelho e PIN (sem o hash no diff, `[ProtectedSecret]`).

## Permissões, eventos e tabelas
- **Permissões:** `shopfloor.create` (PIN), `production.edit` + `shopfloor.create` (registrar aparelho), `settings.view` (lista de aparelhos), `settings.edit` ou `production.approve` (revogar); `production.approve_quality` aceita por PIN.
- **Eventos:** nenhum novo; notificações síncronas ao Proprietário no registro, na revogação e no bloqueio por aparelho.
- **Tabelas:** `identity.shopfloor_devices`, `identity.user_pins`, colunas novas de `identity.refresh_tokens`.

## Telas e estados
- Padrões de tela da Fase 3: 1, 4.
- **`/fab/entrar`** (lista de nomes e teclado numérico grande do kit da PRD-03a), **`/fab`** com o seletor de tema e "Apagar meu PIN deste aparelho", **Configurações → Segurança → Aparelhos da fábrica**, **Meu perfil → Segurança** (meus PINs).
- **Verbos:** "Registrar este aparelho" / "Aparelho registrado"; "Criar PIN" / "PIN criado"; "Revogar aparelho" / "Aparelho revogado"; "Apagar meu PIN" / "PIN apagado".
- **Estados:** sem aparelho registrado ("Este aparelho não está registrado. Entre com e-mail e senha."); PIN bloqueado ("PIN bloqueado por 15 minutos. Entre com e-mail e senha ou espere."); aparelho bloqueado por tentativas; aviso de saída (CA 15); sem conexão (CA 3); lista de aparelhos vazia ("Nenhum aparelho da fábrica registrado." + como registrar).
- **Propositivo:** depois do login completo num aparelho registrado, o PWA oferece "Criar PIN para entrar mais rápido"; o último operador do aparelho aparece primeiro na lista; a lista de aparelhos mostra "Sem uso há {n} dias" perto dos 30.
- **Ajuda:** `Ajuda/fab-entrar.md` (rota `/fab/entrar`), atualização de `Ajuda/fab.md` ("Sem conexão", tema, apagar PIN) e de `Ajuda/configuracoes-seguranca.md` ("Aparelhos da fábrica"); `<Dica>` em "PIN".

## Plano de testes
- **Unidade:** regras do PIN (formato, óbvios, bloqueio por par e por aparelho); perfil sensível com a exceção da `production.approve_quality` (CA 17, 18); prazo absoluto das duas sessões (CA 15, 21).
- **Integração (Testcontainers):** registro com cookie `__Host-`, notificação e teto (CA 4, 5); login completo sem refresh (CA 6); rotação e reuso do segredo (CA 7); job de 30 dias e 180 dias (CA 8); revogação e aparelho revogado a cada request (CA 9, 10); tema por aparelho (CA 11); criar PIN só depois do login completo (CA 12); PIN e lista sem cookie → 404, com `Origin`/`X-CSRF` e rate limit (CA 13); bloqueios e apagamento (CA 14); 12 h não renováveis, `amr` e `session_kind` (CA 15); escopo da sessão por PIN em todos os endpoints do backoffice (teste parametrizado) e teste de arquitetura da lista (CA 16); perfil sensível na criação e a cada login, inspetor aceito (CA 17, 18); troca de vínculo (CA 19); desativado, desligado, afastado e sem permissão (CA 20); prazo absoluto da sessão completa (CA 21); três camadas (CA 22); canário de auditoria e log (CA 23).
- **E2E (Playwright):** instalar o PWA (manifesto válido), registrar o aparelho, criar PIN, entrar por PIN, aviso de saída por inatividade (relógio falso); Cache Storage sem `/api/*` (CA 2); `context.setOffline(true)` mostra "Sem conexão. Esta ação precisa de rede." e "Tentar de novo" funciona ao voltar (CA 3).
- **Manual:** tablet Android de entrada (P-12), registrado na story.

## Fora do escopo
- **Funcionamento sem rede** (fila de apontamentos no IndexedDB por usuário e aparelho, `Idempotency-Key` por ação, instante do aparelho aceito até 2 min no futuro e nunca antes do início da sessão de origem, validade de 72 h, faixa fixa "Sem conexão"): adiado para **depois do piloto, se o Wi-Fi falhar**, numa story própria (ADR 0026 §6).
- Usuário só de chão de fábrica, sem e-mail: só com ADR nova, se os apelidos de e-mail não servirem (ADR 0026 §5).
- Token de quiosque e painel da TV: PNL-01 (Fase 4).
- Biometria e crachá NFC: fora do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, D5, D6, D17 (d) e tabela 1.1, itens 68–72 e 95):
1. **PIN, sessão e aparelho:** valores da D5 (PIN de 6 dígitos PBKDF2; sessão de 12 h absoluta e não renovável; saída depois de 15 min sem toque, com aviso de 60 s; aparelho por 180 dias, revogado depois de 30 dias sem uso; login completo no tablet sem sessão persistente).
2. **Nomes no aparelho:** primeiro nome e inicial, só com aparelho registrado válido.
3. **Operador sem e-mail:** um e-mail da empresa por operador (apelido no domínio `konebras.com.br`), sem mudar código; usuário sem e-mail só com ADR (D5).
4. **Quem revoga aparelho:** `settings.edit` ou `production.approve`; o próprio usuário apaga o próprio PIN; notificação ao Proprietário.
5. **Relógio e fila sem rede:** saem com o adiamento do sem rede (D5, ADR 0026 §6); as regras ficam registradas para quando voltar.
6. **Tema do `/fab`** (ponto 95): por aparelho registrado, padrão automático.
7. **Qualidade × PIN** (D6): o inspetor com `production.approve_quality` pode usar PIN.
8. **Prazo absoluto da sessão completa** (D17 d): 30 dias, parâmetro.

**Pré-condição externa:** a Konebras cria um e-mail da empresa por operador (apelido em `konebras.com.br`) antes de convidar os operadores; não bloqueia a story (o supervisor aponta por quem ainda não tem usuário, PRD-03a).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura (lista permitida) e E2E passando; teste manual no tablet registrado. (Unidade, arquitetura da lista e E2E passando; integração escrita, a rodar no Docker; teste manual no tablet pendente, P-12.)
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`. (Criada e não destrutiva; o teste sobre a cópia fica para a fila do Docker.)
- [x] Permissões só do `ModuleCatalog`; rotas do PIN documentadas como exceção de login (ADR 0009, adendo de 21/10/2026).
- [x] ADR 0026 aplicada (seções 1 a 5 e o prazo absoluto da sessão completa).
- [x] Telas com estados (sem conexão, bloqueado, aviso de saída), kit "chão de fábrica".
- [x] Ajudas novas e atualizadas; teste de cobertura passando.
- [x] Auditoria ligada, com canário sem PIN nem segredo (teste de integração escrito).
- [ ] OpenAPI e cliente Orval regenerados; `vite-plugin-pwa` no `package.json` com a varredura de vulnerabilidades da FND-01. (`openapi.json` editado à mão e cliente gerado dele; regenerar com `BQ_UPDATE_OPENAPI`. `vite-plugin-pwa` 2.0.0, `pnpm audit` sem vulnerabilidades.)
- [x] `docs/03-modelo-dados.md` atualizado (`shopfloor_devices`, `user_pins`, colunas de `refresh_tokens`); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ShopfloorDevice` e `UserPin` no Identity; `PinLoginService` com o mesmo tratamento de tempo e bloqueio do login, rotação do segredo e reconferência do perfil.
- [ ] `session_kind`, `device_id` e `absolute_expires_at` no refresh; prazo absoluto nas duas sessões.
- [ ] Atributo `[PinSessionAllowed]` e filtro de escopo no Host; teste de arquitetura da lista permitida.
- [ ] Job diário de aparelhos (30 e 180 dias) por tenant.
- [ ] `vite-plugin-pwa` (manifesto, service worker só para o shell do `/fab`, sem `/api/*`); estado "Sem conexão".
- [ ] Telas `/fab/entrar`, Aparelhos da fábrica, meus PINs, seletor de tema; ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (PIN, cookie e rotação do aparelho, escopo da sessão, rota anônima, prazo absoluto), @ux-design-expert (teclado de PIN, aviso de saída), @qa.
- Gate: CA 2, 7, 10, 13, 14, 15, 16, 17, 19 e 21 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK (ajustado: sem o sem rede) |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (ajustado: renumerados) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: §2.1 e §2.2) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: PRD-05 e ADR 0026, Marco 3.1) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** o PIN é login (exceção de rota anônima já prevista no CLAUDE.md), só com o aparelho registrado, e não confundido com o token de quiosque; sessão por PIN limitada ao `shopfloor` e proibida para perfis com ações reservadas; fila local por usuário; operador sem e-mail como ponto em aberto (3), sem decidir.
- **Correções da revalidação (21/10/2026):** replanejada pela D5 e pela ADR 0026: o funcionamento sem rede (antigos CAs 10 a 14, fila IndexedDB, janela de horário, 72 h) saiu para "Fora do escopo", com a condição "depois do piloto, se o Wi-Fi falhar"; sem rede, só a mensagem "Sem conexão. Esta ação precisa de rede." (CA 3); arquivo renomeado para `PRD-03b-pwa-pin-aparelho.md`. Valores da D5 (CA 4, 8, 12, 15); segredo rotativo, refresh ligado ao aparelho com `session_kind` e prazo absoluto, `amr=pin`, rota anônima, lista `[PinSessionAllowed]` com teste de arquitetura e aparelho conferido a cada request (§2.1, CA 7, 10, 13, 15, 16); perfil sensível reconferido a cada login com a exceção da `production.approve_quality` (D6, CA 17, 18); troca de vínculo apaga o PIN (CA 19); limite de erros por aparelho (CA 14); notificação ao Proprietário no registro e na revogação, teto de aparelhos, service worker sem `/api/*` (§2.2, CA 2, 4, 5, 9); login completo no tablet sem sessão persistente (CA 6); prazo absoluto da sessão completa (D17 d, CA 21); tema por aparelho (ponto 95, CA 11); operador sem e-mail como pré-condição externa.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condição externa explícita: e-mail da empresa por operador (apelido em `konebras.com.br`), sem bloquear.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PRD-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D5, D6, D17 (d), tabela 1.1 itens 68–72 e 95) e correções da revisão pré-Fase 3 (§2.1 "Chão de fábrica", §2.2 PRD-03b, §2.3 padrão 1); replanejada: sem rede adiado (ADR 0026 §6), arquivo renomeado para `PRD-03b-pwa-pin-aparelho.md`, título "App do operador: aparelho registrado e PIN"; nova ordem (Marco 3.1 "Fábrica visível"); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 22/10/2026 | 1.0 | Implementada (`Done`). Desvios e decisões de implementação: (1) o `amr` entra em todo access token (`["pwd"]`, `["pwd","otp"]` ou `["pin"]`); nenhuma claim nova de sessão ou aparelho: a conferência a cada request usa o cookie do aparelho e a família por PIN viva de (usuário, aparelho), e uma entrada por PIN nova revoga a anterior do mesmo par; (2) o refresh também fica preso ao host em que nasceu (coluna `host`; nulo nas antigas) e a renovação de 7 dias para no prazo absoluto; famílias antigas sem `absolute_expires_at` contam do início, sem `UPDATE` na migration; (3) colunas a mais, necessárias às regras: `previous_secret_hash`, `revoked_reason`, `pin_locked_until`, `pin_failures_since`, `pin_failures` no aparelho; `failures_since`, `failures_in_window`, `last_used_at` no PIN; (4) "o anterior vale só até a resposta chegar" = a mesma tolerância do refresh (`Auth:RefreshReuseTolerance`, 10 s); fora dela, reapresentar revoga o aparelho, as sessões por PIN e os PINs dele e avisa o Proprietário; (5) o navegador não manda `Origin` num GET da mesma origem: o `GET /auth/pin/users` exige `X-CSRF: 1`, `Origin` igual ao host quando vier e `Sec-Fetch-Site: same-origin` quando vier; ordem das respostas nas duas rotas: sem cookie válido 404, depois 403 de origem, depois 403 `module_not_released`; (6) login completo no tablet: flag `ephemeral` no login e no 2FA (a tela `/entrar?aparelho=1`, aberta pela `/fab/entrar`) e sempre que o navegador já tem o cookie do aparelho; registrar o aparelho revoga o refresh que houver; o `/entrar` comum continua persistente (computador e celular) e o `/fab` sem sessão vai à `/fab/entrar`; (7) o PIN exige funcionário ativo ligado ao usuário (criar, entrar e a cada request, com 30 s de cache da situação), lido por `IEmployees`; o Identity passa a referenciar `HR.Contracts`, que ganhou `BirthDateAsync` (só para recusar PIN com a data de nascimento); (8) perfil sensível inclui o Proprietário e a `hr.view_health` (ADR 0008, adendo de 21/10/2026), ainda fora do catálogo; (9) lista permitida revisada (`PinSessionAllowListTests`): os endpoints do `/shopfloor/*` da PRD-03a, `GetShopfloorDevice`, `SetShopfloorDeviceTheme` e `DeleteMyShopfloorPin`, os seis da qualidade e o upload de anexo da NC (PRD-05), `/me`, `/me/permissions`, `Logout`, as cinco rotas da ajuda (inclui o "Pergunte ao BemAqui") e o `ProductionHub`; registrar aparelho e criar PIN ficam fora (exigem login completo); o pedido de material (EST-03) e o "Meu ponto" (ADR 0035) entram com as stories deles; (10) a sessão por PIN só entra no grupo `shopfloor` do `ProductionHub`; revogar aparelho, trocar vínculo e apagar PIN fecham as conexões do hub do usuário; (11) revogar remove as linhas de `user_pins` do aparelho; o job diário (`identity-shopfloor-devices`, 00:30) revoga sem avisar o Proprietário; a revogação manual e o reuso avisam; (12) o bloqueio do aparelho (20 erros em 15 min) também dispara o alerta `auth_abuse` da plataforma (FND-12, `IAlertSink`); (13) revogar aceita `settings.edit` **ou** `production.approve` por `RequireAnyPermissionOf` (ações de módulos diferentes; o módulo conta como liberado se o de uma das ações estiver); (14) a ajuda da `/fab/entrar` vem do `Ajuda/fab-entrar.md` embutido no app (lá não há sessão para a API de ajuda) e "já vista" fica no navegador do tablet; (15) aviso de saída aos 15 min sem toque e saída aos 16 min (texto do CA 15), também no login completo sem persistência; as 12 h absolutas também saem no front; (16) "Sem conexão": cobertura no `/fab` com o evento `offline` e erro de rede de qualquer chamada vira `ApiError` `offline` com a mensagem do CA 3; (17) PWA com `vite-plugin-pwa` 2.0.0 (o 1.x não aceita Vite 8), `sw.js` com escopo `/fab` registrado só pelo `/fab` e pela `/fab/entrar`, precache do shell (o bundle é um só; o `/app` não fica sob o service worker), sem cache em tempo de execução; ícones PNG 192 e 512; o E2E do PWA roda contra `vite build` + `vite preview` (porta 5175, projeto `pwa` do Playwright); (18) o `EndpointAuthorizationTests` (D19) está na `main` e não na base desta branch: entrou aqui com as duas rotas do PIN na lista anônima e `/me/pins` na lista "só autenticado" (no merge com a `main`, ficar com esta versão); (19) `openapi.json` editado à mão, a regenerar com `BQ_UPDATE_OPENAPI`; testes de integração escritos e não executados (fila do Docker); teste manual no tablet Android (P-12) e teste da migration sobre cópia do `konebras` pendentes | Dex (dev) |
