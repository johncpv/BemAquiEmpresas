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

## Imprimir e exportar
- **Baixar PDF** e **Imprimir**, no cabeçalho, geram o **comprovante de recebimento** para o almoxarifado: número, data, pedido, fornecedor, a nota do fornecedor (quando o recebimento foi pela NF-e), o projeto ou **Estoque geral**, quem recebeu e os itens com código, descrição, quantidade recebida, unidade, lote e local.
- No fim vêm as assinaturas **Recebido por** e **Conferido por**. A marca **Recebido pelo Proprietário único** sai no papel quando aparece na tela.
- O comprovante **nunca** traz custo de entrada, preço nem valor, para ninguém: o custo continua só na tela.
- Recebimento estornado sai com a marca **Cancelado** e o motivo do estorno.
- Cada download fica registrado na auditoria.

## Perguntas frequentes
**Não consigo estornar.** Título em ordem de pagamento ou pago impede o estorno: retire o título da OP antes. Sem saldo do item no local (o material já foi usado), o estorno também é recusado.