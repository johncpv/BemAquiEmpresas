---
titulo: A receber
rota: /app/financeiro/receber
aba: receber
padrao: true
modulo: finance
---

## Para que serve
Ver o que os clientes devem à empresa, por vencimento, e abrir cada título para dar baixa quando o dinheiro entrar.

## Como usar
- As parcelas dos contratos entram sozinhas, uma por marco. A parcela **Prevista** espera o marco acontecer ("Aguarda: fabricação concluída") para ganhar vencimento.
- Se um evento do contrato falhar, aparece um aviso com **Reprocessar** para quem pode editar o financeiro.
- Filtre pela situação (em aberto, vencidos, pagos, cancelados) e pelo período de vencimento.
- O rodapé mostra quanto está em aberto e quanto está vencido no filtro inteiro, não só na página.
- Clique no número para abrir o título.
- Clique em **Novo título a receber** para lançar uma venda à mão ou parcelar pela condição de pagamento.
- **Descontos e cancelamentos** mostra o que deixou de entrar no caixa no período.
- **Pagamentos a conferir** (aparece com a contagem quando há algum): boletos que o Inter confirmou com valor diferente do título (pago a menos fora da data do desconto, pago a mais além dos juros e da multa, ou boleto já cancelado). O dinheiro já está na conta, marcado "Recebido do Inter, aguardando conferência". Clique em **Conferir pagamento**, escolha **Baixar com desconto**, **Baixar com juros** ou **Deixar saldo em aberto** e confira o saldo antes e depois. Desconto acima do limite só quem aprova em Caixa e pagamentos confirma.
- Boleto pago sem diferença baixa sozinho pelo aviso do Inter (ou na conferência das 7h e das 13h), com a data do crédito do banco; quem vê o Financeiro recebe "Recebido R$ … de … · título …" no sino.

## Imprimir e exportar
- Clique em **Exportar** e escolha **Excel (.xlsx)** ou **PDF**. Valem a busca, a situação, o vencimento, o cliente, o contrato, o boleto e a ordem da tela, com todas as páginas.
- A planilha traz número, cliente, contrato/projeto, parcela, emissão, vencimento ("Aguarda: …" na parcela prevista), valor, recebido, saldo, situação, boleto e se veio da implantação. As duas últimas linhas, **Em aberto** e **Vencido**, são iguais ao rodapé da tela. O PDF traz as colunas principais.
- A lista de **Pagamentos a conferir** não é exportada nesta versão.
- O limite é de 10.000 linhas no Excel e 2.000 no PDF. Acima disso, o sistema avisa e pede para refinar os filtros (período, situação ou busca).
- Toda exportação fica registrada na auditoria (quem, quando, formato, quantas linhas e os filtros). Com a lista vazia, **Exportar** fica desabilitado.
- Na coluna **Boleto**, o título com boleto emitido ou pago tem **Baixar boleto**: o mesmo PDF da aba Boleto do título.

## Perguntas frequentes
**Quando um título aparece como "Vencido"?** Quando está em aberto e o vencimento é antes de hoje. Vencimento num sábado, domingo ou feriado passa a vencido só no dia seguinte.

**Posso apagar um título lançado errado?** Não. Título nunca é apagado: cancele com o motivo. Se já teve baixa, estorne a baixa antes.

**O que quer dizer "Aguardando conferência"?** O cliente pagou o boleto, mas com um valor que o sistema não decide sozinho. A entrada já conta no saldo da conta; o título só baixa quando alguém confere.

**O que é a etiqueta "Implantação"?** Título que já estava em aberto quando a empresa começou a usar o sistema. Ele não entra no resultado do mês nem na pontualidade do cliente.
