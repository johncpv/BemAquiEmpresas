# POR-01a — Portal do cliente: acesso separado do backoffice e isolamento por cliente

| Campo | Valor |
|---|---|
| ID | POR-01a |
| Título | Identidade própria do portal do cliente (`/portal`): usuários do portal ligados a um contato de um terceiro Cliente, convite pelo backoffice, senha e recuperação, 2FA opcional, sessão e cookies próprios, tokens que não valem no backoffice e vice-versa, isolamento estrito pelo cliente em toda consulta (IDOR), liberação do módulo por tenant, gestão de acessos e auditoria |
| Tamanho | G (quebra da POR-01 em 01a acesso, 01b projetos e documentos, 01c parcelas e boletos) |
| Status | Draft |
| Requisitos | PRD §5.15 (portal do cliente) · PRD §2 (perfil Cliente (portal)) · R-SEC-1 · R-SEC-2 · R-SEC-3 · R-SEC-5 · R-SEC-11 · PRD §6 (Segurança, LGPD) |
| Dependências | Fases 0 a 6 (FND-04, FND-05, FND-06, FND-08, FND-11, FND-13 como referência de identidade separada), CAD-01a (terceiro e contatos), COM-04a (projetos do cliente) |
| Bloqueia | POR-01b, POR-01c, POR-02 |
| Ordem de execução | 1 (Marco 8.1 "Acesso do cliente") |

## User story
Como **cliente da Konebras**, quero **entrar num portal com o meu e-mail e ver só o que é meu**, e como **proprietário**, quero **liberar o portal para um contato do cliente sem dar a ele nenhum acesso ao sistema interno**, para **o cliente se informar sozinho sem risco de ver dado de outro cliente**.

## Contexto e referências
- PRD §5.15 ("Acesso do cliente ao andamento do(s) projeto(s)…, documentos…, boletos em aberto e, futuramente, aprovação online de orçamento"), Fase 8; `02-arquitetura` §4 (quarta superfície, **Portal do cliente** em `/portal`, layout próprio).
- `CLAUDE.md`: sem rota anônima além de login, convite, recuperação de senha, quiosque, webhooks, `tenant-context` e health; as rotas de login, convite e recuperação do portal são dessas categorias, mas pedem ADR (Pontos em aberto 1).
- ADR 0008 (JWT só com identidade `sub`, `tid`, `aud`, `sstamp`, `amr`), ADR 0009 (cookie `__Host-bq_rt`, `X-CSRF`), ADR 0011 (`PasswordHasher`, tokens opacos de 32 bytes, bloqueio), ADR 0012 e FND-13 (a plataforma já tem identidade separada da do tenant: o mesmo padrão serve ao portal), ADR 0010 itens 5 e 6 (senha e rate limit).
- `ModuleCatalog` atual **não tem** o módulo `portal` (o projeto `src/Modules/Portal` existe vazio): a entrada no catálogo é proposta (Pontos em aberto 2).
- ADR 0016 (IDOR pai/filho, `IParties` por id), ADR 0019 §20 (nunca custo e margem para quem não tem `*.view_cost`).

## Critérios de aceite
**Liberação e gestão**
1. **Dado** o módulo `portal` (proposta: "Portal do cliente", área Comercial, ações `view`, `create` = "convidar acesso", `edit`, `delete`) liberado no tenant, **quando** um usuário do backoffice com `portal.create` abre o terceiro (papel Cliente) → aba "Portal" e clica "Convidar para o portal" num contato com e-mail, **então** nasce o usuário do portal `Invited` ligado ao terceiro e ao contato, e o e-mail de convite (template `portal_invite`, fila, link de 72 h, token opaco com hash) é enviado; o mesmo e-mail não pode ter dois acessos no mesmo cliente (409).
2. **Dado** o módulo `portal` não liberado no tenant, **quando** alguém abre `/portal` ou chama a API do portal, **então** a tela mostra "Funcionalidade não liberada nessa versão do sistema." e a API responde 403 `module_not_released`; no backoffice, a aba Portal do terceiro aparece com cadeado.
3. **Dado** `portal.edit`, **quando** clico "Desativar acesso" ou "Reenviar convite", **então** o acesso desativado perde a sessão no próximo request (security stamp) e o convite antigo é invalidado.

**Login e sessão**
4. **Dado** o convite, **quando** o contato abre o link, define a senha (mesma política da ADR 0010: 12+ caracteres, lista de vazadas) e aceita, **então** o acesso fica `Active` e ele entra em `/portal`; a tela de login do portal (`/portal/entrar`) usa o nome e o logo do `tenant-context`.
5. **[Segurança]** **Dado** o login do portal, **quando** emitido, **então** o access token tem `aud` distinto (proposta: `{host}/portal`) e a claim `ptl` com o id do usuário do portal (o `sub` é do usuário do portal, nunca de `identity.users`); o refresh vai num cookie próprio (`__Host-bq_prt`, `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`); um token do backoffice responde 401 em toda rota do portal e um token do portal responde 401 em toda rota do backoffice (teste parametrizado sobre todos os endpoints).
6. **[Segurança]** **Dado** o login, **quando** erra a senha 5 vezes, **então** bloqueia por 15 min com resposta e tempo uniformes (ADR 0011); rate limit por IP real + tenant; recuperação de senha por e-mail com token de 1 h e uso único; tudo auditado.
7. **Dado** o 2FA do portal (proposta: opcional para o cliente, TOTP da FND-05), **quando** ativado, **então** o login pede o código; o tenant pode torná-lo obrigatório para o portal (parâmetro `portal.require_2fa`, proposta).

**Isolamento**
8. **[Segurança]** **Dado** qualquer endpoint do portal, **quando** atende, **então** o cliente (`party_id`) vem **sempre** da sessão do usuário do portal (nunca de parâmetro, corpo ou rota) e toda consulta é filtrada por ele antes de qualquer outro filtro; uma rota com id (`/portal/projects/{id}`) de outro cliente responde 404, com o mesmo tempo da inexistente (teste de IDOR em todos os endpoints com id, com dois clientes e dois tenants).
9. **[Segurança]** **Dado** os DTOs do portal, **quando** o teste de arquitetura roda, **então** nenhum deles tem propriedade de custo, margem, orçado, markup, dado de outro terceiro, usuário interno, comentário interno nem dado pessoal de funcionário (allowlist dos DTOs do portal), e o portal só lê os outros módulos pelos `*.Contracts` com métodos próprios "para o portal" (`ForPortalAsync(partyId)`, proposta).
10. **[Segurança]** **Dado** um contato que deixa de ser do cliente (excluído ou o terceiro inativado), **quando** o usuário do portal tenta entrar, **então** é recusado e a sessão aberta cai no próximo request.

**Auditoria e LGPD**
11. **Dado** convite, aceite, login, falha, recuperação, desativação e cada documento baixado (POR-01b), **quando** consulto a auditoria (backoffice, com `portal.view`), **então** vejo quem, quando, o quê e o IP; o e-mail do usuário do portal é `[PersonalData]`; o usuário do portal entra no `IPersonalDataProvider` (LGPD-01a).

## Regras de negócio e técnicas
- **Tabelas (propostas, schema `portal`):** `portal.portal_users` (`party_id`, `contact_id`, `email`, `normalized_email`, `password_hash`, `security_stamp`, `status` Invited/Active/Inactive, `access_failed_count`, `lockout_end`, `totp_*`, `last_login_at`; único `(party_id, normalized_email)`), `portal.portal_invites`, `portal.portal_refresh_tokens`, `portal.portal_password_resets`, `portal.portal_recovery_codes`.
- **Identidade separada** no módulo Portal, reaproveitando os componentes do Identity por `Identity.Contracts` (hash, TOTP, tokens), como a FND-13 fez para a plataforma; o `TenantDbContext` é o do tenant (host).
- **Esquema de autenticação próprio** (`PortalBearer`) e policy `portal:client`; o filtro de isolamento é obrigatório nos endpoints do portal (teste de arquitetura).
- **Mesmo host do tenant, caminho `/portal`** (proposta, Pontos em aberto 3).

## Permissões, eventos e tabelas
- **Permissões (backoffice):** `portal.view`, `portal.create`, `portal.edit`, `portal.delete` (proposta de módulo novo no `ModuleCatalog`, com seed patch para o Comercial: `portal.view/create/edit`, proposta).
- **Eventos:** `PortalUserActivated` (`{ portalUserId, partyId }`).
- **Tabelas:** schema `portal` (acima); templates `portal_invite`, `portal_password_reset`.

## Telas e estados
- **Portal:** `/portal/entrar`, `/portal/convite/$token`, `/portal/recuperar`, `/portal` (início com os projetos, POR-01b), menu do usuário (senha, 2FA, sair). Layout próprio, sem dock, com a marca do tenant, tema claro/escuro.
- **Backoffice:** aba **Portal** no terceiro (acessos por contato: situação, último acesso, "Convidar", "Reenviar convite", "Desativar acesso").
- **Estados:** convite expirado ("Este convite expirou. Peça um novo à {empresa}."); bloqueado; módulo não liberado; erro.
- **400 px:** o portal é pensado primeiro para o celular.
- **Ajuda:** `Ajuda/portal-entrar.md`, `Ajuda/portal-inicio.md`, `Ajuda/parties-terceiro-portal.md` (aba `portal`); o teste de cobertura inclui as rotas do portal.

## Plano de testes
- **Unidade:** regras de status do acesso.
- **Integração (Testcontainers):** convite e aceite (CA 1, 4); módulo não liberado (CA 2); desativação (CA 3); tokens cruzados em todos os endpoints (CA 5); bloqueio, rate limit e recuperação (CA 6); 2FA (CA 7); IDOR com dois clientes e dois tenants (CA 8); contato removido (CA 10); auditoria (CA 11).
- **Arquitetura:** allowlist dos DTOs do portal e filtro obrigatório (CA 9).
- **E2E (Playwright):** convidar, aceitar, entrar e sair a 400 px.
- **Segurança:** revisão do @cybersecurity (OWASP ASVS de autenticação) antes do `Done`.

## Fora do escopo
- Projetos, documentos, parcelas e boletos: POR-01b e POR-01c. Aprovação online de orçamento: POR-02.
- SSO para o cliente e login por link mágico: depois do MVP.

## Pontos em aberto
1. **ADR do portal** (pré-condição, arquiteto + PO): identidade separada, rotas anônimas do portal (login, convite, recuperação), `aud` e cookies próprios.
2. **Módulo `portal` no `ModuleCatalog`** (pré-condição, PO): nome, área, ações e o seed patch para o Comercial.
3. **Host do portal** (PO): mesmo host do tenant em `/portal` (proposta, como o `02-arquitetura` §4) ou um host próprio (ex.: `portal.konebras.com.br`, que pede DNS e certificado).
4. 2FA do portal opcional (proposta) ou obrigatório.
5. Um contato pode ver mais de um cliente (grupo econômico)? Proposta: não; um acesso por cliente.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura, E2E e revisão de segurança passando.
- [ ] Migration criada (não destrutiva; primeira do schema `portal`); ADR do ponto 1.
- [ ] Permissões só do `ModuleCatalog` (módulo novo com seed).
- [ ] Telas com estados; ajuda; teste de cobertura passando.
- [ ] Auditoria ligada, com `[PersonalData]`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/02-arquitetura.md` e `docs/03-modelo-dados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Módulo Portal (5 camadas), esquema de autenticação e policy; filtro de isolamento.
- [ ] Endpoints `/api/v1/portal/auth/*` e `/api/v1/portal/me`; gestão de acessos em `/api/v1/portal/admin/*` (backoffice).
- [ ] Layout do portal e telas; ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (identidade separada), cybersecurity (autenticação, IDOR), design (layout do portal), @qa.
- Gate: CA 5, 6, 8 e 9 obrigatórios e nunca ignorados.

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

- **Correções feitas na validação:** quebra da POR-01 (maior que G) em acesso (01a), projetos e documentos (01b) e financeiro (01c); `party_id` sempre da sessão (CA 8); tokens cruzados recusados nos dois sentidos (CA 5); o módulo `portal`, que não existe no catálogo, virou pré-condição (ponto 2).
- **Pré-condições para `Ready`:** pontos em aberto 1, 2 e 3.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da POR-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
