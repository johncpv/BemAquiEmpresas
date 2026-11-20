---
titulo: Pendências do PDV
rota: /app/loja/pendencias
aba:
padrao: true
modulo: sales
---

## Para que serve
Mostrar o que a sincronização dos caixas achou ao receber as vendas, as sangrias, os suprimentos e as impressões. Toda venda feita no caixa chega ao sistema **uma vez só**, mesmo que a internet caia e volte várias vezes, e **nunca é jogada fora**: o que não fecha vira uma pendência aqui.

Há dois jeitos de uma pendência nascer:
- **Entrou com divergência:** a venda já está nas vendas, mas algo diferiu (ex.: preço). Basta conferir.
- **Em quarentena:** o evento chegou e está guardado, mas ainda **não teve efeito** (a venda não entrou nas vendas, a sangria não foi lançada). Alguém precisa decidir.

## Como usar
- Os chips no topo mostram quantas pendências abertas há de cada tipo; toque num chip para filtrar. Filtre também por situação, loja e caixa.
- Cada linha mostra o tipo, a loja e o caixa, a venda, o operador e o que divergiu.
- Quem tem a permissão de aprovar vendas toca em **Resolver**, escolhe a resolução e informa o motivo. Tudo fica no histórico (quem, quando e por quê).
  - **Aceitar como está:** para o que já entrou (ex.: preço desatualizado). A pendência fecha.
  - **Liberar da quarentena:** o evento passa a ter efeito (a venda entra nas vendas, a sangria é lançada). O sistema confere de novo; se ainda achar problema, o evento entra assim mesmo e o que foi achado fica como pendência nova ("Liberado da quarentena apesar de…"). Quem é o operador do evento não pode liberar. No aparelho substituído, liberar libera todos os eventos dele de uma vez.
  - **Manter fora:** o evento continua guardado, sem efeito.
- **Exportar** baixa a planilha (CSV) com as pendências do filtro.
- Embaixo, **Exceções por operador** conta, por período, os itens removidos, as quantidades reduzidas, as vendas descartadas e os eventos recusados na conferência.

## Cada tipo de pendência e o que fazer
- **Preço desatualizado:** o caixa vendeu com uma tabela de preço mais antiga que a vigente (ele estava sem internet quando o preço mudou). Vale o preço que o cliente pagou; a linha mostra o vendido e o vigente. Confira e aceite.
- **Preço divergente:** o preço do caixa não bate com o do catálogo da loja na versão vigente, a versão da tabela é desconhecida, ou o produto não está no catálogo da loja. Vale o que o cliente pagou; confira com o operador e aceite.
- **Autorização sem supervisor:** um item foi tirado ou reduzido com autorização do próprio operador, ou de alguém sem PIN aberto naquele caixa naquela hora. A venda entrou; converse com o operador. (O PIN do supervisor no balcão chega numa próxima versão.)
- **Relógio do aparelho:** a data e a hora do aparelho estavam diferentes das do servidor (mais de 2 minutos), ou voltaram no tempo em relação ao evento anterior (este caso fica em quarentena). As duas datas ficam gravadas; para o financeiro vale a do servidor. Acerte o relógio do aparelho.
- **Sequência quebrada:** faltou um evento antes deste, ou o anterior não fecha. Os eventos a partir dali ficam em quarentena até alguém olhar. Confira no caixa se ele ainda tem vendas para enviar; depois libere.
- **Aparelho substituído:** um aparelho bloqueado ou trocado enviou o que tinha guardado. Confira as vendas com o operador antes de liberar.
- **Versão do aparelho:** o caixa está com uma versão do app velha demais. Atualize o app do caixa; confira e libere.
- **Recusado na conferência:** o servidor refaz cada venda e não confia no aparelho. Entra aqui quando o operador não estava com o PIN aberto naquele caixa naquela hora, não tinha a permissão, um item que não era o último saiu sem quem autorizou, os pagamentos não batem com os itens, o troco não fecha, ou um campo tinha algo parecido com número de cartão (o texto é apagado: o sistema nunca guarda número de cartão). Converse com o operador; libere só se estiver tudo certo.
- **Pagamento:** a venda entrou, mas a soma dos pagamentos difere do total, ou um pagamento ficou sem confirmação. Confira com a maquininha ou o banco.
- **Sessão de caixa:** sangria, suprimento ou fechamento de uma sessão que não existe, é de outro caixa ou já estava fechada, ou uma segunda abertura no mesmo caixa. Também aparece quando a sangria passou do dinheiro calculado da sessão, ou quando a venda é de um horário fora da sessão (antes da abertura ou depois do fechamento): ela entra, mas o dinheiro dela não soma na sessão.
- **Documento fiscal:** a loja não estava com o modo "só cupom não fiscal" aceito na versão vigente do texto na hora da venda. Só o Proprietário resolve: aceite o modo (veja a ajuda "Documento fiscal") e libere.

## Perguntas frequentes
**Recebi "Caixa 02 sem enviar vendas há N horas.". O que faço?** O caixa tem vendas esperando para subir, ou está aberto e sem contato. Confira se o aparelho está ligado e com internet. O tempo do aviso é da loja, em Lojas e caixas.

**O CPF do cliente aparece aqui?** Não. O CPF sobe à parte, fica cifrado e nunca entra nos eventos do caixa, nas pendências nem na planilha.

**Posso apagar uma pendência?** Não. Ela fica no histórico. Resolva com o motivo.
