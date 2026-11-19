---
titulo: Vendas sem documento fiscal
rota: /app/loja/vendas-sem-documento-fiscal
aba:
padrao: true
modulo: sales
---

## Para que serve
Mostrar, por período, loja e operador, quantas vendas do balcão saíram **sem documento fiscal** (com o comprovante "Não é documento fiscal") e o valor delas. Serve para a empresa acompanhar o que vendeu nesse modo e levar à contadora.

## Como usar
- Escolha o período (data de negócio da venda, até um ano) e, se quiser, a loja e o operador.
- A tabela mostra cada loja e operador com a quantidade e o valor, e o total no fim.
- Toque em **Exportar** para baixar a planilha (CSV, abre no Excel) com as mesmas linhas.
- Entram as vendas concluídas sem NFC-e nem NF-e. Vendas canceladas ficam de fora. Quando a NFC-e chegar, entram também as vendas que o caixa fechou sem NFC-e numa loja que emite.

## Perguntas frequentes
**A venda de hoje não apareceu.** As vendas do caixa entram aqui quando o caixa sincroniza com o servidor. Sem internet, elas esperam no aparelho e sobem sozinhas.

**Quem vê este relatório?** Quem tem a permissão de ver vendas.
