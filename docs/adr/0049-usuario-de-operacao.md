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

## Adendo 18/11/2026 (PDV-01, parte 2): como foi implementado
- **E-mail técnico no lugar do nulo** (recomendação aceita): a coluna `identity.users.email` continua obrigatória e única, como na anonimização (LGPD-01b), e o usuário de operação guarda `operacao.{id}@operacao.invalid` (domínio reservado da RFC 2606). Ele nunca é exibido (a lista de usuários mostra "Sem e-mail · entra por PIN no caixa"), não recupera senha, não recebe e-mail (`IUserDirectory.EmailOfAsync` devolve nulo) e o login por e-mail o recusa com a resposta e o custo de e-mail inexistente. Tornar a coluna anulável mudaria cerca de 170 usos de `User.Email` sem ganho.
- **Lojas:** `identity.users.operation_store_ids` (`uuid[]`, ids de `pos.stores` sem FK). O PIN só vale em caixa registrado dessas lojas; tirar a loja derruba a sessão no próximo request.
- **PIN:** `identity.pos_pins`, um por usuário (não por aparelho: o caixa troca de aparelho e o PIN do operador não, e o mesmo PIN vale sem rede, PP-39). PBKDF2 do `PasswordHasher`, como o PIN da fábrica; `must_change` no PIN inicial e no "Novo PIN" do gerente.
- **Quem mantém:** criar, trocar as lojas e dar um PIN novo exigem `identity.create` (o gerente que criou mantém); desativar e reativar seguem `identity.edit`, como qualquer usuário. Reativado, volta sem PIN. Só no perfil Comércio (recurso `identity.operation_users`) e com o `pos` liberado.
- **Sem ação reservada:** a lista é a do PIN (ADR 0026 §2: `*.approve`, `finance.pay`, `hr.view_sensitive`, `hr.view_health`, `identity.edit`, `settings.edit` e o Proprietário). Conferida ao criar, ao trocar os grupos do usuário, ao pôr membros no grupo e ao salvar a matriz de um grupo que tem usuário de operação; e, como trava final, a cada entrada por PIN e a cada request da sessão.
- **Nunca token de retaguarda:** o `AuthService` recusa emitir sessão completa, sem persistência, "2FA de novo" ou PIN da fábrica para o usuário de operação, e a renovação de uma família que não seja `PosPin` dele é recusada.
