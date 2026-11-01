---
titulo: Notas de saída — emitidas
rota: /app/fiscal/saida
aba: emitidas
padrao: false
modulo: fiscal
---

## Para que serve
Consultar todas as notas de saída: número, série, data, cliente, projeto, valor, ambiente e situação, com os totais do filtro (valor e impostos). Aqui também ficam os **pedidos de cancelamento** e os **números a inutilizar**.

## Como usar
1. Filtre por período, situação, cliente ou projeto. Os filtros ficam no endereço da página: dá para salvar ou mandar o link.
2. Clique na nota para ver os dados, os itens e tributos, o XML e o DANFE, os eventos e o histórico.
3. Na situação **Cancelamento pedido**, veja as notas com pedido de cancelamento pendente.

## Números a inutilizar
Na situação **A inutilizar** aparecem os números gastos sem nota autorizada (rejeições), já agrupados em faixas contínuas por série, com o motivo e a data. A SEFAZ exige que a lacuna na numeração seja explicada.
1. O Proprietário escolhe a faixa e clica em **Inutilizar números**.
2. Escreva a justificativa (15 a 255 caracteres), confira os números da faixa, digite **INUTILIZAR** e confirme com o código do aplicativo autenticador.
3. Homologada pela SEFAZ, a faixa sai da lista com o protocolo.
- Número com nota autorizada ou cancelada nunca é inutilizado: o sistema recusa antes de enviar.
- Número esperando há mais de 30 dias gera um aviso semanal ao Proprietário e aparece na pauta.

## Situações
- **Rascunho**: ainda não foi à SEFAZ. **Processando**: enviada, esperando a SEFAZ. **Contingência**: a SEFAZ não respondeu; avisaremos quando autorizar.
- **Autorizada**: vale como documento fiscal. **Rejeitada**: a SEFAZ recusou; veja o motivo e corrija. **Denegada**: irregularidade do destinatário; fale com a contadora. **Cancelada**: cancelada na SEFAZ.
- **Homologação**: nota de teste, sem valor fiscal (não conta na receita nem no custo do projeto).
- **Nenhum número pendente.**: não há número a inutilizar.

## Perguntas frequentes
**Nenhuma nota na lista?** Emita a nota a partir de um romaneio expedido, na aba **A emitir**.

**Os impostos do total são os do custo?** Não: o total do filtro soma todos os tributos destacados. O que entra no custo do projeto é o marcado na regra fiscal.

**Quem inutiliza?** Só o Proprietário, com a verificação em duas etapas. Os demais veem a lista.
