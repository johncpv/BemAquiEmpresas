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
- O campo **Projeto** é opcional: liga o recebimento a um projeto aberto.
- **Dar baixa**: informe a conta, a data, o principal recebido e, se houver, juros, multa e desconto. O diálogo mostra o saldo antes e depois e quanto entra na conta.
- **Quitar o saldo com desconto**: zera um saldo que o cliente não vai pagar, sem movimentar a conta.
- **Alterar vencimento**: muda a data de um título em aberto, com o motivo. O Proprietário é avisado.
- **Cancelar título**: tira o título dos totais em aberto, com o motivo. Nada é apagado.
- Na aba Dados, corrija a descrição, a conta e o centro de custo e clique em **Salvar título a receber**.

## Imprimir e exportar
- **Baixar PDF** (no cabeçalho do título) baixa a ficha do título; **Imprimir** abre o mesmo PDF numa aba nova. Sai com o número, o cliente, a origem (manual, contrato, implantação…), a conta do plano e o centro de custo, a emissão, o vencimento, o valor, o recebido e o saldo, cada baixa (data, valor, juros, multa, desconto, conta e se foi estornada) e o boleto (situação e linha digitável, quando há).
- Título **cancelado** sai com a marca **Cancelado** e o motivo na faixa. Parcela **prevista** sai com a faixa "Previsto: aguarda …" (o marco do contrato) ou com o vencimento estimado.
- O PDF mostra o título como ele está na hora; cada download fica na auditoria.

## Perguntas frequentes
**Por que o valor não muda?** Título que já teve baixa não troca o valor: estorne as baixas antes. Título de contrato muda pelo contrato ou pelo aditivo.

**Por que pede aprovação para o desconto?** Desconto acima do limite de Configurações → Parâmetros só é confirmado por quem aprova em Caixa e pagamentos.

**Apareceu "O título mudou desde que você abriu".** Outra pessoa mexeu no título ao mesmo tempo. Clique em **Recarregar** e confira antes de repetir.
