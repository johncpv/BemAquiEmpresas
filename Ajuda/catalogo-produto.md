---
titulo: Produto
rota: /app/catalogo/produtos/$id
aba: produto
padrao: true
modulo: catalog
---

## Para que serve
Ver e ajustar um produto: dados, variações (grade), códigos de barras, preço, foto, custo e margem.

## Como usar
- **Dados**: ajuste o nome de venda, a categoria, o PLU e onde o produto vende, e clique em **Salvar produto**. No produto pai, nome e categoria descem para as variações.
- **Variações**: clique em **Gerar grade**, informe os atributos (ex.: Cor: Azul, Preto; Tamanho: P, M, G) e veja quantas variações saem. Cada combinação ganha item no estoque, código de barras e preço próprios. Combinações que já existem ficam como estão.
- **Códigos de barras**: clique em **Adicionar código**. A caixa ou o fardo têm código próprio: informe em **Quantidade da embalagem** quantas unidades vêm nele.
- **Preço**: **Alterar preço** só aparece para quem tem essa permissão. No produto pai, marque **Aplicar a todas as variações** para usar o mesmo preço em toda a grade. O histórico mostra o preço anterior, o novo, a versão da tabela, quem mudou e quando.
- **Foto**: clique em **Enviar foto** (PNG, JPG ou WEBP até 5 MB). As variações usam a foto do pai.
- **Custo e margem**: para quem tem "Ver custo e margem", o painel mostra o custo médio do item, a margem e o markup. Se a categoria tem margem padrão, aparece o preço sugerido, com **Usar este preço**.
- Para tirar de venda, **Desative**. Produto já vendido não é excluído.

## Perguntas frequentes
**Por que não consigo mudar o preço na edição?** Mudar preço é uma permissão separada ("Alterar preço"). Quem só edita o produto não altera o preço.

**De onde vem o custo?** Do custo médio do item no estoque, que muda a cada entrada (compra ou ajuste).
