# EXE-04a — Horas por etapa: lançamento manual, custo pelo cargo e aba Horas

| Campo | Valor |
|---|---|
| ID | EXE-04a |
| Título | Ação `projects.log_time` (com seed patch); lançamento manual de horas por etapa do projeto de serviço com o funcionário do RH; custo = minutos × custo-hora do cargo (snapshot no lançamento) na razão `project_costs`, com correção e exclusão por diferença; aba Horas (previsto × lançado por etapa e por pessoa, com visibilidade por permissão); LGPD da descrição |
| Tamanho | M |
| Status | Draft (revisão combinada de 28/11/2026) |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §7.3 e §13 (decisão 3) · PRD PRD-4, PRJ-5 · PRD-04 e ADR 0025 (razão de diferenças, snapshot) · USR-01 · ADR 0003 · R-SEC-5 · LGPD |
| Dependências | EXE-RH, EXE-03b (etapas em andamento), USR-01 |
| Bloqueia | EXE-04b, EXE-05b (mão de obra), EXE-07 |
| Ordem de execução | 8 |

## User story
Como **consultor**, quero **lançar as horas que trabalhei em cada etapa**, e como **dono**, quero **ver quanto custaram contra o previsto**, para **saber se o projeto deu lucro e onde o tempo foi.**

## Contexto e referências (conferido no código em 28/11/2026)
- `IHourlyCost.ForPositionAsync` e `IEmployees` (HR.Contracts): custo por cargo, sem salário; `EmployeeSummary` traz `PositionId` e `Status` (Active/OnLeave/Terminated). O RH não guarda histórico de cargo por data.
- `project_costs` é razão de diferenças só inserida (ADR 0025); `LaborCostConsumer`/`MaterialCostConsumer` mostram como gravar `seq` e diferenças.
- **Não existe** contrato público de apontamentos da produção (`ILaborEntries`); `IProjectProductionCosts` só dá custo por apontamento. A soma de 24 h no dia fica restrita a este módulo (documentado).
- `CommercialPersonalDataProvider` faz a anonimização do Commercial.

## Critérios de aceite

**Lançar**
1. **Dado** `projects.log_time`, o recurso e meu usuário vinculado a um funcionário ativo (USR-01: um usuário por funcionário), **quando** lanço horas num projeto `Service` `InProgress` ou `Delivered` (não `Closed`/`Cancelled`) numa etapa `Ready`, `InProgress` ou `AwaitingApproval` (etapa `Waiting` não aceita; `Done` só com `projects.edit` e motivo), com data (≤ hoje; até `time.retroactive_days` atrás, padrão 7, de 0 a 31), duração (1 min a 24 h) e descrição opcional (até 500), **então** grava funcionário, `position_id` = cargo do funcionário **no momento do lançamento** (snapshot) e `hourly_cost_snapshot` = custo-hora desse cargo **hoje**; custo = minutos × custo-hora ÷ 60, 2 casas. "Lançar horas" → "Horas lançadas".
2. **[Segurança]** **Dado** usuário sem funcionário vinculado, **quando** tenta, **então** 422 "Seu usuário não está ligado a um funcionário. Peça ao RH."; funcionário afastado ou desligado na data: 422 "Funcionário {afastado|desligado} nessa data."
3. **Dado** `projects.edit`, **quando** lanço para outro funcionário, **então** grava "Lançado por {usuário}" com as mesmas regras.
4. **Dado** a soma das horas do funcionário no dia **neste módulo** passando de 24 h, **quando** salvo, **então** 422 "As horas de {data} passam de 24 h." (as horas de apontamento da produção não entram na soma: não há contrato para isso; documentado).

**Corrigir e excluir**
5. **Dado** um lançamento, **quando** o autor (com `projects.log_time`, dentro do prazo retroativo) ou alguém com `projects.edit` (a qualquer momento, com motivo até 200) corrige ou exclui, **então** o comando exige `Idempotency-Key` e a `version` do lançamento (409 `concurrency_conflict` se mudou); a correção grava antes e depois.
6. **Dado** o custo, **quando** lanço, corrijo ou excluo, **então** na mesma transação, com o lançamento travado (`FOR UPDATE`), a razão `project_costs` recebe: lançamento → `seq` 1 com o valor (fonte `TimeEntry`); correção → `seq` n+1 com a **diferença** (fonte `TimeEntryCorrection`); exclusão → `seq` n+1 com o valor **negativo** do vigente. Lançar e corrigir também exigem `Idempotency-Key`.
7. **Dado** um projeto `Closed` ou contrato `Cancelled`, **quando** tento lançar, corrigir ou excluir, **então** 409 "Projeto encerrado: as horas não mudam."; e nenhuma recomposição posterior (EXE-04b) toca nele — a aba Horas mostra "Projeto encerrado: custos congelados."
8. **Dado** um cargo sem custo-hora, **quando** lanço, **então** grava custo 0 com o selo "Sem custo-hora" (a recomposição é da EXE-04b).

**Visibilidade**
9. **[Segurança]** **Dado** a aba **Horas**, **quando** abro, **então**: com `projects.view`, só o total por etapa (previsto × lançado, barra âmbar acima de 90% e vermelha acima de 100%, sempre com o número); **por pessoa**, só com `projects.edit` ou `projects.view_cost`; o autor sempre vê os próprios lançamentos; valores em reais só com `projects.view_cost` (DTO sem os campos para os demais, teste de contrato do JSON); nunca salário.
10. **[Segurança]** **Dado** a descrição do lançamento, **quando** grava, **então** é `[PersonalData]`: mascarada no diff da auditoria e no histórico, fora de eventos, exportações, API de integração e logs; aparece só na aba Horas para quem vê por pessoa. **Inventário LGPD:** finalidade (apropriar o custo do trabalho ao projeto e provar o esforço), retenção (a dos documentos do contrato) e anonimização no `CommercialPersonalDataProvider` (descrição vira vazio e o funcionário "Titular anonimizado"; horas e custo ficam).
11. **[Segurança]** **Dado** um lançamento de outro projeto ou tenant, **quando** chamado pela rota de um projeto, **então** 404 (IDOR); 403 `module_not_released` sem o recurso.

**Permissão**
12. **Dado** o `ModuleCatalog`, **quando** entra, **então** nasce `projects.log_time` — "Lançar horas: lançar e corrigir as próprias horas nas etapas dos projetos de serviço, no prazo retroativo." — e o seed patch `projects-log-time` a concede aos grupos que já têm `projects.edit` (ADR 0008 respeitada).

## Regras de negócio e técnicas
- **Custo do retroativo = custo de hoje** (decisão do mantenedor; o PO pode rever): o snapshot é o custo-hora do cargo no momento do lançamento, mesmo que a data trabalhada seja anterior, como a PRD-04 faz no fechamento do apontamento.
- O orçado continua o `project_budget` `Labor`; o previsto em horas é o `planned_hours` da etapa.
- Projeto `Fabrication`: 409 `not_a_service_project`.

## Permissões, eventos e tabelas
- **Ação nova:** `projects.log_time` + seed patch `projects-log-time`.
- **Tabelas (schema `commercial`, novas):** `project_time_entries` (project_id, stage_id, employee_id, position_id, work_date `date`, minutes, description `[PersonalData]`, hourly_cost_snapshot `numeric(18,6)` mascarado no diff, amount `numeric(18,2)`, cost_missing, origin `Manual/Timer`, recorded_by, corrected_at, corrected_by, correction_reason, deleted_at, deleted_by, created_at, xmin); `project_costs` + fontes `TimeEntry`/`TimeEntryCorrection`; parâmetro `time.retroactive_days`.
- **Eventos:** nenhum.

## Telas e estados
- **Aba Horas** (`?aba=horas`) e diálogo "Lançar horas" (etapa atual marcada; próximo passo "Lançar outra" ou "Concluir etapa" quando o checklist estiver completo). Estados: carregando, vazio "Nenhuma hora lançada ainda." com "Lançar horas", erro; sem funcionário vinculado: aviso com o motivo.
- **Ajuda:** `Ajuda/projetos-projeto-horas.md` (`aba: horas`); `<Dica>` em "Sem custo-hora" e "Previstas".

## Plano de testes
- **Unidade:** custo e arredondamento; datas; 24 h; situações da etapa.
- **Integração (Testcontainers, obrigatória: custo do projeto):** lançamento → razão na mesma transação; correção n+1 com diferença; exclusão negativa; corrida de duas correções (uma e um 409); idempotência; projeto encerrado; desligado e afastado; visibilidade por permissão (JSON); descrição mascarada na auditoria e fora da exportação; seed patch; IDOR.
- **Regressão:** Konebras sem mudança.

## Tarefas
- [ ] `ModuleCatalog` + seed patch; `ProjectTimeEntry`, `ProjectTimeService` (lançar, corrigir, excluir), razão; parâmetro.
- [ ] Endpoints `/api/v1/projects/{id}/time-entries` (GET, POST), `/time-entries/{id}` (PUT, DELETE) com `RequireIdempotencyKey`; OpenAPI e Orval.
- [ ] Aba Horas e diálogo; ajuda; inventário LGPD; `CommercialPersonalDataProvider`.

## Dev Notes
- `src/Modules/HR/BemAqui.HR.Contracts/HrContracts.cs`; `src/Modules/Commercial/BemAqui.Commercial.Application/LaborCostConsumer.cs`, `MaterialCostConsumer.cs`, `Domain/ProjectCost.cs`, `CommercialPersonalDataProvider.cs`; `src/Modules/Production/BemAqui.Production.Application/LaborCosting.cs` (snapshot da PRD-04).

## Fora do escopo
- Cronômetro, "Minhas horas", recomposição e Excel (EXE-04b). Faturar por horas (EXE-09).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story EXE-04 escrita a partir da proposta aprovada pelo PO | Claude |
| 28/11/2026 | 0.2 | **Revisão combinada de 28/11/2026:** EXE-04 dividida em 04a (lançamento, custo, aba) e 04b (cronômetro, Minhas horas, recomposição); o RH saiu para a EXE-RH. `Idempotency-Key` e versão em lançar/corrigir/excluir; razão com `seq` n+1 e exclusão negativa, lançamento travado; cargo do lançamento em snapshot; visibilidade por pessoa só com `projects.edit`/`view_cost` (o autor vê o seu); descrição como dado pessoal com inventário LGPD; projeto encerrado sem recomposição; USR-01 e desligado; soma de 24 h só deste módulo (não há contrato de apontamentos). **Decisão do mantenedor:** custo do lançamento retroativo = custo-hora de hoje. | Claude |
