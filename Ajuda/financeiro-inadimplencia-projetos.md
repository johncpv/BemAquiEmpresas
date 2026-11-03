---
titulo: Inadimplência — por projeto
rota: /app/financeiro/inadimplencia
aba: projetos
padrao: false
modulo: finance
---

## Para que serve
Ver o que está vencido em cada projeto, para cobrar a parcela certa do contrato certo.

## Como usar
- Cada linha mostra o projeto, o cliente, o vencido por faixa de atraso, o total e o maior atraso.
- Clique no projeto para abrir a aba Financeiro dele.
- Títulos lançados sem projeto ficam na linha **Sem projeto**.

## Imprimir e exportar
- Clique em **Exportar**, na barra da lista, e escolha **Excel (.xlsx)** ou **PDF**.
- Saem o projeto, o cliente, as quatro faixas de atraso, o total vencido e o maior atraso, com **Sem projeto** no fim e a linha de total.
- O limite é de 10.000 linhas no Excel e 2.000 no PDF. Acima disso, o sistema avisa e pede para refinar os filtros (período, situação ou busca).
- Toda exportação fica registrada na auditoria (quem, quando, formato, quantas linhas e os filtros). Com a lista vazia, **Exportar** fica desabilitado.

## Perguntas frequentes
**Por que o total é igual ao da aba Por cliente?** São os mesmos títulos vencidos, agrupados de outro jeito.
