---
titulo: Faltas de material
rota: /app/estoque/faltas
aba: faltas
padrao: true
modulo: inventory
---

## Para que serve
Ver, por item, o que as OS abertas precisam e o estoque ainda não tem livre. É a lista de onde sai a compra do material que falta.

## Como usar
- Cada linha é um item: a **Falta** somada das OS abertas, o **Disponível** agora, as OS e os projetos afetados e a data prometida mais próxima. A lista começa pela data prometida mais próxima.
- Clique no número da OS para abrir a aba **Material** dela.
- Clique em **Gerar solicitação de compra** para comprar o que falta. Quando a solicitação já existe, o chip mostra **Falta {x}, SC gerada** e o link leva a ela.
- Busque pelo código ou nome do item e filtre por projeto. Os filtros ficam no endereço da página, para você guardar ou mandar o link.
- No celular aparecem item, falta e OS. Toque na linha para ver o disponível, a data prometida e o caminho da compra.

## Perguntas frequentes
**De onde vem a falta?** Da BOM liberada de cada OS. O estoque reserva o que tem livre e o resto vira falta, mesmo sem nenhum saldo.

**A falta some sozinha?** Sim. Quando o material chega, o estoque completa as faltas: primeiro a OS que originou a compra, depois a OS com a BOM liberada há mais tempo. OS cancelada ou concluída deixa de ter falta.

**O que é Calculado na implantação?** A falta foi calculada na implantação das reservas, a partir das BOMs que já estavam liberadas.
