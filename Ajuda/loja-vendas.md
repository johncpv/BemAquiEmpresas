---
titulo: Vendas
rota: /app/loja/vendas
aba:
padrao: true
modulo: sales
---

## Para que serve
Ver todas as vendas das lojas num lugar só: número, data e hora, loja, caixa, operador, total, formas de pagamento e o estado de cada uma. Daqui você abre qualquer venda para saber tudo o que aconteceu com ela.

## Como usar
- Escolha o período (a data é a da venda, no horário de Brasília; até um ano por vez). Sem escolher, a tela abre com as vendas de hoje.
- Filtre pela loja, pelo caixa, pela forma de pagamento e pelo estado. Para ver só as vendas de um operador, toque no nome dele na lista; **Tirar o filtro** volta para todos.
- Os filtros ficam no endereço da página: copie o link para mandar a mesma lista para outra pessoa.
- O rodapé mostra os totais **do filtro inteiro**, não só da página: quantas vendas, quantas canceladas e o total das vendas feitas (as canceladas ficam de fora do total).
- Os chips mostram o estado: **Concluída**, **Cancelada**, **Devolvida** (ou em parte), **Estoque: falta de saldo** (a venda saiu sem saldo suficiente no estoque), **Com divergência** (o pago não bateu com o total) e **Feita sem internet** (o caixa estava sem rede e enviou depois).
- Toque em **Exportar** para baixar a lista em Excel ou PDF com o mesmo filtro. Lista grande demais pede para diminuir o período.
- Toque no número da venda para abrir o detalhe, com os itens, os pagamentos e a trilha.

## Perguntas frequentes
**Por que não vejo a margem?** Custo e margem aparecem só para quem tem a permissão "Ver custo e margem" em Vendas. Sem ela, as colunas não aparecem nem na lista nem na planilha.

**Por que o CPF aparece com asteriscos?** Para proteger o dado do cliente, a lista e a planilha mostram o documento mascarado. O documento inteiro fica no detalhe da venda, com um toque em **Mostrar**, e quem viu fica registrado.

**Vejo vendas de outras lojas. Está certo?** Sim: por enquanto, quem tem acesso a Vendas vê as vendas de todas as lojas da empresa. A restrição por loja está em estudo.

**Uma venda feita agora não apareceu.** O caixa pode estar sem internet: a venda fica guardada no aparelho e chega quando a rede volta. Veja as Pendências do PDV.

**A venda está "Com divergência". O que faço?** Abra a venda e toque em **Resolver nas Pendências do PDV**: lá você vê o que não bateu e decide.
