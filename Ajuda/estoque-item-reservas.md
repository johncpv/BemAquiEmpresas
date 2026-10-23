---
titulo: Reservas do item
rota: /app/estoque/itens/$id
aba: reservas
padrao: false
modulo: inventory
---

## Para que serve
Ver quanto deste item está reservado para OS, quanto está disponível e cada mudança da reserva: quanto foi reservado, liberado e completado, para qual OS, por qual origem e quando.

## Como usar
- No topo aparecem o **Físico**, o **Reservado** e o **Disponível** somados em todos os locais.
- Cada linha do histórico mostra a mudança, a OS (clique para abrir a aba **Material** dela), o reservado e a falta depois, e a origem: o evento da BOM ou da OS, a entrada que trouxe o material ("Entrada MOV-…") ou a recomposição da implantação.
- Com material reservado, **Ver faltas do item** abre a lista Faltas já filtrada por este item.

## Perguntas frequentes
**Por que não consigo tirar o físico todo?** A saída avulsa e a transferência só tiram o disponível. O reservado sai só pela requisição de material da OS.

**O que é Calculado na implantação?** A reserva foi feita na implantação das reservas, a partir das BOMs que já estavam liberadas.
