---
titulo: Novo pedido de compra
rota: /app/compras/pedidos/novo
aba:
padrao: true
modulo: purchasing
---

## Para que serve
Criar o pedido ao fornecedor, à mão ou a partir das solicitações em aberto, já preenchido com o mais provável.

## Como usar
- Vindo de uma solicitação (**Pedir direto ao último fornecedor**), o pedido traz os itens e as quantidades das linhas em aberto, o fornecedor do último pedido do item e o último preço pago, com a marca **Sugerido**. Sem pedido anterior, o preço sugerido é o custo médio do item.
- O **fornecedor** precisa ter o papel Fornecedor no cadastro. A **condição de pagamento** vem do cadastro dele; troque se precisar.
- A **previsão de entrega** vem com hoje mais o prazo padrão de compra (Configurações → Parâmetros). As parcelas previstas no caixa contam a partir dela.
- O **destino** vem das solicitações: o projeto, quando todas são do mesmo projeto; senão, o estoque geral. No pedido à mão, escolha o projeto em andamento ou marque **Estoque geral**.
- Informe o **frete** e o **IPI** (percentual sobre os itens). O rodapé mostra o subtotal, o frete, o IPI e o total enquanto você digita; o sistema recalcula ao salvar.
- **Criar pedido** grava o rascunho com o número PC. Confira e clique em **Enviar para aprovação** no pedido.

## Perguntas frequentes
**Posso pedir um serviço aqui?** Não. Serviço não entra no pedido de compra: lance um título a pagar no Financeiro.

**A solicitação muda quando crio o pedido?** Sim. As linhas atendidas passam a Pedida. Se o pedido for cancelado, elas voltam a Aberta.
