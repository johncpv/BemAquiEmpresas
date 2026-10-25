---
titulo: Capacidade de produzir
rota: /app/estoque/capacidade
aba: capacidade
padrao: true
modulo: inventory
---

## Para que serve
Saber quantos equipamentos iguais a um já feito dá para produzir com o estoque de hoje e o que falta, item a item, para chegar a uma quantidade. Serve para prometer prazo com base no que existe e comprar só a diferença. Nada é gravado: é só uma conta.

## Como usar
- Escolha o **Produto**. A tela usa a BOM liberada mais nova desse produto, com a tag **Sugerido**. Para comparar, troque a **BOM de referência** por outra OS (vale também OS já concluída).
- Informe a **Quantidade desejada** (começa em 1).
- O quadro de cima mostra **Dá para produzir hoje**: o menor número de unidades entre os itens da BOM. **Limita** diz qual item segura a produção.
- Com acesso a Compras, aparece a linha **Com os pedidos em aberto**: quanto daria para produzir se todos os pedidos de compra em aberto chegassem. Ela não entra no total nem nas faltas.
- Cada item mostra a necessidade por unidade (com a perda, na unidade de estoque), a necessidade para a quantidade, o físico, o reservado, o disponível, quantas unidades ele permite e a **Falta**.
- Quem vê custo de projeto também vê o **valor estimado da falta**, pelo custo médio de hoje.
- **Gerar solicitação de compra** abre a solicitação já preenchida com os itens e as quantidades em falta, destino **Estoque geral** (você pode trocar). Nada é gravado até você clicar em **Enviar solicitação**.
- Produto, OS e quantidade ficam no endereço da página, para você guardar ou mandar o link.
- No celular aparecem item, necessidade, disponível e falta. Toque na linha para ver o resto.

## Perguntas frequentes
**De onde vem o disponível?** Do físico de todos os locais menos o que já está reservado para as OS abertas. É o mesmo disponível da tela Saldos.

**Por que um item aparece como Não calculado?** O item está inativo, foi excluído ou a BOM usa uma unidade sem conversão para a unidade de estoque. O aviso na linha diz o que fazer; os outros itens continuam calculados.

**Os pedidos de compra em aberto entram no total?** Não. O total e as faltas usam só o que existe hoje. A linha "Com os pedidos em aberto" é só informativa.

**Isso reserva material?** Não. A capacidade só lê o estoque. A reserva acontece quando a BOM da OS é liberada.
