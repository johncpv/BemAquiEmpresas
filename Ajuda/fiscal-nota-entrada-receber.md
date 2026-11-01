---
titulo: Receber pela nota
rota: /app/fiscal/entrada/$id/recebimento
aba:
padrao: true
modulo: fiscal
---

## Para que serve
Dar entrada no material direto pela NF-e de entrada conferida, sem digitar nada de novo. O recebimento é o mesmo do pedido de compra, só que já vem com os valores da nota: a quantidade, o lote, o frete, o seguro, as outras despesas e o IPI. Ao confirmar, na mesma hora o material entra no estoque pelo custo da nota, os títulos a pagar nascem com o valor e o vencimento das **duplicatas** e a nota fica **Recebida**.

## Como usar
1. Na nota conferida, clique em **Receber pela nota**. A faixa no alto mostra **Valores da NF-e {número} · {fornecedor}**.
2. Confira as linhas: cada linha do pedido vem com a **quantidade da nota** (na unidade de estoque), o preço do pedido e o da nota. A quantidade não se digita: é a da nota.
3. Informe o **lote** do item que controla lote. Quando o XML trouxe o lote, ele já vem com a marca **Sugerido**. Escolha o local, se não for o padrão.
4. Confira o frete, o seguro, as outras despesas e o IPI da nota: eles são divididos pelo valor de cada linha (a última linha fica com o centavo que sobra).
5. Clique em **Receber pela nota**. O diálogo mostra, por item, o custo de entrada e o custo médio antes e depois, um título a pagar por duplicata com o valor e o vencimento, os títulos dos itens sem estoque, a regra de **crédito de impostos** usada e o previsto do pedido antes e depois. Confirme em **Receber pela nota**.
6. Depois, a tela do recebimento oferece **Conferir títulos a pagar** e **Ver movimentos de estoque**.

## Crédito de impostos
O custo de entrada segue o parâmetro **Crédito de impostos na entrada**, em Configurações → Fiscal, seção Entrada. O padrão é **Nenhum**: o ICMS, o IPI, o PIS e a COFINS da nota ficam no custo do material. Quando a contadora indicar que algum tributo é recuperável, marque-o lá: esse valor sai do custo e fica guardado na nota como crédito. O diálogo sempre mostra a regra usada.

## Perguntas frequentes
**O botão está desabilitado com "O preço da nota é diferente do pedido".** O recebimento usa sempre o preço do pedido. Clique em **Pedir correção de preço**: quem aprova compras recebe o aviso com o preço da nota sugerido e corrige no pedido. Depois, abra a nota de novo e receba.

**A nota não trouxe duplicatas.** Os vencimentos vêm da condição de pagamento do pedido, contados a partir da data de emissão da nota.

**As duplicatas não somam o total da nota.** Os títulos ficam pelo valor do recebimento, divididos na proporção das duplicatas, com o aviso "As duplicatas não somam o total da nota" para o financeiro conferir. Não bloqueia.

**E os itens marcados Sem item de estoque?** Não entram no estoque. Viram títulos próprios na conta do plano escolhida na conferência, um por duplicata, na proporção do valor deles.

**Recebi errado.** Quem aprova compras estorna o recebimento: os títulos são cancelados, o estoque sai e a nota volta a **Conferida**, pronta para receber de novo.

**A nota não tem pedido.** Ligue a nota a um pedido para receber.
