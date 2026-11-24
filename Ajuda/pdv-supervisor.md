---
titulo: Caixa (PDV) — chamar supervisor
rota: /pdv
aba: supervisor
padrao: false
modulo: pos
---

## Para que serve
Algumas ações do caixa pedem a senha de um supervisor para ninguém fazer sozinho: **desconto acima do limite da loja** (F5), **troca de preço** (F11), **remoção de um item que não é o último lançado** (F6) e **cancelamento de uma venda já concluída** (Reimprimir cupom → Cancelar venda).

## Como usar
- Escolha o motivo na lista (ou "Outro" e escreva). O motivo é obrigatório.
- Se você tem a permissão da ação, ela sai direto, registrada com o seu nome. Senão, aparece **Chamar supervisor** com o resumo (o item e de quanto para quanto).
- O supervisor toca no nome dele e digita o PIN de 6 números **neste caixa**. Aprovado, a ação acontece; você continua no caixa, sem sair.
- **Sem internet:** o supervisor digita o **código sem internet** dele (criado em Meu perfil → Segurança), não o PIN. O caixa confere no aparelho e aceita só desconto, cancelamento de item e cancelamento de venda paga toda em dinheiro, dentro dos limites da loja; acima deles aparece "Acima do limite sem internet. Conecte para aprovar.", e a troca de preço diz "A troca de preço precisa de internet.". Quando a rede volta, o servidor confere tudo de novo.
- Cancelar venda pede o supervisor **sempre**, mesmo para quem tem a permissão. Se a venda teve cartão ou Pix, a tela pede: "Estorne na maquininha e informe o NSU do estorno." Só conclui depois da sua confirmação. O dinheiro da venda sai do caixa: devolva ao cliente.
- Desconto em % ou em R$, no item ou no total. Mudar os itens depois tira o desconto no total; mudar a quantidade ou o preço de um item tira o desconto dele.

## Perguntas frequentes
**"Outra pessoa precisa aprovar."** O supervisor não aprova a própria ação. Chame outra pessoa.

**PIN errado.** A tela mostra quantas tentativas faltam. Com 5 erros seguidos, o PIN daquele supervisor bloqueia por 15 minutos.

**"Esta venda é de outro caixa. Use a devolução."** Só se cancela venda do turno aberto neste caixa. Venda de outro dia ou de outro caixa volta pela devolução.

**A venda foi para a quarentena depois da aprovação.** A aprovação vale só para aquela ação, naquele item e com aqueles valores, e por 10 minutos. Se o valor mudou depois, ou passou o prazo, chame o supervisor de novo. Autorização de outra pessoa sem o PIN dela no caixa também vai para a quarentena: o Proprietário confere e libera.

**O desconto no total pediu supervisor, mesmo dentro do limite.** O limite da loja vale para a venda inteira: o desconto no total somado aos descontos dos itens não pode passar dele.

**Sem internet.** A aprovação do supervisor precisa de rede nesta versão. Remova o último item lançado (sai sem supervisor) ou espere a conexão.

**O supervisor não aparece na lista.** Ele precisa ter o PIN do caixa (Meu perfil → Segurança) e a permissão da ação no grupo dele.
