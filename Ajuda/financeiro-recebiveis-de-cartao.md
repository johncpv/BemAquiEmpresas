---
titulo: Recebíveis de cartão
rota: /app/financeiro/receber
aba: receber-cartoes
padrao: false
modulo: finance
recurso: finance.sale_receivables
---

## Para que serve
Acompanhar o dinheiro que a maquininha (a adquirente) vai depositar pelas vendas em cartão, sem digitar nada: o fechamento do caixa e as vendas da loja virtual criam esses recebíveis sozinhos.

## Como usar
- Em **A receber**, escolha o filtro **Origem: Cartões**. A coluna **Líquido previsto** mostra o que deve cair na conta: o bruto das vendas menos a taxa prevista.
- Nasce **um recebível por maquininha, bandeira, forma e data de crédito**. A taxa e o prazo (D+N) são os da tabela da forma de pagamento vigente no dia da venda.
- No **crédito parcelado**, cada parcela é um recebível: a primeira cai em D+N e as seguintes a cada 30 dias.
- Abra o recebível para ver o bruto, a taxa e o link **Ver a sessão de caixa e as vendas**.
- Quando a maquininha depositar, vá em **Conciliação**, na linha do depósito, e use **Baixar recebíveis de cartão**: a maquininha deposita um valor só para vários dias e bandeiras, então um depósito baixa vários recebíveis. O sistema já marca os da mesma maquininha que vencem até a data do depósito.

## Regras
- Cada recebível é baixado pelo líquido previsto. Se o depósito vier menor, o último (pelo vencimento) fica com a diferença em aberto, para você conferir com o extrato da maquininha. Se vier maior, inclua os recebíveis que faltam.
- Os recebíveis de cartão não entram na régua de cobrança, na inadimplência nem na lista padrão de clientes.
- No **Fluxo de caixa**, eles aparecem na data prevista do crédito, pelo líquido.
- A receita no DRE vem das vendas do dia, por loja, e não do recebível.

## Perguntas frequentes
**Por que não consigo cancelar o recebível?** Ele acompanha as vendas e o fechamento do caixa. Para mudar, cancele a venda ou reabra o caixa.

**A taxa da maquininha entra como despesa no DRE?** Ainda não: a conta da taxa no plano de contas está a confirmar com a contadora. Hoje o recebível já vale o líquido e mostra a taxa prevista.
