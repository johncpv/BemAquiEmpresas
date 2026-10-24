---
titulo: Ajuste de estoque
rota: /app/estoque/ajustes/$id
aba: itens
padrao: true
modulo: inventory
---

## Para que serve
Ver um ajuste de estoque, o que cada item mudaria (ou mudou) no saldo, e decidir: aprovar, reprovar, cancelar ou estornar.

## Como usar
- No topo ficam o número, a situação, o modo (**Quantidade contada** ou **Diferença**), quem lançou e, nos fatos, os itens, as sobras, as faltas e o valor.
- Em **Itens**, cada cartão mostra o saldo do sistema no envio, o contado ou a diferença e o valor pelo custo médio. Enquanto o ajuste está pendente, **Agora** mostra o disponível, o físico e o reservado de hoje e a diferença que a aprovação aplicaria. Depois de aprovado, cada linha mostra a diferença aplicada, o custo e o movimento (MOV).
- **Aprovar ajuste** (com a ação **Aprovar** no Estoque, sendo outra pessoa): o diálogo mostra, por item, o saldo antes → depois e o total de sobras e faltas. Se o saldo mudou desde o envio, ele avisa e mostra os valores novos. Ao confirmar, o saldo muda na hora.
- **Reprovar ajuste**: escreva o motivo; nada muda no saldo e quem lançou recebe o comentário.
- **Cancelar ajuste**: só quem lançou, enquanto pendente, com o motivo.
- **Estornar ajuste**: desfaz um ajuste aprovado com os movimentos inversos, por outra pessoa com a ação **Aprovar**, com o motivo.
- Aprovado, o ajuste fica em modo leitura. Para corrigir, lance outro ajuste ou estorne este.
- Depois de decidir, os próximos passos levam ao próximo aguardando você e, se o item ficou abaixo do mínimo, a **Gerar solicitação de compra**.

## Perguntas frequentes
**Apareceu "O saldo ficaria …, abaixo do reservado para OS".** Parte do material está reservado para OS abertas, e o ajuste não pode tirar o que as OS esperam. Clique em **Ver reservas do item** para ver quais OS reservaram. Libere a reserva ou baixe pela requisição da OS antes de ajustar.

**Posso mudar a quantidade antes de aprovar?** Não. Quem aprova decide sobre o que foi enviado. Para mudar, reprove com o comentário e quem lançou envia outro ajuste.

**Por que a diferença na aprovação é outra?** No modo **Quantidade contada**, o contado vale sobre o saldo do momento da aprovação. Se entrou ou saiu material depois do envio, a diferença é recalculada.

**Há um inventário em contagem neste local.** Enquanto o inventário conta o local, ajustes e estornos manuais ficam bloqueados. Feche ou cancele o inventário antes.
