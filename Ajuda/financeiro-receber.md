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

## Perguntas frequentes
**Quando um título aparece como "Vencido"?** Quando está em aberto e o vencimento é antes de hoje. Vencimento num sábado, domingo ou feriado passa a vencido só no dia seguinte.

**Posso apagar um título lançado errado?** Não. Título nunca é apagado: cancele com o motivo. Se já teve baixa, estorne a baixa antes.

**O que é a etiqueta "Implantação"?** Título que já estava em aberto quando a empresa começou a usar o sistema. Ele não entra no resultado do mês nem na pontualidade do cliente.
