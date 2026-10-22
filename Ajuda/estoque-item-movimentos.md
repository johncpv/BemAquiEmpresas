---
titulo: Movimentos do item
rota: /app/estoque/itens/$id
aba: movimentos
padrao: false
modulo: inventory
---

## Para que serve
Ver o kardex do item: todos os movimentos na ordem em que foram lançados, com o saldo do local e o custo médio logo depois de cada um.

## Como usar
- O topo mostra o físico total e o custo médio atual; com mais de um local, o saldo de cada local.
- Com mais de um local, filtre por **Local** para ver só o kardex daquele local.
- Clique no número para abrir o movimento.

## Perguntas frequentes
**Por que o custo médio não muda numa saída?** A saída usa o custo médio do momento. Só as entradas mudam o custo médio.

**Por que a ordem não segue a data?** O kardex segue a ordem de lançamento, para o saldo e o custo médio de cada linha baterem com o que o sistema calculou na hora.
