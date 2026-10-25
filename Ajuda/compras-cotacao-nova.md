---
titulo: Nova cotação
rota: /app/compras/cotacoes/nova
aba:
padrao: true
modulo: purchasing
---

## Para que serve
Abrir uma cotação com os itens das solicitações (ou digitados) e os fornecedores que vão cotar.

## Como usar
- Vinda das solicitações (**Cotar estes itens** ou **Cotar** na visão **Por item**), a cotação traz um item por linha, com as quantidades das solicitações somadas, e a **Última compra** de cada item como referência.
- Os fornecedores sugeridos são os últimos que venderam esses itens, até 3, com a tag **Sugerido**. Tire quem não quiser e inclua outros em **Incluir fornecedor**: só aparecem terceiros ativos com o papel Fornecedor.
- Sem solicitação, digite os itens e as quantidades (na unidade de estoque ou noutra com conversão).
- **Criar cotação** grava com o número COT-AAAA-0001. As solicitações passam para **Em cotação** e não entram em outra cotação nem em pedido direto.

## Perguntas frequentes
**Posso incluir um fornecedor depois?** Pode, na aba **Respostas**, enquanto a cotação estiver aberta. O mesmo fornecedor não se repete.

**E se eu desistir?** Cancele a cotação com o motivo: as solicitações voltam para **Aberta**.
