---
titulo: Notas de saída — emitidas
rota: /app/fiscal/saida
aba: emitidas
padrao: false
modulo: fiscal
---

## Para que serve
Consultar todas as notas de saída: número, série, data, cliente, projeto, valor, ambiente e situação, com os totais do filtro (valor e impostos).

## Como usar
1. Filtre por período, situação, cliente ou projeto. Os filtros ficam no endereço da página: dá para salvar ou mandar o link.
2. Clique na nota para ver os dados, os itens e tributos, o XML e o DANFE e o histórico.

## Situações
- **Rascunho**: ainda não foi à SEFAZ. **Processando**: enviada, esperando a SEFAZ. **Contingência**: a SEFAZ não respondeu; avisaremos quando autorizar.
- **Autorizada**: vale como documento fiscal. **Rejeitada**: a SEFAZ recusou; veja o motivo e corrija. **Denegada**: irregularidade do destinatário; fale com a contadora. **Cancelada**: cancelada na SEFAZ.
- **Homologação**: nota de teste, sem valor fiscal (não conta na receita nem no custo do projeto).

## Perguntas frequentes
**Nenhuma nota na lista?** Emita a nota a partir de um romaneio expedido, na aba **A emitir**.

**Os impostos do total são os do custo?** Não: o total do filtro soma todos os tributos destacados. O que entra no custo do projeto é o marcado na regra fiscal.
