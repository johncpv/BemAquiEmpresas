---
titulo: Usuários — usuário de operação
rota: /app/acessos/usuarios
aba: operacao
padrao: false
modulo: identity
recurso: identity.operation_users
---

## Para que serve
Cadastrar quem só usa o caixa da loja (operador de caixa, por exemplo) sem precisar de e-mail. O usuário de operação entra só por PIN, nos caixas registrados das lojas dele, e nunca acessa a retaguarda.

## Como usar
- **Novo usuário de operação:** informe o nome, o grupo (por exemplo, Operador de caixa), as lojas onde a pessoa trabalha e um **PIN inicial** de 6 números. Passe o PIN para a pessoa: na primeira entrada no caixa, ela troca por um PIN só dela.
- Na lista, ele aparece com a marca **Operação** e "Sem e-mail · entra por PIN no caixa".
- **Lojas:** troque as lojas em que o PIN vale. Se a pessoa estiver num caixa de uma loja que saiu, a sessão cai.
- **Novo PIN:** quando a pessoa esqueceu o PIN ou ele foi apagado por erros. O PIN anterior deixa de valer e a sessão no caixa cai; na próxima entrada, ela troca o PIN novo por um só dela.
- **Desativar** (ou registrar o desligamento do funcionário ligado) apaga o PIN e tira a pessoa do caixa na hora. Reativado, ele volta sem PIN: dê um **Novo PIN**.

## Perguntas frequentes
**Por que o grupo foi recusado?** Usuário de operação nunca tem ação reservada (aprovar, pagar, ver dado sensível, editar usuários ou configurações). Escolha um grupo sem essas ações, como Operador de caixa.

**Ele pode entrar com e-mail e senha?** Não. Ele não tem e-mail nem senha; só o PIN no caixa. Se precisar da retaguarda, convide a pessoa como usuário comum.

**Quem cria o usuário de operação?** Quem pode convidar usuários. O PIN nunca aparece na tela depois de salvo nem fica no histórico.
