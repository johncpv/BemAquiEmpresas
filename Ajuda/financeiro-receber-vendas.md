---
titulo: Recebíveis das vendas
rota: /app/financeiro/receber
aba: receber-vendas
padrao: false
modulo: finance
recurso: finance.sale_receivables
---

## Para que serve
Ver, em **A receber**, o dinheiro das vendas que ainda vai cair na conta: as vendas da loja virtual, das OS e dos outros canais sem caixa viram recebíveis sozinhas, sem ninguém lançar título.

## Como usar
- Em **A receber**, escolha o filtro **Origem: Vendas** para ver só os recebíveis das vendas. A coluna **Canal** mostra de onde veio a venda (Loja virtual, OS, Balcão).
- Nasce **um recebível por dia, canal, forma de pagamento, maquininha (adquirente) e data de crédito**. Ele vale o **líquido**: o bruto das vendas menos a taxa prevista da maquininha, cadastrada em **Formas de pagamento**.
- O vencimento é o dia previsto do crédito (o "D+N" da taxa). Pix e dinheiro vencem no próprio dia da venda.
- Venda cancelada sai do recebível do dia sozinha; se era a única do grupo, o recebível fica cancelado.
- Quando o dinheiro cair na conta, dê baixa no recebível como em qualquer título (ou concilie pelo extrato).

## Perguntas frequentes
**Por que o recebível de venda não entra na régua de cobrança?** Porque quem paga é a maquininha ou o banco, não o cliente: não há a quem mandar lembrete. A régua continua só para os títulos lançados à mão e as parcelas de contrato.

**Posso cancelar ou mudar o valor do recebível de venda?** Não à mão. Ele acompanha as vendas: para tirá-lo, cancele a venda.

**A receita das vendas aparece no DRE pelo recebível?** Não. O DRE mostra a receita pelo valor das vendas do dia, por loja e canal. O recebível só diz quando e quanto cai na conta.

**O recebível do caixa (PDV) também aparece aqui?** Sim: quando o caixa fecha, nasce um recebível por sessão de caixa, forma, maquininha, bandeira e data de crédito (no parcelado, um por parcela). O dinheiro já entra baixado na conta do caixa e o Pix fica a conciliar. Os de cartão também têm o filtro **Origem: Cartões**.

**O que é o título com origem "Pedido de venda"?** É a cobrança de um pedido de venda por boleto com Pix do Inter: um título por pedido, contra o cliente. Ele entra no filtro **Origem: Vendas**, fica fora da régua de cobrança e não conta receita no DRE (a receita entra pela venda que o pedido pago vira). Quando o banco baixa o boleto, o pedido fica pago sozinho e a venda não gera outro recebível. Para cancelar esse título, cancele o pedido.
