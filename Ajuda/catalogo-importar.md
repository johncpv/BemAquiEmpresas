---
titulo: Importar produtos
rota: /app/catalogo/produtos/importar
aba: importar
padrao: true
modulo: catalog
---

## Para que serve
Subir a planilha dos seus produtos de uma vez, em vez de cadastrar um a um. Cada linha vira um produto com o item do estoque, o código de barras, o preço e, se você quiser, o saldo inicial.

## Como usar
1. Clique em **Baixar o modelo**. A primeira aba, "Como preencher", explica cada coluna: código interno, nome, unidade, NCM, código de barras, preço de venda, custo, saldo inicial e categoria.
2. Preencha a aba **Produtos**, uma linha por produto, até 20.000 linhas. Não mude os nomes das colunas.
3. Clique em **Enviar planilha**. Antes de gravar, a prévia mostra quantas linhas criam produto, quantas atualizam e quantas têm erro, com o motivo de cada uma ("Linha 14: código de barras inválido.").
4. Clique em **Importar**. Só as linhas sem erro entram. As com erro descem em **Baixar linhas com erro**: corrija nessa mesma planilha e envie de novo.
5. Com saldo inicial na planilha, o sistema pede a confirmação **Lançar saldo inicial** e o local de destino (o padrão é o local "Loja" ou, sem ele, o local padrão do estoque). Se preferir, use **Importar sem o saldo**.
6. No fim, o relatório mostra os criados, os atualizados e os ignorados, e oferece **Conferir preços** ou **Voltar aos primeiros passos**.

## Perguntas frequentes
**Posso importar a mesma planilha de novo?** Pode. O código interno, ou o código de barras quando não há código interno, acha o produto: ele é atualizado, não duplicado. O saldo inicial entra uma vez só por produto: reimportar não lança de novo.

**Por que meus preços foram ignorados?** Preço só entra para quem tem a permissão "Alterar preço" no Catálogo. Sem ela, a prévia avisa e a importação segue sem os preços.

**Por que o saldo inicial não foi lançado?** Lançar saldo exige a permissão "Ajustar estoque" no Estoque. A linha com saldo também precisa do custo (maior que zero), e item que controla lote ou série recebe o saldo pela tela do Estoque.

**O que muda num produto que já existe?** O nome, a categoria, um código de barras novo e o preço. A unidade e o NCM são do Estoque: mude na tela do item.

**A planilha é grande. Preciso esperar?** Acima de 500 linhas, a importação roda em segundo plano. Você pode sair da tela: o sino avisa quando terminar, com o link para o relatório.

**E produto com grade (cor, tamanho)?** A planilha importa produto simples. Crie as variações na tela do produto.
