---
titulo: OS de fabricação — etapas
rota: /app/producao/os/$id
aba: etapas
padrao: true
modulo: production
---

## Para que serve
Ver e ajustar as etapas desta OS antes de a fábrica começar: letra, nome, centro de trabalho, horas padrão, etapa de qualidade e checklist.

## Como usar
- O cabeçalho mostra o número, a situação, o cliente, o produto, a quantidade e a etapa atual (o display de andar com a letra da etapa).
- A faixa de fatos mostra a data prometida, o **Fim previsto** pelo plano, as horas padrão somadas, a etapa atual e o roteiro de origem.
- **Previsão do plano:** cada etapa que falta mostra **Previsto {início} → {fim}** e as **horas restantes**, calculados pelo Planejamento a partir da fila e das horas de cada centro por dia. Quando o plano não consegue datar, aparece **Sem previsão: {motivo}** (por exemplo, etapa sem centro de trabalho). A data prevista muda sozinha a cada cálculo e não entra no histórico da OS. Veja **Produção → Planejamento**.
- Com material em falta, o cabeçalho mostra o chip **Falta material** com quantos itens faltam. Clique nele para abrir a aba **Material**.
- Enquanto nenhuma etapa começou, quem edita na produção inclui, remove e reordena etapas, troca o centro e as horas e edita o checklist. Clique em **Salvar etapas**. A primeira etapa fica pronta e as demais aguardando.
- **Trocar roteiro** substitui as etapas pelas do roteiro escolhido. O diálogo mostra quantas etapas e horas mudam, antes e depois. O roteiro sugerido aparece com a tag **Sugerido**.
- **Cancelar OS** pede o motivo. A OS que já começou só é cancelada por quem aprova na produção.
- Depois da primeira etapa iniciada, as etapas ficam só para leitura.
- A aba **BOM** (para quem vê a Engenharia) tem a lista de materiais da OS. A primeira etapa começa sem BOM; as seguintes só começam, e só são concluídas pelo supervisor, com a BOM liberada.
- **Pronta para concluir:** todas as etapas e a qualidade foram feitas. A OS espera o supervisor concluir. Ela aparece assim na lista, no Kanban e aqui. Para quem aprova na produção, o próximo passo é **Concluir OS**. A etapa **Expedição** do roteiro é preparar e embalar: a saída do produto é pelo romaneio, depois de concluir.
- **Concluir OS** (quem aprova na produção, com login completo): o diálogo mostra o que muda. A OS passa a **Concluída**; se é a última OS de fabricação do projeto, o projeto passa a **Fabricado** e a parcela do marco "fabricação concluída" é liberada, com valor e vencimento para quem vê o financeiro. A OS conclui com a quantidade inteira.
- **Entrada no estoque** (com o Estoque liberado): na mesma conclusão, o produto acabado entra no estoque com a quantidade da OS. O diálogo mostra o **local de entrada** (o local padrão, com a tag **Sugerido**; dá para trocar), os **números de série** de cada unidade (gerados no formato código do item, ano e sequência, e editáveis antes de confirmar), as reservas de material que sobraram e serão liberadas e, para quem vê custo e margem, o **custo apurado da OS** e o custo unitário. A mensagem é **OS concluída. Produto acabado no estoque.**
- O **custo apurado da OS** são as horas apontadas mais o material baixado para a OS, menos o devolvido. O produto entra no estoque por esse custo dividido pela quantidade, mesmo quando quem conclui não vê o custo.
- **Custo da OS zerado:** sem horas nem material, o diálogo mostra o aviso âmbar e pede para marcar **Confirmo a entrada com custo 0** antes de concluir.
- Se algo falha (por exemplo, um número de série que já existe no item), nada é gravado: a OS continua **Pronta para concluir** e o erro aparece no diálogo. Se a conclusão demorar mais de 30 segundos, o diálogo avisa; não feche a tela.
- **Sem item de estoque do produto:** **Concluir OS** fica desabilitado com "Escolha o item de estoque do produto antes de concluir.". Quem edita na produção clica em **Escolher item do produto**, busca o produto acabado ou componente e confirma.
- Enquanto falta etapa, qualidade ou há apontamento aberto, **Concluir OS** fica desabilitado e diz o motivo, por exemplo "Falta aprovar a Qualidade".
- Depois de concluir, os próximos passos levam à parcela liberada, ao **Novo romaneio** (já com o projeto e as séries da OS), às séries e ao projeto. A aba **Séries** mostra a entrada, as séries e os lotes consumidos.
- **Estornar conclusão** (quem aprova na produção): informe o motivo. A OS volta para a última etapa e o projeto volta de Fabricado para Em andamento. A entrada no estoque ganha o movimento inverso, as séries ficam **Canceladas** e a reserva do material volta a seguir a BOM. A parcela já liberada continua em aberto e o Financeiro é avisado. A mensagem é **Conclusão estornada**.
- O estorno é recusado se uma série já foi **expedida** no romaneio ("O produto já foi expedido."), se o produto está num romaneio aberto (cancele o romaneio antes) ou se o produto sem série já saiu do estoque.
- **Concluída antes do controle de estoque:** a OS foi concluída antes de o sistema dar entrada do produto acabado. O produto não entrou no estoque e não tem séries.

## Perguntas frequentes
**Mudar as etapas aqui muda o roteiro?** Não. A mudança vale só para esta OS. Para mudar o roteiro de todos, edite em Engenharia → Roteiros.

**O que é "Sem roteiro"?** Nenhum roteiro foi sugerido para o item. Clique em **Escolher roteiro** para a OS ganhar as etapas.

**O que são as horas padrão?** As horas de trabalho esperadas para uma unidade em cada etapa. O planejamento usa este número.

**Concluir a OS dá entrada no estoque?** Sim, com o Estoque liberado nesta versão do sistema: o produto acabado entra pelo custo apurado da OS, com as séries e os lotes consumidos. Essa entrada não soma de novo no custo do projeto: o custo já entrou pelo material baixado e pelas horas.

**E as OS concluídas antes da entrada no estoque?** Continuam sem entrada e sem séries, com o aviso "Concluída antes do controle de estoque". O sistema não refaz o estoque delas. Se o produto ainda está na fábrica, lance a entrada pelo Estoque (Saldos → Lançar entrada).

**Por que o estorno da conclusão foi recusado?** Porque a parcela "fabricação concluída" já foi recebida, mesmo em parte, ou o projeto já está instalando ou entregue. Fale com o Financeiro antes de estornar.

**O estorno cancela a parcela?** Não. O título a receber continua em aberto; quem decide é o Financeiro. Concluir a OS de novo não libera a parcela outra vez.

**Quem só aprova a qualidade conclui a OS?** Não. Concluir e estornar a conclusão são do supervisor, com a ação **Aprovar** da Produção.