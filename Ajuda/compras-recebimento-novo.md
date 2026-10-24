---
titulo: Registrar recebimento
rota: /app/compras/pedidos/$id/recebimento
aba:
padrao: true
modulo: purchasing
---

## Para que serve
Dar entrada no material que chegou, conferindo com o pedido. Ao confirmar, o estoque, o custo médio e o título a pagar saem juntos, sem digitar de novo no Financeiro.

## Como usar
- O formulário já vem com o **saldo a receber** de cada item, a data de hoje e o local padrão do estoque, com a marca **Sugerido**. Ajuste o que veio de fato. Item que não chegou fica com 0.
- Informe a quantidade na unidade de estoque. Se o item tem o código do fornecedor com a unidade dele, marque **Na unidade do fornecedor**: o sistema converte pelo fator.
- O **lote** é obrigatório no item que controla lote. A validade é opcional.
- O preço é o do pedido e não muda aqui. Preço diferente: peça a correção a quem tem a permissão Aprovar em Compras.
- O **frete** e o **IPI** vêm proporcionais ao que está sendo recebido e só podem baixar.
- Clique em **Registrar recebimento**. O diálogo mostra, por item, o **custo de entrada** e o custo médio antes e depois, os títulos a pagar que nascem e o previsto do pedido antes e depois. Confirme em **Registrar recebimento**.
- Depois, a tela do recebimento oferece **Conferir título a pagar**, **Ver movimentos de estoque** e, quando o material completou a falta de uma OS, **Ver OS que receberam material**.

## Perguntas frequentes
**Chegou mais do que o pedido.** Com a tolerância em 0% (o padrão), não entra mais que o saldo: o excedente vira outro pedido. A tolerância fica em Configurações → Parâmetros → Compras.

**Como o custo de entrada é calculado?** Valor da linha (quantidade × preço do pedido) mais a parte do frete e do IPI dela, dividido pela quantidade. O frete e o IPI são divididos pelo valor de cada linha.

**O botão está desabilitado.** A mensagem ao lado diz o motivo: você pediu ou aprovou o pedido, ou o pedido está em reaprovação depois de uma correção de preço.