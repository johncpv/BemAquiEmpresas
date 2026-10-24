---
titulo: Pedido de compra — financeiro
rota: /app/compras/pedidos/$id
aba: financeiro
padrao: false
modulo: purchasing
---

## Para que serve
Ver os títulos a pagar previstos que o pedido lançou no fluxo de caixa. A aba aparece para quem vê o Financeiro.

## Como usar
- Na aprovação, nasce um título previsto por parcela da condição de pagamento, com a descrição "Pedido {número} — parcela {n}/{m}". A soma das parcelas fecha com o total do pedido.
- A data de cada parcela é a previsão de entrega mais os dias da parcela, com a marca **Estimado**.
- A cada recebimento, o próprio recebimento cria os títulos em aberto e o previsto do pedido cai para o saldo que falta receber. Com o pedido todo recebido ou o saldo encerrado, o previsto é cancelado.
- O link **Ver OP** aparece quando o título já está numa ordem de pagamento.
- Cancelar o pedido cancela os títulos previstos. Título numa ordem de pagamento ou pago impede o cancelamento.

## Perguntas frequentes
**Em que conta o título entra?** Na conta 2.1 Materiais, com a natureza Material, que são os padrões de Compras. O frete e o IPI do próprio fornecedor vão nas mesmas parcelas.
