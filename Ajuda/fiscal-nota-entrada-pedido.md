---
titulo: Nota de entrada — pedido e divergências
rota: /app/fiscal/entrada/$id
aba: pedido
padrao: false
modulo: fiscal
---

## Para que serve
Liga a nota ao pedido de compra e mostra, item por item, o que o pedido tem a receber e o que a nota faturou: quantidade e preço em unidade de estoque, com a diferença em quantidade, valor e percentual. O frete e o IPI da nota também são comparados com os do pedido.

## Como usar
1. Confirme o **pedido sugerido** (o citado na nota ou o com mais itens em comum) ou escolha outro pedido aprovado do fornecedor.
2. Veja as diferenças: acima do saldo ou do preço do pedido aparece em vermelho (crítico); abaixo, em âmbar (alerta). O texto da diferença aparece ao lado.
3. Se a compra não teve pedido, marque **Sem pedido**: a conferência fica com quem aprova compras, com o motivo.
4. Clique em **Conferir nota**. Se a nota não serve, clique em **Recusar nota** e informe o motivo.

## Perguntas frequentes
**O preço da nota é diferente do pedido.** O recebimento usa o preço do pedido até alguém com a permissão de aprovar compras corrigir o preço no pedido.

**Não vejo os valores do pedido.** Ver os valores do pedido pede a permissão de ver Compras. O vínculo aparece mesmo assim.

**"Nota sem pedido: peça a conferência a quem aprova compras."** Só quem aprova compras confere a nota sem pedido.
