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
- **Falta** é o que a BOM pede e o estoque não tem livre. Com Compras liberado, a falta já abre a solicitação de compra sozinha e acompanha a falta: o chip mostra **Falta {x}, SC gerada** e **Ver solicitação** leva a ela. Sem solicitação, **Gerar solicitação de compra** abre uma já preenchida com o item, a quantidade que falta e a necessidade.
- Quando o material chega (recebimento, entrada, devolução ou transferência), a falta se completa sozinha: primeiro a OS que originou a compra, depois a OS com a BOM liberada há mais tempo.
- O cabeçalho da OS mostra **Falta material** com quantos itens faltam. Clique no chip para abrir esta aba.
- Se a BOM for revisada, a reserva acompanha a revisão nova: reserva mais se a necessidade subiu e libera o que sobrou se ela desceu.
- **Baixado acima da BOM** aparece quando já foi baixado mais do que a revisão nova pede. Devolva o excedente pela requisição de material.
- **Calculado na implantação** marca a reserva feita na implantação das reservas, a partir das BOMs que já estavam liberadas.
- No celular aparecem item, reservado e falta. Toque na linha para ver a necessidade, o baixado e os locais.
- Embaixo fica o **Histórico da reserva**: quanto foi reservado, liberado, completado, baixado e devolvido, por qual origem e quando.

## Requisitar e devolver
- **Requisitar material** (com a ação **Criar** no Estoque) abre o diálogo com todo o reservado a baixar. No item com lote vem o lote **Sugerido**: o de validade mais próxima e, sem validade ou empatado, o mais antigo pela entrada. Se ele não basta, a linha já vem dividida; use **Dividir em outro lote** para dividir à mão.
- **Material extra** acrescenta um item fora da BOM. O que passa do reservado, e o material extra, é **Consumo acima da BOM**: sai do disponível do item e pede o motivo.
- A confirmação mostra, por item, a quantidade, o lote e o disponível antes → depois, e o total pelo custo médio de agora. Clique em **Baixar material**: a reserva vira baixado e o custo entra no projeto na hora.
- Se o consumo acima da BOM passa de R$ 1.000,00, o botão vira **Enviar para aprovação**: nada sai do estoque até outra pessoa com a ação **Aprovar** no Estoque decidir.
- **Devolver ao estoque** escolhe as linhas baixadas, a quantidade (até o que ainda não voltou) e o local. A devolução sai pelo custo da baixa original e reduz o custo do projeto. Se a BOM ainda pede o item, o material volta a ficar reservado para a OS.
- Na OS cancelada com material baixado, devolva o que voltou ou clique em **Manter no projeto como perda**, com o motivo: o custo continua no projeto.
- A lista **Requisições e devoluções** mostra cada documento, quem pediu, quando e o valor. Clique no número para ver o detalhe e o histórico.

## Perguntas frequentes
**Por que a aba diz para liberar a BOM?** A reserva só existe depois que a BOM da OS é liberada. Clique em **Abrir BOM**, revise as linhas e libere.

**O reservado sai do estoque?** Não. Ele continua no físico, mas não sai em saída avulsa, transferência nem em outra OS. Só a requisição de material desta OS baixa o reservado.

**O operador pediu material pelo app. Onde vejo?** O pedido aparece na lista **Requisições e devoluções** e em Estoque → Requisições, em **Aguardando você**. Abra e clique em **Confirmar pedido**.

**O que acontece se a OS for cancelada ou concluída?** As reservas são liberadas e as faltas zeram. O material volta a ficar disponível e completa as faltas de outras OS.

**Quem vê esta aba?** Quem vê a Produção ou o Estoque. A aba só aparece com o Estoque liberado na versão do sistema.
