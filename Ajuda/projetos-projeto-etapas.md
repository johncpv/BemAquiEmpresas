---
titulo: Projeto de serviço — etapas
rota: /app/projetos/$id
aba: etapas
padrao: false
modulo: projects
recurso: projects.service_execution
---

## Para que serve
Ver o caminho do projeto de serviço até o aceite do cliente: cada etapa do modelo, quem cuida dela, o que estava previsto, o que já foi feito e quanto do checklist está marcado.

## Como usar
- A aba **Etapas** vem logo depois de **Resumo**. Cada linha mostra a letra e o nome da etapa, o papel (Execução, Entrega ou Aceite do cliente), a situação, o responsável, o previsto (início e fim), o realizado e o progresso do checklist (ex.: 4/6). No celular, cada etapa vira um cartão.
- As etapas vêm do **modelo de etapas** (Projetos > Modelos de etapas). Na aprovação do orçamento, o sistema copia o modelo sugerido: o do item de serviço, senão o padrão do segmento, senão o padrão da empresa. As horas previstas de cada etapa são as horas por unidade do modelo vezes a quantidade do item.
- Projeto sem etapas mostra **Este projeto ainda não tem etapas.** e o botão **Usar um modelo de etapas** (para quem pode editar projetos). O modelo sugerido já vem marcado.
- No cabeçalho, ao lado do indicador P · I · E · A · C, aparece **Etapa atual**: a primeira etapa ainda não concluída.

## Concluir e aprovar
- A etapa atual aparece num painel acima da lista, com o checklist e o botão do próximo passo. No celular, o botão fica fixo no rodapé.
- **Iniciar etapa** (quem edita projetos): a etapa pronta para começar passa a **Em andamento**. Se o projeto ainda está planejado, ele passa a Em execução junto (pede a data do início).
- **Checklist:** com a etapa em andamento, marque cada item como **Feito** ou **Não se aplica**; clique de novo para desmarcar. O sistema guarda quem marcou e quando.
- **Concluir etapa:** com o checklist completo, informe a data (até hoje). A próxima etapa fica pronta para começar.
  - Etapa de **Entrega** e etapa que libera parcela do contrato: só quem aprova projetos conclui. Data anterior a hoje pede o motivo; mais de 30 dias, também a permissão de aprovar no financeiro. Concluir a Entrega registra a entrega do projeto.
  - Etapa de **Aceite do cliente**: só quem tem a permissão Registrar aceite do cliente. Informe quem aceitou e, se tiver, anexe o termo assinado. Concluir registra o aceite do cliente.
- **Exige aprovação:** quem conclui deixa a etapa **Aguardando aprovação**, e quem aprova projetos clica **Aprovar etapa**. Quem concluiu só aprova a própria etapa se a regra "Quem concluiu a etapa pode aprová-la?" (Configurações > Parâmetros) deixar; aí a etapa ganha o selo **Autoaprovada**.
- Com etapas, os botões soltos "Iniciar execução", "Registrar entrega" e "Registrar aceite do cliente" saem do cabeçalho: o próximo passo é o da etapa atual (ex.: "Concluir Treinamento").

## Etapa do projeto x etapa do modelo
- **P · I · E · A · C** é a situação do projeto (Planejado, Em execução, Entregue, Aceito, Encerrado) e continua igual.
- **Etapa atual** é a etapa do modelo em que o trabalho está (ex.: "T Treinamento"). As duas convivem.

## Perguntas frequentes
**Por que não consigo concluir a etapa?** Falta marcar algum item do checklist, a data é depois de hoje, ou a etapa pede outra permissão (Entrega e etapa que libera parcela: aprovar projetos; Aceite: registrar aceite do cliente).

**Outra pessoa mexeu no projeto ao mesmo tempo.** O sistema grava só um dos comandos e avisa o outro: recarregue e tente de novo.

**Usei um modelo num projeto já entregue. Alguma parcela foi liberada?** Não. As etapas nascem concluídas pela data da entrega (e a de aceite, pela data do aceite, se houver), sem publicar nenhum evento e sem mexer em nenhum título.

**Posso trocar o modelo?** Sim, enquanto nenhuma etapa começou. Depois disso, só o responsável e as datas previstas mudam.

**Por que não consigo usar um modelo?** O projeto está encerrado ou cancelado, ou você não tem permissão para editar projetos.

**Mudei o modelo de etapas. O projeto muda?** Não. O projeto guarda uma cópia, com o nome e a revisão do modelo na hora da cópia (aparece na Linha do tempo).

**O PDF e o aplicativo do cliente mostram o responsável?** Não. Eles mostram só a etapa atual (letra, nome e posição).
