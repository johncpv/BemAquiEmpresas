---
titulo: Cotação — comparativo
rota: /app/compras/cotacoes/$id
aba: comparativo
padrao: false
modulo: purchasing
---

## Para que serve
Ver as respostas lado a lado, escolher o vencedor de cada item e concluir a cotação. Depois, gerar os pedidos de compra.

## Como usar
- No alto, um cartão por fornecedor com o **Custo total** (itens + frete + IPI, como entra no custo do estoque), o prazo e a condição. O menor total entre quem cotou todos os itens leva o chip **Menor custo**.
- Na tabela, cada item tem um grupo de opções, um fornecedor por coluna, com o preço, o total da linha e o custo com frete e IPI. Use as setas do teclado para trocar de fornecedor. O de menor custo da linha já vem marcado, com a tag **Sugerido**: o frete do fornecedor entra rateado pelo valor dos itens.
- **Escolher tudo de {fornecedor}** marca esse fornecedor em todos os itens que ele cotou.
- Escolheu outro que não o de menor custo? Informe o **Motivo da escolha**: Prazo, Qualidade, Condição de pagamento, Fornecedor homologado ou Outro (com o texto, até 200 caracteres). O motivo fica no histórico.
- A **Última compra** de cada item aparece como referência. **Tem só {quantidade}** avisa que o fornecedor não tem tudo.
- **Concluir cotação** grava os vencedores. Item que ninguém cotou volta para a solicitação. Concluída, o próximo passo é **Gerar pedidos de compra**: um pedido em rascunho por fornecedor vencedor, com a prévia do total de cada um. Depois, **Enviar todos para aprovação** manda cada pedido para a própria alçada e mostra quais foram enviados e quais não, com o motivo.
- No celular, os cartões vêm primeiro e a tabela rola para o lado, com a coluna do item fixa.

## Perguntas frequentes
**Uma resposta só basta?** Basta. A cotação mostra o aviso **1 cotação** e o pedido gerado leva o mesmo aviso na aprovação, sem bloquear.

**Gerar de novo duplica os pedidos?** Não. Os pedidos já gerados aparecem em **Pedidos desta cotação**.

**Por que um pedido não foi enviado?** Nenhuma regra de alçada cobre aquele valor ou o nível não tem aprovador possível. Ajuste as alçadas em Financeiro → Alçadas e envie o pedido pela tela dele.
