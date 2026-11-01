---
titulo: Nota de saída — eventos
rota: /app/fiscal/saida/$id
aba: eventos
padrao: false
modulo: fiscal
---

## Para que serve
Acompanhar a nota na SEFAZ: envio, tentativas, autorização ou rejeição, e os eventos depois da autorização: **cancelamento** e **cartas de correção**, cada um com a sequência, o protocolo, o texto, quem pediu e quando.

## Como usar
1. Veja a tentativa atual, o protocolo de autorização e a chave de acesso.
2. Rejeição: o código, o motivo e a ação sugerida ficam aqui e no topo da nota.
3. Cada evento aparece em um cartão: **Cancelamento** ou **Carta de correção nº**, com a situação (Enviado, Registrado ou Recusado com o motivo) e o protocolo da SEFAZ.
4. Use **Baixar XML do evento** para guardar ou mandar o arquivo. O XML de cada evento fica guardado por 5 anos, ao lado do XML da nota.

## Situações
- **Nenhum evento nesta nota.**: a nota não foi cancelada nem corrigida.
- **Enviado**: o pedido foi ao Emissor Fiscal e espera a SEFAZ. Se demorar, a conferência automática confirma depois; não peça de novo.
- **Recusado**: a SEFAZ (ou o Emissor, antes dela) recusou; o motivo aparece no cartão e a nota não mudou.
- Cancelada fora do BemAqui (no painel do Emissor Fiscal): o evento aparece aqui do mesmo jeito, com o protocolo.

## Perguntas frequentes
**O que é a tentativa?** Cada nova emissão depois de uma rejeição é uma tentativa nova, com outra chave de envio. O número gasto pela rejeição vai para "Números a inutilizar".

**Por que não há o XML de um evento?** O Emissor Fiscal ainda não entregou o arquivo. A conferência automática tenta de novo; o evento continua valendo pelo protocolo.

**Posso apagar um evento?** Não. Nota, eventos e arquivos fiscais nunca são apagados.
