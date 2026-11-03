---
titulo: A pagar
rota: /app/financeiro/pagar
aba: pagar
padrao: true
modulo: finance
---

## Para que serve
Ver o que a empresa precisa pagar, por vencimento, com a forma de pagamento de cada título.

## Como usar
- Filtre pela situação: em aberto, vencidos, previstos, em ordem de pagamento, pagos e cancelados.
- O rodapé mostra o líquido em aberto e o vencido no filtro inteiro.
- Clique no número para abrir o título.
- Clique em **Novo título a pagar** para lançar uma nota ou um boleto.
- Clique em **Exportar** e escolha **Excel (.xlsx)** ou **PDF** para baixar a lista com a busca, a situação e a ordem que estão na tela (todas as páginas, não só a atual).

## Exportar
- O arquivo sai com o nome `contas-a-pagar-AAAA-MM-DD`. A planilha traz o cabeçalho congelado, o filtro em cada coluna, os valores em reais e as datas prontas para somar e filtrar, e uma aba **Filtros** com o que foi aplicado, quem emitiu e quando. O PDF traz as colunas principais; a planilha traz também conta, projeto, emissão, valor, retenções e pago.
- O limite é de 10.000 linhas no Excel e 2.000 no PDF. Acima disso, o sistema avisa e pede para refinar os filtros (situação, busca ou período).
- Os salários seguem a mesma regra da tela: sem aprovar nem pagar no Financeiro, cada folha sai numa linha só, com o total.
- Toda exportação fica registrada na auditoria (quem, quando, formato, quantas linhas e os filtros).

## Perguntas frequentes
**Como pago um título?** Pela ordem de pagamento: ela reúne os títulos, passa pela aprovação e só então o pagamento é registrado. Não existe baixa direta no título.

**O que é "Estimado"?** Um título previsto com vencimento estimado, como o do instalador enquanto a obra não termina. Ele entra no fluxo de caixa marcado como estimativa.

**Por que o valor líquido é menor que o valor?** O líquido desconta as retenções da nota (ISS, INSS, IR, PIS/COFINS/CSLL). É o que sai da conta.
