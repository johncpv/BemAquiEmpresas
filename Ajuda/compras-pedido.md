---
titulo: Pedido de compra — itens
rota: /app/compras/pedidos/$id
aba: itens
padrao: true
modulo: purchasing
---

## Para que serve
Ver o pedido inteiro e fazer o próximo passo: enviar para aprovação, aprovar, reprovar, editar, cancelar, mandar ao fornecedor, baixar o PDF, registrar o recebimento, corrigir o preço ou encerrar o saldo.

## Como usar
- O cabeçalho mostra o número, a situação, o fornecedor e o destino. A faixa de fatos traz o total, a previsão de entrega, a condição de pagamento e o que já foi recebido.
- A aba **Itens** lista cada item com a quantidade, o preço unitário e o total da linha, e as solicitações que ele atende. No celular, cada item vira um cartão.
- **Enviar para aprovação** resolve a alçada pelo **valor de referência**: o maior entre o total e a soma dos pedidos do mesmo fornecedor nos últimos 30 dias. Sem aprovador possível, o sistema recusa e diz o motivo; ajuste as alçadas em Financeiro → Alçadas.
- **Aprovar pedido** abre o impacto no caixa: as parcelas, as datas estimadas e, para quem vê o Financeiro, o saldo projetado no dia de cada parcela. Aparecem também o aviso de fracionamento e o aviso **1 cotação**, que não bloqueiam. No último nível, a mensagem é "Pedido aprovado. Previsão lançada no caixa.".
- **Reprovar pedido** pede o motivo e volta o pedido para rascunho. Quem pediu recebe o aviso.
- **Cancelar pedido** pede o motivo e mostra quantos títulos previstos saem do caixa. Pedido com recebimento não é cancelado: encerre o saldo.
- **Registrar recebimento** abre a entrada do material que chegou. O botão fica desabilitado, com o motivo ao lado, para quem pediu ou aprovou o pedido e enquanto o pedido está em reaprovação.
- **Corrigir preço** (na linha, permissão Aprovar em Compras) pede o preço novo e o motivo. Ele vale só para o saldo a receber; acima do valor aprovado mais a tolerância, o pedido volta para a aprovação.
- **Enviar ao fornecedor** aparece no pedido aprovado (e vira o primeiro próximo passo depois da aprovação). Ele manda o PDF do pedido por e-mail ao contato do fornecedor e o pedido passa a **Enviado**. Veja a aba **Documentos**.
- **Baixar PDF** gera o PDF do pedido na hora, em qualquer situação, sem mandar nada (antes da aprovação, com a marca **Rascunho**).
- A faixa **Alterado depois do envio: reenvie ao fornecedor.** aparece quando o pedido mudou depois de enviado. Depois de reaprovado, clique em **Reenviar** na faixa: o fornecedor recebe o PDF novo e a faixa some.
- **Encerrar saldo do pedido** aparece no pedido parcialmente recebido: o pedido fica recebido com a marca **Encerrado com saldo de {valor}**.

## Imprimir e exportar
- **Baixar PDF** e **Imprimir** ficam no cabeçalho em toda situação. O PDF é gerado na hora e não fica guardado (o guardado é o do envio ao fornecedor).
- Em rascunho ou aguardando aprovação, o PDF sai com a marca **Rascunho** e a faixa "Pedido não aprovado: não envie ao fornecedor.", para conferir. Cancelado, sai com a marca **Cancelado**. Do aprovado em diante, sem marca.
- **Enviar ao fornecedor** continua só no pedido aprovado. O PDF que vai no envio nunca tem marca.

## Perguntas frequentes
**Por que não consigo aprovar?** O pedido não está na sua alçada, você já aprovou outro nível dele ou foi você quem pediu. A mensagem na faixa diz qual é o caso.

**O pedido para um projeto já entra no custo do projeto?** Entra em **Comprometido**, não no realizado. O material vira custo realizado quando sai do estoque para a OS.
