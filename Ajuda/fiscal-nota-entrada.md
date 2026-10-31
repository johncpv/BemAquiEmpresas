---
titulo: Nota de entrada — itens e de-para
rota: /app/fiscal/entrada/$id
aba: itens
padrao: true
modulo: fiscal
---

## Para que serve
Liga cada item que o fornecedor faturou ao item do nosso estoque (o **de-para**), com a quantidade e o preço convertidos para a unidade de estoque. O de-para fica lembrado: na próxima nota do mesmo fornecedor, o item já vem ligado.

## Como usar
1. Se o fornecedor ainda não está cadastrado, clique em **Cadastrar fornecedor com os dados da nota**. Se está cadastrado sem o papel de fornecedor, clique em **Adicionar papel**.
2. Para cada item, veja a marca: **Lembrado** (de-para de uma nota anterior) ou **Sugerido** (mesmo NCM ou nome parecido). Aceite a sugestão, escolha outro item ou marque **Sem item de estoque** (serviço ou despesa, com a conta do plano).
3. Quando a unidade do fornecedor é diferente da nossa (caixa e unidade, por exemplo), informe o **fator de conversão**: quanto 1 unidade do fornecedor vale na unidade de estoque.
4. Vá para a aba **Pedido e divergências**.

## Perguntas frequentes
**O item não existe no estoque.** Quem pode cadastrar itens clica em **Cadastrar item** e volta para ligar. Ou marque **Sem item de estoque**, se não for material de estoque.

**Errei o de-para.** Enquanto a nota não foi conferida, escolha o item certo de novo. Depois de conferida, a nota não muda.
