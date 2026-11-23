---
titulo: Divergências de estoque
rota: /app/estoque/divergencias
aba: divergencias
padrao: true
modulo: inventory
---

## Para que serve
Ver onde o sistema e a prateleira da loja não batem. Cada linha é um item que o caixa vendeu sem o sistema ter saldo naquele local.

## Por que a venda não foi barrada
O cliente já levou a mercadoria quando a venda chega ao sistema, às vezes horas depois, se o caixa ficou sem internet. Por isso a venda concluída **nunca** é recusada por saldo: sai do estoque o que havia, e o restante fica aqui como falta, até alguém acertar o estoque. O saldo do sistema nunca fica negativo.

Se a empresa não quer vender item sem saldo, deixe **Configurações → Parâmetros → Loja: venda sem saldo** em **Não vender sem saldo** (é o padrão). Aí o próprio caixa barra o item sem saldo, pelo último saldo que recebeu. Mesmo assim, uma venda feita sem internet com um saldo antigo pode chegar aqui.

## Como usar
- Cada linha mostra o item, a loja (o local de estoque dela), a **Falta**, o **Disponível** agora, **desde quando** falta e as vendas ligadas.
- **Ajustar estoque** abre o ajuste do local, para lançar a contagem real do item. Aprovado o ajuste, o disponível sobe.
- **Transferir do depósito** abre a transferência do item de outro local para a loja.
- **Aplicar baixa** lança a saída que faltou, da venda mais antiga para a mais nova, enquanto o disponível cobre cada uma. O que o disponível não cobre continua na lista. Precisa da permissão de ajuste do Estoque.
- Busque pelo código ou nome do item e filtre pelo local.

## Perguntas frequentes
**A venda cancelada some da lista?** Sim. O cancelamento fecha primeiro a falta da venda, sem dar entrada do que nunca saiu, e só devolve ao estoque o que tinha saído.

**E o custo da venda?** A venda guarda o custo médio do item no momento da baixa. Quando a falta é aplicada depois e nada tinha saído antes, o custo da venda passa a ser o da baixa aplicada.

**Quem resolveu a divergência fica registrado?** Sim. Quem aplicou a baixa e quando ficam no histórico.
