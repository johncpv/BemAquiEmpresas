---
titulo: Pedido de compra — documentos
rota: /app/compras/pedidos/$id
aba: documentos
padrao: false
modulo: purchasing
---

## Para que serve
Mandar o pedido aprovado ao fornecedor em PDF, acompanhar cada envio e guardar a proposta do fornecedor e outros documentos do pedido.

## Como usar
- **Enviar ao fornecedor** (pedido aprovado, permissão Editar em Compras) abre o envio já preenchido: o destinatário é o contato do fornecedor que recebe cobrança (ou o primeiro com e-mail), o assunto é "Pedido de compra {número} — {empresa}" e vai um PDF novo do pedido, anexo. Coloque até 3 endereços em **Cópia** e, se quiser, uma mensagem curta (até 1.000 caracteres, texto puro). Clique em **Enviar ao fornecedor**: a mensagem "Pedido enviado para a fila de e-mail" confirma, e o pedido passa a **Enviado**.
- Endereço que não está nos contatos do fornecedor mostra o aviso **Este e-mail não está nos contatos de {fornecedor}.** Confira e marque **Confirmo o destinatário** para enviar.
- **Reenviar** gera outro PDF e outro envio. Use quando o fornecedor não recebeu, quando o envio falhou ou quando o pedido foi alterado depois do envio e reaprovado (a faixa "Alterado depois do envio" some no reenvio).
- Cada envio aparece num cartão com o destinatário, quem mandou, quando, o PDF enviado e a situação do e-mail: **Na fila**, **Aguardando verificação do anexo** (o PDF passa pelo antivírus antes de sair), **Enviado** ou **Falhou**, com o motivo e o botão **Reenviar**.
- **Baixar PDF**, no topo do pedido, gera o PDF na hora sem mandar nada.
- **Anexar documento** aceita PDF, PNG ou JPG até 10 MB. Quem pode editar o pedido anexa; quem vê o pedido baixa. O arquivo passa por verificação de vírus antes de ficar disponível: enquanto isso, aparece **Em verificação**.

## Perguntas frequentes
**O que vai no PDF?** Os dados e o logo da empresa, o número e a data, o fornecedor e o CNPJ, a condição de pagamento, a previsão e o local de entrega (o endereço da empresa), os itens com o código e a unidade do fornecedor quando cadastrados, os preços, o frete, o IPI, o total e a observação. Não vai custo médio, margem nem o projeto.

**O e-mail tem link para o sistema?** Não. O fornecedor recebe só o PDF anexo e a sua mensagem. As respostas dele vão para o seu e-mail.

**Apareceu "Limite diário de envios atingido". E agora?** A empresa pode mandar até 50 orçamentos e pedidos por e-mail por dia, e cada usuário tem o próprio limite (em Configurações → Parâmetros). Clique em **Baixar PDF** e mande por fora, ou envie amanhã.

**Por que vejo o e-mail com asteriscos?** Endereço de contato é dado pessoal: sem a permissão de ver Terceiros, ele aparece mascarado. O histórico de alterações também mostra os endereços mascarados.

**O documento anexado vai para o fornecedor?** Não. Só o PDF do pedido vai no envio; o anexo fica no sistema.
