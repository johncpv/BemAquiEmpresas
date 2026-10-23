---
titulo: OS de fabricação — material
rota: /app/producao/os/$id
aba: material
padrao: false
modulo: production
---

## Para que serve
Ver o material que esta OS precisa e o que o estoque já separou para ela. Para cada item da BOM liberada aparecem a necessidade, o **Reservado**, o baixado, a **Falta** e o local.

## Como usar
- A reserva nasce sozinha quando a BOM da OS é liberada. Enquanto o estoque separa o material, a aba mostra **Reservando material…** e se atualiza sozinha. Ao terminar, aparece o aviso **Material reservado: {n} itens, {f} em falta.**, que também chega como notificação para quem libera BOM.
- O estoque reserva primeiro no local padrão (Almoxarifado) e depois nos outros locais, em ordem de código. Cada local aparece na linha do item.
- **Falta** é o que a BOM pede e o estoque não tem livre. Clique em **Gerar solicitação de compra** para comprar só o que falta. Quando a solicitação já existe, o chip mostra **Falta {x}, SC gerada** e o link leva a ela.
- Quando o material chega (recebimento, entrada, devolução ou transferência), a falta se completa sozinha: primeiro a OS que originou a compra, depois a OS com a BOM liberada há mais tempo.
- O cabeçalho da OS mostra **Falta material** com quantos itens faltam. Clique no chip para abrir esta aba.
- Se a BOM for revisada, a reserva acompanha a revisão nova: reserva mais se a necessidade subiu e libera o que sobrou se ela desceu.
- **Baixado acima da BOM** aparece quando já foi baixado mais do que a revisão nova pede. Devolva o excedente pela requisição de material.
- **Calculado na implantação** marca a reserva feita na implantação das reservas, a partir das BOMs que já estavam liberadas.
- No celular aparecem item, reservado e falta. Toque na linha para ver a necessidade, o baixado e os locais.
- Embaixo fica o **Histórico da reserva**: quanto foi reservado, liberado e completado, por qual origem e quando.

## Perguntas frequentes
**Por que a aba diz para liberar a BOM?** A reserva só existe depois que a BOM da OS é liberada. Clique em **Abrir BOM**, revise as linhas e libere.

**O reservado sai do estoque?** Não. Ele continua no físico, mas não sai em saída avulsa, transferência nem em outra OS. Só a requisição de material desta OS baixa o reservado.

**O que acontece se a OS for cancelada ou concluída?** As reservas são liberadas e as faltas zeram. O material volta a ficar disponível e completa as faltas de outras OS.

**Quem vê esta aba?** Quem vê a Produção ou o Estoque. A aba só aparece com o Estoque liberado na versão do sistema.
