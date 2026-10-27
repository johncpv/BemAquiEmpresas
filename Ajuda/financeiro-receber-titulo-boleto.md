---
titulo: Boleto do título
rota: /app/financeiro/receber/$id
aba: boleto
padrao: false
modulo: finance
---

## Para que serve
Emite o boleto com Pix do título pelo Banco Inter, mostra a linha digitável, o Pix copia-e-cola e o QR Code, envia ao cliente por e-mail e cancela.

## Como usar
- Clique em **Emitir boleto**. O valor é o saldo do título; vencimento, multa, juros, desconto e o e-mail do cliente já vêm preenchidos. Confira e confirme.
- O banco confirma em alguns segundos. Enquanto isso, a aba mostra "Aguardando"; se demorar, avisamos quando o boleto ficar pronto.
- Com o boleto emitido, use **Copiar** na linha digitável ou no Pix, **Baixar PDF**, **Enviar ao cliente** ou **Cancelar boleto** (com o motivo).
- Se o banco recusar, a aba mostra o motivo. Corrija o cadastro do cliente e clique em **Tentar de novo**: o sistema confere no banco antes de criar outra cobrança.

## Perguntas frequentes
**O boleto muda o título?** Não. Valor, situação e vencimento do título só mudam com a baixa.

**Título vencido pode ter boleto?** Pode, com vencimento novo a partir de hoje. O título mantém o vencimento original.

**Mudei o vencimento ou o valor do título. E o boleto?** O sistema cancela o boleto no banco e avisa o Financeiro. Emita outro se precisar.

**Desconto acima do limite?** Só quem aprova no Financeiro emite. Use "Usar os valores padrão" para voltar aos valores de Configurações → Cobrança.
