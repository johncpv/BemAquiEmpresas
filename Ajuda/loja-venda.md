---
titulo: Venda
rota: /app/loja/vendas/$id
aba:
padrao: true
modulo: sales
---

## Para que serve
Ver uma venda inteira: quem vendeu, em qual loja e caixa, os itens com o preço da tabela e o praticado, os pagamentos e tudo o que aconteceu com ela.

## Como usar
- No alto ficam a data e a hora, o total (e a margem, para quem pode ver custo), os chips do estado e o consumidor.
- **Consumidor:** o CPF ou CNPJ aparece mascarado. Toque em **Mostrar** para ver o documento inteiro; essa leitura fica registrada no histórico (quem viu e quando).
- **Itens:** cada item com a quantidade, o preço da tabela, o preço praticado, o desconto e o total. Quando o operador trocou o preço ou deu desconto, o motivo aparece embaixo do item, e o preço praticado diferente fica destacado.
- **Pagamentos:** a forma, o valor, o troco e, no cartão, só a bandeira, o NSU e a autorização (o sistema nunca guarda o número do cartão).
- **Trilha:** tudo o que aconteceu com a venda, em ordem (veja a ajuda da aba).
- Venda com divergência ou com falta de saldo no estoque mostra o atalho **Resolver nas Pendências do PDV**.

## Perguntas frequentes
**O preço praticado está diferente do preço da tabela.** O operador trocou o preço no caixa, com o motivo e, quando pede, a aprovação do supervisor. Veja a aba Trilha.

**Por que a margem não aparece?** Ela aparece só para quem tem "Ver custo e margem" em Vendas, e só quando o custo de todos os itens já saiu da baixa de estoque.

**Posso cancelar a venda por aqui?** Ainda não: o cancelamento é feito no caixa, com a aprovação do supervisor.
