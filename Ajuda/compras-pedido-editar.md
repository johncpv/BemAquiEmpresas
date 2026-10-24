---
titulo: Editar pedido de compra
rota: /app/compras/pedidos/$id/editar
aba:
padrao: true
modulo: purchasing
---

## Para que serve
Corrigir o pedido antes do recebimento: itens, quantidades, preços, fornecedor, condição, previsão de entrega, frete e IPI.

## Como usar
- No **rascunho**, **Salvar pedido** só grava. As mudanças valem no envio para aprovação.
- Pedido **aguardando aprovação**, **aprovado** ou **enviado** volta para a aprovação quando muda valor, fornecedor, condição, data ou itens. O diálogo mostra o novo total e o que acontece com o caixa: os títulos previstos de antes continuam até a nova aprovação, que os troca pelas parcelas novas.
- Pedido que já tinha sido enviado ao fornecedor fica com a faixa **Alterado depois do envio: reenvie ao fornecedor.**
- Se um título previsto do pedido já estiver numa ordem de pagamento, a edição é recusada e aparece o link **Ver OP**: retire o título da ordem antes.

## Perguntas frequentes
**Mudei só a observação. Volta para a aprovação?** Não. Só mudança de valor, fornecedor, condição, data ou itens volta para a aprovação.

**Dá para editar pedido recebido?** Não. Com recebimento, o pedido não muda; o encerramento do saldo fica no recebimento.
