---
titulo: Lojas e caixas
rota: /app/loja/lojas
aba: lojas
padrao: true
modulo: sales
---

## Para que serve
Cadastrar as lojas e os caixas de cada uma, e ver qual aparelho vende em cada caixa. Toda venda do balcão sai de um caixa conhecido, e o número da venda começa pelo código da loja e o número do caixa.

## Como usar
- Clique em **Nova loja** e informe o código curto (2 a 6 letras ou números, como LOJA ou CENTRO), o nome, o **local de estoque** de onde sai a mercadoria e a **tabela de preço** do balcão.
- Dentro da loja, clique em **Novo caixa** e informe o número (01, 02...), a **conta do tipo Caixa** onde entra o dinheiro e o **modo de tela**: Toque (grade de produtos) ou Teclado e leitor (código de barras).
- Cada caixa tem a sua conta do tipo Caixa: duas lojas ou dois caixas não usam a mesma. Crie a conta em Financeiro → Contas e caixas, se faltar.
- A coluna do aparelho mostra o último contato. "Sem contato há N h" avisa quando o caixa está desligado ou sem internet há algum tempo.
- Para ligar um aparelho ao caixa, abra **/pdv** no computador ou tablet da loja e toque em **Registrar este aparelho**.
- Na loja, **Aviso de sangria a partir de** (padrão R$ 1.000,00) faz o caixa pedir uma sangria quando o dinheiro na gaveta passa do valor; **Teto de dinheiro no caixa** (opcional, maior que o aviso) bloqueia o recebimento em dinheiro até a sangria. O caixa recebe os valores na próxima sincronização e nunca mostra ao operador quanto há na gaveta.

## Perguntas frequentes
**O código da loja pode mudar?** Pode, mas as vendas já feitas continuam com o número antigo. Prefira escolher o código certo no começo.

**Como desativo um caixa?** Bloqueie o aparelho dele antes (em Configurações → Segurança → Aparelhos) e depois desmarque **Caixa ativo**.

**A loja "Loja" apareceu sozinha.** Ela vem com o perfil Comércio, ligada ao local "Loja", à tabela "Balcão" e à conta "Caixa PDV 01". Edite à vontade.
