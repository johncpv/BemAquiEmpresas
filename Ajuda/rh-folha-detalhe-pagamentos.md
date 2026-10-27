---
titulo: Pagamentos da folha
rota: /app/rh/folha/$id
aba: pagamentos
padrao: false
modulo: hr
---

## Para que serve
Ver os títulos a pagar que o fechamento da folha gerou e mandar para ordem de pagamento: um título por funcionário (o salário líquido) e um por guia, no nome do órgão.

## Como usar
- Feche a folha com **Fechar folha**. O diálogo mostra o que falta resolver (holerite sem lançamento, funcionário sem conta para pagamento, guia sem órgão), cada um com o link **Resolver**.
- Confira a **data de pagamento**: ela vem sugerida pelo tipo da folha (Mensal no 5º dia útil do mês seguinte, adiantamento no dia 20, 13º em 30/11 e 20/12; Férias e Rescisão, informe). Dia não útil antecipa. Os padrões ficam em Configurações → Parâmetros → RH, "A confirmar com a contadora".
- Confirme com o código do aplicativo autenticador: fechar a folha pede o 2FA de novo.
- Na aba **Pagamentos**, use **Criar ordem de pagamento dos salários** (os salários já vêm marcados) e **Criar ordem de pagamento das guias**.
- Para mudar algo depois de fechar, use **Reabrir folha** e informe o motivo. Os títulos ainda em aberto são cancelados; com pagamento em andamento, estorne ou cancele a ordem antes.

## Avisos do fechamento
- **Novos na folha:** é a primeira folha desses funcionários. Confira com mais atenção.
- **Conta alterada:** a conta para pagamento foi trocada há pouco e está em quarentena. O salário fica **fora do pagamento automático** até a revisão da conta no RH (o atalho **Revisar conta** leva até ela).

## Perguntas frequentes
**Quem fecha pode pagar?** Não. **Quem fecha não paga:** quem fechou a folha não registra o pagamento dela no Financeiro. A exceção é o Proprietário único, e o pagamento fica com a marca de autopagamento.

**A conta do funcionário aparece no Financeiro?** Não. O Financeiro vê só o final da conta. Os dados completos aparecem para quem paga, no momento do "Pagar".

**Por que vejo "Título gerado" em vez da situação?** A situação do pagamento aparece para quem acompanha o Financeiro.

**Quando a folha fica "Paga"?** Quando todos os títulos dela forem pagos. Se um pagamento for estornado, a folha volta a "Fechada".
