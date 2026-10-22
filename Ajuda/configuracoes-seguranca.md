---
titulo: Segurança
rota: /app/configuracoes/seguranca
aba: seguranca
padrao: true
modulo: settings
---

## Para que serve
Exigir a verificação em duas etapas de todos os usuários da empresa e cuidar dos aparelhos da fábrica, os tablets liberados para a entrada por PIN no app do operador.

## Como usar
- Clique em **Exigir** e confirme. Quem ainda não tem a verificação será levado à configuração no próximo acesso.
- Para voltar atrás, clique em **Deixar de exigir**.

## Aparelhos da fábrica
- A lista mostra cada tablet registrado, quem registrou, o último uso, quantos PINs ele tem e até quando o registro vale (180 dias).
- Registrar é no próprio tablet: abra o app do operador, entre com e-mail e senha e toque em **Aparelho** → **Registrar este aparelho**. É preciso editar a produção e apontar no app do operador.
- **Revogar aparelho** (com permissão de editar as configurações ou de aprovar na produção): o PIN deixa de funcionar nele e quem estiver entrando por PIN sai na próxima ação. O Proprietário recebe o aviso.
- Perto dos 30 dias sem uso, a linha mostra "Sem uso há {n} dias". Com 30 dias sem uso, o aparelho é revogado sozinho.
- O limite é de 10 aparelhos ativos. Para registrar outro, revogue um que não é mais usado.

## Perguntas frequentes
**Mesmo sem exigir, alguém precisa de 2FA?** Sim: o grupo Proprietário e quem aprova ou paga sempre precisam.

**Quem pode usar PIN?** Quem aponta no app do operador. Quem aprova, paga, vê dados sensíveis do RH ou mexe em usuários e configurações entra sempre com e-mail e senha; a exceção é quem só aprova ou reprova a qualidade.

**Um tablet foi perdido. O que faço?** Clique em **Revogar aparelho** na linha dele. A entrada por PIN nele acaba na hora.