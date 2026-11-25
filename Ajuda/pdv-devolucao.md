---
titulo: Caixa (PDV) — devolução, troca e vale-troca
rota: /pdv
aba: devolucao
padrao: false
modulo: pos
---

## Para que serve
Receber de volta um produto vendido, no todo ou em parte, e resolver na hora: com **vale-troca** (o padrão), **dinheiro** ou **estorno no cartão**. A troca é uma devolução em vale usada logo numa venda nova. Tudo a partir do cupom do cliente: sem a venda original não há devolução.

## Como usar
- Toque em **Devolução** no alto da tela de venda. Precisa de internet: a venda pode ser de outro caixa e é conferida no servidor.
- **Localize a venda** pelo número do cupom (o que sai impresso, ex.: LOJA-02.3-000127) ou pelo CPF do cliente, quando ele informou o CPF na compra.
- **Escolha os itens e as quantidades.** O caixa mostra quanto de cada item ainda pode voltar (o vendido menos o já devolvido).
- Para cada item, o **destino**: **Volta ao estoque** (entra de novo na loja) ou **Avariado (perda)** (entra e sai como perda). O motivo é obrigatório.
- Escolha o **reembolso**:
  - **Vale-troca** (padrão): sai impresso com código, valor e validade. Marque **Troca** quando o cliente já vai levar outro produto: o vale paga a venda nova.
  - **Dinheiro**: pede o supervisor e não pode passar do dinheiro que há no caixa.
  - **Estorno no cartão**: estorne na maquininha e informe o **NSU do estorno** (está no comprovante).
- Toque em **Concluir devolução**. Sai o recibo de devolução e, no vale, o vale-troca.
- O valor devolvido é o que o cliente pagou no item (com o desconto da venda), não o preço de hoje.

## Usar o vale-troca como pagamento
- Na tela de pagamento, toque em **Vale-troca**, digite o código e toque em **Consultar vale**. Aparecem o saldo e a validade.
- O valor vem com o menor entre o saldo e o que falta. Se o vale não cobrir a venda, receba o resto em outra forma; se sobrar, o restante continua no mesmo vale.
- Sem internet, a forma **Vale-troca** fica desabilitada com "Disponível com internet.".

## Perguntas frequentes
**"Prazo de devolução encerrado em dd/mm."** A venda passou do prazo de troca da loja (30 dias, se a loja não mudou). Só segue com o supervisor: ele digita o PIN neste caixa.

**"Nenhuma venda encontrada com este número ou CPF."** Confira o número no cupom. Venda feita sem CPF só é achada pelo número. Venda cancelada não volta pela devolução.

**"O valor passa do dinheiro em caixa."** Faça um suprimento antes ou devolva em vale-troca.

**"Vale vencido em ..." ou "Vale sem saldo."** O vale não serve mais. O vale tem validade de 90 dias (a loja pode mudar).

**O cliente perdeu o vale.** O código só existe no papel; o sistema guarda só o final dele. Sem o papel, o vale não é usado.

**Posso cancelar a venda depois de uma devolução?** Não. O restante volta por outra devolução.
