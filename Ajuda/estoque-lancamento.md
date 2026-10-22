---
titulo: Lançamento manual
rota: /app/estoque/lancamentos/$id
aba: linhas
padrao: true
modulo: inventory
---

## Para que serve
Ver o que foi enviado num lançamento manual (saldo inicial, entrada, saída ou transferência) e decidir quando ele está aguardando aprovação.

## Como usar
- **Aprovar lançamento** mostra o saldo do local e o custo médio antes → depois, calculados agora. A saída sai pelo custo médio do momento da aprovação.
- Quem aprova decide sobre o que foi enviado, sem editar. Para mudar algo, use **Reprovar lançamento** com o motivo: quem lançou recebe o aviso e lança de novo.
- Quem lançou pode **Cancelar lançamento** enquanto ele está pendente.
- Na aba **Histórico de alterações** fica quem enviou, quem decidiu e quando.

## Perguntas frequentes
**Por que não consigo aprovar o meu lançamento?** Quem lança não aprova o próprio lançamento. A exceção é quando só existe uma pessoa que pode aprovar: aí a aprovação passa com a marca "Autoaprovado" e o Proprietário é avisado.

**E se faltar saldo na hora de aprovar?** O sistema recusa e nada é gravado. O lançamento continua pendente.
