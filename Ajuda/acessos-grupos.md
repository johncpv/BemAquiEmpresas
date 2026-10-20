---
titulo: Grupos de acesso
rota: /app/acessos/grupos
aba: grupos
padrao: true
modulo: identity
---

## Para que serve
Definir o que cada grupo de pessoas pode ver e fazer no sistema. O usuário recebe as permissões dos grupos em que está; em mais de um grupo, vale a soma.

## Como usar
- Escolha um grupo na lista. A matriz mostra, para cada módulo, a chave **Habilitado** e as ações (Ver, Criar, Editar, Aprovar, Excluir e as específicas, como "Ver dados sensíveis").
- Ligar **Habilitado** marca **Ver**. Marcar qualquer outra ação também marca **Ver**. Desmarcar **Ver** limpa o módulo.
- Passe o mouse em **Aprovar** para ver o que aprovar significa naquele módulo.
- Clique em **Salvar grupo**. As permissões valem no próximo acesso dos membros.
- **Novo grupo** pode começar de um grupo parecido. **Duplicar** copia as permissões sem os membros. **Inativar** tira as permissões do grupo de todos os membros.

## Como a permissão é calculada
1. O módulo precisa estar liberado na versão contratada (módulo não liberado aparece como "Não liberado").
2. O módulo precisa estar habilitado em algum grupo do usuário.
3. Cada ação é marcada no grupo.

## Perguntas frequentes
**O que é "Ver custo e margem"?** Em Orçamentos e em Projetos, mostra custo, composição, markup e margem. Sem ela, a pessoa vê o preço, mas não o custo. Vem marcada no Comercial e no Financeiro.

**Por que não consigo marcar uma ação?** Ninguém concede uma permissão que não tem. Aprovar, pagar, ver custo e margem, ver dados sensíveis e editar acessos ou configurações só o grupo Proprietário concede.

**Posso editar o grupo Proprietário?** Não. Ele é do sistema, tem acesso total ao que está liberado e precisa ter ao menos um membro.

**Não consigo alterar um grupo do qual faço parte.** É uma proteção: peça a alguém do grupo Proprietário.
