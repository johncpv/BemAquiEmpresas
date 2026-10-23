---
titulo: Solicitações de compra
rota: /app/compras/solicitacoes
aba: solicitacoes
padrao: true
modulo: purchasing
---

## Para que serve
Ver tudo o que precisa ser comprado e para quando. As solicitações nascem de três jeitos: à mão, pela **falta da BOM** de uma OS e pelo **estoque mínimo** dos itens.

## Como usar
- Quem compra abre a lista em **Aguardando você**: as solicitações abertas, ainda sem cotação nem pedido. Em **Mostrar**, troque para **Abertas e em cotação** ou **Todas**.
- Cada linha mostra o número (SC-AAAA-0001), a origem, o destino (o projeto ou **Estoque geral**), a OS, os itens, a necessidade mais próxima e a situação.
- O chip **Precisa em {n} dias** aparece quando faltam até 7 dias para a necessidade; **Atrasada {n} d**, quando a data já passou. A faixa lateral da linha acompanha o chip.
- **Por item** soma as linhas abertas de todas as solicitações por item, para comprar junto o que vários pedem.
- **Nova solicitação** abre o formulário com a necessidade já sugerida.
- Os filtros ficam no endereço da página, para você guardar ou mandar o link.
- No celular aparecem número, itens, necessidade e situação. Toque na linha para ver a origem, o destino e a OS.

## Perguntas frequentes
**Quem abre a solicitação da falta da BOM?** O sistema. Quando a BOM de uma OS é liberada e o estoque não tem tudo livre, nasce uma solicitação por OS com o que falta. Se a falta diminuir (chegou material) ou zerar, a linha em aberto diminui ou é cancelada sozinha, com o motivo "Falta resolvida pelo estoque".

**E o estoque mínimo?** Todo dia às 6h o sistema confere cada item com estoque mínimo: disponível em todos os locais mais o que já está em compra para o estoque geral. Abaixo do mínimo, pede até o mínimo, numa solicitação do dia, e quem compra recebe o aviso "{n} itens abaixo do mínimo.". Quando o item volta ao mínimo, a linha em aberto é cancelada.

**A solicitação precisa de aprovação?** Não. A aprovação é do pedido de compra, que nasce da cotação ou direto ao fornecedor.

**O que é Calculado na implantação?** A linha foi criada a partir das faltas que já existiam antes das compras entrarem no sistema.
