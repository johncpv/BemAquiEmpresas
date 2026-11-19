---
titulo: Caixa (PDV) — pagamento
rota: /pdv
aba: pagamento
padrao: false
modulo: pos
---

## Para que serve
Receber a venda em uma ou mais formas (parte em dinheiro, parte no cartão, por exemplo) e ver o troco na hora, sem fazer conta. O cartão é passado na maquininha da loja, fora do sistema: o caixa só registra o que a maquininha aprovou. Funciona igual com e sem internet; tudo fica gravado neste aparelho e sobe sozinho quando a conexão volta.

## Como usar
- **Pagar:** com os itens lançados, toque em **Pagar** (F10). A tela mostra o total, o quanto **falta** e as formas de pagamento da loja.
- **Dinheiro:** digite o valor que o cliente entregou ou use os botões de valor rápido (o exato e as notas acima). O **troco** aparece em destaque. O pagamento é gravado com o valor da venda e o troco à parte.
- **Cartão (débito, crédito ou parcelado):** cobre na maquininha e, depois de aprovado, escolha a **bandeira** e as **parcelas** (quando a forma for parcelada) e toque em **Cartão aprovado**. O **NSU** é opcional. A tela nunca pede o número do cartão, a validade nem o código de segurança.
- **Pix:** confira o recebimento **no aplicativo do banco, não na tela do cliente**, e toque em **Recebi o Pix**. Esse Pix fica marcado para conferência contra o extrato no fechamento do caixa.
- **Várias formas:** pague uma parte, escolha outra forma e receba o que falta. A venda conclui sozinha quando a soma fecha o total.
- **Venda concluída:** aparecem o número da venda e o troco. **Nova venda** já fica selecionada (Enter começa a próxima).
- **Voltar aos itens:** antes de receber qualquer valor, volta para a venda para mudar os itens.

## Regras
- **Só o dinheiro dá troco.** Em Pix ou cartão, o valor não passa do que falta.
- **Pagamento recebido não se apaga.** Depois do primeiro pagamento, a venda não volta aos itens nem pode ser descartada: o pagamento só sai por estorno, com o supervisor.
- **Fiado e vale-troca** precisam de internet. Sem rede, aparecem desabilitados com "Disponível com internet.".
- Cada pagamento fica registrado com o seu nome.

## Perguntas frequentes
**O que é o NSU?** É o número da transação impresso no comprovante da maquininha (até 12 letras ou números). Ajuda a achar o pagamento num estorno ou na conferência. Não é o número do cartão: se você digitar algo que parece número de cartão, o caixa recusa com "Não digite o número do cartão.".

**A bandeira que preciso não aparece.** As bandeiras vêm da tabela de taxas da forma de pagamento (Financeiro → Formas de pagamento). Sem bandeira cadastrada, o caixa mostra todas.

**Apareceu "Nenhuma forma de pagamento ativa nesta loja."** Cadastre as formas em Financeiro → Formas de pagamento, no sistema. O caixa recebe a lista na próxima sincronização.

**E se a maquininha não respondeu?** Com a maquininha integrada (próximas versões), o caixa pode mostrar "Não sabemos se o pagamento foi aprovado". Confira o último comprovante na maquininha antes de cobrar de novo e escolha **Foi aprovado** ou **Não foi aprovado**.

**E o cupom?** A impressão do cupom chega com a configuração da impressora do caixa.
