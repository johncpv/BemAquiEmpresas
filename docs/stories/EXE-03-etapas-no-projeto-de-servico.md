# EXE-03 — Etapas no projeto de serviço: andamento, checklist, responsável e indicador

| Campo | Valor |
|---|---|
| ID | EXE-03 |
| Título | O projeto de serviço copia as etapas do modelo; cada etapa tem responsável, datas previstas, checklist e situação (Aguardando, Pronta, Em andamento, Concluída); iniciar e concluir etapa com `projects.edit` (aprovação com `projects.approve` quando o modelo pede); a conclusão das etapas "Entrega" e "Aceite do cliente" faz o mesmo que os botões da EXE-01; evento `ProjectStageCompleted`; indicador de etapa com as letras do modelo |
| Tamanho | M |
| Status | Draft |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §4.2 e §7 · PRD PRJ-4 · PRD-01 (etapas copiadas do roteiro, editáveis antes de começar) · PRD-03a (checklist) · COM-04b (padrão do diálogo que mostra a parcela liberada) · R-SEC-5 |
| Dependências | EXE-01a, EXE-01b (página do projeto), EXE-02 |
| Bloqueia | EXE-04, EXE-05 |
| Ordem de execução | 3 (Execução depois do contrato, antes da F2 do Conecta) |

## User story
Como **responsável pelo projeto de serviço**, quero **ver em que etapa o projeto está, quem cuida de cada uma e o que falta no checklist**, para **conduzir a implantação até o aceite do cliente sem planilha paralela.**

## Contexto e referências
- A OS de fabricação já faz isso para a fábrica (etapas copiadas do roteiro, checklist linha a linha, etapa atual = menor sequência não concluída). Aqui o dono é o **projeto** (Commercial), porque o módulo `production` não está no perfil Serviços.
- O `StageIndicator` (display de andar) já aceita letras e nomes arbitrários (`ProjectItemsTab.tsx` usa as do roteiro da OS).

## Critérios de aceite

**Nascimento e edição**
1. **Dado** a aprovação de um orçamento que gera projeto `Service` com modelo escolhido (EXE-05) ou, até a EXE-05, o modelo sugerido (item → segmento → padrão do tenant), **quando** o projeto nasce, **então** as etapas do modelo são **copiadas** para `project_stages` (a primeira `Ready`, as demais `Waiting`) com o checklist em `project_stage_checklist`, e `projects.stage_template_id` guarda a origem.
2. **Dado** um projeto `Service` existente sem etapas (como o da BlockWay), **quando** alguém com `projects.edit` clica "Usar um modelo de etapas", escolhe o modelo (o sugerido vem marcado) e confirma, **então** as etapas são copiadas; etapas cujo papel já aconteceu (entrega/aceite registrados na EXE-01) nascem `Done` com a data registrada.
3. **Dado** um projeto `Service` sem nenhuma etapa iniciada, **quando** edito as etapas no projeto (incluir, remover, renomear, reordenar, mudar horas ou checklist) ou troco o modelo, **então** grava com as mesmas validações do modelo (EXE-02 CA 3); depois da primeira etapa iniciada, só incluir etapa nova no fim **antes** da etapa de Entrega e editar responsável/datas; o resto responde 409 "O projeto já começou: as etapas não mudam." Troca de modelo com marco do contrato apontando para letra que sumiria: 409 com a lista dos marcos (regra completa na EXE-05).

**Andamento**
4. **Dado** `projects.edit` e uma etapa `Ready`, **quando** clico "Iniciar etapa", **então** ela vai a `InProgress` com `started_at`; se o projeto estava `Planned`, vai a `InProgress` com `started_on` e publica `ProjectStarted` (EXE-01).
5. **Dado** uma etapa `InProgress`, **quando** marco os itens do checklist (Feito / Não se aplica), **então** cada marca grava quem e quando (linha a linha, como `production_order_checklist`).
6. **Dado** `projects.edit`, uma etapa `InProgress` com o checklist completo e papel **Execução** sem "Exige aprovação", **quando** clico "Concluir etapa" e confirmo (data ≤ hoje, padrão hoje), **então** ela vai a `Done`, a próxima fica `Ready`, publica `ProjectStageCompleted` (`{ projectId, contractId, stageCode, occurredOn }`) e o diálogo mostra a parcela liberada quando houver marco ligado à etapa (EXE-05).
7. **Dado** uma etapa com "Exige aprovação", **quando** quem tem só `projects.edit` conclui, **então** ela fica "Aguardando aprovação" e quem tem `projects.approve` vê "Aprovar etapa"; se só existe um aprovador e é a mesma pessoa, conclui com o selo "Autoaprovada" (`SoleOwnerRule`, como a PRD-03a).
8. **Dado** a etapa de papel **Entrega**, **quando** é concluída (exige `projects.approve`), **então** faz o mesmo que "Registrar entrega" da EXE-01 (projeto `Delivered`, `ProjectDelivered` + `ProjectFabricated`, opção de aceite junto quando não há etapa de aceite nem marco de aceite) e publica também o `ProjectStageCompleted`.
9. **Dado** a etapa de papel **Aceite do cliente**, **quando** é concluída (exige `projects.register_acceptance`, nome de quem aceitou e anexo opcional), **então** faz o mesmo que "Registrar aceite do cliente" da EXE-01a e publica o `ProjectStageCompleted`.
10. **Dado** um projeto com etapas, **quando** abro o cabeçalho, **então** "Registrar entrega" e "Registrar aceite do cliente" soltos **não** aparecem quando o modelo tem a etapa correspondente (o caminho é concluir a etapa); o próximo passo mostra o verbo da etapa atual ("Concluir Treinamento").
11. **Dado** `projects.approve` e uma etapa `Done`, **quando** clico "Reabrir etapa" com motivo (até 200), **então** ela e as seguintes voltam (a reaberta `InProgress`, as seguintes `Waiting`, checklists mantidos), o histórico guarda o motivo e publica `ProjectStageReopened`; etapa que liberou parcela com baixa: 409 "A parcela desta etapa já foi recebida. Use um aditivo."; sem baixa, o Finance volta o título a `Planned` (como o desfazer aceite da EXE-01). Reabrir Entrega/Aceite desfaz entrega/aceite pelas mesmas regras.
12. **[Segurança]** **Dado** qualquer ação de etapa, **quando** chamada, **então** confere o recurso `projects.service_execution`, a camada 3 no servidor, o contrato `Active`, a versão do projeto (`xmin`) e que a etapa é do projeto da rota (IDOR: etapa de outro projeto → 404).

**Visão**
13. **Dado** um projeto com etapas, **quando** abro, **então** o indicador de etapa do cabeçalho usa as letras e nomes das etapas (etapa atual em âmbar), e a nova aba **Etapas** lista cada etapa com situação, responsável, previsto (início/fim), realizado e o progresso do checklist (ex.: 4/6).
14. **Dado** uma etapa com fim previsto vencido e não concluída, **quando** listo, **então** ela tem a faixa lateral vermelha e o projeto aparece "Atrasado" na lista; a ≤ 2 dias do fim previsto, âmbar "Em risco".
15. **Dado** `projects.edit`, **quando** defino o responsável (usuário ativo com `projects.view`) e as datas previstas, **então** grava; o responsável vê a etapa na "Minha pauta" (DSH-04) quando ela fica `Ready`.

## Regras de negócio e técnicas
- **Sequencial na v1** (como a OS): só inicia a etapa `Ready`; etapa atual = menor sequência não `Done`.
- **Etapa de Entrega/Aceite** usa os mesmos serviços da EXE-01a (uma regra só, com a tabela de transições, as datas e a idempotência dela).
- **Estado do projeto derivado:** primeira etapa iniciada → `InProgress`; Entrega concluída → `Delivered`; Aceite concluído → `accepted_on`. Sem etapa de Entrega no modelo: o botão da EXE-01 continua.

## Permissões, eventos e tabelas
- **Permissões:** `projects.edit` (editar etapas, iniciar, checklist, concluir Execução sem aprovação, responsável e datas), `projects.approve` (aprovar etapa, concluir Entrega, reabrir etapas de execução e de entrega), `projects.register_acceptance` (concluir e reabrir a etapa de Aceite, EXE-01a).
- **Eventos novos (`Commercial.Contracts`):** `ProjectStageCompleted`, `ProjectStageReopened` (`{ projectId, contractId, stageCode, occurredOn }`, sem nome nem texto livre). O consumo no Finance entra na EXE-05; até lá são só publicados (outbox) e aparecem na linha do tempo.
- **Tabelas (schema `commercial`, novas):** `project_stages` (project_id, sequence, code, name, role, standard_hours, position_id, status `Waiting/Ready/InProgress/AwaitingApproval/Done`, responsible_user_id, planned_start, planned_end, started_at, completed_at, completed_by, completed_on, requires_approval, approved_by, self_approved, xmin), `project_stage_checklist` (stage_id, sequence, text, result `Done/NotApplicable`, marked_at, marked_by); `projects` + `stage_template_id`. Migration aditiva.

## Telas e estados
- **Aba Etapas** (`?aba=etapas`, primeira aba depois de Resumo no `Service`): vazio "Este projeto ainda não tem etapas." com "Usar um modelo de etapas" (o sugerido em destaque); carregando; erro com "Tentar de novo".
- **Painel da etapa** (lateral): checklist, responsável, datas, "Iniciar etapa" / "Concluir etapa" / "Aprovar etapa" / "Reabrir etapa"; verbos de confirmação "Etapa iniciada", "Etapa concluída", "Etapa aprovada", "Etapa reaberta".
- **Linha do tempo** (COM-05): etapas iniciadas, concluídas e reabertas.
- **400 px:** etapas em cartões; o botão da etapa atual fixo no rodapé.
- **Ajuda:** `Ajuda/projetos-projeto-etapas.md` (`aba: etapas`, `recurso: projects.service_execution`); `<Dica>` em "Exige aprovação" e "Reabrir etapa".

## Plano de testes
- **Unidade:** máquina da etapa; etapa atual; derivação do estado do projeto; regras de edição antes/depois de começar.
- **Integração (Testcontainers):** cópia do modelo na aprovação; "Usar um modelo" no projeto da BlockWay já entregue; conclusão de Entrega e Aceite publica os mesmos eventos da EXE-01; reabrir com e sem baixa; IDOR.
- **Front (Vitest):** aba Etapas nos estados; indicador com letras do modelo; próximo passo.

## Tarefas
- [ ] Domain: `ProjectStage`, `ProjectStageChecklistItem`, regras.
- [ ] Application: `ProjectStageService` (copiar, editar, iniciar, checklist, concluir, aprovar, reabrir), reuso dos serviços de entrega/aceite da EXE-01; `QuoteApprovalService.Create` copia o modelo.
- [ ] Infrastructure: migration aditiva; `ProjectViews` com a etapa atual (letra, nome) para lista, indicador e `ConnectedAppScopes` (`projects.view` da API de integração passa a devolver a etapa).
- [ ] Endpoints: `/api/v1/projects/{id}/stages` (GET, PUT antes de começar, POST `apply-template`), `/stages/{stageId}/start|complete|approve|reopen`, `/stages/{stageId}/checklist/{n}`; OpenAPI e Orval.
- [ ] Front: aba Etapas, painel da etapa, indicador do cabeçalho por etapas, lista de projetos com etapa atual e atraso.
- [ ] Docs: `04-fluxos-estados` (máquina da etapa do projeto), `03-modelo-dados`, ajuda.

## Dev Notes
- **Reaproveitar o desenho** (não o código do outro módulo, ADR 0006): máquina e checklist de `src/Modules/Production/` (PRD-01, PRD-03a: `production_order_steps`, `production_order_checklist`, `SoleOwnerRule`), risco `ProductionOrderRisk`.
- **Commercial:** `ProjectService.cs`, `ProjectService.Timeline.cs`, `ProjectViews.cs`, `QuoteApprovalService.Create.cs`.
- **Front:** `src/Web/src/components/ui/stage-indicator`, `src/Web/src/pages/projects/ProjectPage.tsx`, `ProjectTimelineTab.tsx`; padrão de painel lateral das telas de OS.
- **Minha pauta:** `CommercialAgendaSources.cs`.

## Fora do escopo
- Horas por etapa (EXE-04). Marcos por etapa e consumo do `ProjectStageCompleted` no Finance (EXE-05). Etapas em paralelo, dependências e Gantt do projeto de serviço. Termo de aceite em PDF e recusa (EXE-06).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir da proposta aprovada pelo PO (27/11/2026) | Claude |
| 27/11/2026 | 0.2 | Ajuste às revisões da EXE-01: depende de EXE-01a e EXE-01b; a etapa de Aceite usa `projects.register_acceptance` e as regras de transição, data e idempotência da EXE-01a | Claude |
