---
titulo: Título a receber
rota: /app/financeiro/receber/$id
aba: dados
padrao: true
modulo: finance
---

## Para que serve
Ver e corrigir os dados de um título a receber e registrar o que o cliente pagou.

## Como usar
- **Dar baixa**: informe a conta, a data, o principal recebido e, se houver, juros, multa e desconto. O diálogo mostra o saldo antes e depois e quanto entra na conta.
- **Quitar o saldo com desconto**: zera um saldo que o cliente não vai pagar, sem movimentar a conta.
- **Alterar vencimento**: muda a data de um título em aberto, com o motivo. O Proprietário é avisado.
- **Cancelar título**: tira o título dos totais em aberto, com o motivo. Nada é apagado.
- Na aba Dados, corrija a descrição, a conta e o centro de custo e clique em **Salvar título a receber**.

## Perguntas frequentes
**Por que o valor não muda?** Título que já teve baixa não troca o valor: estorne as baixas antes. Título de contrato muda pelo contrato ou pelo aditivo.

**Por que pede aprovação para o desconto?** Desconto acima do limite de Configurações → Parâmetros só é confirmado por quem aprova em Caixa e pagamentos.

**Apareceu "O título mudou desde que você abriu".** Outra pessoa mexeu no título ao mesmo tempo. Clique em **Recarregar** e confira antes de repetir.
