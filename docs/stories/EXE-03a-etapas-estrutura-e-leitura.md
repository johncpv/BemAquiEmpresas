# EXE-03a — Etapas no projeto de serviço: estrutura, cópia do modelo, edição antes de começar e leitura

| Campo | Valor |
|---|---|
| ID | EXE-03a |
| Título | Tabelas das etapas do projeto; cópia do modelo na aprovação (etapas congeladas no snapshot do contrato); "Usar um modelo de etapas" em projeto existente, inclusive já entregue, sem evento nem efeito financeiro; edição antes de começar; aba Etapas em leitura; etapa do modelo como `currentStage` separado do estado do projeto (`ProjectStageMap`), também na API v1 e no PDF |
| Tamanho | M |
| Status | Done (28/11/2026) |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §4.2 · PRD PRJ-4 · PRD-01 (etapas copiadas do roteiro, editáveis antes de começar) · EXE-01a (`ProjectStageMap`, snapshot `v1`, `IntegrationProjectEndpoints`) · INT-03 · R-SEC-5 |
| Dependências | EXE-01a, EXE-01b (página do projeto), EXE-02 |
| Bloqueia | EXE-03b, EXE-05a |
| Ordem de execução | 4 |

## User story
Como **responsável pelo projeto de serviço**, quero **ver as etapas do projeto, quem cuida de cada uma e o que tem em cada checklist**, para **saber o caminho até o aceite do cliente.**

## Contexto e referências (conferido no código em 28/11/2026)
- `ProjectStageMap` (`Commercial.Domain/ProjectExecution.cs`, EXE-01a) dá a etapa **do projeto** (P · I · E · A · C); continua sendo o estado. A etapa **do modelo** (ex.: "T Treinamento") é outra informação e vai num campo separado.
- `ContractSnapshot` (`ContractSnapshots.cs`) é o registro do que foi vendido (`v1`, com `ExecutionKind` desde a EXE-01a).
- A API v1 do Conecta (`IntegrationProjectEndpoints`) devolve `IntegrationProjectStatus` mínimo (número, nome, letra, nome da etapa, situação, data prometida).

## Critérios de aceite

**Estrutura e cópia**
1. **Dado** a migration, **quando** roda, **então** nascem `commercial.project_stages` e `commercial.project_stage_checklist` (Tabelas) e `projects` ganha `stage_template_id` e `stage_template_revision`; só `CREATE TABLE` e `ADD COLUMN`.
2. **Dado** a aprovação de um orçamento que gera projeto `Service`, **quando** o projeto nasce, **então** as etapas do modelo do orçamento (EXE-05b) ou, até lá, do modelo sugerido (item → segmento → padrão do tenant) são **copiadas** (a primeira `Ready`, as demais `Waiting`), com horas previstas = horas por unidade × quantidade do item que originou a sugestão (sem item, × 1); o snapshot do contrato ganha `stages` congeladas (código, nome, papel), aditivo ao `v1`. Sem modelo nenhum, o projeto nasce sem etapas (EXE-01a continua valendo).
3. **Dado** `projects.edit` e um projeto `Service` `Planned`, `InProgress` ou `Delivered` **sem** etapas, **quando** clico "Usar um modelo de etapas", escolho (o sugerido vem marcado) e confirmo, **então** as etapas são copiadas. Em projeto já `InProgress`, a primeira fica `Ready`; em projeto já `Delivered`: todas as etapas de Execução e a de Entrega nascem `Done` com `completed_on = delivered_on`, a de Aceite `Done` com `accepted_on` (se houver aceite vigente) ou `Ready`; **sem publicar nenhum evento e sem alterar nenhum título** (teste mede outbox e `finance.receivables`). `Closed` ou `Cancelled`: 409 "Projeto encerrado ou cancelado: as etapas não mudam."
4. **Dado** a cópia, **quando** acontece, **então** o histórico do projeto mostra "Etapas do modelo {nome} (revisão {n})".

**Edição antes de começar**
5. **Dado** um projeto `Service` sem nenhuma etapa iniciada, **quando** edito as etapas (incluir, remover, renomear, reordenar, mudar papel, horas ou checklist) ou troco o modelo, **então** grava com as validações da EXE-02 (CA 3, 422 por linha) e a checagem de marcos do contrato: renomear, remover ou mudar o papel de etapa apontada por marco `StageCompleted` dá 409 com a lista dos marcos (a checagem é um ponto de extensão `IStageMilestoneGuard`, que passa sempre até a EXE-05a e é preenchido por ela). Depois da primeira etapa iniciada: só responsável e datas previstas mudam; o resto dá 409 "O projeto já começou: as etapas não mudam."
6. **[Segurança]** **Dado** qualquer rota de etapas, **quando** chamada, **então** exige o recurso `projects.service_execution` (403 `module_not_released`), a camada 3 no servidor, a versão do projeto (`xmin`, 409 `concurrency_conflict`) e que a etapa seja do projeto da rota (IDOR: 404); o responsável é usuário ativo do tenant com `projects.view`.

**Leitura**
7. **Dado** um projeto com etapas, **quando** abro, **então** a nova aba **Etapas** (depois de Resumo) lista cada etapa com letra, nome, papel, situação, responsável, previsto (início e fim), realizado e o progresso do checklist (ex.: 4/6), em leitura; vazio: "Este projeto ainda não tem etapas." com "Usar um modelo de etapas" (`projects.edit`).
8. **Dado** o cabeçalho, **quando** o projeto tem etapas, **então** o indicador do projeto (P · I · E · A · C) continua; ao lado aparece "Etapa atual: {letra} {nome}" (`currentStage`).
9. **Dado** `ProjectDetail`, `ProjectSummary`, o PDF do resumo e a API v1 (`IntegrationProjectStatus`), **quando** o projeto tem etapas, **então** ganham `currentStage` (`{ code, name, sequence, total }`, aditivo); a API v1 e o PDF **não** levam o nome do responsável; o contrato `openapi-integration-v1.json` é atualizado de forma aditiva.
10. **Dado** a Konebras, **quando** lê projetos, PDF e API v1, **então** nada muda (regressão sobre a cópia do `konebras`).

## Permissões, eventos e tabelas
- **Permissões:** `projects.view` (ler), `projects.edit` (usar modelo, editar antes de começar, responsável e datas). Nenhuma ação nova.
- **Eventos:** nenhum nesta story.
- **Tabelas (schema `commercial`, novas):** `project_stages` (project_id, sequence, code, name, role, planned_hours `numeric(18,4)`, position_id, requires_approval, status `Waiting/Ready/InProgress/AwaitingApproval/Done`, responsible_user_id, planned_start, planned_end, started_at, completed_on, completed_at, completed_by, completion_version int padrão 0, approved_by, self_approved, xmin; único `(project_id, code)` e `(project_id, sequence)` DEFERRABLE), `project_stage_checklist` (stage_id, sequence, text, result `Done/NotApplicable` nulo, marked_at, marked_by).

## Telas e estados
- Aba Etapas (leitura), estados de carregando, vazio com ação e erro; celular: etapas em cartões.
- **Ajuda:** `Ajuda/projetos-projeto-etapas.md` (`aba: etapas`, `recurso: projects.service_execution`); `<Dica>` em "Etapa atual".

## Plano de testes
- **Integração:** cópia na aprovação com snapshot; "Usar um modelo" em `Planned`, `InProgress` e `Delivered` (zero evento, zero título alterado), 409 em `Closed`/`Cancelled`; edição antes e depois de começar; IDOR; 403 sem recurso; API v1 e PDF com `currentStage` sem responsável; regressão Konebras.
- **Front (Vitest):** aba Etapas nos estados; cabeçalho com "Etapa atual".

## Tarefas
- [x] Domain/Application: `ProjectStage`, cópia, edição, `IStageMilestoneGuard` (sem implementação real até a EXE-05a), `currentStage` nas views.
- [x] Snapshot `stages`; `IntegrationProjectEndpoints` e `ProjectSummaryPdf` com `currentStage`.
- [x] Endpoints `GET/PUT /api/v1/projects/{id}/stages`, `POST .../stages/apply-template`; OpenAPI e Orval.
- [x] Front (aba, cabeçalho) e ajuda; docs `03-modelo-dados`.

## Dev Notes
- `src/Modules/Commercial/BemAqui.Commercial.Domain/ProjectExecution.cs`, `Application/ProjectExecutionService.cs`, `QuoteApprovalService.Create.cs`, `ContractSnapshots.cs`, `ProjectViews.cs`, `Documents/ProjectSummaryPdf.cs`, `Endpoints/IntegrationProjectEndpoints.cs`.

## Fora do escopo
- Iniciar, concluir, aprovar (EXE-03b); reabrir, atraso e pauta (EXE-03c); marcos por etapa (EXE-05a).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story EXE-03 escrita a partir da proposta aprovada pelo PO | Claude |
| 27/11/2026 | 0.2 | Ajuste às revisões da EXE-01 | Claude |
| 28/11/2026 | 0.3 | **Revisão combinada de 28/11/2026:** EXE-03 dividida em 03a (estrutura e leitura), 03b (andamento) e 03c (reabrir, atraso, pauta). Etapas congeladas no snapshot; "Usar um modelo" em projeto entregue sem evento nem efeito em título e recusado em encerrado/cancelado; checagem de marcos na edição pelo ponto de extensão preenchido na EXE-05a; `currentStage` separado da `ProjectStageMap`, na API v1 e no PDF sem o responsável. **Decisão do mantenedor:** horas previstas = horas por unidade × quantidade do item. | Claude |
| 28/11/2026 | 1.0 | **Implementada.** Tabelas `project_stages` e `project_stage_checklist` e colunas novas em `projects` (migration `ProjectStagesExe03a`, só CREATE TABLE, ADD COLUMN nulável e constraints novas, letra e sequência únicas DEFERRABLE). Domínio `ProjectServiceStage`/`ProjectServiceStageCheck` (o nome `ProjectStage` já era o record do display de andar da EXE-01a) e regras puras `ServiceStagePlan` (cópia pela situação, horas × quantidade, etapa atual). `ProjectStageService`: cópia na aprovação pelo `IStageTemplates.SuggestAsync` (item → segmento → padrão), snapshot com `stages`, "Usar um modelo" e troca antes de começar, edição (`StageTemplateRules.ValidateSteps`, 422 por linha `stages[n].campo`), `IStageMilestoneGuard` com `NoStageMilestoneGuard` até a EXE-05a, IDOR 404, versão do projeto. Rotas `GET/PUT /api/v1/projects/{id}/stages` e `POST .../stages/apply-template` no grupo com `RequireProfileFeature`. `currentStage` em `ProjectDetail`, `ProjectSummary` (só `ListByPartyAsync`), PDF do resumo ("Etapa atual: K Kickoff (1 de 4)") e API v1 (`IntegrationCurrentStage`, omitido quando nulo). Linha do tempo "Etapas do modelo {nome} (revisão {n})". Contador "Usado em {n} projetos" da EXE-02 ligado. Front: aba Etapas (tabela; cartões no celular; vazio com "Usar um modelo de etapas"), "Etapa atual" com `<Dica>` no cabeçalho, ajuda `projetos-projeto-etapas.md`. | Claude |
| 28/11/2026 | 1.1 | **Decisões sem o PO (opção mais segura):** (1) `projects` ganhou também `stage_template_name`, `stages_applied_at`, `stages_applied_by` (histórico do CA 4 com o nome da hora da cópia) e `stages_changed_at` (a edição das etapas muda o xmin do projeto); (2) etapa concluída pela cópia em projeto entregue fica com `completion_version = 0` e `completed_by` nulo (não houve conclusão por alguém nem evento); (3) o segmento da sugestão é o primeiro segmento do perfil Serviços do tenant; (4) a edição das etapas (PUT) fica só na API nesta story: a aba é de leitura, como pede o CA 7; (5) projeto entregue sem etapas que recebe etapas pelo PUT segue a mesma regra da cópia (concluídas pela entrega); (6) `apply-template` e `PUT` sem `Idempotency-Key` (a versão do projeto já barra o duplo clique); (7) modelo inativo ou excluído no "Usar um modelo", 422 em `templateId`; (8) `currentStage` só com o recurso `projects.service_execution` (sem o recurso, nulo, como os dados do aceite na EXE-01a); (9) o resultado do checklist da etapa é `StageChecklistResult` (o `ChecklistResult` da Produção já existe no OpenAPI com `NonConforming`). | Claude |
