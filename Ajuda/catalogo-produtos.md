---
titulo: Produtos
rota: /app/catalogo/produtos
aba: produtos
padrao: true
modulo: catalog
---

## Para que serve
Cadastrar o que a loja vende: o nome que aparece no caixa, o código de barras, o preço e a categoria de cada produto. Cada produto fica ligado a um item do estoque, que guarda a unidade, o custo e o saldo.

## Como usar
- Clique em **Novo produto**. O item do estoque nasce junto, com o mesmo nome, sem digitar duas vezes. Tem muitos produtos? Use **Importar planilha**.
- Busque pelo nome, pelo PLU da balança ou pelo código de barras. A busca acha também as variações (cor, tamanho) e mostra o produto pai.
- Use os filtros **Categoria** e **Preço → Só os sem preço** para achar o que falta completar. Produto sem preço não vende no caixa.
- **Exportar** gera o Excel ou o PDF com a mesma busca e os mesmos filtros da tela.
- Clique no nome para abrir a ficha: dados, variações, códigos de barras, preço e histórico.

## Perguntas frequentes
**Por que não vejo custo e margem?** Custo e margem só aparecem para quem tem "Ver custo e margem" no Catálogo. Sem essa permissão, a tela e a exportação nem trazem esses valores.

**Produto com grade aparece uma vez só?** Sim. A lista mostra o produto pai com a quantidade de variações; cada variação tem a sua ficha, com código e preço próprios.

**Posso importar uma planilha?** Pode. Clique em **Importar planilha**, baixe o modelo, preencha e envie. A prévia mostra o que entra e o que tem erro antes de gravar.
