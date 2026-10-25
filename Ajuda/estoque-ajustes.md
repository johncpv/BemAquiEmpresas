---
titulo: Ajustes de estoque
rota: /app/estoque/ajustes
aba: ajustes
padrao: true
modulo: inventory
---

## Para que serve
Acertar o saldo quando o que existe na prateleira não bate com o sistema e não há documento de origem: perda ou avaria, erro de lançamento, sobra encontrada ou contagem. Todo ajuste passa pela aprovação de outra pessoa antes de mexer no saldo.

## Como usar
- **Lançar ajuste** (com a ação **Ajustar estoque** no Estoque): escolha o local, o modo do ajuste e o motivo, adicione os itens e clique em **Enviar para aprovação**. Também dá para lançar a partir de **Saldos**.
- Quem aprova no Estoque abre direto em **Aguardando você**: os ajustes pendentes lançados por outras pessoas. Em **Mostrar**, escolha **Todos os ajustes**; filtre por situação e modo.
- Clique no número para ver o ajuste, os itens e decidir.
- A situação aparece em cor: **Aguardando aprovação** (informativo), **Aprovado** (ok), **Reprovado** (crítico), **Cancelado** e **Estornado** (neutros). A marca **Autoaprovado** aparece quando o Proprietário era o único que podia aprovar.

## Perguntas frequentes
**Por que todo ajuste precisa de aprovação?** O ajuste muda o saldo sem nota, pedido ou requisição. Para o estoque não mudar sem alguém responder por isso, outra pessoa com a ação **Aprovar** no Estoque decide, qualquer que seja o valor.

**O ajuste pendente mexe no saldo ou reserva material?** Não. O saldo e o custo só mudam quando o ajuste é aprovado.

**Não aparece nada em "Aguardando você".** Você não tem a ação **Aprovar** no Estoque, ou os pendentes foram lançados por você: quem lança não aprova o próprio ajuste.
