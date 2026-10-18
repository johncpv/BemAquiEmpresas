# ADR 0011: Identity enxuto (hash do ASP.NET Core Identity, sem UserManager)

- Status: Aceito (18/10/2026).
- Contexto: a FND-04 pede ASP.NET Core Identity por tenant e hash PBKDF2. O Identity completo (`UserManager` + `IdentityDbContext` + stores) traria um segundo DbContext por tenant, com o modelo de tabelas próprio dele. Isso conflita com a ADR 0006 (um único `TenantDbContext`, com uma interface de dados por módulo) e com o modelo de `docs/03-modelo-dados.md`.

## Decisão
- Usar do Identity só o `PasswordHasher<User>`: PBKDF2 com rehash automático quando os parâmetros mudam. As entidades são próprias, no schema `identity`, seguindo o `03-modelo-dados.md`: `users`, `refresh_tokens`, `user_invites` e `password_resets`.
- **Sessão:**
  - access token JWT de 15 min, só com identidade (ADR 0008);
  - refresh opaco de 7 dias, guardado como SHA-256, rotativo em famílias;
  - reapresentar um refresh fora da tolerância de 10 s revoga a família inteira;
  - o cookie de refresh é `__Host-bq_rt`, com `HttpOnly`, `Secure`, `SameSite=Strict` e `Path=/` (ADR 0009).
- **Proteção do login:**
  - bloqueio por 15 min depois de 5 senhas erradas seguidas;
  - resposta e tempo uniformes, com verificação de um hash fictício quando o e-mail não existe;
  - security stamp conferido a cada request, com cache de 30 s invalidado na hora.
- **Tokens opacos** (refresh, convite, redefinição): 32 bytes aleatórios; no banco fica só o hash.

## Consequências
- O 2FA (FND-05) usa o TOTP nativo do .NET, gravado nas entidades próprias: segredo cifrado (FND-11) e códigos de recuperação como hash.
- Uma troca futura para o Identity completo exigiria migrar as tabelas. Como o hash é o mesmo formato do Identity, as senhas não precisariam ser redefinidas.
