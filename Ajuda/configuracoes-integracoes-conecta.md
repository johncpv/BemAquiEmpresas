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
- **Conectar**: peça o **código de pareamento** a quem administra o Conecta (no Conecta, em Configurações → Integrações → BemAqui Empresas; são 8 letras e números, como ABCD-2345). Digite o código e clique em **Conectar**: o ERP leva você ao Conecta, onde um usuário administrador confirma, e volta para a tela **Autorizar o Conecta**. Lá você confere o que o Conecta poderá fazer e confirma com o código do aplicativo autenticador.
- Ao voltar do Conecta, a página mostra "Conectado à conta … do Conecta" e o próximo passo: abrir uma conversa no Conecta, onde o painel do cliente mostra pedidos e cobranças.
- **Desconectar**: o Conecta para de atender pedidos e cobranças por WhatsApp na hora. Os pedidos já criados continuam no ERP. Desconectar existe só nesta página.
- **Autorizar de novo**: aparece quando a conexão está **Suspensa**, e também quando o Conecta pode ganhar algo novo ("O Conecta pode ganhar: Abrir pedidos de orçamento · Mandar avisos do ERP pelo WhatsApp do Conecta"). Repita o caminho de **Conectar** (o código de pareamento é opcional aqui); a conexão nova substitui a anterior e já nasce com o que faltava, numa vez só. Os pedidos criados pelo Conecta continuam dele depois de reconectar.
- Depois de conectado, o próximo passo é ativar o WhatsApp do Conecta para os avisos de cobrança chegarem ao cliente.
- A referência técnica (para quem integra) fica recolhida em **Para desenvolvedores**: a autorização, a conexão, os escopos e a lista das rotas da API v1 com o escopo de cada uma, gerada do contrato da API.
- **Avisos ao Conecta** (com a permissão de ver o Conecta): mostra "Os avisos estão chegando ao Conecta" ou, quando algum falha, "O Conecta não está recebendo os avisos desde dd/mm", com **Reenviar os parados**. **Avançado** abre o registro detalhado na aba **Enviados** dos eventos.


## O que o Conecta poderá fazer
O mesmo texto da tela de autorizar. Cada item só vale se quem autorizou também puder fazer no ERP:
- Ver produtos e preços
- Ver o saldo em estoque
- Ver clientes (nome, telefone, e-mail, documento)
- Cadastrar clientes
- Completar o contato de clientes (o documento nunca muda)
- Ver pedidos de venda
- Criar pedidos de venda
- Alterar pedidos de venda em aberto
- Ver cobranças
- Ver notas fiscais
- Ver projetos e a etapa em que estão
- Abrir pedidos de orçamento
- Mandar avisos do ERP pelo WhatsApp do Conecta

Na prática: o Conecta consulta produtos e o disponível, acha o cliente pelo telefone da conversa (o documento sai mascarado; pela busca do CPF ou CNPJ, só o primeiro nome e as iniciais), cadastra cliente novo, cria o pedido como **rascunho** ("Pedido do Conecta: confirme com o cliente"), confirma, gera a cobrança e mostra o boleto e a nota. Ele nunca troca um e-mail, telefone ou endereço já preenchido, nunca muda o documento e nunca cancela pedido pago: isso é feito no ERP, por uma pessoa. Quando alguém troca o e-mail ou o telefone de um cliente no ERP, o contato anterior recebe um aviso da troca.

As buscas de cliente por CPF, CNPJ ou telefone têm limite por minuto e um teto por dia; cada busca fica no histórico, sem o número buscado. Se o Conecta consultar muitos documentos diferentes em uma hora, os Proprietários recebem um aviso.

## Situações
- **Conectado**: o Conecta usa o ERP em nome de quem autorizou, só com o que essa pessoa também pode fazer.
- **Suspensa**: quem autorizou perdeu o acesso, ou a autorização venceu (vale 12 meses; o aviso sai 30 dias antes). Autorize de novo.
- **Desconectada por segurança**: o Conecta usou uma credencial antiga, o que pode indicar vazamento. Conecte de novo.
- **Desconectada**: alguém desconectou aqui, ou o Conecta desconectou do lado dele.

## Perguntas frequentes
**Quem pode conectar e desconectar?** Só os membros do Proprietário, com a verificação em duas etapas ativa. Quem vê as Configurações enxerga a situação.

**O Conecta pode aprovar, pagar ou mudar usuários?** Não. Aprovar, pagar, ver dado sensível de RH, mudar usuários e mudar configurações nunca são concedidos a um aplicativo.

**E se a minha permissão mudar?** O que o Conecta pode fazer muda junto, já na próxima chamada. Se a permissão voltar, ele volta a poder.

**O que aparece no histórico?** As alterações feitas pelo Conecta aparecem como "Integração Conecta (autorizada por Fulano)" e, quando o Conecta informa quem atendeu, "· atendente Ana". O nome do atendente não dá permissão nenhuma: é só para a trilha.

**Telefone ou e-mail que veio do Conecta vale como prova de quem é o cliente?** Não. O contato gravado pelo Conecta fica marcado "Informado pelo Conecta" no cadastro até uma pessoa clicar em **Confirmar contato**. Quando o atendente precisa confirmar quem é o cliente, o ERP manda um código de 6 dígitos ao e-mail que já estava no cadastro (nunca a um que veio do Conecta); o código vale 10 minutos e aceita 5 tentativas. O pedido e a conferência ficam no histórico do cliente, sem o código.

**O 2FA de quem autorizou foi zerado. E agora?** A conexão continua, mas os Proprietários recebem o aviso. Confira se foi esperado; se não, desconecte.
