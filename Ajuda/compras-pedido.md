---
titulo: Pedido de compra — itens
rota: /app/compras/pedidos/$id
aba: itens
padrao: true
modulo: purchasing
---

## Para que serve
Ver o pedido inteiro e fazer o próximo passo: enviar para aprovação, aprovar, reprovar, editar ou cancelar.

## Como usar
- O cabeçalho mostra o número, a situação, o fornecedor e o destino. A faixa de fatos traz o total, a previsão de entrega, a condição de pagamento e o que já foi recebido.
- A aba **Itens** lista cada item com a quantidade, o preço unitário e o total da linha, e as solicitações que ele atende. No celular, cada item vira um cartão.
- **Enviar para aprovação** resolve a alçada pelo **valor de referência**: o maior entre o total e a soma dos pedidos do mesmo fornecedor nos últimos 30 dias. Sem aprovador possível, o sistema recusa e diz o motivo; ajuste as alçadas em Financeiro → Alçadas.
- **Aprovar pedido** abre o impacto no caixa: as parcelas, as datas estimadas e, para quem vê o Financeiro, o saldo projetado no dia de cada parcela. Aparecem também o aviso de fracionamento e o aviso **1 cotação**, que não bloqueiam. No último nível, a mensagem é "Pedido aprovado. Previsão lançada no caixa.".
- **Reprovar pedido** pede o motivo e volta o pedido para rascunho. Quem pediu recebe o aviso.
- **Cancelar pedido** pede o motivo e mostra quantos títulos previstos saem do caixa. Pedido com recebimento não é cancelado.

## Perguntas frequentes
**Por que não consigo aprovar?** O pedido não está na sua alçada, você já aprovou outro nível dele ou foi você quem pediu. A mensagem na faixa diz qual é o caso.

**O pedido para um projeto já entra no custo do projeto?** Entra em **Comprometido**, não no realizado. O material vira custo realizado quando sai do estoque para a OS.
