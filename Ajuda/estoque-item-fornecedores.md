---
titulo: Fornecedores do item
rota: /app/estoque/itens/$id
aba: fornecedores
padrao: false
modulo: inventory
---

## Para que serve
Guardar o código e a unidade que cada fornecedor usa para este item, para a nota de entrada achar o item sozinha, sem converter na calculadora.

## Como usar
- Clique em **Vincular fornecedor** e escolha o fornecedor. Só aparecem terceiros ativos com o papel Fornecedor.
- Informe o código como sai na nota dele, a unidade dele (ex.: CX) e quanto ela vale na unidade de estoque.
- Clique em **Salvar código do fornecedor**.

## Perguntas frequentes
**O terceiro não aparece na busca.** Ele precisa ter o papel Fornecedor. Abra o cadastro do terceiro e marque o papel.

**Dois itens podem ter o mesmo código do mesmo fornecedor?** Não. Cada código de um fornecedor aponta para um item só.

**O que significa "Fornecedor excluído"?** O cadastro do fornecedor foi excluído. O vínculo fica no histórico, mas não é mais usado nas notas.
