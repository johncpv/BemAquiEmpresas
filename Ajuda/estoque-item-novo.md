---
titulo: Novo item
rota: /app/estoque/itens/novo
aba: novo
padrao: true
modulo: inventory
---

## Para que serve
Cadastrar um item novo, com os dados que estoque, compras, produção e nota fiscal precisam desde o primeiro movimento.

## Como usar
- Comece pelo **tipo**. Ele sugere o resto: matéria-prima vem com lote marcado, produto acabado com série marcada, e a garantia vem do padrão do tipo.
- Informe o **código** (use o mesmo do sistema antigo), o nome e a unidade de estoque.
- Em **Fiscal**, informe o NCM de 8 dígitos. Ele é obrigatório para matéria-prima, componente e produto acabado.
- Clique em **Salvar item**. O detalhe do item abre com os próximos passos.
- Para um item parecido com outro, abra o outro e clique em **Duplicar item**: tudo vem copiado, menos o código.

## Perguntas frequentes
**Não sei o NCM. E agora?** Confira na nota fiscal de compra do item ou pergunte à contadora. Material de consumo e serviço podem ficar sem NCM.

**O que é "Controla lote"?** Cada entrada recebe um número de lote, e cada saída diz de qual lote saiu. Útil para chapas, tintas e insumos com validade.

**O que é "Controla número de série"?** Cada unidade tem um número próprio, como o elevador entregue ao cliente.
