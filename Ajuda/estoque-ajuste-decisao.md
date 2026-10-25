---
titulo: Decisão do ajuste
rota: /app/estoque/ajustes/$id
aba: decisao
padrao: false
modulo: inventory
---

## Para que serve
Ver quem lançou o ajuste, quem decidiu, quando e com qual comentário, e o estorno, quando houver.

## Como usar
- **Quem lançou** e a data do envio. Quando o ajuste veio do inventário, **Origem** leva ao inventário.
- **Quem decidiu**: quem aprovou ou reprovou e quando, com o comentário. No cancelamento, quem cancelou e o motivo.
- A marca **Autoaprovado** aparece quando o Proprietário era o único com a ação **Aprovar** no Estoque e decidiu o próprio ajuste. O Proprietário recebe um aviso.
- **Estorno** mostra quem estornou, quando e por quê.

## Perguntas frequentes
**Por que não consigo estornar?** Só quem tem a ação **Aprovar** no Estoque estorna, e não pode ser quem lançou o ajuste (salvo o Proprietário único). O motivo aparece nesta aba.
