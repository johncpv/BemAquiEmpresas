# EXE-03c — Etapas no projeto de serviço: reabrir em cascata, atraso, pauta e linha do tempo

| Campo | Valor |
|---|---|
| ID | EXE-03c |
| Título | Reabrir etapa (e as seguintes) numa transação só, tudo ou nada, com as checagens financeiras da EXE-01a/05a; reabrir a Entrega só sem parcela ligada à entrega; atraso e "Em risco" parametrizável; etapa na Minha pauta; linha do tempo |
| Tamanho | M |
| Status | Draft (revisão combinada de 28/11/2026) |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §4.2 · EXE-01a (desfazer aceite: `acceptance_locked`, `project_closed`, `HasBillingSlip`, NF) · EXE-05a (reversão por etapa) · DSH-04 (Minha pauta) · COM-05 (linha do tempo) · R-SEC-5 |
| Dependências | EXE-03b, EXE-05a |
| Bloqueia | — |
| Ordem de execução | 7 |

## User story
Como **responsável pelo projeto**, quero **voltar uma etapa quando o trabalho precisa ser refeito e ver o que está atrasado**, para **corrigir o rumo sem bagunçar o financeiro.**

## Critérios de aceite

**Reabrir em cascata**
1. **Dado** `projects.approve` e uma etapa `Done` de papel Execução, **quando** clico "Reabrir etapa", informo o motivo (obrigatório, até 200) e confirmo, **então** o servidor **valida tudo antes** (a etapa e todas as seguintes `Done`): para cada uma com marco financeiro, as checagens da EXE-05a CA 10 (baixa, `HasBillingSlip`, NF); para a de Aceite, as da EXE-01a CA 15 e 16 (`acceptance_locked`, `project_closed`). Se **qualquer** uma falhar: 409 com a lista ("A etapa {letra}: a parcela já foi recebida ou cobrada.") e **nada** muda.
2. **Dado** a validação ok, **quando** grava, **então** numa transação só (projeto travado, versão sobe): a etapa reaberta vai a `InProgress`, as seguintes a `Waiting` (checklists mantidos), o aceite vigente é desfeito pelo `RevertAcceptanceAsync` da EXE-01a quando a de Aceite estava `Done`, e os eventos saem em **ordem inversa** (da última etapa para a reaberta): `TechnicalAcceptanceReverted` (se houver) → `ProjectStageReopened` de cada etapa (`{ stageCode, version }` = a `completion_version` reaberta). O histórico guarda o motivo.
3. **[Segurança]** **Dado** o reabrir, **quando** chamado, **então** exige `Idempotency-Key`, `expectedStatus`, versão do projeto, o recurso e a camada 3 (reabrir a de Aceite exige também `projects.register_acceptance`); projeto `Closed` ou `Cancelled`: 409 `project_closed`.

**Reabrir a Entrega**
4. **Dado** a etapa de Entrega `Done` (projeto `Delivered`), **quando** o contrato tem marco de gatilho Entrega, Aceite ou Fabricação concluída (compatibilidade), **então** "Reabrir etapa" fica **proibido**: 409 `delivery_locked` "A entrega libera parcela neste contrato. Para corrigir, use um aditivo." (decisão do mantenedor: o mais seguro).
5. **Dado** a Entrega `Done` sem nenhum desses marcos, **quando** reabro, **então** além do CA 2 o projeto volta a `InProgress` (`delivered_on` nulo) e publica `ProjectDeliveryReverted` (`{ projectId, contractId, version }`); o Finance remove o gatilho `Delivered` do registro (sem título para mexer) e o `ContractCompletionConsumer` ignora (o projeto não estava `Closed`).

**Atraso, pauta e linha do tempo**
6. **Dado** uma etapa não concluída com fim previsto vencido, **quando** listo, **então** tem a faixa lateral vermelha e o projeto aparece "Atrasado"; a até N dias do fim previsto (parâmetro `projects.stage_risk_days`, padrão 2, de 0 a 30, `settings.edit`), âmbar "Em risco".
7. **Dado** o responsável da etapa, **quando** a etapa fica `Ready`, **então** ela entra na "Minha pauta" dele ("Etapa {letra} {nome} de {projeto} pronta para começar"); atrasada: "Etapa {letra} de {projeto} atrasada desde {data}". Pelo contrato `IProjects.StageAgendaAsync`. Etapa `AwaitingApproval` entra na pauta de quem tem `projects.approve`.
8. **Dado** a linha do tempo do projeto (COM-05), **quando** abro, **então** vejo etapas iniciadas, concluídas, aprovadas e reabertas (com o motivo para quem tem `projects.view`).

## Permissões, eventos e tabelas
- **Permissões:** `projects.approve` (reabrir), `projects.register_acceptance` (reabrir a de Aceite), `settings.edit` (parâmetro de risco).
- **Eventos:** `ProjectStageReopened` (definido na EXE-03b); novo `ProjectDeliveryReverted` (Finance remove o gatilho `Delivered` sem título).
- **Tabelas:** nenhuma nova; parâmetro `projects.stage_risk_days`.

## Telas e estados
- "Reabrir etapa" no painel da etapa, com o bloco de efeito nas parcelas (EXE-01b) listando o que volta a aguardar; bloqueado: botão desabilitado com o motivo e o link "Fazer aditivo".
- **Ajuda:** "Reabrir etapa" em `projetos-projeto-etapas.md`; `<Dica>` em "Em risco".

## Plano de testes
- **Integração (Testcontainers):** cascata tudo ou nada (uma etapa bloqueada impede todas); ordem inversa dos eventos; reabrir com aceite vigente desfaz o aceite; Entrega proibida com marco e permitida sem; `ProjectDeliveryReverted` no Finance; `project_closed`; idempotência; pauta (pronta, atrasada, aguardando aprovação).
- **Front (Vitest):** faixa e chip de atraso; botão bloqueado com motivo.

## Tarefas
- [ ] `ProjectStageService.ReopenAsync` (validação prévia, transação, eventos em ordem inversa); `ProjectDeliveryReverted` e consumidor no Finance.
- [ ] `IProjects.StageAgendaAsync` e fonte de pauta; linha do tempo; parâmetro.
- [ ] Endpoint `POST /api/v1/projects/{id}/stages/{stageId}/reopen`; OpenAPI e Orval; front e ajuda.

## Dev Notes
- `src/Modules/Commercial/BemAqui.Commercial.Application/ProjectExecutionService.cs` (`RevertAcceptanceAsync`, `LockedReasonAsync`), `CommercialAgendaSources.cs`, `ProjectService.Timeline.cs`; Finance `MilestoneRevertedConsumer` (EXE-05a).

## Fora do escopo
- Recusa formal do cliente com termo (EXE-06). Etapas em paralelo.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 28/11/2026 | 0.1 | **Revisão combinada de 28/11/2026:** reabrir, atraso e pauta saem da antiga EXE-03. Cascata tudo ou nada com validação antes e eventos em ordem inversa, com as checagens da EXE-01a. **Decisão do mantenedor** (o PO pode rever): reabrir a Entrega é proibido quando o contrato tem parcela ligada à entrega, ao aceite ou à fabricação (compatibilidade); sem parcela, permitido com o evento `ProjectDeliveryReverted`. "Em risco" parametrizável. | Claude |
