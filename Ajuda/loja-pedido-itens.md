---
titulo: Pedido: itens
rota: /app/loja/pedidos/$id
aba: itens
padrao: false
modulo: sales
---

## Para que serve
Ver os produtos do pedido, com preço, desconto, total e quanto de cada um já está **reservado** no estoque.

## Como usar
- Para mudar itens, quantidades ou o cliente pela tela, cancele o pedido e use **Recriar pedido** (a edição direta na tela chega numa próxima versão). No pedido aberto, a reserva acompanha qualquer alteração: subir pede estoque (tudo ou nada) e descer libera.
- Depois da cobrança, os itens ficam travados. Para mudar, cancele a cobrança antes.
- A coluna **Reservado** mostra o estoque separado para o pedido. Se ficar menor que a quantidade, aparece "Reserva com falta".

## Perguntas frequentes
**Por que o reservado está menor que a quantidade?** Parte do estoque saiu do local depois da confirmação (por exemplo, num inventário). O pedido mostra "Reserva com falta": reponha o item ou ajuste o pedido.

**O preço mudou na tabela depois que criei o pedido. Qual vale?** O preço é conferido de novo ao confirmar: se mudou, a confirmação mostra o novo e pede para confirmar com ele. Depois de confirmado, o preço fica congelado.
