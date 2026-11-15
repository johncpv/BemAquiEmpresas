---
titulo: Usuários
rota: /app/acessos/usuarios
aba: usuarios
padrao: true
modulo: identity
---

## Para que serve
Convidar as pessoas da equipe, escolher os grupos de cada uma, ver quem é funcionário e quem é externo e desativar quem saiu.

## Como usar
- **Convidar usuário:** informe nome, e-mail e grupos. Os grupos do último convite já vêm marcados. A pessoa recebe um e-mail com o link, que vale por 72 horas.
- **Funcionário** (no convite, opcional): escolha o funcionário para ligar o login a ele já no convite. O nome vem preenchido e, para quem vê dados sensíveis do RH, o e-mail pessoal também. Só aparecem funcionários ativos que ainda não têm usuário; depois do convite, o funcionário fica reservado para este login. Deixe em branco para quem não é funcionário, como a contadora. O campo só aparece para quem também edita funcionários no RH.
- Na ficha do funcionário, **Convidar como usuário** abre este mesmo convite com o funcionário já escolhido.
- A coluna **Funcionário** mostra a quem o login está ligado (o nome leva à ficha, para quem vê o RH) ou **Externo** quando não há vínculo.
- **Mostrar:** filtre por **Sem funcionário** (logins externos) ou **Sem grupo** (pessoas que ainda não veem nada no sistema).
- **Editar grupos:** pelo menu da linha. A mudança vale no próximo acesso.
- **Reenviar convite** gera um link novo e invalida o anterior. **Cancelar convite** invalida o link.
- **Desativar** tira o acesso na hora, mas mantém o nome da pessoa no histórico. **Reativar** devolve o acesso com os mesmos grupos.

## Perguntas frequentes
**Apareceu "Já existe um usuário com esse e-mail".** Clique em **Abrir o usuário** para ajustar os grupos dele.

**Apareceu "Este funcionário já tem um usuário vinculado".** O funcionário já tem login (ou um convite pendente). Abra a ficha dele no RH para ver ou desvincular.

**O login de quem foi desligado some?** Ao registrar o desligamento no RH, o login ligado ao funcionário é desativado sozinho (a pessoa sai na hora, inclusive do chão de fábrica). Ele continua na lista como Inativo. A exceção é o último membro ativo do grupo Proprietário, que nunca é desativado.

**O que é "Obrigatória pendente" na verificação em duas etapas?** A pessoa precisa de 2FA (é do Proprietário, aprova ou paga, ou a empresa exige) e ainda não configurou. Ela será levada à configuração no próximo acesso.
