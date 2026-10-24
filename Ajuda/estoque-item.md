---
titulo: Item
rota: /app/estoque/itens/$id
aba: dados
padrao: true
modulo: inventory
---

## Para que serve
Ver e alterar os dados de um item: identificação, dados fiscais, estoque mínimo, lote, série e garantia.

## Como usar
- Altere o que precisar e clique em **Salvar item**.
- O custo médio aparece só para leitura: ele é calculado pelos movimentos de estoque.
- Para quem vê Compras, **Última compra** mostra o preço e o fornecedor do último pedido aprovado do item, com o link para o pedido.
- **Duplicar item** abre um item novo com estes dados, sem o código.
- **Inativar item** tira o item dos seletores. Ele continua nos documentos que já o usam.

## Perguntas frequentes
**Por que não consigo trocar a unidade de estoque?** Item que já teve movimento não troca a unidade, para não misturar quantidades. O mesmo vale para desligar o lote ou a série.

**Posso deixar um item sem NCM?** Só se ele estiver inativo, ou se for material de consumo ou serviço. Para reativar, preencha o NCM.

**Por que não consigo excluir?** Item com movimento, saldo, estrutura de produto ou documento só pode ser inativado.
