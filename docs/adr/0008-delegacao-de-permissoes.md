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

## Consequências
- A FND-06b implementa a regra de delegação e a FND-06c implementa a versão de permissões.
- A FND-04 implementa as claims e o security stamp.
