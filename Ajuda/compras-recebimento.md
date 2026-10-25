---
titulo: Recebimento — itens
rota: /app/compras/recebimentos/$id
aba: itens
padrao: true
modulo: purchasing
---

## Para que serve
Consultar o que entrou no estoque com este recebimento e, se foi lançado errado, estornar.

## Como usar
- O cabeçalho mostra o número, a situação, o pedido e o fornecedor. A faixa de fatos traz o total, a data, os itens e quem recebeu.
- A aba **Itens** mostra cada item com a quantidade, o lote, o local, o **custo de entrada** e o link para o movimento de estoque.
- **Estornar recebimento** (permissão Aprovar em Compras) pede o motivo. Os títulos a pagar do recebimento são cancelados antes, o material sai do estoque e o saldo volta ao previsto do pedido.
- A marca **Recebido pelo Proprietário único** aparece quando quem pediu ou aprovou recebeu porque não havia outra pessoa para receber.

## Perguntas frequentes
**Não consigo estornar.** Título em ordem de pagamento ou pago impede o estorno: retire o título da OP antes. Sem saldo do item no local (o material já foi usado), o estorno também é recusado.