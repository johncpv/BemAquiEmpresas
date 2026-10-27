---
titulo: Ordens de pagamento
rota: /app/financeiro/ordens-de-pagamento
aba:
padrao: true
modulo: finance
---

## Para que serve
Garantir que nenhum dinheiro saia da empresa sem uma ordem aprovada. Toda saída para fornecedor passa por aqui.

## Como usar
- Em **A pagar**, marque os títulos (ou use **Selecionar os que vencem até…**) e clique em **Criar ordem de pagamento**.
- Use o filtro **Aguardando você** para ver o que espera a sua aprovação.
- O filtro **Aprovadas pelo próprio solicitante** mostra as ordens que o próprio Proprietário pediu e aprovou.

## Perguntas frequentes
**Quem aprova?** Quem tem a permissão de aprovar no Financeiro (o Proprietário). Nesta versão, aprova qualquer valor.

**Quem paga?** O Financeiro, depois da aprovação, registrando a saída da conta.

**Posso aprovar a ordem que eu pedi?** Não. A exceção é o Proprietário único, ou o parâmetro "Proprietário aprova a própria ordem" ligado.

**Salários: conta lida no pagamento.** O título de salário paga na conta do funcionário, que fica no RH. A ordem guarda só o final da conta. No "Pagar", o bloco **Para onde vai o dinheiro** lê os dados na hora, só para quem paga. Se a conta mudou depois do envio, a ordem precisa de nova aprovação (**Reenviar para aprovação**). Conta em quarentena não entra na ordem.

**Quem fechou a folha pode pagar os salários?** Não. A exceção é o Proprietário único, com a marca de autopagamento.
