# POR-01a — Portal do cliente: acesso separado do backoffice e isolamento por cliente

| Campo | Valor |
|---|---|
| ID | POR-01a |
| Título | Identidade própria do portal do cliente em **host próprio** (ADR 0040): usuários do portal ligados a um contato de um terceiro Cliente (e-mail único por tenant, um contato vê um cliente só), convite pelo backoffice, senha e recuperação com resposta uniforme e rate limit, 2FA opcional, chave de assinatura, claim de tipo, `aud` e cookies próprios, validação estrita nos dois esquemas, refresh com detecção de reuso, isolamento estrito pelo cliente em toda consulta (IDOR), módulo `portal` no catálogo com seed patch do Comercial, gestão de acessos, auditoria e o Kit Portal |
| Tamanho | G (quebra da POR-01 em 01a acesso, 01b projetos e documentos, 01c parcelas e boletos) |
| Status | Ready (pré-condição externa) |
| Requisitos | PRD §5.15 (portal do cliente) · PRD §2 (perfil Cliente (portal)) · R-SEC-1 · R-SEC-2 · R-SEC-3 · R-SEC-5 · R-SEC-11 · PRD §6 (Segurança, LGPD) |
| Dependências | FND-02 e FND-03b (`catalog.tenant_domains` e tela do tenant), FND-04, FND-05, FND-06 (`ModuleCatalog`), FND-08 (e-mail), FND-11 (chaves), FND-13 (padrão de identidade separada), CAD-01a (terceiro e contatos), COM-04a (projetos do cliente), LGPD-01a (`IPersonalDataProvider`); Fase 7 concluída (ordem das fases, sem dependência técnica) |
| Bloqueia | POR-01b, POR-01c, POR-02, WPP-01 (opt-in pelo próprio contato no portal) |
| Ordem de execução | 1 (Marco 8.1 "Acesso do cliente") |

## User story
Como **cliente da Konebras**, quero **entrar num portal com o meu e-mail e ver só o que é meu**, e como **proprietário**, quero **liberar o portal para um contato do cliente sem dar a ele nenhum acesso ao sistema interno**, para **o cliente se informar sozinho sem risco de ver dado de outro cliente**.

## Contexto e referências
- PRD §5.15 ("Acesso do cliente ao andamento do(s) projeto(s)…, documentos…, boletos em aberto e, futuramente, aprovação online de orçamento"), Fase 8; `02-arquitetura` §4 (quarta superfície, **Portal do cliente** em host próprio, ADR 0040).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D16 (opção da segurança: host próprio, 2FA opcional para ver, código por e-mail obrigatório no aceite), D18 (protótipo e Kit Portal antes da Fase 8); tabela 3.5, pontos 1 a 5; §4.3 POR-01a e transversal; §4.4 padrão 5.
- **ADR 0040** (`docs/adr/0040-identidade-portal-cliente.md`): host próprio, chave de assinatura própria, claim de tipo de token, validação estrita de `aud` nos dois esquemas, cookies próprios, refresh com detecção de reuso. **Adendos de 21/10/2026:** ADR 0003 (módulo `portal` e as suas ações), ADR 0009 (rotas anônimas do portal).
- ADR 0008 (JWT só com identidade `sub`, `tid`, `aud`, `sstamp`, `amr`), ADR 0009 (cookie `__Host-`, `X-CSRF`), ADR 0011 (`PasswordHasher`, tokens opacos de 32 bytes, bloqueio), ADR 0012 e FND-13 (identidade separada da plataforma: o mesmo padrão serve ao portal), ADR 0010 itens 5 e 6 (senha e rate limit), ADR 0016 (IDOR pai/filho, `IParties` por id; seed patch), ADR 0019 §20 (custo e margem nunca para quem não tem `*.view_cost`).
- **Achado do código:** hoje o esquema do backoffice tem `ValidateAudience = false` e só confere o host em `OnTokenValidated` (`TenantJwtAuthentication.cs`); com o portal, os dois esquemas passam a validar `aud`, emissor e tipo de token de forma estrita.
- **Por que host próprio:** o cookie `__Host-bq_rt` com `Path=/` do backoffice iria junto para `/portal` no mesmo host; com host próprio, nenhum cookie de um lado chega ao outro.

## Critérios de aceite
**Host próprio do portal**
1. **Dado** o super admin na tela do tenant (FND-03b), **quando** cadastra o host do portal, **então** ele entra em `catalog.tenant_domains` com `kind = Portal` (as linhas existentes ficam `kind = App`), um único host `Portal` por tenant, host único no catálogo e `verified_at` preenchido quando o DNS e o certificado respondem; o padrão sugerido é **`portal.` + o domínio do tenant** (Konebras: `portal.konebras.com.br`), editável para um domínio próprio do cliente.
2. **[Segurança]** **Dado** uma requisição, **quando** o middleware resolve o tenant pelo host, **então** também resolve a superfície (`App` ou `Portal`): no host do portal só respondem o SPA em `/portal/*` (a raiz `/` redireciona para `/portal`), `/api/v1/portal/*`, `/api/v1/public/tenant-context` e `/health/*`; `/app`, `/fab`, `/tv` e o resto de `/api/v1/*` respondem 404; no host do backoffice, `/portal/*` e `/api/v1/portal/*` respondem 404 (teste parametrizado sobre todos os endpoints).

**Liberação e gestão**
3. **Dado** o módulo `portal` ("Portal do cliente", área Comercial, ações `view`, `create` = "convidar", `edit`, `delete`) entrando no `ModuleCatalog`, **quando** o seed patch `portal-commercial` (ADR 0016, adendo da ADR 0003) roda, **então** o grupo Comercial do seed ganha `portal.view`, `portal.create` e `portal.edit`, uma vez, com o `perm_version` incrementado; `portal.delete` fica só com o Proprietário.
4. **Dado** o módulo liberado no tenant e `portal.create`, **quando** abro o terceiro (papel Cliente) → aba "Portal" e clico "Convidar para o portal" num contato com e-mail, **então** nasce o usuário do portal `Invited` ligado ao terceiro e ao contato, e o e-mail de convite (template `portal_invite`, fila, link de 72 h para o host do portal, token opaco guardado como hash) é enviado; o **e-mail é único por tenant** (o mesmo e-mail em outro cliente responde 409 "Este e-mail já tem acesso ao portal por outro cliente."), então um contato vê um cliente só.
5. **Dado** o módulo `portal` não liberado no tenant, **quando** alguém abre o host do portal ou chama a API do portal, **então** a tela do portal mostra "Funcionalidade não liberada nessa versão do sistema." e a API responde 403 `module_not_released`; no backoffice, a aba Portal do terceiro aparece com cadeado.
6. **Dado** `portal.edit`, **quando** clico "Desativar acesso" ou "Reenviar convite", **então** o acesso desativado perde a sessão no próximo request (security stamp e revogação da família de refresh) e o convite antigo é invalidado; "Remover acesso" exige `portal.delete`.

**Login, sessão e tokens**
7. **Dado** o convite, **quando** o contato abre o link, define a senha (ADR 0010: 12+ caracteres, lista de vazadas) e aceita, **então** o acesso fica `Active` e ele entra em `/portal`; a tela de login (`/portal/entrar`) usa o nome e o logo do `tenant-context`.
8. **[Segurança]** **Dado** o login do portal, **quando** o token é emitido, **então** é assinado com a **chave própria do portal** (nunca a do backoffice), tem `aud` = host do portal, o emissor do portal, a claim de tipo `typ = portal` e `sub` = id do usuário do portal (nunca de `identity.users`); o esquema do backoffice passa a validar `aud` = host do backoffice, o emissor e `typ = staff` (fim do `ValidateAudience = false`); **um token do backoffice responde 401 em toda rota do portal e um token do portal responde 401 em toda rota do backoffice**, inclusive com a assinatura trocada (teste parametrizado sobre todos os endpoints dos dois lados).
9. **[Segurança]** **Dado** o refresh do portal, **quando** emitido, **então** vai no cookie `__Host-bq_prt` (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, só no host do portal), com `X-CSRF` (ADR 0009), rotação a cada uso e **detecção de reuso**: um refresh já usado revoga a família inteira, sobe o security stamp e registra o evento na auditoria.
10. **[Segurança]** **Dado** o login, o aceite do convite e a recuperação de senha, **quando** chamados, **então** as respostas e o tempo são **uniformes** (e-mail inexistente, convite inválido ou expirado e senha errada não se distinguem; recuperação sempre responde "Se houver acesso com este e-mail, enviamos as instruções."), com rate limit pela política nomeada `portal-auth` (5 tentativas por e-mail em 15 min e 20 por IP real + tenant por hora); 5 senhas erradas bloqueiam por 15 min (ADR 0011); recuperação por e-mail com token de 1 h e uso único; tudo auditado.
11. **Dado** o 2FA do portal (TOTP da FND-05), **quando** o cliente o ativa em "Minha conta", **então** o login pede o código; é **opcional** para ver; o aceite de orçamento (POR-02) exige sempre o código por e-mail, com ou sem 2FA.
12. **[Segurança]** **Dado** o e-mail do contato trocado no cadastro do terceiro (CAD-01a), o contato excluído ou o terceiro inativado, **quando** a mudança é gravada, **então** o Parties publica `PartyContactChanged` e o Portal desativa o acesso ligado (security stamp e família de refresh revogados); um novo acesso exige novo convite para o e-mail novo; no login, o e-mail também é conferido com o do contato atual pelo `IParties`.

**Isolamento**
13. **[Segurança]** **Dado** qualquer endpoint do portal, **quando** atende, **então** o cliente (`party_id`) vem **sempre** da sessão do usuário do portal (nunca de parâmetro, corpo ou rota) e toda consulta é filtrada por ele antes de qualquer outro filtro; uma rota com id (`/api/v1/portal/projects/{id}`) de outro cliente responde 404, com o mesmo tempo da inexistente (teste de IDOR em todos os endpoints com id, com dois clientes e dois tenants).
14. **[Segurança]** **Dado** os DTOs do portal, **quando** o teste de arquitetura roda, **então** nenhum deles tem propriedade de custo, margem, orçado, markup, dado de outro terceiro, usuário interno, comentário interno nem dado pessoal de funcionário (allowlist dos DTOs do portal); o portal só lê os outros módulos pelos `*.Contracts` com métodos próprios `ForPortalAsync(partyId)`; documentos e arquivos só saem por endpoint autorizado do portal (nenhuma URL direta do storage nem link público), com o storage no prefixo do tenant.

**Auditoria, LGPD e Kit Portal**
15. **Dado** convite, aceite, login, falha, reuso de refresh, recuperação, desativação e cada documento baixado (POR-01b), **quando** consulto a auditoria (backoffice, com `portal.view`), **então** vejo quem, quando, o quê e o IP; o e-mail do usuário do portal é `[PersonalData]`; o usuário do portal entra no `IPersonalDataProvider` (LGPD-01a), e convites e redefinições vencidos são expurgados em 30 dias.
16. **Dado** o portal no celular a 400 px, **quando** abro qualquer tela, **então** valem os padrões do Kit Portal: texto base de 16 px, alvos de 44 px, vocabulário leigo (teste de texto que reprova "OS", "marco", "romaneio" e letras de etapa nas telas do portal), bloco "Fale com a {empresa}" (telefone e e-mail do perfil da empresa, CAD-06) em todo erro, a tela "Preferências de aviso" (`/portal/preferencias`, preenchida pela POR-01b e pela WPP-01) e a mensagem própria de indisponibilidade ("O portal da {empresa} está em manutenção. Tente de novo em alguns minutos.").

## Regras de negócio e técnicas
- **Host:** `catalog.tenant_domains` + `kind` (`App`/`Portal`, padrão `App`, expansão sem perda), índice único parcial `(tenant_id) WHERE kind = 'Portal'`; `ITenantContext` ganha `Surface`. O vhost do portal no `stack-nginx` com certificado do certbot (ADR 0004).
- **Tabelas (schema `portal`):** `portal.portal_users` (`party_id`, `contact_id`, `email`, `normalized_email` **único no tenant**, `password_hash`, `security_stamp`, `status` Invited/Active/Inactive, `access_failed_count`, `lockout_end`, `totp_*`, `last_login_at`), `portal.portal_invites`, `portal.portal_refresh_tokens` (`family_id`, `replaced_by`, `revoked_at`), `portal.portal_password_resets`, `portal.portal_recovery_codes`.
- **Identidade separada** no módulo Portal, reaproveitando os componentes do Identity por `Identity.Contracts` (hash, TOTP, tokens), como a FND-13 fez para a plataforma; o `TenantDbContext` é o do tenant (host do portal).
- **Esquema de autenticação** `PortalBearer` com chave própria (protegida pelo Data Protection, rotação documentada na ADR 0040), policy `portal:client`; o filtro de isolamento é obrigatório nos endpoints do portal (teste de arquitetura).
- **Rotas anônimas do portal** (adendo da ADR 0009): `/api/v1/portal/auth/login`, `/refresh`, `/invite/accept`, `/password/forgot`, `/password/reset` e `/api/v1/public/tenant-context` no host do portal; na lista versionada do teste de arquitetura.

## Permissões, eventos e tabelas
- **Permissões (backoffice):** módulo novo `portal` no `ModuleCatalog` (`portal.view`, `portal.create`, `portal.edit`, `portal.delete`), seed patch `portal-commercial` (Comercial: `view`, `create`, `edit`).
- **Eventos:** `PortalUserActivated` (`{ portalUserId, partyId }`); consome `PartyContactChanged` (`{ partyId, contactId, change }`, novo no `Parties.Contracts`).
- **Tabelas:** schema `portal` (acima); `catalog.tenant_domains` + `kind`; templates `portal_invite`, `portal_password_reset`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 5 (Kit Portal: celular primeiro, 16 px, alvos de 44 px, vocabulário leigo com teste de texto, "Fale com a {empresa}", "Preferências de aviso", indisponibilidade).
- **Portal (host próprio):** `/portal/entrar`, `/portal/convite/$token`, `/portal/recuperar`, `/portal/redefinir/$token`, `/portal` (início com os projetos, POR-01b), `/portal/conta` (senha, 2FA, sair), `/portal/preferencias`. Layout próprio (`PortalLayout`), sem dock, com a marca do tenant, tema claro, escuro e automático.
- **Backoffice:** aba **Portal** no terceiro (acessos por contato: situação, último acesso, "Convidar para o portal", "Reenviar convite", "Desativar acesso", "Remover acesso"); campo do host do portal na tela do tenant (FND-03b).
- **Verbos:** "Convidar para o portal" / "Convite enviado"; "Desativar acesso" / "Acesso desativado"; "Entrar"; "Criar senha" / "Senha criada".
- **Estados:** carregando; vazio da aba ("Nenhum contato com acesso ao portal." + "Convidar para o portal"); convite expirado ("Este convite expirou. Peça um novo à {empresa}." + bloco "Fale com a {empresa}"); bloqueado; módulo não liberado; erro; indisponível.
- **Propositivo:** o convite sugere o contato que "Recebe cobrança" (CAD-01a); depois de aprovar um orçamento, o `NextSteps` da COM-04a oferece "Convidar o cliente para o portal" (com `portal.create`).
- **400 px:** o portal é pensado primeiro para o celular.
- **Ajuda:** `Ajuda/portal-entrar.md`, `Ajuda/portal-inicio.md`, `Ajuda/portal-conta.md`, `Ajuda/portal-preferencias.md`, `Ajuda/cadastros-terceiro-portal.md` (aba `portal`), todas previstas; o teste de cobertura inclui as rotas do portal.

## Plano de testes
- **Unidade:** regras de status do acesso; resolução de superfície pelo host.
- **Integração (Testcontainers):** host do portal no catálogo (CA 1); superfícies separadas (CA 2); seed patch uma vez só (CA 3); convite, e-mail único no tenant e aceite (CA 4, 7); módulo não liberado (CA 5); desativação e remoção (CA 6); tokens cruzados e assinatura trocada em todos os endpoints dos dois lados (CA 8); refresh com reuso (CA 9); resposta uniforme, rate limit, bloqueio e recuperação (CA 10); 2FA opcional (CA 11); troca do e-mail do contato (CA 12); IDOR com dois clientes e dois tenants (CA 13); auditoria e expurgo (CA 15).
- **Arquitetura:** allowlist dos DTOs, filtro obrigatório e lista de rotas anônimas (CA 13, 14); nenhum endpoint do portal sem `portal:client`.
- **Front:** teste de vocabulário das telas do portal (CA 16).
- **E2E (Playwright):** convidar, aceitar, entrar e sair a 400 px no host do portal; o token do portal recusado no backoffice.
- **Segurança:** revisão do @cybersecurity (OWASP ASVS de autenticação e sessão) antes do `Done`.

## Fora do escopo
- Projetos, documentos, parcelas e boletos: POR-01b e POR-01c. Aceite online de orçamento: POR-02.
- SSO para o cliente e login por link mágico: depois do MVP.
- Um contato com acesso a mais de um cliente (grupo econômico): fora (um contato vê um cliente só).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.5, pontos 1–5):
1. ADR 0040 (D16): chave própria, claim de tipo, validação estrita nos dois esquemas, teste "token de um lado dá 401 no outro" (CA 8, 9).
2. Módulo `portal` no `ModuleCatalog` com o seed patch `portal-commercial` (CA 3).
3. **Host próprio**: linha `kind = Portal` em `catalog.tenant_domains`, padrão `portal.` + domínio do tenant (Konebras: `portal.konebras.com.br`), mesma SPA servida só em `/portal/*` nesse host (CA 1, 2).
4. 2FA opcional para ver; código por e-mail obrigatório no aceite (POR-02) (CA 11).
5. Um contato vê um cliente só, garantido pelo e-mail único por tenant (CA 4).

Decisões da revisão aplicadas: D16; D18 (protótipo e Kit Portal antes do código, CA 16); §4.3 POR-01a (resposta uniforme e rate limit, e-mail único, refresh com reuso, troca do e-mail invalida o acesso, documentos só por endpoint autorizado, CA 4, 9, 10, 12, 14); §4.3 transversal (rate limit por política nomeada, storage com prefixo de tenant); adendos das ADRs 0003 e 0009.

**Pré-condição externa:** registro DNS do host do portal (`portal.konebras.com.br`) pela Konebras, apontando para a VPS, para o certificado e o `verified_at` (não bloqueia o `Ready`; no tenant `demo`, o host do portal é de teste).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura, front, E2E e revisão de segurança passando.
- [ ] Migration criada (não destrutiva; primeira do schema `portal`; `tenant_domains.kind` como expansão); ADR 0040 aceita.
- [ ] Permissões só do `ModuleCatalog` (módulo novo com o seed patch `portal-commercial`).
- [ ] Vhost do host do portal no `stack-nginx` com certificado; `KnownProxies` sem mudança.
- [ ] Telas com estados e Kit Portal; ajudas previstas escritas; teste de cobertura passando.
- [ ] Auditoria ligada, com `[PersonalData]`; expurgo registrado.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/02-arquitetura.md` (superfícies por host) e `docs/03-modelo-dados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `tenant_domains.kind`, `Surface` no `ITenantContext` e o roteamento por superfície; campo na tela do tenant.
- [ ] Módulo Portal (5 camadas), esquema `PortalBearer` com chave própria e policy; validação estrita de `aud`, emissor e `typ` no esquema do backoffice.
- [ ] Endpoints `/api/v1/portal/auth/*` e `/api/v1/portal/me`; gestão de acessos em `/api/v1/portal/admin/*` (backoffice); consumidor de `PartyContactChanged`.
- [ ] Kit Portal (`PortalLayout`, `PortalCard`, `CompanyContactBlock`, `PortalUnavailable`, teste de vocabulário) a partir do protótipo aprovado (D18); telas e ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (identidade separada, superfícies por host), cybersecurity (autenticação, sessão, IDOR), design (protótipo e Kit Portal antes do código), @qa.
- Gate: CA 2, 8, 9, 10, 13 e 14 obrigatórios e nunca ignorados.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: FND-02/03b, LGPD-01a; WPP-01 em "Bloqueia") |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** quebra da POR-01 (maior que G) em acesso (01a), projetos e documentos (01b) e financeiro (01c); `party_id` sempre da sessão (CA 8); tokens cruzados recusados nos dois sentidos (CA 5); o módulo `portal`, que não existe no catálogo, virou pré-condição (ponto 2).
- **Correções da revalidação (21/10/2026):** pontos 1 a 5 decididos (ADR 0040 com chave própria, claim de tipo e validação estrita nos dois esquemas, CA 8; módulo `portal` com seed patch, CA 3; host próprio `kind = Portal` com o padrão `portal.` + domínio, CA 1, 2; 2FA opcional e código no aceite, CA 11; um cliente por contato pelo e-mail único, CA 4); §4.3 aplicada (CA 9, 10, 12, 14); auditoria com reuso e expurgo (CA 15); Kit Portal (padrão 5, CA 16); dependências e "Bloqueia" ajustados; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: DNS do host do portal (não bloqueia).
- **Conferência do fechamento (21/10/2026, amostragem do PO):** host próprio e token cruzado com 401 conferidos com a ADR 0040; checklist refeita, sem mudança de resultado.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da POR-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D16 com host próprio, D18, tabela 3.5 pontos 1–5, §4.3 POR-01a e transversal, §4.4 padrão 5, ADR 0040); nova ordem (Marco 8.1, ordem 1, antes do go-live); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
