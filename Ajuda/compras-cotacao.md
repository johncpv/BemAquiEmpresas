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

## Perguntas frequentes
**Posso mudar uma resposta?** Enquanto a cotação estiver aberta, sim: **Alterar resposta**. Cada alteração fica no histórico, com quem lançou e quando.

**Por que não consigo salvar?** Acima do limite, falta a proposta anexada. Confira também a condição de pagamento e o prazo de cada item com preço.

**Depois de concluída, dá para mudar?** Não. A cotação concluída não muda mais; para cotar de novo, crie outra cotação.
