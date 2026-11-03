---
titulo: Ordem de pagamento — títulos
rota: /app/financeiro/ordens-de-pagamento/$id
aba: titulos
padrao: true
modulo: finance
---

## Para que serve
Ver os títulos da ordem, quanto vai ser pago e **para onde vai o dinheiro** (chave Pix, boleto ou conta).

## Como usar
- **Aprovar** e **Reprovar** aparecem para quem aprova. Reprovar pede o motivo.
- **Pagar** aparece para o Financeiro quando a ordem está aprovada: escolha a conta e a data, confira o total e confirme.
- Se o pagamento passar do aprovado, o sistema oferece **Reenviar para aprovação**.
- **Baixar PDF** gera o PDF da ordem como ela está agora: os títulos, para onde vai o dinheiro (com os dados mascarados, como na tela), o total, as alçadas, as decisões com quem e quando, e as linhas de assinatura. **Imprimir** abre o mesmo PDF numa aba nova para mandar à impressora.

## Perguntas frequentes
**O que são os avisos em âmbar?** O titular não é o fornecedor, o fornecedor foi cadastrado há pouco ou os dados de pagamento mudaram há pouco. Confira antes de aprovar.

**Os dados podem mudar depois do envio?** Não. A ordem guarda o retrato do envio.

**Como desfaço um pagamento?** Com **Estornar pagamento**, informando o motivo e confirmando o código do aplicativo autenticador. Só quem aprova em Caixa e pagamentos estorna. Os títulos voltam para A pagar.

**O que é "Pago e estornado em …"?** O título já foi pago numa ordem anterior e o pagamento foi estornado. Confira com o fornecedor se o valor voltou antes de aprovar de novo.

**Por que o PDF diz "Rascunho" ou "Cancelado"?** Enquanto a ordem não está aprovada (aguardando ou reprovada), o PDF sai com a marca **Rascunho**: ainda não vale como autorização de pagamento. Ordem cancelada ou estornada sai com a marca **Cancelado**. Aprovada ou paga, sai sem marca. Cada PDF gerado fica registrado na auditoria.
