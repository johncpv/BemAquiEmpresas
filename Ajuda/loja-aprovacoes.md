---
titulo: Aprovações de supervisor
rota: /app/loja/aprovacoes
aba:
padrao: true
modulo: sales
---

## Para que serve
Acompanhar tudo o que precisou do **PIN de um supervisor** no caixa: desconto acima do limite da loja, troca de preço, remoção de item e cancelamento de venda. Cada aprovação guarda quem pediu (o operador), quem aprovou (o supervisor), o motivo e os valores antes e depois.

## Como usar
- Escolha o período e, se quiser, a loja e o tipo.
- Os quadros mostram a quantidade e o valor (a diferença entre antes e depois) **por operador**, **por supervisor** e **por tipo**. Operador com muitos cancelamentos ou supervisor que aprova quase tudo merecem uma conversa.
- A tabela mostra cada aprovação. "Usada" diz se a venda com a ação aprovada já chegou ao servidor.
- Toque em **Exportar** para baixar a planilha (CSV, abre no Excel).
- Quem vê o custo das vendas encontra no fim a lista **Vendas abaixo do custo**: linhas vendidas, já com o desconto, por menos que o custo da baixa do estoque.

## Perguntas frequentes
**O supervisor pode aprovar a própria venda?** Não. O caixa recusa com "Outra pessoa precisa aprovar.", e o servidor confere de novo.

**O Proprietário fica sabendo dos cancelamentos?** Sim. Todo cancelamento de venda paga gera um aviso para o Proprietário com a venda, o valor, o operador, o supervisor e o motivo.

**Onde mudo o limite de desconto?** Em Loja → Lojas e caixas, no campo "Limite de desconto sem supervisor (%)". Até esse limite o operador dá o desconto sozinho (sempre com motivo).

**Por que o alerta de cancelamentos por caixa não aparece?** O percentual que dispara o alerta ainda não foi definido (pendência PP-22). Por enquanto, use o quadro "Por tipo" e o filtro por loja.
