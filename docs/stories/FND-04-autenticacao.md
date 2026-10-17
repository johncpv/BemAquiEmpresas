# FND-04 — Autenticação do tenant

| Campo | Valor |
|---|---|
| ID | FND-04 |
| Título | Identity por tenant: login, refresh rotativo em cookie, recuperação de senha, bloqueio e aceite de convite |
| Tamanho | G |
| Status | Draft |
| Requisitos | R-SEC-1 · R-SEC-3 · R-SEC-4 (aceite de convite) · R-SEC-5 (login, logout e falhas auditados) · R-PLT-3 (proprietário e convite no provisionamento) |
| Dependências | FND-02, FND-03a, FND-07, FND-08, FND-11; telas dependem de FND-09a e FND-09c |
| Bloqueia | FND-05, FND-06c, FND-13 (reaproveita o mecanismo) e todas as rotas autenticadas |
| Ordem de execução | 8 |

## User story
Como **usuário de uma empresa cliente**, quero **entrar com e-mail e senha, continuar logado com segurança e recuperar a senha sozinho**, para **usar o sistema sem pedir ajuda e sem expor minha conta**.

## Contexto e referências
- PRD R-SEC-1 (política de senha, bloqueio, recuperação por e-mail), R-SEC-3 (JWT curto + refresh rotativo em cookie httpOnly), R-SEC-4 (convite), R-SEC-5.
- `docs/02-arquitetura.md` §5: Identity por tenant; access 15 min; refresh 7 dias rotativo em cookie httpOnly/Secure/SameSite=Strict; reuso revoga a família; rate limit por IP e usuário; login/logout/falhas auditados.
- `docs/03-modelo-dados.md` → `identity.users`, `identity.refresh_tokens` (`family_id`, `replaced_by`), `identity.user_invites`.
- Revisão §2.2, ADR 0008: o JWT carrega só identidade (`tid`, `aud`, security stamp); permissões vêm de cache versionado.
- Revisão §2.3, CAs de segurança da FND-04.
- Backlog original: CA "access 15 min / refresh 7 dias; reuso revoga a família; 5 tentativas bloqueiam 15 min; e-mail de recuperação expira em 1 h".

## Critérios de aceite
**Login e sessão**
1. **Dado** um usuário ativo com senha correta, **quando** faz login, **então** recebe um access token JWT de 15 min no corpo da resposta e um refresh token de 7 dias em cookie `HttpOnly`, `Secure`, `SameSite=Strict`, sem `Domain` (prefixo conforme Pontos em aberto).
2. **Dado** o JWT emitido, **quando** inspeciono as claims, **então** há só `sub`, `tid`, `aud` (host do tenant), `sstamp` (security stamp), `iat`, `exp` e `jti`, sem lista de permissões nem dado pessoal além do id.
3. **[Segurança]** **Dado** o front logado, **quando** inspeciono `localStorage`, `sessionStorage` e cookies legíveis por JS, **então** o access token não está em nenhum deles (fica só em memória).
4. **Dado** um refresh válido, **quando** chamo `POST /api/v1/auth/refresh`, **então** recebo novo access e novo refresh, e o anterior fica marcado `replaced_by`.
5. **[Segurança]** **Dado** um refresh já substituído, **quando** é reapresentado fora da janela de tolerância, **então** toda a família é revogada e o usuário precisa entrar de novo; o evento é auditado.
6. **[Segurança]** **Dado** duas abas que pedem refresh ao mesmo tempo com o mesmo token, **quando** as duas chegam dentro da janela de tolerância, **então** as duas recebem sessão válida e a família não é revogada.
7. **[Segurança]** **Dado** `POST /auth/refresh` ou `POST /auth/logout`, **quando** a requisição não tem `Origin` igual ao host do tenant ou não tem o cabeçalho anti-CSRF exigido, **então** responde 403.
8. **Dado** logout, **quando** o usuário sai, **então** o refresh é revogado, o cookie é apagado e o evento é auditado.

**Bloqueio, rate limit e respostas uniformes**
9. **Dado** 5 tentativas de senha erradas seguidas, **quando** faço a 6ª, **então** a conta fica bloqueada por 15 min, mesmo com a senha certa, e a falha é auditada.
10. **[Segurança]** **Dado** um e-mail inexistente, um usuário inativo ou uma senha errada, **quando** tento login, **então** a resposta tem o mesmo status, a mesma mensagem ("E-mail ou senha inválidos.") e tempo de resposta equivalente.
11. **[Segurança]** **Dado** o rate limit, **quando** o mesmo IP real (do forwarded header confiável), o mesmo tenant ou o mesmo usuário passam do limite configurado em login, recuperação ou convite, **então** recebem 429 com `Retry-After`.

**Recuperação de senha**
12. **Dado** "Esqueci minha senha", **quando** informo qualquer e-mail, **então** a resposta é sempre "Se o e-mail estiver cadastrado, você receberá um link." (sem enumerar), e só o e-mail cadastrado recebe o link.
13. **Dado** o link de redefinição, **quando** passa 1 h ou ele já foi usado, **então** é recusado ("Link expirado ou já usado. Peça um novo.").
14. **[Segurança]** **Dado** uma redefinição concluída, **quando** termina, **então** todas as famílias de refresh do usuário são revogadas, o security stamp muda e o 2FA continua ativo (não é desligado).
15. **[Segurança]** **Dado** um e-mail de convite ou de redefinição, **quando** inspeciono o link, **então** o host é o primário do tenant no catálogo, mesmo que a requisição tenha vindo com outro `Host`.

**Usuário desativado e convite**
16. **[Segurança]** **Dado** um usuário logado, **quando** é desativado, **então** o próximo request dele recebe 401, porque o security stamp é verificado por request (com cache invalidado na desativação).
17. **Dado** um convite válido, **quando** a pessoa abre o link, define a senha conforme a política e confirma, **então** o usuário fica ativo, o convite fica `accepted_at` e a tela oferece configurar o 2FA (FND-05) como próximo passo.
18. **Dado** um convite expirado ou já aceito, **quando** é aberto, **então** a tela explica e orienta pedir novo convite ao administrador.
19. **Dado** o provisionamento de um tenant (FND-03a), **quando** chega na etapa `CreateOwner`/`SendInvite`, **então** o usuário proprietário é criado como convite pendente e o e-mail de convite é enviado (ponta a ponta do CA 1 da FND-03a).

## Regras de negócio e técnicas
- ASP.NET Core Identity por tenant (tabelas no schema `identity` do banco do tenant), hash de senha PBKDF2 padrão do Identity.
- Política de senha configurável (ver Pontos em aberto); mensagens em PT-BR.
- Tokens de convite e de redefinição: aleatórios, guardados só com hash, de uso único.
- Verificação do security stamp por request com cache curto e invalidação na desativação e na redefinição de senha.
- Rate limit do ASP.NET Core com partição por IP real + tenant + usuário.
- Auditoria: login com sucesso, falha, bloqueio, logout, refresh reutilizado, redefinição e aceite de convite, com IP.
- `Guid` v7, datas UTC.

## Permissões, eventos e tabelas
- **Tabelas:** `identity.users`, `identity.refresh_tokens`, `identity.user_invites`, tabelas padrão do Identity que se aplicarem.
- **Rotas anônimas:** login, refresh, logout, esqueci/redefinir senha, aceite de convite (exceções do CLAUDE.md).
- **Permissões:** nenhuma nova (criar convites é da FND-06c, com `identity.create`).
- **Eventos:** `UserActivated` (aceite de convite).

## Telas e estados
Referência: protótipo e identidade (FND-09a). Telas sem dock, com marca do tenant.
- **Entrar:** e-mail, senha, "Esqueci minha senha", botão "Entrar". Estados: enviando (botão com indicador), erro uniforme inline, conta bloqueada ("Muitas tentativas. Tente de novo em 15 minutos."), 429.
- **Esqueci minha senha:** e-mail e "Enviar link"; confirmação "Se o e-mail estiver cadastrado, você receberá um link."
- **Redefinir senha:** nova senha com indicador da política, confirmação, "Salvar senha"; link expirado com ação "Pedir novo link".
- **Aceitar convite:** nome (pré-preenchido do convite), senha, "Criar acesso"; ao concluir, oferece "Configurar verificação em duas etapas" (propositivo) ou "Fazer depois" quando o 2FA não for obrigatório.
- **Ajuda:** `Ajuda/` para Entrar, Esqueci minha senha, Redefinir senha e Aceitar convite (se rotas públicas exigirem ajuda; ver FND-02 Pontos em aberto).
- **Propositivo:** e-mail do convite já preenchido; após redefinir, o login abre com o e-mail preenchido.

## Plano de testes
- **Unidade:** política de senha; emissão de claims (CA 2); janela de tolerância do refresh.
- **Integração (Testcontainers):** login e refresh (CA 1, 4); reuso fora da janela revoga a família (CA 5); concorrência dentro da janela (CA 6); anti-CSRF (CA 7); bloqueio após 5 erros (CA 9); respostas uniformes (CA 10); rate limit (CA 11); redefinição expirando em 1 h e uso único (CA 13); redefinição revoga sessões sem desligar 2FA (CA 14); host primário no link (CA 15); desativação vale no próximo request (CA 16); convite (CA 17, 18, 19).
- **E2E (Playwright):** login, logout, esqueci a senha com SMTP de teste e aceite de convite; verificação de que o token não está no storage (CA 3).

## Fora do escopo
- 2FA (FND-05).
- Cadastro de usuários e envio de convites pela tela (FND-06c).
- Sessões ativas visíveis e revogáveis (R-SEC-7, desejável; sem story na Fase 0).
- SSO (R-SEC-8, pós-MVP).
- Autenticação do super admin (FND-13).

## Pontos em aberto
- **Cookie `__Host-` com Path restrito (revisão):** o prefixo `__Host-` exige `Path=/`, então as duas exigências conflitam. Opções: `__Host-` com `Path=/`, ou `__Secure-` com `Path=/api/v1/auth`. Decisão técnica a registrar em ADR antes da execução.
- **Política de senha (R-SEC-1):** tamanho mínimo e regras não estão definidos.
- **Valores de rate limit** por IP, tenant e usuário, e **janela de tolerância** do refresh concorrente (ex.: 30 s).
- **Validade do convite:** não definida em nenhum doc.
- **Rota pública de contexto** (nome e logo do tenant na tela de login): ver FND-02.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migrations criadas (Identity, `refresh_tokens`, `user_invites`).
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Telas com estados de carregando, vazio (não se aplica) e erro.
- [ ] Ajuda das telas escrita (conforme decisão sobre rotas públicas).
- [ ] Padrões e próximo passo propositivos avaliados (e-mail pré-preenchido, oferta de 2FA).
- [ ] Auditoria ligada para eventos de autenticação.
- [ ] Story marcada no backlog; ADR do cookie registrada.

## Tarefas técnicas sugeridas
- [ ] Identity por tenant no `TenantDbContext` (schema `identity`).
- [ ] Emissão de JWT só com identidade; chave de assinatura protegida (FND-11).
- [ ] Refresh rotativo com família, reuso e janela de tolerância.
- [ ] Anti-CSRF por `Origin` + cabeçalho próprio nos endpoints de cookie.
- [ ] Bloqueio, rate limit particionado e respostas uniformes.
- [ ] Recuperação de senha e aceite de convite com tokens de hash e uso único.
- [ ] Verificação do security stamp por request.
- [ ] Etapas `CreateOwner` e `SendInvite` do provisionamento.
- [ ] Telas Entrar, Esqueci minha senha, Redefinir senha e Aceitar convite; store de token em memória com refresh silencioso.

## Quality gates
- Agentes previstos: @dev, cybersecurity (autenticação e sessão), @ux-design-expert (telas), @qa.
- Gate: revisão de segurança aprovada; os testes de reuso e concorrência do refresh rodam estáveis 10 vezes seguidas.

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

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** ADR do cookie (`__Host-` × Path restrito); política de senha; valores de rate limit e da janela de refresh; validade do convite.
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story detalhada com ADR 0008 e os CAs de segurança da revisão; telas de login movidas da FND-09 para cá | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
_A preencher pelo @dev._

## QA Results
_A preencher pelo @qa._