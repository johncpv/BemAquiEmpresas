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
- O 2FA (FND-05) usa TOTP da RFC 6238 (HMAC-SHA1, 30 s, 6 dígitos, ±1 intervalo), implementado no módulo porque o .NET não expõe o TOTP do Identity fora do `UserManager`. Os dados ficam nas entidades próprias: o segredo é cifrado com `SecretKind.TotpSecret` (FND-11) e os 10 códigos de recuperação são guardados como hash.

## Adendo (FND-05, 18/10/2026): desafio de 2FA opaco
- A story sugeria um JWT intermediário com `scope=2fa`. Em vez dele, o desafio é um **token opaco de 5 min**, guardado como hash em `identity.two_factor_challenges`, com contador de tentativas: 5 erros derrubam o desafio.
- Como não é um bearer token, ele não passa pelo JwtBearer e não abre nenhuma rota além de `/auth/2fa/*`. Isso atende o CA 3 sem depender de policy por escopo.
- O desafio tem dois propósitos:
  - `verify`: o usuário tem 2FA e falta o código;
  - `setup`: o 2FA é obrigatório e ainda não foi configurado. Esse desafio só libera a configuração (CA 8).
- O anti-replay e o uso único dos códigos de recuperação são feitos por UPDATE condicional atômico (`totp_last_step < passo`, `used_at is null`), então duas requisições paralelas com o mesmo código não passam juntas.
- Ativar ou desativar o 2FA troca o security stamp e revoga as sessões. A resposta já traz uma sessão nova.
- Uma troca futura para o Identity completo exigiria migrar as tabelas. Como o hash é o mesmo formato do Identity, as senhas não precisariam ser redefinidas.
