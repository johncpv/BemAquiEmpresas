---
titulo: Conciliação bancária
rota: /app/financeiro/conciliacao
aba:
padrao: true
modulo: finance
---

## Para que serve
Conferir, lado a lado, o extrato que o banco informou com o que o sistema registrou, e achar na hora o que entrou ou saiu sem registro. Com tudo conciliado e o saldo do banco igual ao do sistema, quem aprova em Caixa e pagamentos fecha o período.

## Como usar
- Escolha a **conta** e o **período**. Cada linha mostra um **par**: a linha do banco à esquerda, a transação do sistema à direita e, no meio, o motivo da sugestão e a confiança.
- **Conciliar** aceita a sugestão. Com o foco na linha, a tecla **Enter** faz o mesmo. **Aceitar todas de confiança alta** concilia de uma vez os pares de confiança Alta, depois de mostrar a lista com os valores e o total.
- **Escolher à mão** abre as transações não conciliadas da conta para você marcar uma ou mais. A soma precisa ser igual ao valor da linha. Também dá para juntar várias linhas do banco numa só transação, em "No sistema, não no banco" → **Escolher linhas**.
- **Sem par no banco** lista as linhas do banco sem transação no sistema:
  - Tarifa, IOF, juros ou rendimento: **Lançar sem OP**. O diálogo vem com o tipo, a conta do plano mais usada e a data e o valor da linha (travados). Lançar é de quem paga em Caixa e pagamentos; numa saída, o sistema pede de novo o código da verificação em duas etapas. O Proprietário é avisado e o lançamento entra em "Movimentos sem OP" no extrato da conta.
  - Entrada de um cliente com título em aberto do mesmo valor: **Baixar título**. A baixa usa a data e a conta da linha; juros, multa e desconto seguem os limites do financeiro.
  - Outra saída: o sistema **não** cria pagamento. Use **Vincular a uma OP paga** (a transação do pagamento já existe) ou **Abrir OP para regularizar** (o título segue a aprovação e só depois é pago; a linha espera pendente).
- **No sistema, não no banco** lista o que o sistema registrou e ainda não apareceu no extrato. Há mais de 5 dias, a transação fica em alerta: abra a origem e confira.
- **Desfazer conciliação** solta a linha e as transações, com motivo, sem estornar nada. Um lançamento ou uma baixa feitos pela conciliação só saem pelo estorno no extrato da conta ou no título.
- **Fechar conciliação** aparece quando a conta está "Conciliada até dd/mm/aaaa": todas as linhas até o dia estão conciliadas ou ignoradas e o saldo do banco no dia é igual ao do sistema.

## Perguntas frequentes
**O que é a confiança?** Alta: o boleto do Inter com o mesmo identificador do banco, ou o mesmo valor e a mesma data sem outra transação igual. Média: mesmo valor com até 3 dias de diferença e o CPF/CNPJ do terceiro ou o número do título ou da OP na descrição. Linha "não verificada pelo banco" (OFX de contingência) nunca tem confiança Alta.

**Por que não consigo conciliar uma linha?** Quem importou um arquivo OFX não confirma a conciliação dele. Peça a outra pessoa do Financeiro (o Proprietário também pode).

**Por que pede aprovação para lançar uma tarifa?** A partir de uma linha de arquivo OFX, valor acima do teto (padrão R$ 500,00, em Configurações → Parâmetros, limites do financeiro) só com quem aprova em Caixa e pagamentos.

**Como o sistema reconhece uma tarifa?** Pelo tipo que o banco informou ou pelas regras do extrato. Quando o mesmo tipo de linha se repete, use **Criar regra a partir desta linha**: a palavra da descrição, o tipo do lançamento e a conta do plano ficam guardados para as próximas.

**O que muda depois de fechar?** Desfazer conciliação, ignorar linha ou lançar qualquer movimento com data no período fechado (lançamento sem OP, transferência, baixa, pagamento de OP) passa a exigir quem aprova em Caixa e pagamentos e um motivo, e o Proprietário é avisado.

**A conferência do boleto some ao conciliar?** Não. O pagamento do Inter que aguarda conferência é casado com a linha do banco, mas a conferência do título continua no filtro "Pagamentos a conferir" de A receber.
