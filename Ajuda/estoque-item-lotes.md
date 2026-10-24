---
titulo: Lotes do item
rota: /app/estoque/itens/$id
aba: lotes
padrao: false
modulo: inventory
---

## Para que serve
Ver cada lote deste item: de qual fornecedor veio, quando entrou, a validade, quanto ainda tem e onde está. Daqui você rastreia o lote e corrige o lote digitado errado.

## Como usar
- Cada linha mostra o **lote**, a **validade**, o **saldo**, o **fornecedor**, a **entrada** e o **local**. Os mais novos aparecem primeiro.
- A validade ganha o chip **Vence em {n} dias** quando faltam até 30 dias, e **Vencido** quando já passou. O cartão vencido tem a faixa vermelha; o que vence logo, a âmbar.
- Clique em **Rastrear** para ver para onde o lote foi: OS, séries, projetos, clientes e romaneios.
- Clique em **Corrigir** para trocar o número ou a validade digitados errado. Escreva o motivo e clique em **Salvar correção**. O motivo fica no histórico de alterações.
- Lote que já teve saída só tem a **validade** corrigida. O número fica travado, porque já está nos documentos de saída.
- No celular ficam lote, validade e saldo. Toque na linha para ver fornecedor, entrada, local e as ações.

## Perguntas frequentes
**Por que diz "Este item não controla lote."?** O item está sem controle de lote. Marque o controle de lote na aba **Dados** antes da próxima entrada.

**De onde vem o lote?** O lote nasce na primeira entrada com o número: recebimento, entrada manual ou saldo inicial.

**Quem pode corrigir?** Quem tem a ação de editar no Estoque.
