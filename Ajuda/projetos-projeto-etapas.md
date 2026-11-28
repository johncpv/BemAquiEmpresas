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

## Etapa do projeto x etapa do modelo
- **P · I · E · A · C** é a situação do projeto (Planejado, Em execução, Entregue, Aceito, Encerrado) e continua igual.
- **Etapa atual** é a etapa do modelo em que o trabalho está (ex.: "T Treinamento"). As duas convivem.

## Perguntas frequentes
**Usei um modelo num projeto já entregue. Alguma parcela foi liberada?** Não. As etapas nascem concluídas pela data da entrega (e a de aceite, pela data do aceite, se houver), sem publicar nenhum evento e sem mexer em nenhum título.

**Posso trocar o modelo?** Sim, enquanto nenhuma etapa começou. Depois disso, só o responsável e as datas previstas mudam.

**Por que não consigo usar um modelo?** O projeto está encerrado ou cancelado, ou você não tem permissão para editar projetos.

**Mudei o modelo de etapas. O projeto muda?** Não. O projeto guarda uma cópia, com o nome e a revisão do modelo na hora da cópia (aparece na Linha do tempo).

**O PDF e o aplicativo do cliente mostram o responsável?** Não. Eles mostram só a etapa atual (letra, nome e posição).
