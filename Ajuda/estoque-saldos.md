---
titulo: Saldos de estoque
rota: /app/estoque/saldos
aba: saldos
padrao: true
modulo: inventory
---

## Para que serve
Ver o que existe de cada item em cada local: o físico, o que já está reservado para OS, o disponível, o estoque mínimo, o custo médio e a situação. É daqui que saem as entradas, as saídas e as transferências manuais.

## Como usar
- **Lançar entrada** é a ação principal: escolha o item, o local (o padrão já vem marcado como **Sugerido**), a quantidade, o custo unitário (o custo médio atual vem sugerido) e o motivo. Item com lote pede o número do lote.
- **Lançar saída** tira material para consumo interno, amostra, perda ou entrega direto na obra. Ela sai pelo custo médio do momento. Com projeto, o valor entra no custo do projeto.
- **Transferir** muda o material de um local para outro. O custo médio não muda, porque ele é do item, não do local.
- **Lançar ajuste** (com a ação **Ajustar estoque**) acerta sobra ou falta sem documento de origem: digite a quantidade contada ou a diferença, com o motivo. O ajuste vai para a aprovação de outra pessoa e só muda o saldo quando aprovado. Veja em **Ajustes**.
- A quantidade pode ser digitada em outra unidade do item (caixa, rolo): o diálogo mostra a conversão para a unidade de estoque.
- Antes de confirmar, o diálogo mostra o saldo do local e o custo médio **antes → depois**.
- Enquanto a implantação do estoque está aberta, aparecem **Lançar saldo inicial** e **Saldo inicial em lote**. Quem aprova no Estoque conclui a implantação em **Concluir implantação do estoque**.
- Filtre por local, categoria, tipo e situação. No celular aparecem item, local, disponível e situação; toque na linha para ver o resto.

## Perguntas frequentes
**Por que meu lançamento foi para aprovação?** Entrada ou saída acima de R$ 1.000,00, ou entrada com custo mais de 20% diferente do custo médio, precisa da aprovação de outra pessoa. O saldo só muda quando ela aprovar.

**O que é o disponível?** É o físico menos o que já está reservado para OS abertas.

**De onde vem o reservado?** Da BOM liberada de cada OS. O material reservado continua no físico, mas a saída avulsa e a transferência só tiram o disponível: o sistema recusa e mostra "Há {x} disponível; {y} está reservado para OS.". Veja o que falta comprar em **Faltas**.

**Por que não consigo lançar saldo inicial?** O saldo inicial só vale enquanto a implantação está aberta e para item sem nenhum movimento no local. Depois, use **Lançar ajuste**.

**A saída pode deixar o saldo negativo?** Não. O sistema recusa e mostra quanto há no local. Ela também não tira o que está reservado para OS.
