---
titulo: Requisição de material
rota: /app/estoque/requisicoes/$id
aba: linhas
padrao: true
modulo: inventory
---

## Para que serve
Ver uma requisição de material, uma devolução ou uma perda da OS: o que foi pedido, o que saiu do estoque, por qual lote e valor, e quem fez cada passo.

## Como usar
- No topo ficam a situação, a marca **Autoaprovado** (quando o Proprietário único decidiu sozinho) e o valor **Acima da BOM**, quando houver.
- **Confirmar pedido** (pedido do operador, com a ação **Criar** no Estoque): abre o diálogo com as linhas pedidas, o lote sugerido e a quantidade. Ajuste ao que foi separado e clique em **Baixar material**. O operador vê **Material separado** no app.
- **Recusar pedido**: escreva o motivo. Nada sai do estoque e o operador vê **Recusado: {motivo}** no app.
- **Aprovar requisição** e **Reprovar requisição** (com a ação **Aprovar** no Estoque): quem aprova decide sobre o que foi enviado, sem editar. A aprovação baixa o material com o saldo de agora; a reprovação pede o motivo e avisa quem enviou. Quem enviou não aprova a própria requisição.
- **Estornar requisição**: lança os movimentos inversos pelo custo da baixa, devolve a reserva à OS e tira o custo do projeto. Quem baixou estorna em até 24 h e até R$ 1.000,00 (com a ação **Editar**); fora disso, outra pessoa com a ação **Aprovar**.
- Em **Linhas**, cada linha mostra o item, o lote, o local, a quantidade, o excedente com o motivo, o movimento (MOV) e o valor.
- **Histórico de alterações** mostra quem pediu, quem confirmou, quem decidiu e quando.

## Perguntas frequentes
**Por que não consigo estornar?** A requisição tem devolução ou perda (estorne a devolução antes), ou o estorno passa do limite do autor e precisa de outra pessoa com a ação **Aprovar** no Estoque. O motivo aparece acima dos fatos.

**O estorno da devolução deu "Saldo insuficiente".** O material que voltou ao estoque já foi usado em outro lugar. Ajuste o saldo antes ou estorne o outro movimento.
