---
titulo: Novo produto
rota: /app/catalogo/produtos/novo
aba: produto-novo
padrao: true
modulo: catalog
---

## Para que serve
Cadastrar um produto para vender, já ligado ao item do estoque.

## Como usar
- **Nome de venda**: como o produto aparece no caixa e na etiqueta. É também o nome do item novo no estoque.
- **Categoria**: vem a **Geral**. Troque se já criou categorias como Bebidas ou Mercearia.
- **PLU**: só para produto pesado na balança. A balança imprime a etiqueta com esse código curto.
- **Item do estoque**:
  - **Criar o item agora**: escolha a unidade (vem UN) e, se tiver, o NCM. O código interno é gerado sozinho (MER-000001).
  - **Usar um item que já existe**: só aparecem itens que ainda não têm produto.
- **Código de barras**: leia com o leitor ou digite os números. Em **Quantidade da embalagem**, deixe 1 para a unidade avulsa; para o código da caixa com 12, informe 12.
- **Preço**: só aparece para quem tem "Alterar preço". Pode ficar para depois.
- Clique em **Salvar produto**. A ficha abre com os próximos passos: gerar a grade, adicionar outro código ou definir o preço.

## Perguntas frequentes
**O código de barras foi recusado. Por quê?** O último número do código é um dígito de conferência. Se ele não bate, aparece "Código de barras inválido. Confira os dígitos.". Se o código já é de outro produto, a mensagem diz qual.

**Preciso do NCM?** Para vender com cupom não fiscal, não. Para emitir nota fiscal, sim: preencha depois na ficha do item no estoque.
