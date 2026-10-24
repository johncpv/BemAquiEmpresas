---
titulo: Pedido de compra — decisões
rota: /app/compras/pedidos/$id
aba: decisoes
padrao: false
modulo: purchasing
---

## Para que serve
Ver quem aprovou ou reprovou o pedido, em que nível, por qual regra e com qual valor de referência, em cada envio.

## Como usar
- Cada envio para aprovação é uma **tentativa**, numerada. A tentativa mais recente aparece primeiro.
- Em cada tentativa aparecem os níveis (aprovador da regra, valor de referência e situação) e a linha do tempo: enviado, aprovado, reprovado, alterado ou cancelado, com quem fez, quando e o comentário.
- O aviso de **fracionamento** aparece quando a soma dos pedidos do mesmo fornecedor em 30 dias levou o pedido para uma alçada mais alta.
- A marca **Autoaprovado** aparece quando quem pediu aprovou, por ser o único Proprietário com a permissão de aprovar. O Proprietário recebe o aviso.

## Perguntas frequentes
**Dá para apagar uma decisão?** Não. As decisões de cada tentativa ficam no histórico, mesmo depois de uma reprovação ou de uma edição.
