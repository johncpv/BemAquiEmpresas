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
- A faixa de fatos mostra a data prometida, as horas padrão somadas, a etapa atual e o roteiro de origem.
- Enquanto nenhuma etapa começou, quem edita na produção inclui, remove e reordena etapas, troca o centro e as horas e edita o checklist. Clique em **Salvar etapas**. A primeira etapa fica pronta e as demais aguardando.
- **Trocar roteiro** substitui as etapas pelas do roteiro escolhido. O diálogo mostra quantas etapas e horas mudam, antes e depois. O roteiro sugerido aparece com a tag **Sugerido**.
- **Cancelar OS** pede o motivo. A OS que já começou só é cancelada por quem aprova na produção.
- Depois da primeira etapa iniciada, as etapas ficam só para leitura.
- **Pronta para concluir:** todas as etapas e a qualidade foram feitas. A OS espera o supervisor concluir. Ela aparece assim na lista, no Kanban e aqui.
- **Concluir OS** (quem aprova na produção, com login completo): o diálogo mostra o que muda. A OS passa a **Concluída**; se é a última OS de fabricação do projeto, o projeto passa a **Fabricado** e a parcela do marco "fabricação concluída" é liberada, com valor e vencimento para quem vê o financeiro. A OS conclui com a quantidade inteira. A mensagem é **OS concluída**.
- Enquanto falta etapa, qualidade ou há apontamento aberto, **Concluir OS** fica desabilitado e diz o motivo, por exemplo "Falta aprovar a Qualidade".
- Depois de concluir, os próximos passos levam à parcela liberada e ao projeto.
- **Estornar conclusão** (quem aprova na produção): informe o motivo. A OS volta para a última etapa e o projeto volta de Fabricado para Em andamento. A parcela já liberada continua em aberto e o Financeiro é avisado. A mensagem é **Conclusão estornada**.

## Perguntas frequentes
**Mudar as etapas aqui muda o roteiro?** Não. A mudança vale só para esta OS. Para mudar o roteiro de todos, edite em Engenharia → Roteiros.

**O que é "Sem roteiro"?** Nenhum roteiro foi sugerido para o item. Clique em **Escolher roteiro** para a OS ganhar as etapas.

**O que são as horas padrão?** As horas de trabalho esperadas para uma unidade em cada etapa. O planejamento usa este número.

**Concluir a OS dá entrada no estoque?** Ainda não. O produto acabado ainda não entra no estoque nesta versão. A entrada, com as séries e os lotes, chega numa próxima versão.

**Por que o estorno da conclusão foi recusado?** Porque a parcela "fabricação concluída" já foi recebida, mesmo em parte, ou o projeto já está instalando ou entregue. Fale com o Financeiro antes de estornar.

**O estorno cancela a parcela?** Não. O título a receber continua em aberto; quem decide é o Financeiro. Concluir a OS de novo não libera a parcela outra vez.

**Quem só aprova a qualidade conclui a OS?** Não. Concluir e estornar a conclusão são do supervisor, com a ação **Aprovar** da Produção.