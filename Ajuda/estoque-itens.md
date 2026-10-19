---
titulo: Itens
rota: /app/estoque/itens
aba: itens
padrao: true
modulo: inventory
---

## Para que serve
Cadastrar tudo o que a empresa compra, fabrica, consome ou vende: matérias-primas, componentes, produtos acabados, material de consumo e serviços. Estoque, compras, produção e nota fiscal usam o mesmo cadastro.

## Como usar
- Busque pelo código ou por parte do nome. A busca ignora acentos.
- Filtre por tipo, categoria ou situação. Os filtros ficam no endereço da página, então dá para compartilhar a lista filtrada.
- Clique no item para abrir os dados. Use **Novo item** para cadastrar.
- Pela busca da barra do topo (Ctrl K), digite duas letras do código ou do nome para ir direto a um item.

## Perguntas frequentes
**Por que um item não aparece na lista?** Ele pode estar inativo. Ligue o filtro **Inativos**.

**Posso ter dois itens com o mesmo código?** Não. O código é único, para não haver confusão no estoque e nas notas.

**Onde vejo o saldo do item?** O saldo entra com o módulo de movimentos de estoque.

**Os produtos do Dolibarr já estão aqui?** Sim, depois da importação. Eles aparecem com a etiqueta "Importado do Dolibarr" e a referência de origem. O custo médio e o saldo não vêm do Dolibarr: o saldo inicial é digitado no estoque.

**Por que o item importado está inativo?** Matéria-prima, componente e produto acabado sem NCM entram inativos. Abra o item, preencha o NCM de 8 dígitos, salve e clique em **Reativar**.
