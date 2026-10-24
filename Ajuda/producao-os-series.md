---
titulo: OS de fabricação — séries
rota: /app/producao/os/$id
aba: series
padrao: false
modulo: production
---

## Para que serve
Ver o que a conclusão da OS deu de entrada no estoque: o movimento de entrada do produto acabado, o número de série de cada unidade, onde ela está e de quais lotes de matéria-prima ela foi feita.

## Como usar
- A aba aparece quando o Estoque está liberado nesta versão do sistema. Quem vê a produção vê a aba.
- O topo mostra a **entrada no estoque**: o número do movimento (quem vê o Estoque clica para abrir), a quantidade, o produto, o local e quando entrou.
- **Número de série:** uma linha por unidade, com a situação (**No estoque**, **Expedida** ou **Cancelada**) e o local. O formato é o código do item, o ano e a sequência, por exemplo `ELV-0001-2026-00001`.
- **Lotes de matéria-prima consumidos:** o material baixado para a OS menos o devolvido, por item e lote. Cada série herda os lotes da OS inteira. Material sem controle de lote aparece como "sem lote".
- O custo do produto acabado não aparece aqui. Ele fica na aba **Custos**, para quem vê custo e margem.
- Se a conclusão foi estornada, a entrada aparece como **Entrada estornada** e as séries como **Cancelada**.

## Perguntas frequentes
**Por que o produto não tem série?** O item não controla série. A entrada é por quantidade. Para controlar série, marque o item em Estoque → Itens → Dados antes de concluir a OS.

**O que é "Concluída antes do controle de estoque"?** A OS foi concluída antes de o sistema dar entrada do produto acabado. O produto não entrou no estoque e não tem séries. Se ele ainda está na fábrica, lance a entrada pelo Estoque.

**Dá para mudar uma série depois de concluir?** Não. As séries são editadas no diálogo **Concluir OS**, antes de confirmar. Para corrigir depois, estorne a conclusão e conclua de novo.
