---
titulo: Conecta
rota: /app/configuracoes/integracoes/conecta
aba:
padrao: true
modulo: conecta
---

## Para que serve
Ligar o ERP ao Conecta com um clique, para o atendimento pelo WhatsApp vender, cobrar e avisar o cliente usando os dados do ERP, sem ninguém copiar chave nenhuma. A página mostra a situação da conexão, o que o Conecta poderá fazer e quando foi usado pela última vez.

## Como usar
- **Conectar**: o ERP leva você ao Conecta, que pede o login ou a escolha da conta e volta para a tela **Autorizar o Conecta**. Lá você confere o que o Conecta poderá fazer e confirma com o código do aplicativo autenticador.
- **Desconectar**: o Conecta para de atender pedidos e cobranças por WhatsApp na hora. Os pedidos já criados continuam no ERP. Desconectar existe só nesta página.
- **Autorizar de novo**: aparece quando a conexão está **Suspensa**. Repita o caminho de **Conectar**; a conexão nova substitui a anterior.
- Depois de conectado, o próximo passo é ativar o WhatsApp do Conecta para os avisos de cobrança chegarem ao cliente.
- A referência técnica (para quem integra) fica recolhida em **Para desenvolvedores**.

## Situações
- **Conectado**: o Conecta usa o ERP em nome de quem autorizou, só com o que essa pessoa também pode fazer.
- **Suspensa**: quem autorizou perdeu o acesso, ou a autorização venceu (vale 12 meses; o aviso sai 30 dias antes). Autorize de novo.
- **Desconectada por segurança**: o Conecta usou uma credencial antiga, o que pode indicar vazamento. Conecte de novo.
- **Desconectada**: alguém desconectou aqui, ou o Conecta desconectou do lado dele.

## Perguntas frequentes
**Quem pode conectar e desconectar?** Só os membros do Proprietário, com a verificação em duas etapas ativa. Quem vê as Configurações enxerga a situação.

**O Conecta pode aprovar, pagar ou mudar usuários?** Não. Aprovar, pagar, ver dado sensível de RH, mudar usuários e mudar configurações nunca são concedidos a um aplicativo.

**E se a minha permissão mudar?** O que o Conecta pode fazer muda junto, já na próxima chamada. Se a permissão voltar, ele volta a poder.

**O que aparece no histórico?** As alterações feitas pelo Conecta aparecem como "Integração Conecta (autorizada por Fulano)".

**O 2FA de quem autorizou foi zerado. E agora?** A conexão continua, mas os Proprietários recebem o aviso. Confira se foi esperado; se não, desconecte.
