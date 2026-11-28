# EXE-03b — Etapas no projeto de serviço: iniciar, checklist, concluir e aprovar

| Campo | Valor |
|---|---|
| ID | EXE-03b |
| Título | Andamento das etapas: iniciar (reaproveita o "Iniciar execução" da EXE-01a), marcar checklist, concluir e aprovar etapa; Entrega e Aceite pelos serviços da EXE-01a; `completion_version` e eventos `ProjectStageCompleted` com `{ stageCode, version }`; todo comando de etapa trava a versão do projeto; etapa com marco financeiro exige `projects.approve` e as regras de data e idempotência da EXE-01a; autoaprovação pelo modo configurável da FIN-10a |
| Tamanho | M |
| Status | Done (28/11/2026) |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §4.2 · EXE-01a (transições, datas, `Idempotency-Key`, `register_acceptance`, 2FA de login) · FIN-10a (modo de autoaprovação) · PRD-03a (checklist) · R-SEC-5 |
| Dependências | EXE-03a |
| Bloqueia | EXE-05a, EXE-03c, EXE-04a |
| Ordem de execução | 5 |

## User story
Como **responsável pelo projeto de serviço**, quero **iniciar e concluir cada etapa com o checklist em dia**, para **o projeto andar até a entrega e o aceite com registro de quem fez o quê.**

## Contexto e referências (conferido no código em 28/11/2026)
- `ProjectExecutionService` (EXE-01a) tem `StartAsync`, `RegisterDeliveryAsync`, `RegisterAcceptanceAsync`, `RevertAcceptanceAsync`, `CheckAsync(project, version, expected, action)` e `DateErrorAsync(date, backdateReason)` (30 dias com motivo; acima, `finance.approve`).
- `SelfApprovalMode` (Finance.Contracts, FIN-10a: `Never`, `OwnerOnly`, `AnyApprover`) é a codificação do modo de autoaprovação.
- `ApproverRequiresTwoFactorRule` cobre quem tem `.approve` e as `TwoFactorActions` (EXE-01a).

## Critérios de aceite

**Iniciar e checklist**
1. **Dado** `projects.edit` e a etapa `Ready`, **quando** clico "Iniciar etapa", **então** ela vai a `InProgress` (`started_at`); se o projeto está `Planned`, a mesma transação chama `ProjectExecutionService.StartAsync` (mesmas regras e o `ProjectStarted`). Com etapas, o botão solto "Iniciar execução" do cabeçalho some (o caminho é iniciar a etapa).
2. **Dado** uma etapa `InProgress`, **quando** marco um item do checklist (Feito / Não se aplica) ou desmarco, **então** grava quem e quando; **[Segurança]** o item tem de ser da etapa e do projeto da rota (IDOR: 404).

**Concluir e aprovar**
3. **Dado** uma etapa `InProgress` de papel Execução, **sem** marco financeiro ligado (`IStageMilestoneGuard.HasMilestone` = falso; até a EXE-05a, sempre falso), com o checklist completo e sem "Exige aprovação", **quando** quem tem `projects.edit` clica "Concluir etapa" (data ≤ hoje), **então** vai a `Done`, `completion_version` + 1, a próxima fica `Ready` e publica `ProjectStageCompleted` (`{ projectId, contractId, stageCode, version, occurredOn }`).
4. **[Segurança]** **Dado** uma etapa **com** marco financeiro ligado, **quando** alguém conclui, **então** exige `projects.approve` (que cai no 2FA de login), `Idempotency-Key`, `expectedStatus` e a regra de data retroativa da EXE-01a (`DateErrorAsync`: até 30 dias com motivo; acima, `finance.approve`); sem `projects.approve`: 403 `forbidden` "Esta etapa libera parcela: quem conclui são usuários com permissão de aprovar projetos."
5. **Dado** uma etapa com "Exige aprovação", **quando** quem tem só `projects.edit` conclui, **então** ela fica `AwaitingApproval`; quem tem `projects.approve` vê "Aprovar etapa". A autoaprovação (aprovar a etapa que a própria pessoa concluiu) segue o parâmetro `projects.stage_self_approval` com a mesma codificação e as mesmas regras de troca da FIN-10a (`Never`/`OwnerOnly`/`AnyApprover`, padrão `OwnerOnly`, troca só pelo Proprietário com o código do autenticador); autoaprovada ganha o selo "Autoaprovada".
6. **Dado** a etapa de papel **Entrega**, **quando** é concluída, **então** chama `ProjectExecutionService.RegisterDeliveryAsync` na mesma transação (mesmas permissões — `projects.approve` —, datas, idempotência e eventos da EXE-01a) e publica também o `ProjectStageCompleted`.
7. **Dado** a etapa de papel **Aceite do cliente**, **quando** é concluída, **então** chama `RegisterAcceptanceAsync` (`projects.register_acceptance`, nome de quem aceitou, anexo opcional) e publica o `ProjectStageCompleted`. Etapa de Aceite **sem** marco `TechnicalAcceptance` no contrato não trava o encerramento (`CompletionRules.RequiresAcceptance` continua olhando só o marco).
8. **Dado** um projeto com etapas de Entrega ou Aceite, **quando** abro o cabeçalho, **então** os botões soltos "Registrar entrega" e "Registrar aceite do cliente" não aparecem (o caminho é a etapa) e o próximo passo é o verbo da etapa atual ("Concluir Treinamento").

**Concorrência e idempotência**
9. **[Segurança]** **Dado** qualquer comando de etapa (iniciar, checklist, concluir, aprovar), **quando** grava, **então** trava a linha do projeto (`SELECT … FOR UPDATE`) e sobe a versão dele na mesma transação; dois comandos paralelos no mesmo projeto: uma gravação e um 409 `concurrency_conflict` (teste); duplo clique com a mesma `Idempotency-Key` grava uma vez e devolve a mesma resposta.
10. **[Segurança]** **Dado** um tenant sem o recurso, **quando** chama, **então** 403 `module_not_released`; Konebras sem mudança (regressão).

## Permissões, eventos e tabelas
- **Permissões:** `projects.edit` (iniciar, checklist, concluir etapa sem marco), `projects.approve` (concluir etapa com marco, aprovar, Entrega), `projects.register_acceptance` (Aceite). Nenhuma ação nova.
- **Parâmetro novo (Commercial):** `projects.stage_self_approval` (`SelfApprovalMode`).
- **Eventos novos (`Commercial.Contracts`):** `ProjectStageCompleted` e `ProjectStageReopened` (este publicado na EXE-03c), `{ projectId, contractId, stageCode, version, occurredOn }`, sem nome nem texto. Consumidor no Finance só na EXE-05a.
- **Tabelas:** nenhuma nova (usa `project_stages.completion_version` da EXE-03a).

## Telas e estados
- Aba Etapas ganha o painel da etapa: checklist, "Iniciar etapa", "Concluir etapa", "Aprovar etapa"; diálogos com o bloco de efeito nas parcelas da EXE-01b (prévia do servidor) quando a etapa tem marco. Verbos: "Etapa iniciada", "Etapa concluída", "Etapa aprovada".
- Celular: o botão da etapa atual fixo no rodapé.
- **Ajuda:** `Ajuda/projetos-projeto-etapas.md` ganha "Concluir e aprovar"; `<Dica>` em "Exige aprovação" e "Autoaprovada".

## Plano de testes
- **Unidade:** máquina da etapa; regra de permissão com/sem marco.
- **Integração (Testcontainers):** iniciar chama o `StartAsync`; concluir publica evento com versão crescente; Entrega e Aceite pelos serviços da EXE-01a (mesmos eventos); autoaprovação nos três modos; paralelo (um 409); idempotência; IDOR do checklist; 403 sem recurso; regressão Konebras.
- **Front (Vitest):** painel da etapa, próximo passo, botões soltos escondidos.

## Tarefas
- [x] `ProjectStageService` (iniciar, checklist, concluir, aprovar) sobre o `ProjectExecutionService`; parâmetro de autoaprovação.
- [x] Endpoints `POST /api/v1/projects/{id}/stages/{stageId}/start|complete|approve`, `PUT .../checklist/{n}` com `RequireIdempotencyKey`; OpenAPI e Orval.
- [x] Front e ajuda; `04-fluxos-estados` (máquina da etapa).

## Dev Notes
- `src/Modules/Commercial/BemAqui.Commercial.Application/ProjectExecutionService.cs` (`CheckAsync`, `DateErrorAsync`, `StartAsync`, `RegisterDeliveryAsync`, `RegisterAcceptanceAsync`); `src/Modules/Finance/BemAqui.Finance.Application/ApprovalResolver.cs` e `PaymentOrderRules.CanDecide` (como a FIN-10a aplica o modo); `src/Modules/Identity/BemAqui.Identity.Application/TwoFactorRequirement.cs`.

## Fora do escopo
- Reabrir, atraso, pauta e linha do tempo (EXE-03c). Consumo do `ProjectStageCompleted` no Finance (EXE-05a).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 28/11/2026 | 0.1 | **Revisão combinada de 28/11/2026:** parte de andamento da antiga EXE-03. Iniciar reaproveita o `StartAsync`; `completion_version` e eventos com versão; trava do projeto em todo comando de etapa; idempotência e IDOR do checklist. **Decisões do mantenedor** (o PO pode rever): etapa com marco financeiro exige `projects.approve` + `Idempotency-Key` + `expectedStatus` + data retroativa da EXE-01a; sem marco, `projects.edit`; autoaprovação pelo modo configurável da FIN-10a (parâmetro próprio do Commercial, padrão "Só o Proprietário"). | Claude |
| 28/11/2026 | 1.0 | **Implementada.** `ProjectStageService.Progress` (iniciar, checklist, concluir, aprovar) sobre o `ProjectExecutionService`, em transação com `SELECT … FOR UPDATE` no projeto e a versão (xmin) subindo por `stages_changed_at`; `ServiceStageFlow` (máquina e permissão por papel e marco) e `StageApprovalRules` (autoaprovação, mesma regra da FIN-10a); eventos `ProjectStageCompleted`/`ProjectStageReopened` registrados no outbox; parâmetro `projects.stage_self_approval` em `commercial.parameters` (GET/PUT de `/commercial/parameters`, troca só pelo Proprietário com 2FA e aviso aos outros Proprietários) e a seção em Configurações > Parâmetros; endpoints `POST …/stages/{stageId}/start|complete|approve` e `PUT …/stages/{stageId}/checklist/{n}` com `Idempotency-Key`; painel da etapa na aba Etapas (checklist, botões, rodapé fixo no celular, selos "Exige aprovação" e "Autoaprovada"); cabeçalho sem os botões soltos com etapas e com o verbo da etapa atual; ajuda e 04-fluxos-estados. Sem migration (nenhuma coluna nova). | Claude |
| 28/11/2026 | 1.1 | **Decisões sem o PO (opção mais segura):** (1) aprovador que conclui etapa com "Exige aprovação" só vai direto a `Done` se a autoaprovação deixar (selo "Autoaprovada"); senão fica aguardando — o "Exige aprovação" nunca é pulado. (2) Na Entrega e no Aceite com "Exige aprovação", os serviços da EXE-01a rodam na **aprovação** (é quando libera parcela), com a data da conclusão; o aprovar pede de novo a situação vista, o motivo da data anterior e, no Aceite, quem aceitou; aprovar Aceite exige também `projects.register_acceptance`. (3) Concluir: data não pode ser antes do início da etapa (422). (4) Projeto já entregue (ou já aceito) por fora da etapa: concluir a Entrega (ou o Aceite) só conclui a etapa, sem registrar de novo. (5) A rota de concluir aceita `projects.edit`, `projects.approve` ou `projects.register_acceptance`; o serviço exige a ação certa pelo papel e pelo marco. (6) "Quem marcou e quando" grava também ao desmarcar. (7) A regra de marco (CA 4) usa o `IStageMilestoneGuard` existente (`MilestonesForAsync` com a letra); até a EXE-05a ela fica provada só nos testes de unidade, porque nenhuma etapa tem marco. (8) Bloco de efeito nas parcelas só na Entrega e no Aceite (prévia do servidor da EXE-01b); etapa de Execução com marco mostra a frase fixa até a EXE-05a. | Claude |
