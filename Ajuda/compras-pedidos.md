---
titulo: Pedidos de compra
rota: /app/compras/pedidos
aba:
padrao: true
modulo: purchasing
---

## Para que serve
Acompanhar os pedidos aos fornecedores, do rascunho à aprovação e ao recebimento, e decidir os que estão na sua alçada.

## Como usar
- Cada linha mostra o número, o fornecedor, o destino (projeto ou **Estoque geral**), o total, a previsão de entrega e a situação: **Rascunho**, **Aguardando aprovação**, **Aprovado**, **Enviado**, **Parcialmente recebido**, **Recebido** ou **Cancelado**. No celular ficam o número, o fornecedor, o total e a situação; toque na linha para ver o resto.
- Quem aprova abre a lista em **Aguardando você**: só os pedidos que você pode decidir agora, no nível em que estão. Troque em **Mostrar** para ver os abertos ou todos.
- Pedido aprovado com a previsão de entrega vencida e ainda não recebido ganha a faixa vermelha e o texto **Atrasado {n} d**.
- **Novo pedido** abre o formulário em branco. Para pedir o que as solicitações pedem, abra a solicitação e use **Pedir direto ao último fornecedor**: o pedido vem preenchido.

## Imprimir e exportar
- Clique em **Exportar** e escolha **Excel (.xlsx)**. Valem o **Mostrar** (inclusive **Aguardando você**), a situação, o projeto e a busca da tela.
- A planilha traz número, fornecedor, destino, total, previsão de entrega, situação e as datas de aprovação e de envio. A última linha, **Total**, soma os pedidos da planilha.
- Exportar só gera **Excel (.xlsx)** nesta lista, com todas as páginas. O limite é de 10.000 linhas; acima disso, o sistema pede para refinar os filtros.
- Toda exportação fica registrada na auditoria (quem, quando, formato, quantas linhas e os filtros). Com a lista vazia, **Exportar** fica desabilitado.

## Perguntas frequentes
**Por que um pedido não aparece em Aguardando você?** Ele está em outro nível, fora da sua faixa de valor, ou foi você quem pediu. Quem pediu não aprova o próprio pedido, a não ser que seja o único Proprietário com a permissão de aprovar.

**O pedido já mexe no caixa?** Só depois de aprovado. A aprovação lança um título previsto por parcela da condição de pagamento, com a marca Estimado, no fluxo de caixa.
