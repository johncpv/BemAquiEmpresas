# ADR 0040: Identidade do portal do cliente

- Status: Aceito (21/10/2026). O PO (John) aprovou a decisão D16 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`) pela **opção da segurança** (host próprio para o portal), os pontos 1 a 16 da tabela 3.5 e os achados da seção 4.3 (POR-01a, POR-02).
- Contexto: o portal (Fase 8) dá a contatos dos clientes acesso aos próprios projetos, documentos, boletos e ao aceite de orçamento. É uma segunda população de usuários, de fora da empresa, no mesmo tenant. Hoje o JWT do backoffice é validado com `ValidateAudience = false` (o `aud` é conferido contra o host do request no `OnTokenValidated`), então um token emitido para outra superfície no mesmo host seria aceito. O cookie de refresh `__Host-bq_rt` tem `Path=/` por exigência do prefixo `__Host-` (ADR 0009): com o portal em `/portal` no mesmo host, o navegador mandaria o refresh do backoffice para as rotas do portal e vice-versa. Os revisores divergiram: o arquiteto preferia `/portal` no host do tenant; a segurança, host próprio.
- Complementa as ADRs 0008 (identidade no token), 0009 (cookies, rotas anônimas, adendo de 21/10/2026), 0010 (senha, rate limit), 0011 (identity enxuto), 0012 (identidade separada da plataforma), 0016 (IDOR) e 0037 (LGPD).

## Decisão

### 1. Host próprio
- O portal roda num **host próprio do tenant** (ex.: `portal.konebras.com.br`), registrado em `catalog.tenant_domains` com o tipo `Portal`. O host do portal só serve o front do portal e a API `/api/v1/portal/*`; o host do backoffice não serve o portal.
- O tenant é resolvido pelo host, como em qualquer request; o middleware recusa rota de backoffice no host do portal e rota do portal no host do backoffice (404).

### 2. Identidade separada
- Schema **`portal`**: `portal_users` (ligado a `party_id` e `contact_id`, e-mail normalizado **único por tenant**, `password_hash`, `security_stamp`, `status`, bloqueio, `totp_*`), `portal_invites`, `portal_refresh_tokens`, `portal_password_resets`, `portal_recovery_codes`. Nenhum usuário do portal existe em `identity.users`.
- **Chave de assinatura própria** do portal, **claim de tipo de token** (`typ = portal` × `typ = staff`), `aud` = host do portal e **validação estrita nos dois esquemas** (`ValidateAudience = true` com o host do request, emissor e tipo conferidos); o esquema do backoffice também passa a validar o `aud` na `TokenValidationParameters`, sem depender só do evento.
- Cookie de refresh **`__Host-bq_prt`** (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`), que só existe no host do portal; refresh rotativo com **detecção de reuso** (reuso revoga a família).
- **Teste obrigatório:** token do backoffice dá 401 em toda rota do portal e token do portal dá 401 em toda rota do backoffice (parametrizado sobre todos os endpoints), também com o token levado de um host para o outro.

### 3. Acesso e 2FA
- **2FA opcional para ver** (TOTP da FND-05); o tenant pode torná-lo obrigatório (`portal.require_2fa`).
- **Código por e-mail obrigatório no aceite de orçamento** (POR-02), de uso único, curto e com prazo, mesmo com o TOTP ligado.
- Convite por e-mail (72 h, token opaco com hash), recuperação de senha (1 h, uso único); **resposta uniforme e rate limit** por política nomeada no login, no convite e na recuperação; bloqueio de 5 erros por 15 min (ADR 0011).
- **Um contato vê um cliente só.** O `party_id` vem **sempre** da sessão, nunca do request; rota com id de outro cliente responde 404 com o mesmo tempo da inexistente (teste de IDOR com dois clientes e dois tenants).
- **Trocar o e-mail do contato** no backoffice invalida o acesso ao portal (novo convite). Contato excluído ou terceiro inativado derruba a sessão no próximo request.
- Documentos só por endpoint autorizado do portal (nunca URL de storage); DTOs do portal com allowlist (sem custo, margem, dado de outro terceiro, usuário interno nem dado de funcionário), lidos dos módulos por métodos `ForPortalAsync(partyId)` nos `*.Contracts`.

### 4. Aceite de orçamento
- O aceite **só avisa o Comercial** (mantém a D5 da Fase 2): a aprovação interna continua com `quotes.approve`.
- Grava a revisão do orçamento, o **hash do PDF congelado no envio**, IP, user-agent e o código conferido, e manda **recibo por e-mail** com o hash. Decisões do cliente só de inclusão (nunca editadas).
- **Revisão nova invalida o aceite** anterior; revisão diferente da aceita bloqueia a sugestão de aprovação na COM-04a. **Sem link de aceite sem login.** Assinatura eletrônica com validade jurídica fica fora.

### 5. Módulo no catálogo
- Módulo **`portal`** ("Portal do cliente", área Comercial) no `ModuleCatalog`, com `view`, `create` (convidar acesso), `edit` e `delete` (adendo de 21/10/2026 à ADR 0003), e seed patch para o Comercial (`portal.view`, `portal.create`, `portal.edit`).
- Módulo não liberado: o portal mostra "Funcionalidade não liberada nessa versão do sistema." e a API responde 403 `module_not_released`.

## Consequências
- **POR-01a** implementa as seções 1, 2, 3 e 5 e o adendo à ADR 0009; **POR-01b/01c** os documentos e boletos pelos métodos `ForPortalAsync`; **POR-02** a seção 4.
- O PO cadastra o DNS e o certificado do host do portal (vhost no `stack-nginx`, certbot) antes da POR-01a; o `catalog.tenant_domains` ganha o tipo do domínio.
- O reforço do `aud` no esquema do backoffice entra na POR-01a, com o teste de regressão de login, refresh e SignalR.
- `docs/02-arquitetura.md` §4 (quarta superfície em host próprio) e `docs/03-modelo-dados.md` (schema `portal`) atualizados.

## Alternativas consideradas
- **`/portal` no host do tenant** (recomendação do arquiteto): **rejeitada**. O cookie `__Host-` exige `Path=/`, então o navegador enviaria o `__Host-bq_rt` do backoffice às rotas do portal e o `__Host-bq_prt` às do backoffice; a separação ficaria toda no servidor, e um erro de rota exporia a sessão de uma superfície na outra. Com host próprio, o navegador isola os cookies.
- **2FA obrigatório para ver:** rejeitada; o cliente leigo abandona o portal, e ver o andamento tem risco baixo. O ponto de risco, o aceite, tem o código por e-mail.
