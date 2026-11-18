# ADR 0049: Usuário de operação (sem e-mail, só PIN em aparelho da loja)

- Status: Aceito (18/11/2026). Decisão do PO PP-12 (18/11/2026), que substitui para o varejo a solução de apelido de e-mail da P-24 (ADR 0026 §5).
- Contexto: o operador de caixa, o garçom e a cozinha trocam muito, muitas vezes não têm e-mail da empresa, e o apelido de e-mail (solução da fábrica, ADR 0026 §5) não escala no varejo. A ADR 0026 §5 já previa que "usuário sem e-mail" só viria com ADR nova.

## Decisão
- **Usuário de operação** é um usuário do tenant marcado `is_operation_user`, **sem e-mail e sem senha**. É criado pelo gerente (quem tem `identity.create`) com nome, os grupos e a loja (ou lojas) onde trabalha, e já recebe o PIN inicial, que ele troca no primeiro uso.
- **Só entra por PIN em aparelho registrado da loja** (aparelho do tipo Caixa, ADR 0048). Não existe login por e-mail, convite, recuperação de senha nem 2FA para ele; o PIN segue as regras da ADR 0026 §2 (hash forte, 5 erros bloqueiam 15 min, 10 em 24 h apagam, limite por aparelho).
- **Não acessa a retaguarda:** a sessão dele é sempre a sessão por PIN, que só alcança a lista `[PinSessionAllowed]` do PDV. Ele nunca recebe ação sensível (`*.approve`, `finance.pay`, `hr.view_sensitive`, `hr.view_health`, `identity.edit`, `settings.edit`): o grupo que tiver alguma delas não aceita usuário de operação como membro.
- **Supervisor de balcão** com PIN (ADR 0048 §5, PP-11) é sempre um usuário completo (com e-mail); usuário de operação não aprova.
- **Ciclo de vida:** desativar o usuário de operação ou desligar o funcionário ligado a ele apaga os PINs e derruba as sessões por PIN, como no usuário completo. A troca de um usuário de operação para completo (ganhar e-mail) é um convite normal, que tira a marca.
- **LGPD:** o usuário de operação tem só nome e, opcionalmente, o vínculo com o funcionário; nenhum dado de contato.
- Tudo auditado: criação, PIN inicial (sem o valor), troca de grupo e de loja.

## Consequências
- PDV-01 (segunda parte, CA 12) implementa a marca, a criação pelo gerente e o bloqueio da retaguarda; o PIN no aparelho de caixa (CA 11) vem junto.
- `docs/pdv/03-modelo-dados.md`: `identity.users` ganha `is_operation_user` (e o e-mail passa a aceitar nulo só para esse usuário).
- A P-24 (operador da fábrica sem e-mail) pode reaproveitar esta ADR no futuro, com decisão própria.
