---
titulo: Contas e caixas
rota: /app/financeiro/contas
aba: contas
padrao: true
modulo: finance
---

## Para que serve
Cadastrar as contas bancárias e os caixas da empresa, com o saldo de partida. O fluxo de caixa começa desse saldo.

## Como usar
- Clique em **Nova conta bancária**. Em **Banco**, escolha o banco pelo nome ou pelo código e informe a agência e a conta com o dígito. Em **Caixa**, basta o nome.
- O **saldo inicial** é opcional. Quando você digita o saldo, a data vem com hoje; ajuste para o dia do saldo. Saldo e data só valem juntos.
- A primeira conta já nasce **padrão**: ela vem escolhida nos lançamentos novos. Para trocar, marque **Conta padrão** em outra conta.
- Conta sem saldo mostra **Saldo inicial não informado** e o atalho **Informar saldo inicial**.

## Perguntas frequentes
**Posso cadastrar saldo negativo?** Pode. O sistema só avisa, para você conferir se a conta começa no cheque especial.

**Por que não consigo mudar o saldo inicial?** A conta já tem movimentação. Ajuste o saldo por um lançamento.

**Por que não consigo trocar o banco, a agência ou a conta?** Essa troca exige ser do grupo Proprietário ou poder aprovar no financeiro. Toda troca fica no histórico e avisa o Proprietário.

**Por que vejo a agência e a conta com asteriscos?** Seu grupo só consulta o financeiro. Os dados completos aparecem para quem pode editar.

**Como tiro uma conta de uso?** Inative. Conta com movimentação não pode ser excluída. A conta padrão precisa ser trocada antes.
