# FND-05 — Verificação em duas etapas (TOTP)

| Campo | Valor |
|---|---|
| ID | FND-05 |
| Título | 2FA por app autenticador, códigos de recuperação e exigência por tenant |
| Tamanho | M |
| Status | Done |
| Requisitos | R-SEC-2 |
| Dependências | FND-04, FND-11 (segredo TOTP cifrado); telas dependem de FND-09a e FND-09c |
| Bloqueia | FND-06b/06c (exigência por grupo e para quem aprova ou paga), FND-13 (reaproveita o TOTP) |
| Ordem de execução | 9 |

## User story
Como **usuário**, quero **proteger minha conta com um código do app autenticador**, e como **proprietário**, quero **tornar o 2FA obrigatório na empresa**, para **que uma senha vazada não baste para entrar no sistema**.

## Contexto e referências
- PRD R-SEC-2: 2FA opcional por TOTP + códigos de recuperação; proprietário pode tornar obrigatório para o tenant.
- `docs/02-arquitetura.md` §5: TOTP nativo do Identity; proprietário pode exigir 2FA para todos ou por perfil.
- `docs/03-modelo-dados.md` → `identity.users` (`totp_enabled`, `totp_secret` cifrado, `recovery_codes` com hash), `identity.tenant_settings` (2FA obrigatório).
- Revisão §2.3, CAs de segurança da FND-05: anti-replay de TOTP; 5 tentativas por desafio; token intermediário só com escopo de 2FA; 2FA obrigatório por padrão para o Proprietário e para quem aprova ou paga.
- Backlog original: login com 2FA pede código; código de recuperação usável uma vez; proprietário obriga 2FA e usuários sem 2FA são levados à configuração no próximo login.

## Critérios de aceite
1. **Dado** um usuário sem 2FA, **quando** inicia a ativação, **então** vê o QR code e a chave manual; o 2FA só fica ativo depois que ele confirma um código válido.
2. **Dado** a ativação confirmada, **quando** termina, **então** o sistema mostra uma única vez os códigos de recuperação, com opção de copiar e baixar, e guarda só o hash deles.
3. **Dado** um usuário com 2FA, **quando** acerta a senha, **então** recebe só um token intermediário com escopo `2fa` (curta duração), que não acessa nenhuma rota além da verificação do código.
4. **Dado** o desafio de 2FA, **quando** informo o código certo, **então** recebo a sessão completa (access + refresh, como na FND-04).
5. **[Segurança]** **Dado** um código TOTP já aceito, **quando** é reapresentado no mesmo intervalo de tempo, **então** é recusado (anti-replay).
6. **[Segurança]** **Dado** um desafio de 2FA, **quando** erro o código 5 vezes, **então** o desafio é invalidado, o usuário volta ao início do login e a falha é auditada.
7. **Dado** um código de recuperação, **quando** o uso no desafio, **então** entro; **quando** tento usar o mesmo código de novo, **então** é recusado.
8. **Dado** a configuração `security.require_2fa` ligada no tenant, **quando** um usuário sem 2FA faz login, **então** é levado à configuração do 2FA e não acessa outra tela até concluir. A tela em que o proprietário liga essa configuração (Configurações → Segurança) fica na FND-06c, que já tem a policy de permissão e o módulo `settings`.
9. **[Segurança]** **Dado** a regra de exigência, **quando** um usuário está num grupo que exige 2FA ou é avaliado por uma regra registrada (`ITwoFactorRequirementRule`), **então** o 2FA é obrigatório para ele. Esta story entrega o mecanismo e a regra "tenant exige para todos"; as regras "Proprietário" e "quem tem `*.approve` ou `*.pay`" são registradas nas FND-06b e FND-06c.
10. **Dado** um usuário com 2FA não obrigatório, **quando** desativa o 2FA, **então** precisa informar a senha e um código válido; se o 2FA for obrigatório para ele, a opção de desativar não aparece e a API recusa.
11. **Dado** a redefinição de senha (FND-04), **quando** conclui, **então** o 2FA continua ativo (já coberto na FND-04, CA 14, e repetido aqui no teste de regressão).

## Regras de negócio e técnicas
- TOTP padrão RFC 6238 (30 s, 6 dígitos), tolerância de ±1 intervalo; último intervalo aceito gravado por usuário para o anti-replay.
- Segredo TOTP cifrado com `SecretKind.TotpSecret` (FND-11), nunca devolvido depois da ativação.
- Token intermediário: JWT com `scope=2fa`, validade curta e vinculado ao desafio (contador de tentativas).
- `tenant_settings`: chave `security.require_2fa` (booleano), auditada.
- Mensagens em PT-BR; o QR code é gerado no servidor ou no cliente sem serviço externo.

## Permissões, eventos e tabelas
- **Tabelas:** `identity.users` (campos de 2FA + `totp_last_step`), `identity.tenant_settings`, tabela de desafios de 2FA (ou cache) com contador.
- **Permissões:** nenhuma nova nesta story. A tela que liga a exigência para todos usa `settings.edit` e fica na FND-06c.
- **Eventos:** `TwoFactorEnabled`, `TwoFactorDisabled` (auditoria).

## Telas e estados
- **Configurar verificação em duas etapas** (perfil do usuário e fluxo obrigatório no login): passo 1 QR e chave manual; passo 2 código de confirmação ("Ativar"); passo 3 códigos de recuperação ("Copiar", "Baixar", "Concluir"). Estados: carregando o QR, código inválido inline, sucesso "Verificação ativada".
- **Desafio no login:** campo de 6 dígitos com `inputmode="numeric"` e preenchimento automático do SO, link "Usar código de recuperação". Estados: erro com tentativas restantes, desafio expirado com ação "Entrar de novo".
- **Configurações → Segurança:** movida para a FND-06c na validação do PO.
- **Ajuda:** `Ajuda/` para Configurar 2FA e Desafio de 2FA.
- **Propositivo:** após aceitar convite (FND-04), oferta direta de configurar o 2FA; se for obrigatório, o fluxo começa sozinho.

## Plano de testes
- **Unidade:** validação TOTP com relógio fixo (`IClock`); anti-replay; regras de exigência combinadas.
- **Integração (Testcontainers):** fluxo completo de ativação; token intermediário barrado em outra rota (CA 3); 5 erros invalidam o desafio (CA 6); código de recuperação de uso único (CA 7); exigência para todos com `tenant_settings` ligado no teste (CA 8); desativação bloqueada quando obrigatória (CA 10).
- **E2E (Playwright):** ativar 2FA com gerador TOTP no teste, sair e entrar com código e com código de recuperação.

## Fora do escopo
- WebAuthn/passkeys e SMS.
- Regras de exigência por grupo e para quem aprova ou paga (registradas nas FND-06b e FND-06c).
- Step-up de 2FA para aprovar OP por link (Fase 2, revisão §3).

## Pontos em aberto
- **Módulo dono de Configurações:** o PRD usa `settings.edit` (R-AJD-8), mas o módulo `settings` não está no catálogo do CLAUDE.md. Confirmar o código e a área (Sistema) na FND-06.
- **Quantidade de códigos de recuperação** (sugestão: 10) e **validade do token intermediário** (sugestão: 5 min): não definidos.
- **"Por perfil"** (02-arquitetura §5) é interpretado como "por grupo" (flag no grupo, FND-06b). Confirmar.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migrations criadas.
- [ ] Permissão registrada no seed: não se aplica (a tela de exigência está na FND-06c).
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda das telas escrita.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada (ativação, desativação, exigência, falhas).
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] TOTP do Identity com segredo cifrado e anti-replay.
- [ ] Códigos de recuperação com hash e uso único.
- [ ] Token intermediário com escopo `2fa` e contador de tentativas.
- [ ] `ITwoFactorRequirementRule` e a regra "tenant exige para todos".
- [ ] Redirecionamento obrigatório no login quando exigido.
- [ ] Telas de configuração e de desafio.

## Quality gates
- Agentes previstos: @dev, cybersecurity, @qa.
- Gate: testes de anti-replay e de limite de tentativas obrigatórios.

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

- **Correções feitas na validação:** Tela Configurações → Segurança movida para a FND-06c (dependia da policy e do módulo `settings`).
- **Pré-condições para `Ready`:** Quantidade de códigos de recuperação e validade do token intermediário.
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story detalhada com os CAs de segurança da revisão | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
- **API (FND-05), 18/10/2026, branch `feat/fnd-05-2fa`.** As pré-condições estão decididas na ADR 0010: 10 códigos de recuperação e desafio de 5 min. O desafio é um token opaco, não um JWT (adendo da ADR 0011).
  - **TOTP:** RFC 6238 (`Totp`), conferido contra os vetores da RFC. O segredo é cifrado com `SecretKind.TotpSecret` e nunca volta para o cliente depois da ativação.
  - **Perfil (`/me/2fa`, autenticado):**
    - `GET` devolve `enabled`, `required` e `recoveryCodesLeft`;
    - `POST /setup` devolve a chave manual e a URI `otpauth://` (o QR é desenhado no cliente);
    - `POST /confirm` ativa com um código válido e devolve os 10 códigos de recuperação uma única vez, já com uma sessão nova (CA 1, 2);
    - `POST /disable` exige senha e código e responde 403 `auth.2fa_required` quando o 2FA é obrigatório (CA 10).
  - **Login:**
    - com 2FA, o login devolve `twoFactor { challenge, purpose: "verify" }`, sem cookie e sem access token (CA 3);
    - `POST /auth/2fa/verify` aceita o código do app ou um código de recuperação e entra (CA 4, 7);
    - código repetido é recusado (CA 5), e 5 erros derrubam o desafio (CA 6).
  - **Exigência:** `ITwoFactorRequirementRule` com a regra `TenantRequiresTwoFactorRule` (`security.require_2fa` em `identity.tenant_settings`). Quando o 2FA é exigido e não está configurado, o login devolve `purpose: "setup"`, e o desafio só libera `/auth/2fa/setup` e `/auth/2fa/confirm` (CA 8, 9).
  - **Aceite de convite:** se o tenant exige 2FA, o aceite devolve o passo de configuração em vez da sessão.
  - **Auditoria:**
    - eventos: `TwoFactorChallenged`, `TwoFactorSetupRequired`, `TwoFactorVerified`, `TwoFactorCodeRejected`, `TwoFactorChallengeFailed`, `RecoveryCodeUsed`, `TwoFactorEnabled`, `TwoFactorDisabled` e `TwoFactorDisableRejected`;
    - a coluna `audit.audit_log.action` foi ampliada para 64 caracteres, sem perda de dados.
  - **Testes:** unidade do TOTP e do Base32, e 6 testes de integração. O CA 11 é a regressão da FND-04 (redefinição mantém o 2FA).
  - **Pendente:** telas (configurar, desafio, perfil), ajuda dessas telas e E2E, depois da FND-09a/b/c.

- **Telas (FND-05), 18/10/2026, branch `feat/fnd-04-telas`.**
  - **Desafio no login** (`/entrar/verificacao`): código com `inputmode=numeric` e `one-time-code`, o link "Usar código de recuperação", as tentativas restantes e "Entrar de novo" quando o desafio cai.
  - **Configuração obrigatória** (`/entrar/configurar-2fa`): sem "Fazer depois".
  - **Perfil** (`/app/conta/seguranca`): ativar, ver os códigos restantes e desativar com senha e código. Quando o 2FA é obrigatório, a opção de desativar não aparece.
  - **Componentes:** `TwoFactorSetup` (QR desenhado no navegador com `qrcode`, mais a chave manual) e `RecoveryCodes` (copiar, baixar e concluir).
  - **Ajuda:** `verificacao-2fa`, `configurar-2fa`, `conta-seguranca`.

## QA Results
_A preencher pelo @qa._