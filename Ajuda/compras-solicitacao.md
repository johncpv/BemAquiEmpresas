---
titulo: Solicitação de compra — itens
rota: /app/compras/solicitacoes/$id
aba: itens
padrao: true
modulo: purchasing
---

## Para que serve
Ver o que a solicitação pede, para quando e em que pé está cada item, e ajustar ou cancelar o que ainda está em aberto.

## Como usar
- Cada linha mostra o item, a quantidade na unidade de estoque, a necessidade e a situação (**Aberta**, **Em cotação**, **Pedida** ou **Cancelada**). No celular, cada linha vira um cartão.
- **Editar linha** muda a quantidade e a necessidade de uma linha em aberto. Numa linha automática (falta da BOM ou estoque mínimo), a linha ganha a marca **Editada à mão** e o sistema não muda mais a quantidade nem a data dela.
- Se você diminuir a linha da falta abaixo do que a OS precisa, aparece o aviso "A OS {número} continua com falta de {x}.".
- **Cancelar linha** e **Cancelar solicitação** pedem o motivo. Solicitação com item em cotação ou pedido não é cancelada inteira: cancele só as linhas em aberto.
- A faixa de fatos mostra o destino, a OS, a necessidade mais próxima e quem pediu (ou **Sistema · Falta da BOM**, **Sistema · Estoque mínimo**).
- **Cotar estes itens** (com a permissão de criar em Compras) abre a nova cotação com as linhas em aberto e os últimos fornecedores desses itens já escolhidos. As linhas passam a **Em cotação**.
- **Pedir direto ao último fornecedor** (com a permissão de criar em Compras) abre o novo pedido de compra com as linhas em aberto, o fornecedor do último pedido de cada item e o último preço pago. As linhas atendidas passam a **Pedida**.

## Perguntas frequentes
**A linha da falta mudou sozinha. Por quê?** A falta da OS mudou: chegou material, a BOM foi revista ou a OS foi cancelada. A linha em aberto acompanha a falta. Linha em cotação ou pedido não muda; quem compra recebe o aviso.

**Cancelei uma linha automática. Ela volta?** Não. A linha cancelada por você não é recriada pela falta da BOM; se ainda precisar comprar, faça uma solicitação manual. No estoque mínimo, se o item continuar abaixo do mínimo, o job do dia seguinte pede de novo.

**Isso mexe no estoque ou no financeiro?** Não. A solicitação não reserva material nem gera título a pagar; isso nasce no pedido de compra aprovado.
