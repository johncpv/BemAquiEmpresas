---
titulo: Cotação — respostas
rota: /app/compras/cotacoes/$id
aba: respostas
padrao: true
modulo: purchasing
---

## Para que serve
Registrar o que cada fornecedor respondeu: os preços, o frete, o IPI, a condição de pagamento e a validade da proposta.

## Como usar
- Cada fornecedor aparece com a situação: **Aguardando resposta**, **Respondida** ou **Não cotou**, e com **Lançada por {nome} em {data}** quando alguém registrou a resposta.
- **Registrar resposta** abre os itens: o preço unitário (na unidade de estoque ou, quando o item tem a unidade do fornecedor cadastrada, por essa unidade; o sistema divide pelo fator), a quantidade disponível (vem a pedida) e o prazo em dias. Embaixo, o frete, o IPI %, a condição (vem a do cadastro do fornecedor) e a validade.
- **Copiar preços da última compra** preenche os preços vazios com o último preço pago, como ponto de partida.
- O rodapé mostra o **Custo total** ao vivo: itens + frete + IPI. Acima do limite (padrão R$ 5.000,00), a resposta só é salva com a **proposta do fornecedor anexada**: o botão de anexar aparece ali mesmo.
- **Não cotou** marca o fornecedor sem resposta. **Remover** tira da cotação quem ainda não respondeu.
- Com as respostas registradas, vá para a aba **Comparativo** para escolher os vencedores.

## Imprimir e exportar
- **Baixar pedido de cotação**, no cabeçalho, abre a lista dos fornecedores com a situação da resposta: escolha um e baixa o PDF só para ele. Na aba Respostas, cada fornecedor tem o próprio **Baixar PDF**.
- O pedido de cotação leva o número, a data, o fornecedor (razão social, CNPJ e contato), os itens com o código e a unidade do fornecedor quando cadastrados, o local de entrega (o endereço da empresa) e, por item, os campos em branco para o preço, o prazo e a condição. Embaixo, frete, IPI, condição de pagamento e validade em branco, e **Resposta por** com o seu nome e e-mail.
- O PDF de um fornecedor nunca traz preço, nome ou resposta de outro fornecedor, nem a última compra.
- Enquanto ninguém respondeu, os próximos passos oferecem **Baixar pedido de cotação** para cada fornecedor que aguarda.
- Só a cotação aberta gera o pedido de cotação. Concluída ou cancelada, a mensagem é "A cotação não está aberta.".

## Perguntas frequentes
**Posso mudar uma resposta?** Enquanto a cotação estiver aberta, sim: **Alterar resposta**. Cada alteração fica no histórico, com quem lançou e quando.

**Por que não consigo salvar?** Acima do limite, falta a proposta anexada. Confira também a condição de pagamento e o prazo de cada item com preço.

**Depois de concluída, dá para mudar?** Não. A cotação concluída não muda mais; para cotar de novo, crie outra cotação.
