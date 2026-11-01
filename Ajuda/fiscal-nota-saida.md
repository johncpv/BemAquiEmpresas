---
titulo: Nota de saída — dados
rota: /app/fiscal/saida/$id
aba: dados
padrao: true
modulo: fiscal
---

## Para que serve
Conferir e emitir a NF-e do equipamento expedido: destinatário, operação, transporte, informações complementares e os totais. Tudo vem preenchido do romaneio, do projeto e da regra fiscal. Depois de autorizada, é aqui que a nota é cancelada, corrigida por carta de correção ou tem o cancelamento pedido.

## Como usar
1. Confira o destinatário (vem do cadastro do cliente). Para corrigir documento, IE ou endereço, corrija no cadastro do cliente e descarte e refaça o rascunho.
2. Ajuste frete, outras despesas, as informações complementares e o envio ao cliente, e clique em **Salvar rascunho**.
3. Clique em **Pré-visualizar** para ver o DANFE com a faixa "Prévia, sem valor fiscal" (não gasta número).
4. Clique em **Emitir NF-e**. O diálogo mostra o destinatário, os itens, os totais, os impostos, o que entra no custo do projeto e o ambiente. Se faltar algo (integração, configuração, certificado, regra, endereço com IBGE), o diálogo lista cada item com o link para resolver.
5. Depois do envio: "Nota enviada à SEFAZ. Aguardando a autorização…". Se a SEFAZ demorar mais de 30 segundos, a tela avisa e a conferência automática segue. Nunca emita de novo enquanto processa.

## Cancelar
Só o Proprietário cancela, em uma etapa, dentro do **prazo de cancelamento** (24 horas da autorização, em Configurações → Fiscal).
1. Clique em **Cancelar NF-e** e escreva o motivo (15 a 255 caracteres). Se veio de um pedido, o motivo do pedido já vem preenchido.
2. O diálogo mostra quanto tempo resta ("Restam 3 h 12 min para cancelar esta nota."), a nota, o imposto que sai do custo do projeto e o romaneio, que **continua expedido**: cancelar não mexe no estoque.
3. Digite **CANCELAR** e confirme. O sistema pede o código do aplicativo autenticador (verificação em duas etapas).
4. A nota mostra "Cancelamento enviado" e, confirmado pela SEFAZ, passa a **Cancelada** com o protocolo. O imposto sai do custo do projeto, a nota deixa de contar no faturamento e o DANFE com a marca CANCELADA fica em Documentos, ao lado do original. Os membros do Proprietário são avisados e o cliente recebe o e-mail (se a opção estiver marcada).

## Pedir cancelamento
Quem vê o Fiscal, mas não é Proprietário, vê "Só o Proprietário cancela notas fiscais." e o botão **Pedir cancelamento**.
1. Clique em **Pedir cancelamento**, escreva o motivo e confirme. O pedido não muda a nota.
2. Os membros do Proprietário recebem o aviso com o motivo, o prazo restante e o link, e o pedido entra na pauta deles, do que vence primeiro.
3. O Proprietário cancela pelo link (o pedido fica atendido) ou usa **Recusar pedido** com o motivo. Sem decisão, o pedido expira sozinho no fim do prazo e quem pediu é avisado.

## Carta de correção
Para corrigir um dado simples sem cancelar (ex.: volumes, peso, um texto das informações complementares). Exige a ação de editar no Fiscal.
1. Clique em **Carta de correção** e escreva a correção (15 a 1000 caracteres).
2. A carta **não pode mudar valores, impostos, quantidades, destinatário nem a data de emissão**. Se o texto parecer mudar um desses dados, o diálogo mostra um aviso âmbar; confira antes de registrar.
3. Clique em **Registrar carta de correção**. A carta entra na aba Eventos com a sequência e o protocolo, e o XML do evento fica guardado. Cada nota aceita até 20 cartas; a última vale sobre as anteriores.

## Situações
- **Rejeitada**: o motivo e a ação sugerida aparecem em destaque. Use **Corrigir e emitir de novo**: a nota volta a rascunho com os dados; o número gasto vai para "Números a inutilizar".
- **Denegada**: a SEFAZ denegou por irregularidade do destinatário. Fale com a contadora; a nota não volta.
- **Autorizada**: o XML e o DANFE ficam em Documentos e vão para o e-mail de cobrança do cliente (se "Enviar ao cliente" estiver marcado).
- **Fora do prazo de cancelamento. A devolução fica com a contadora.**: passado o prazo, a nota não é mais cancelada; a saída é a nota de devolução, que a contadora emite.
- **Cancelada**: os próximos passos oferecem **Emitir nova NF-e do romaneio** e **Estornar expedição**.

## Perguntas frequentes
**Posso digitar o imposto?** Não. Os tributos saem da regra fiscal. Mude a regra do item (entre as ativas que valem) ou a quantidade, o valor e o desconto, e o cálculo refaz na hora.

**A nota cria título a receber?** Não. A parcela do contrato é só cobrança; a receita conta pela data da nota.

**Descartar apaga?** Não. O rascunho descartado sai da fila e fica registrado, sem número. Nota com número só é cancelada ou inutilizada, e nunca apagada.

**Cancelar devolve o estoque?** Não. O estoque saiu no romaneio. Se o equipamento voltar, estorne a expedição no romaneio depois de cancelar a nota.

**Por que pede o código do aplicativo de novo?** Cancelar e inutilizar têm efeito fiscal que não se desfaz: a verificação em duas etapas confirma que é você.
