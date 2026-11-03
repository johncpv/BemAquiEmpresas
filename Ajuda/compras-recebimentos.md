---
titulo: Recebimentos — a receber
rota: /app/compras/recebimentos
aba: a-receber
padrao: true
modulo: purchasing
---

## Para que serve
Ver o que ainda vai chegar dos fornecedores e dar entrada no material conferindo com o pedido. A tela abre nesta aba.

## Como usar
- A lista traz os pedidos aprovados, enviados ou parcialmente recebidos que ainda têm saldo, da previsão de entrega mais próxima para a mais distante.
- Pedido com a previsão já vencida ganha a faixa vermelha na lateral e o chip **Atrasado {n} d**.
- Clique em **Registrar recebimento** na linha do pedido. Quem não tem a permissão Registrar recebimento vê **Ver pedido**.
- No celular, cada pedido vira uma linha com o número, o fornecedor, a previsão e a ação; toque na seta para ver os itens e o total.

## Imprimir e exportar
- Clique em **Exportar** e escolha **Excel (.xlsx)**. Vale a busca da tela.
- A planilha traz pedido, fornecedor, previsão, o **saldo a receber** (o que falta de cada item pelo preço do pedido) e a situação.
- Exportar só gera **Excel (.xlsx)** nesta lista, com todas as páginas. O limite é de 10.000 linhas; acima disso, o sistema pede para refinar os filtros.
- Toda exportação fica registrada na auditoria (quem, quando, formato, quantas linhas e os filtros). Com a lista vazia, **Exportar** fica desabilitado.

## Perguntas frequentes
**O pedido aprovado não aparece aqui.** Ele aparece quando tem saldo a receber. Pedido em aprovação, em rascunho, cancelado ou todo recebido fica fora.

**Quem pode registrar o recebimento?** Quem tem a permissão Registrar recebimento em Compras, como o grupo Almoxarifado. Quem pediu ou aprovou o pedido não registra o recebimento dele.