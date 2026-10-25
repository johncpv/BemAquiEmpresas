---
titulo: Inventário — divergências
rota: /app/estoque/inventarios/$id
aba: divergencias
padrao: false
modulo: inventory
---

## Para que serve
Ver o que deu diferente na contagem, em quantidade e em valor, e mandar as linhas duvidosas para a recontagem por outra pessoa.

## Como usar
- A aba é de quem tem a ação **Ajustar estoque** no Estoque. No topo ficam as sobras, as faltas e o líquido em R$, pelo custo médio.
- Cada cartão mostra a diferença, o valor, a foto do saldo, o contado e quem contou. As linhas vêm da maior para a menor diferença em valor.
- **Teve movimento durante a contagem**: entrou ou saiu material do item entre a abertura e a contagem da linha. A diferença já desconta esse movimento: contado − (foto + movimentos).
- Marque as linhas e clique **Pedir recontagem**. Elas voltam para a contagem e só quem não contou a primeira vez pode recontar. A recontagem vale na diferença; a primeira contagem aparece no cartão e no histórico.
- Itens ainda não contados não entram aqui: a decisão sobre eles é feita ao fechar o inventário.

## Perguntas frequentes
**Por que a diferença não é o contado menos a foto?** Porque o local continuou movimentando. O sistema soma à foto o que entrou ou saiu até a contagem de cada linha.

**Quem pode recontar?** Qualquer pessoa com a ação **Criar** no Estoque, menos quem contou a linha original.
