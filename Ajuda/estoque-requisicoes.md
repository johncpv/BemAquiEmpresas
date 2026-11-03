---
titulo: Requisições de material
rota: /app/estoque/requisicoes
aba: requisicoes
padrao: true
modulo: inventory
---

## Para que serve
Acompanhar o material que sai do estoque para as OS: as requisições baixadas, os pedidos dos operadores, as requisições que esperam aprovação e as devoluções.

## Como usar
- A lista abre em **Aguardando você**: os pedidos de material dos operadores (para quem baixa no Estoque) e as requisições com consumo acima da BOM esperando aprovação (para quem aprova no Estoque). Em **Mostrar**, escolha **Todas as requisições** para ver o resto.
- **Para separar hoje**, no topo, mostra as OS em aberto com material reservado a baixar. As que têm pedido do operador vêm primeiro, com a faixa âmbar. Clique na OS para abrir a aba **Material** e requisitar.
- Clique no número da requisição para ver o detalhe, confirmar ou recusar o pedido, aprovar ou estornar.
- No celular, cada requisição vira um cartão com o número, a OS e a situação. Toque para ver o material, o valor e quem pediu.
- Situações: **Pedido do operador** (ainda não baixou nada), **Aguardando aprovação** (consumo acima da BOM acima de R$ 1.000,00), **Baixada**, **Recusada** e **Estornada**.

## Imprimir e exportar
- Clique em **Exportar** e escolha **Excel (.xlsx)**. Valem o "Mostrar" (inclusive **Aguardando você**), a situação e o tipo da tela, com todas as páginas.
- A planilha traz número, tipo, OS, etapa, projeto, itens, quem pediu, quando e a situação. Não traz valor.
- A folha de separação de cada requisição sai na tela da requisição (**Baixar PDF**).
- O limite é de 10.000 linhas no Excel e 2.000 no PDF. Acima disso, o sistema avisa e pede para refinar os filtros.
- Toda exportação fica registrada na auditoria (quem, quando, formato, quantas linhas e os filtros). Com a lista vazia, **Exportar** fica desabilitado.

## Perguntas frequentes
**O pedido do operador já tirou o material do estoque?** Não. O pedido só avisa o almoxarifado. O material sai quando alguém do Estoque confirma o pedido, escolhendo o lote e a quantidade.

**Por que uma requisição está aguardando aprovação?** O consumo acima da BOM (o que passou do reservado, mais o material extra) passou de R$ 1.000,00 pelo custo médio. Outra pessoa com a ação **Aprovar** no Estoque decide. Até lá nada sai do estoque.

**A lista está vazia. E agora?** Não há pedido nem aprovação esperando você. Use **Ver OS com material reservado** para abrir as OS e requisitar o material.
