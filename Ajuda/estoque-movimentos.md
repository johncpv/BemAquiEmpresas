---
titulo: Movimentos de estoque
rota: /app/estoque/movimentos
aba: movimentos
padrao: true
modulo: inventory
---

## Para que serve
Consultar todas as entradas, saídas, transferências e saldos iniciais, do mais recente para o mais antigo, com o custo de cada um e quem lançou.

## Como usar
- Busque pelo número do movimento (MOV-AAAA-000001) ou do documento de origem.
- Filtre por local, tipo, origem (lançamento manual ou documento) e período em **De** e **Até**.
- A quantidade aparece com sinal: entrada positiva e saída negativa.
- Clique no número para ver o movimento, a origem e, se for o caso, **Estornar movimento**.

## Perguntas frequentes
**Posso apagar ou corrigir um movimento?** Não. Movimento não muda nem é apagado: para desfazer, estorne. O estorno lança o movimento inverso e o original fica marcado como estornado.

**Por que a ordem não segue a data?** A lista segue a ordem em que os movimentos foram lançados. O saldo inicial, por exemplo, tem a data de corte, mas entra na ordem em que foi lançado.

**De onde vem o resumo diário que o Proprietário recebe?** Todo dia o Proprietário recebe o resumo das entradas, saídas e estornos manuais do dia anterior, com link para esta tela filtrada pelo dia.
