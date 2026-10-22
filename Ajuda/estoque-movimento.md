---
titulo: Movimento de estoque
rota: /app/estoque/movimentos/$id
aba:
padrao: true
modulo: inventory
---

## Para que serve
Ver um movimento: o item, o local, a quantidade, o custo, o saldo do local e o custo médio do item logo depois dele, a origem e quem lançou.

## Como usar
- **Estornar movimento** aparece só no lançamento manual. Escreva o motivo e confira o saldo antes → depois.
- Quem lançou estorna o próprio movimento até R$ 1.000,00 e em até 24 horas. Acima disso, ou depois, o estorno é de outra pessoa com permissão de aprovar no Estoque.
- O estorno de uma transferência desfaz as duas pernas juntas.
- Movimento de outro documento (requisição, recebimento, produção, expedição, ajuste ou inventário) é estornado pelo próprio documento.

## Perguntas frequentes
**O que acontece com o custo médio no estorno de uma entrada?** A entrada sai pelo custo dela. Se o resultado ficar negativo ou o físico zerar, o custo médio continua o último válido.

**Por que aparece "Autoaprovado"?** Quando só existe uma pessoa que pode aprovar, ela pode estornar o próprio movimento. O sistema marca e avisa o Proprietário.
