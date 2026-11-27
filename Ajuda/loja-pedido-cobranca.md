---
titulo: Pedido: cobrança
rota: /app/loja/pedidos/$id
aba: cobranca
padrao: false
modulo: sales
---

## Para que serve
Ver a cobrança do pedido: o título no Financeiro e o boleto com Pix do Inter, com a linha digitável e o Pix copia-e-cola para mandar ao cliente.

## Como usar
- Logo depois de gerar, o boleto fica "Solicitado" até o banco confirmar (alguns segundos). Depois aparecem a linha digitável e o Pix.
- **Copiar Pix** copia o código para colar no WhatsApp ou no e-mail. **Compartilhar boleto** abre o compartilhamento do celular.
- Quando o cliente paga, o banco avisa e o pedido fica pago sozinho, virando venda.
- Se o banco receber um valor diferente, aparece "Pagamento em conferência": o Financeiro confere antes de o pedido ficar pago.

## Perguntas frequentes
**O cliente pagou por Pix fora do boleto. O que faço?** Use **Registrar pagamento**. O sistema confere o boleto no banco antes e o cancela; se o boleto já tiver sido pago, o pedido fica pago pela baixa do banco.

**O boleto venceu sem pagamento.** O pedido volta a "Aberto", com o estoque ainda reservado pelo prazo normal. Gere uma nova cobrança ou cancele o pedido.
