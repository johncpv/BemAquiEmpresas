# ADR 0012: Sessão do super admin por cookie opaco, com inatividade

- Status: Aceito (18/10/2026).
- Contexto: a FND-13 pede um esquema de autenticação próprio no host da plataforma, com 2FA obrigatório, painel do Hangfire protegido e expiração por inatividade. O tempo de inatividade não estava definido.
  - O painel do Hangfire é aberto direto no navegador e não envia `Authorization`. Por isso, a sessão precisa estar num cookie.
  - O JWT do tenant (ADR 0008) foi feito para a SPA, com refresh rotativo, e não serve aqui.

## Decisão
- **Esquema `Platform`:** sessão em cookie opaco `__Host-bq_pa` (HttpOnly, Secure, SameSite=Strict, Path=/, sem Domain). O cookie é de sessão, sem Expires.
  - No catálogo (`catalog.platform_sessions`) fica só o hash, junto com o host da plataforma, `created_at` e `last_seen_at`.
  - O esquema ignora o cabeçalho `Authorization`, então um token de tenant não vale na plataforma. O prefixo `__Host-` impede que o cookie seja enviado a outro host.
- **Expiração (padrão técnico, configurável em `PlatformAuth`):**
  - inatividade de 30 min (`IdleTimeout`);
  - teto de 8 h, mesmo com uso contínuo (`AbsoluteTimeout`);
  - `last_seen_at` é atualizado no máximo uma vez por minuto.
- **Anti-CSRF:** todo POST em `/api/v1/platform` exige `Origin` do próprio host e `X-CSRF: 1`.
- **Login:**
  - o super admin só é criado pela CLI (`platform-admin create`), que mostra uma única vez um link de definição de senha válido por 24 h;
  - o login sempre devolve um desafio opaco de 5 min (`setup` no primeiro acesso, `verify` depois). Só o 2FA abre a sessão;
  - valem as mesmas regras da FND-04/05: senha da ADR 0010, bloqueio de 15 min após 5 erros, anti-replay atômico e 10 códigos de recuperação;
  - o rate limit de `/api/v1/auth` também cobre `/api/v1/platform/auth`.
- **Roteamento por host:**
  - `/api/v1/platform` e `/hangfire` só existem no host da plataforma. Nos hosts de tenant, respondem 404;
  - no host da plataforma, qualquer outra rota responde 404;
  - sem sessão, `/hangfire` redireciona para `/plataforma/entrar?volta=/hangfire`.
- **Allowlist:**
  - fica no nginx (ADR 0010 item 7);
  - a app tem uma segunda camada opcional, `Platform:AllowedNetworks` (CIDR). Vazia, a app não filtra;
  - fora da lista, a resposta é 404.
- **Auditoria:** os eventos do super admin vão para a `audit.audit_log` do catálogo, que já é imutável, com `entity = "PlatformAuth"`. Ela faz o papel da `platform_audit_log` proposta na FND-13. A coluna `action` foi ampliada para 64 caracteres.

## Consequências
- Primitivas compartilhadas (`Totp`, `Base32`, `OpaqueToken`, `PasswordPolicy`) foram para `BuildingBlocks.Security`, e Identity e Platform usam o mesmo código.
- Para mudar os tempos de sessão, basta mudar a configuração. Se o PO definir outros valores, eles entram em `PlatformAuth` sem código novo.

## Adendo (FND-03b, 18/10/2026): telas e jobs da plataforma
- **Caminhos:** as telas da plataforma ficam em `/plataforma/*` (`/plataforma/entrar`, `/plataforma/definir-senha`, `/plataforma/tenants`), no host da plataforma.
  - Elas não se confundem com o `/entrar` dos tenants e funcionam também em desenvolvimento.
  - O redirecionamento do Hangfire e o link da CLI apontam para esses caminhos.
- **401 na plataforma:** o mutator leva ao login da plataforma, sem tentar o refresh do tenant.
- **`PlatformJob<TArgs>`:** classe base para os jobs que só tocam no catálogo. O primeiro é o provisionamento, que roda antes de o tenant existir.
  - O teste de arquitetura aceita `TenantJob`, `GlobalRecurringJob` e `PlatformJob`.
  - Job que mexe em dado de tenant continua obrigado a receber o `tenantId`.
