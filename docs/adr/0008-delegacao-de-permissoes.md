# ADR 0008 — Delegação de permissões e identidade no token

- Status: Aceito (16/10/2026)
- Complementa as ADRs 0002 e 0003.

## Decisão
- **Ninguém concede o que não tem.** Um usuário só marca, num grupo, ações que ele mesmo possui, e só adiciona membros a grupos cujas permissões estão todas contidas nas dele.
- **Ações reservadas ao Proprietário:** só um membro do Proprietário pode:
  - adicionar alguém ao grupo Proprietário;
  - conceder `*.approve`, `finance.pay`, `hr.view_sensitive`, `identity.edit` e `settings.edit`.
- **Grupos próprios:** um usuário não altera os grupos de que é membro, a menos que seja Proprietário.
- **Convites:** os `group_ids` do convite são validados duas vezes, na criação e no aceite, com as mesmas regras.
- **Sem mass assignment:** nenhum endpoint aceita `is_system`, `group_ids` ou ações por atribuição em massa. Cada operação tem o seu comando.
- **Auditoria:** toda mudança de grupo, permissão ou vínculo é auditada e notificada aos membros do Proprietário.
- **O JWT carrega só identidade:** `sub`, `tid` (tenant), `aud` (host primário do tenant), `sstamp` (security stamp) e `amr`. Não carrega permissões.
- **Permissões efetivas:**
  - vêm de cache por `{tenantId}:{userId}`, com versão;
  - a versão é incrementada quando muda um grupo, um vínculo ou a liberação de módulo do tenant;
  - a versão é conferida a cada request.
- **Security stamp:** é conferido a cada request via cache. Desativar o usuário, trocar a senha ou resetar o 2FA invalida o stamp no próximo request.

## Adendo (FND-06b, 18/10/2026)
- Inativar grupo exige `identity.edit`: inativar é edição, porque grupos não são excluídos.
- Adicionar membro a qualquer grupo exige que o ator tenha todas as permissões do grupo. Se houver ação reservada no grupo, só o Proprietário adiciona.
- **Cache de permissões (FND-06c):**
  - a versão é `users.perm_version`, lida a cada request;
  - a chave do cache é `{tenantId}:{userId}:{versão}`;
  - a versão sobe quando grupo, membros, inativação ou situação do usuário mudam;
  - os módulos liberados são aplicados a cada chamada e entram na versão enviada ao front.
- **Cabeçalho `X-Perm-Version`:** os endpoints com `perm:` e o `/me/permissions` devolvem a versão. Quando ela muda, o front recarrega `/me/permissions` sem pedir novo login.

## Consequências
- A FND-06b implementa a regra de delegação e a FND-06c implementa a versão de permissões.
- A FND-04 implementa as claims e o security stamp.

- Ver ADR 0016 (seed patch: concessões acrescentadas pelo sistema aos grupos do seed, fora da delegação).

## Adendo (20/10/2026, decisão D5 do PO na revisão pré-Fase 2): quem paga e quem aprova
- **`finance.pay` para o Financeiro por seed patch.** O patch `finance-pay-financeiro` (ADR 0016) acrescenta `finance.pay` ao grupo Financeiro do seed (`seed_key = finance`), em todo tenant novo e nos existentes no próximo deploy. Entra com a FIN-03a, a primeira story que registra pagamento.
  - O patch é aplicado pelo sistema, fora da regra de delegação, e não muda a regra: fora do seed, só um membro do Proprietário concede `finance.pay`.
- **`finance.approve` continua só no Proprietário na Fase 2.** Quem aprova a ordem não é quem registra o pagamento.
  - A FIN-03b (alçadas) vai para o fim da fase. A delegação da aprovação a outro grupo fica para quando a empresa tiver mais de um aprovador; até lá, a regra padrão "Proprietário aprova tudo" vale.
  - O texto do protótipo "até R$ 5.000 o financeiro aprova" é só ilustrativo.
- **2FA:** continua exigido no login de quem tem `*.approve` ou `*.pay` (FND-06c CA 12). A exigência de 2FA a cada ação (aprovar, pagar, estornar, transferir) foi adiada pelo PO (P-19 em `06-pendencias-riscos.md`).

- Ver ADR 0019 (custo e margem com `quotes.view_cost` e `projects.view_cost`, que não são ações reservadas).

## Adendo 21/10/2026 (revisão pré-Fase 3, decisões D5 e D13 do PO)
- **`engineering.approve` para o grupo Engenharia por seed patch** (`engineering-release-bom`, ENG-02): libera a BOM. O patch é aplicado pelo sistema, fora da regra de delegação, como o `finance-pay-financeiro`; fora do seed, só um membro do Proprietário concede `engineering.approve`. Quem tem `engineering.approve` passa a ter 2FA obrigatório no login (FND-06c CA 12).
- **`amr` no access token:** o token continua só com identidade (`sub`, `tid`, `aud`, `sstamp`) e ganha `amr` (`pwd`, `otp`, `pin`). A sessão aberta por PIN (`amr = ["pin"]`) só alcança os endpoints da lista permitida (ADR 0026 §4); a permissão efetiva continua vindo do cache versionado, e o filtro do Host recusa o resto com 403 `pin_session_scope`.
- **Perfil sensível e PIN:** ganhar `*.approve`, `finance.pay`, `hr.view_sensitive`, `identity.edit` ou `settings.edit` apaga o PIN do usuário, conferido a cada login por PIN; exceção da `production.approve_quality` (ADR 0003, adendo de 21/10/2026).
