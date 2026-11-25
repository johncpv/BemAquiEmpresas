---
titulo: Venda: trilha
rota: /app/loja/vendas/$id
aba: trilha
padrao: false
modulo: sales
---

## Para que serve
Contar a história completa da venda, do primeiro item lançado no caixa até o financeiro, para você entender o que aconteceu sem precisar perguntar a ninguém.

## Como usar
Cada passo tem a hora (horário de Brasília), o grupo e o que houve:
- **Caixa:** os passos no aparelho, em ordem: a venda começou, itens lançados, alterados ou removidos, desconto, troca de preço, venda em espera, pagamento e conclusão, com quem fez.
- **Supervisor:** cada aprovação (desconto, troca de preço, cancelamento), quem aprovou para quem, o antes e o depois e o motivo. Aprovação feita sem rede fica destacada.
- **Sincronização:** quando a venda foi feita no caixa e quando chegou ao servidor. Mais de 5 minutos entre um e outro quer dizer que o caixa estava sem internet.
- **Estoque:** a baixa de cada item ou a falta de saldo (a venda nunca é recusada por falta de estoque; a falta fica para alguém resolver).
- **Financeiro:** venda do caixa entra no fechamento da sessão de caixa. Logo abaixo da trilha aparecem a sessão e, para quem vê o financeiro, os títulos que o fechamento lançou.
- **Impressão:** o cupom impresso e cada reimpressão (2ª via, 3ª via...).
- **Pendência:** o que ficou para conferir nas Pendências do PDV (aberta ou resolvida).

## Perguntas frequentes
**Por que aparece "feito sem internet"?** O caixa estava sem rede e guardou a venda no aparelho; ela chegou ao servidor depois. Isso é normal.

**O que quer dizer "sequência do aparelho com falha"?** O servidor encontrou um buraco ou uma alteração na sequência de eventos do caixa. A venda foi para a conferência nas Pendências do PDV.

**Muitas reimpressões na mesma venda.** Vale conferir com o operador: reimpressão repetida pode indicar troca de cupom.
