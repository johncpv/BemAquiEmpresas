---
titulo: Conversões do item
rota: /app/estoque/itens/$id
aba: conversoes
padrao: false
modulo: inventory
---

## Para que serve
Dizer quanto vale cada unidade em que o item é comprado ou consumido, na unidade de estoque. Exemplo: a chapa é estocada em quilo e consumida em metro quadrado.

## Como usar
- Clique em **Nova conversão**. A unidade de estoque já vem num dos lados.
- Escolha a outra unidade e informe o fator: "1 M2 = 15,7 KG". A prévia mostra quanto valem 100 da unidade de estoque.
- Clique em **Salvar conversão**. Não precisa cadastrar o caminho inverso: o sistema calcula.

## Perguntas frequentes
**Mudei o fator. Os movimentos antigos mudam?** Não. Os movimentos guardam a quantidade na unidade de estoque. O fator novo vale para os próximos documentos.

**Posso converter entre duas unidades que não são a de estoque?** Cadastre as duas em relação à unidade de estoque. O sistema passa por ela para converter de uma para a outra.

**Quantas casas o resultado tem?** O fator aceita até 6 casas, e o resultado sai com 4.
