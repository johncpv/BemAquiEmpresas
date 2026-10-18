# FND-06c — Usuários, convite e permissão efetiva

| Campo | Valor |
|---|---|
| ID | FND-06c |
| Título | Cadastro de usuários com vários grupos, convite por e-mail, desativação, `/me/permissions`, `<Can>`, 2FA obrigatório para quem aprova ou paga e Configurações → Segurança |
| Tamanho | M |
| Status | Done |
| Requisitos | R-SEC-4 · R-SEC-12 · R-SEC-2 (2FA obrigatório para quem aprova ou paga) |
| Dependências | FND-06b, FND-04, FND-05, FND-08; telas dependem de FND-09b e FND-09c. **Pré-condição:** ADR 0008 aceita |
| Bloqueia | FND-06d, FND-14 (ajuda filtrada por permissão), FND-15 |
| Ordem de execução | 14 |

## User story
Como **proprietário da empresa**, quero **convidar pessoas, colocá-las em um ou mais grupos e desativá-las sem perder o histórico**, e como **usuário**, quero **ver só o que posso usar**, para **que cada um tenha exatamente o acesso da sua função**.

## Contexto e referências
- PRD R-SEC-4 (nome, e-mail, grupos, situação ativo/convite pendente/inativo, 2FA, último acesso; convite por e-mail; desativar sem apagar histórico), R-SEC-12.
- `docs/04-fluxos-estados.md` §3.7 (fórmula da permissão efetiva e ordem de verificação).
- `docs/02-arquitetura.md` §3 (policy dinâmica `perm:{codigo}`), §4 (`<Can perm>`; autorização real sempre no backend).
- `docs/03-modelo-dados.md` → `identity.users`, `user_groups`, `user_invites` (`group_ids`, `token_hash`, `expires_at`, `accepted_at`, `invited_by`).
- Revisão §2.2, ADR 0008: JWT só com identidade; permissões de cache versionado; regra de delegação. Revisão §2.3 (FND-05): 2FA obrigatório por padrão para o Proprietário e para quem aprova ou paga.
- Protótipo: "Usuários e acessos" → aba Usuários (colunas Usuário, Grupos, Verificação em duas etapas, Último acesso, Situação).
- Backlog original (CAs): usuário em dois grupos recebe a união; remover do grupo tira o acesso no próximo request; rota sem permissão → 403 `forbidden`; teste de integração cobrindo as três camadas.

## Critérios de aceite
**Permissão efetiva**
1. **Dado** um usuário nos grupos Financeiro e Comercial, **quando** chamo `GET /api/v1/me/permissions`, **então** recebo a união das ações dos dois grupos, limitada aos módulos liberados do tenant, com módulo, área, situação (`released`, `enabled`) e ações.
2. **Dado** o teste de integração das três camadas, **quando** um usuário chama um endpoint (a) de módulo não liberado, (b) de módulo liberado sem grupo que o habilite, (c) de módulo habilitado sem a ação, ou (d) de módulo liberado, habilitado e com a ação, **então** recebe, respectivamente, 403 `module_not_released`, 403 `forbidden`, 403 `forbidden` e 200.
3. **[Segurança]** **Dado** um usuário removido de um grupo, **quando** faz o próximo request, **então** perde as permissões daquele grupo (versão do cache incrementada; o JWT não carrega permissões).
4. **Dado** o Proprietário, **quando** chama qualquer endpoint de módulo liberado, **então** tem todas as ações, inclusive as específicas.

**Usuários e convite**
5. **Dado** um administrador com `identity.create`, **quando** convida "nome, e-mail, grupos" e confirma "Enviar convite", **então** o usuário aparece como "Convite pendente" e recebe o e-mail (FND-08) com link de uso único.
6. **[Segurança]** **Dado** um administrador que não pertence ao Proprietário, **quando** convida alguém para um grupo com ação que ele não tem, com `*.approve`, `*.pay`, `hr.view_sensitive`, ou para o grupo Proprietário, **então** a API recusa com 403 `delegation_denied` (ADR 0008).
7. **Dado** um convite pendente, **quando** clico "Reenviar convite" ou "Cancelar convite", **então** o token anterior é invalidado e, no reenvio, um novo e-mail é enviado.
8. **Dado** um e-mail já cadastrado no tenant, **quando** tento convidar de novo, **então** a tela informa e oferece abrir o usuário existente (propositivo).
9. **Dado** um usuário ativo, **quando** clico "Desativar" e confirmo "Desativar", **então** ele fica "Inativo", perde o acesso no próximo request (FND-04 CA 16) e os registros feitos por ele continuam com o nome dele no histórico.
10. **Dado** o último membro ativo do Proprietário, **quando** tento desativá-lo, **então** a API recusa com 409 ("O grupo Proprietário precisa ter ao menos um membro ativo.").
11. **Dado** um usuário inativo, **quando** clico "Reativar", **então** ele volta a "Ativo" com os mesmos grupos.

**2FA obrigatório**
12. **[Segurança]** **Dado** um usuário cuja permissão efetiva inclui `*.approve` ou `*.pay`, ou que é do Proprietário, **quando** faz login sem 2FA, **então** é levado à configuração do 2FA antes de acessar outra tela (regra registrada em `ITwoFactorRequirementRule`).

**Configurações → Segurança**
13. **Dado** um usuário com `settings.edit` em Configurações → Segurança, **quando** liga "Exigir verificação em duas etapas para todos" e confirma "Exigir", **então** `security.require_2fa` é gravado e auditado, a confirmação diz "Exigência ligada" e a regra da FND-05 passa a valer no próximo login de cada usuário; sem `settings.edit`, a API responde 403 `forbidden`.

**Front**
14. **Dado** o front, **quando** um componente é envolvido em `<Can perm="finance.approve">`, **então** só aparece se a permissão estiver em `/me/permissions`; a autorização continua sendo verificada no backend.
15. **Dado** uma mudança de permissão do usuário logado, **quando** o próximo request detecta versão nova (cabeçalho de versão de permissões), **então** o front recarrega `/me/permissions` sem exigir novo login.

## Regras de negócio e técnicas
- A policy dinâmica `perm:{modulo}.{acao}` e o serviço de permissão efetiva são entregues na FND-06b (os endpoints de grupo já precisam deles); esta story os expõe em `/me/permissions`, liga a versão de permissões às mudanças de usuário e entrega o `<Can>`.
- Cache de permissões efetivas por usuário com chave versionada (`user_id`, `perm_version`); a versão muda ao alterar grupos, membros, liberação do tenant ou status do usuário.
- Situação do usuário: `Invited` (convite pendente), `Active`, `Inactive`. Usuários nunca são apagados.
- `last_login_at` atualizado no login; exibido em America/Sao_Paulo.
- Convite: token aleatório guardado com hash, uso único, validade conforme Pontos em aberto; `group_ids` validados pela regra de delegação no envio e no aceite.

## Permissões, eventos e tabelas
- **Tabelas:** `identity.users` (+ `status`, `perm_version`), `identity.user_groups`, `identity.user_invites`.
- **Permissões:** `identity.view` (lista), `identity.create` (convidar), `identity.edit` (grupos, desativar, reativar, reenviar e cancelar convite), `settings.view` e `settings.edit` (Configurações → Segurança).
- **Eventos:** `UserInvited`, `UserDeactivated`, `UserGroupsChanged`.

## Telas e estados
Referência: protótipo, "Usuários e acessos" → aba Usuários.
- **Lista:** DataTable (FND-09c) com Usuário (nome e e-mail), Grupos (chips), Verificação em duas etapas (chip "Ativa"/"Pendente"/"Obrigatória pendente"), Último acesso, Situação, ações. Filtros por situação e grupo.
- **Convidar usuário:** nome, e-mail, grupos (multi-seleção); grupos que o ator não pode conceder aparecem desabilitados com `<Dica>` do motivo; botão "Enviar convite"; toast "Convite enviado para {e-mail}".
- **Detalhe/edição:** grupos, situação, 2FA, último acesso, histórico (FND-09c); ações "Desativar", "Reativar", "Reenviar convite", "Cancelar convite".
- **Estados:** carregando (skeleton); vazio só com o proprietário ("Convide as pessoas da equipe" + "Convidar usuário"); erro com motivo e ação.
- **Propositivo:** o último grupo usado no convite vem pré-selecionado; depois de convidar, oferece "Convidar outra pessoa".
- **Configurações → Segurança:** chave "Exigir verificação em duas etapas para todos", com `<Dica>` explicando o efeito ("Quem ainda não tem será levado à configuração no próximo acesso"); confirmação com o mesmo verbo ("Exigir" → "Exigência ligada"). Estados: carregando e erro com "Tentar de novo". Movida da FND-05 na validação do PO, porque depende da policy e do módulo `settings`.
- **Ajuda:** `Ajuda/acessos-usuarios.md` (aba `usuarios`) e `Ajuda/configuracoes-seguranca.md`.

## Plano de testes
- **Unidade:** formato de `/me/permissions`; regra de 2FA por `*.approve`/`*.pay`.
- **Integração (Testcontainers):** as três camadas (CA 2, obrigatório); união de grupos (CA 1); remoção vale no próximo request (CA 3); convite com delegação negada (CA 6); reenviar/cancelar (CA 7); desativar e último Proprietário (CA 9, 10); 2FA obrigatório para quem aprova (CA 12); Configurações → Segurança com e sem `settings.edit` (CA 13).
- **E2E (Playwright):** convidar, aceitar pelo SMTP de teste, entrar, ver só os módulos permitidos; remover do grupo e ver o efeito sem novo login (CA 15).

## Fora do escopo
- Menu por permissão e tela de módulo não liberado (FND-06d).
- Sessões ativas visíveis e revogáveis (R-SEC-7).
- Vínculo do usuário com funcionário (`employee_id`, Fase 1).

## Pontos em aberto
- **Validade do convite:** não definida (mesmo ponto da FND-04).
- **Cabeçalho de versão de permissões (CA 15):** mecanismo proposto; confirmar na ADR 0008.
- **Quem pode ver a lista de usuários:** `identity.view` mostra e-mails de todos; confirmar se é aceitável sob LGPD ou se o e-mail exige outra ação.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migrations criadas.
- [ ] Permissões registradas no seed (`identity.view`, `identity.create`, `identity.edit`, `settings.view`, `settings.edit`).
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda da tela escrita.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada (convites, grupos do usuário, desativação).
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Incrementar a versão de permissões nas mudanças de usuário (grupos, desativação) usando o serviço da FND-06b.
- [ ] Tela Configurações → Segurança (exigir 2FA para todos), com `settings.edit`.
- [ ] Endpoint `/me/permissions` e cabeçalho de versão.
- [ ] Endpoints de usuários e convites com delegação.
- [ ] Regra de 2FA para Proprietário e para `*.approve`/`*.pay`.
- [ ] `<Can>` e provider de permissões no front (TanStack Query).
- [ ] Tela Usuários conforme protótipo.

## Quality gates
- Agentes previstos: @dev, cybersecurity, @qa.
- Gate: teste de integração das três camadas obrigatório e nunca ignorado.

## Validação do PO
Checklist do @po (Pax), 16/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança da revisão §2.3 que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** CA 2 reescrito em Dado/Quando/Então; recebeu a tela Configurações → Segurança (CA 13); CAs renumerados.
- **Pré-condições para `Ready`:** Validade do convite.
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story detalhada com ADR 0008 proposta e os CAs de segurança da revisão | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
- **API (FND-06c), 18/10/2026, branch `feat/fnd-06c-usuarios`.** O convite vale 72 h (ADR 0010 item 5) e o e-mail só aparece para quem tem `identity.view` (ADR 0010 item 10). O cabeçalho de versão (CA 15) é `X-Perm-Version`, registrado na ADR 0008.
  - **Cache versionado (CA 3, ADR 0008):**
    - `users.perm_version` sobe quando grupos, membros, inativação de grupo ou situação do usuário mudam;
    - a chave do cache é `{tenant}:{user}:{versão}` e a versão é lida a cada request, então funciona com várias instâncias;
    - a versão enviada ao front também inclui os módulos liberados.
  - **`GET /me/permissions` (CA 1, 4):** permissões, `isOwner`, `version` e módulos (código, área, `released`, `enabled`, ações). O Proprietário tem todas as ações dos módulos liberados.
  - **Três camadas (CA 2):** testadas de ponta a ponta. Os quatro casos respondem `module_not_released`, `forbidden`, `forbidden` e 200.
  - **Usuários (`/api/v1/identity/users`):**
    - lista com grupos, 2FA (`active`, `pending`, `required_pending`), último acesso e situação;
    - convite com nome, e-mail e grupos (CA 5). Os grupos passam pela delegação no envio e de novo no aceite (CA 6, ADR 0008);
    - e-mail já cadastrado responde 409 `user_exists` com o id (CA 8);
    - reenviar troca o link e cancelar o invalida (CA 7);
    - desativar e reativar mantêm os grupos (CA 9, 11). O último Proprietário ativo não pode ser desativado nem sair do grupo (CA 10);
    - grupos do usuário seguem a mesma delegação.
  - **2FA (CA 12):** a `ApproverRequiresTwoFactorRule` exige 2FA de quem tem `*.approve` ou `*.pay` e do Proprietário.
  - **Configurações → Segurança (CA 13):** `GET`/`PUT /api/v1/settings/security`, com `settings.view` e `settings.edit`. Grava `security.require_2fa`, com auditoria.
  - **Testes:** 6 de integração.
  - **Pendente:**
    - front: `<Can>` (CA 14), recarga pelo `X-Perm-Version` (CA 15), telas de Usuários e de Segurança e a ajuda, depois da FND-09a/b/c;
    - E2E.

- **Telas (FND-06c), 18/10/2026, branch `feat/fnd-06bc-telas`.**
  - **"Usuários e acessos → Usuários"** (`/app/acessos/usuarios`): DataTable com busca e ordem na URL, chips de grupo, de 2FA e de situação, e último acesso no horário de Brasília.
  - **Convite:**
    - os grupos do último convite vêm marcados;
    - e-mail já cadastrado oferece "Abrir o usuário";
    - depois de enviar, oferece "Convidar outra pessoa".
  - **Ações:** editar grupos, reenviar ou cancelar o convite, desativar com confirmação e reativar.
  - **"Configurações → Segurança":** "Exigir" confirma com "Exigência ligada" (CA 13).
  - **`<Can perm>`** atende o CA 14.
  - **Ajuda:** `acessos-usuarios.md` e `configuracoes-seguranca.md`.
  - **Testes:** E2E da matriz, do Proprietário, do convite e da segurança.

## QA Results
_A preencher pelo @qa._