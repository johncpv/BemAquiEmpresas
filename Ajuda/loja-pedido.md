---
titulo: Pedido de venda
rota: /app/loja/pedidos/$id
aba:
padrao: true
modulo: sales
---

## Para que serve
Levar o pedido do rascunho até o pagamento: confirmar (reserva o estoque), cobrar por boleto com Pix do Inter ou registrar o pagamento feito de outro jeito, e cancelar quando o cliente desiste.

## Como usar
- O botão em destaque no topo é sempre o **próximo passo**: **Confirmar pedido**, **Gerar cobrança** ou **Registrar pagamento**. No celular, ele fica fixo no rodapé.
- **Confirmar pedido** reserva todos os itens de uma vez: se faltar algum, nada é reservado e a tela diz quanto há de cada um.
- **Gerar cobrança** abre o diálogo já preenchido com o total, o vencimento e o e-mail do cliente. Se faltar CPF/CNPJ ou endereço de cobrança, use **Completar cadastro**.
- **Registrar pagamento** é para o valor recebido em dinheiro, na maquininha ou por Pix fora do boleto. Se havia boleto, o sistema confere no banco antes; se o boleto já foi pago, o pedido fica pago pela baixa do banco.
- Quando o pedido é pago, ele vira venda: o link **Ver venda** leva até ela.
- A faixa colorida no topo avisa problemas, como "Pago depois de cancelado" ou "Desconto para aprovar", com o que fazer.

## Perguntas frequentes
**Apareceu "Este pedido mudou desde que você abriu".** Outra pessoa (ou o banco) mexeu no pedido ao mesmo tempo. Toque em **Recarregar** e veja como ele ficou antes de tentar de novo.

**Cancelei um pedido pago. E o dinheiro?** Para cancelar um pedido pago, quem aprova em Vendas marca "Já devolvi o valor ao cliente". A venda é estornada (o estoque volta) e, no boleto, a devolução fica registrada no Financeiro.
