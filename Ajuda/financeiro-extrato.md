---
titulo: Extrato da conta
rota: /app/financeiro/contas/$id/extrato
aba:
padrao: true
modulo: finance
---

## Para que serve
Ver tudo o que entrou e saiu de uma conta no período, com o saldo depois de cada movimento.

## Como usar
- Escolha o período em **De** e **Até**. O padrão é o mês corrente.
- A primeira linha é o **Saldo inicial** (quando o período começa no saldo informado) ou o **Saldo anterior** (o saldo inicial mais tudo o que veio antes).
- Clique na origem para abrir o título, a ordem de pagamento ou o extrato da outra conta da transferência.
- **Transferir** move dinheiro entre contas. **Lançar tarifa ou rendimento** registra tarifas, IOF, juros e rendimentos sem ordem de pagamento.
- Marque **Só movimentos sem OP** para ver só esses lançamentos.

## Imprimir e exportar
- Clique em **Exportar** (acima do extrato) e escolha **Excel (.xlsx)** ou **PDF do extrato**. Valem a conta, o período **De/Até** e o "Só movimentos sem OP" que estão na tela, com todas as páginas.
- A planilha começa pelo **Saldo inicial** ou **Saldo anterior**, traz cada movimento com o tipo, a origem, a entrada, a saída e o saldo depois dele, e termina no **Saldo final**. Estornos aparecem na coluna **Situação** ("Estorno" e "Estornado").
- O **PDF do extrato** traz a conta (banco, agência e número como a tela mostra), o período, o saldo de partida, os movimentos, os totais de entradas e saídas, o saldo final e até quando os movimentos estão conciliados. Sem saldo inicial informado, o PDF avisa no topo que os saldos podem estar errados.
- O limite é de 10.000 linhas no Excel e 2.000 no PDF. Acima disso, o sistema avisa e pede para refinar os filtros (período, situação ou busca).
- Toda exportação fica registrada na auditoria (quem, quando, formato, quantas linhas e os filtros). Com a lista vazia, **Exportar** fica desabilitado.

## Perguntas frequentes
**Como corrijo um recebimento ou um pagamento?** Pelo título (estorno da baixa) ou pela ordem de pagamento (estorno do pagamento). O extrato só estorna transferências e lançamentos sem OP.

**Por que não consigo transferir para uma conta?** Contas criadas há menos de 30 dias só recebem transferência de quem aprova em Caixa e pagamentos.

**Quem fica sabendo?** O Proprietário é avisado de todo lançamento sem OP, de toda transferência acima do valor configurado e de todo estorno.
