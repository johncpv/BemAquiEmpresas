---
titulo: Recebimento — financeiro
rota: /app/compras/recebimentos/$id
aba: financeiro
padrao: false
modulo: purchasing
---

## Para que serve
Conferir os títulos a pagar que o recebimento criou. A aba aparece para quem vê o Financeiro.

## Como usar
- Nasce um título em aberto por parcela da condição do pedido, com o vencimento a partir da data do recebimento e a descrição "Recebimento {número} do pedido {número} — parcela {n}/{m}".
- O valor é o dos itens mais o frete e o IPI deste recebimento. A soma das parcelas fecha com o total.
- Clique no número do título para conferir no Financeiro. O link **Ver OP** aparece quando o título já está numa ordem de pagamento.

## Perguntas frequentes
**E o previsto do pedido?** Ele cai para o saldo que ainda falta receber. Com o pedido todo recebido, o previsto é cancelado.