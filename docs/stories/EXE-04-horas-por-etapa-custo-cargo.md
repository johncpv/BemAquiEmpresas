# EXE-04 — Horas por etapa e custo pelo cargo no projeto de serviço

| Campo | Valor |
|---|---|
| ID | EXE-04 |
| Título | Lançamento de horas por etapa do projeto de serviço (manual e por cronômetro), com o funcionário do RH e o custo pelo cargo (snapshot no lançamento, recomposição quando o custo do cargo muda); custo na razão `project_costs` (natureza Mão de obra) e no Orçado × realizado; aba Horas (previsto × lançado por etapa e por pessoa, estouro) e "Minhas horas"; nova ação `projects.log_time`; adendo de 27/11/2026 à ADR 0046: cadastro de cargos e funcionários do RH liberado para quem tem projeto de serviço |
| Tamanho | M |
| Status | Draft |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §7.3, §9.2 e §13 (decisão 3) · PRD PRD-4 e PRJ-5 · PRD-04 e ADR 0025 (custo-hora do cargo, snapshot, razão de diferenças) · PRD-03a (retroativo, apontamento por outro) · USR-01 (usuário ↔ funcionário) · ADR 0003 (ação específica) · ADR 0046 · LGPD (salário não aparece) · R-SEC-5 |
| Dependências | EXE-03 (etapas), USR-01, CAD-04a/b (cargos e funcionários) |
| Bloqueia | EXE-05 ("Preencher mão de obra pelas etapas"), EXE-07 |
| Ordem de execução | 4 (Execução depois do contrato, antes da F2 do Conecta) |

## User story
Como **consultor**, quero **lançar as horas que trabalhei em cada etapa do projeto**, e como **dono**, quero **ver quanto essas horas custaram contra o que orcei**, para **saber se a implantação deu lucro e onde o tempo foi.**

## Contexto e referências
- Hoje a mão de obra orçada (`quote_item_costs` `Labor` com `position_id`) só tem realizado pelos apontamentos das OS de produção (PRD-04), e o módulo `production` não está no perfil Serviços: no projeto de serviço o realizado de Mão de obra fica zerado.
- O custo-hora é **por cargo** (`IHourlyCost.ForPositionAsync`, sem salário exposto). O vínculo usuário ↔ funcionário existe desde a USR-01.
- **Decisão do PO (27/11/2026):** custo pelo cargo e funcionário do RH; o tenant com projeto de serviço passa a ter o cadastro de cargos e funcionários liberado, **sem obrigar ponto nem folha**.

## Critérios de aceite

**Lançar horas**
1. **Dado** `projects.log_time`, o recurso `projects.service_execution` e meu usuário vinculado a um funcionário ativo (USR-01), **quando** abro "Lançar horas" num projeto `Service` `InProgress` (ou `Delivered` sem estar `Closed`), escolho a etapa (a atual vem marcada), a data (≤ hoje, até 7 dias atrás; parâmetro `time.retroactive_days`, de 0 a 31), a duração (hh:mm, de 1 min a 24 h no dia) e a descrição (opcional, até 500), **então** o lançamento grava funcionário, cargo e o custo-hora do cargo **naquele momento** (snapshot) e o custo = minutos × custo-hora / 60, arredondado em 2 casas; "Lançar horas" → "Horas lançadas".
2. **Dado** o cronômetro, **quando** clico "Iniciar cronômetro" numa etapa, **então** nasce um lançamento aberto (um por funcionário, como a PRD-03a); "Parar" fecha com a duração; o cronômetro aparece no topo de qualquer tela e sobrevive a recarregar a página; aberto há mais de 12 h mostra o aviso "Cronômetro aberto há {n} h. Pare ou corrija."
3. **[Segurança]** **Dado** um usuário sem funcionário vinculado, **quando** tenta lançar, **então** 422 "Seu usuário não está ligado a um funcionário. Peça ao RH." e a tela mostra o atalho "Ligar ao funcionário" para quem tem `identity.edit`; funcionário afastado ou desligado na data: 422 "Funcionário {afastado|desligado} nessa data."
4. **Dado** `projects.edit`, **quando** lanço para outro funcionário, **então** grava "Lançado por {usuário}" (`recorded_by`), com as mesmas regras de data.
5. **Dado** a soma do funcionário no dia passando de 24 h ou sobreposição com o cronômetro aberto, **quando** salvo, **então** 422 "As horas de {data} passam de 24 h."
6. **Dado** um lançamento, **quando** o próprio autor (com `projects.log_time`) corrige ou exclui no prazo retroativo, ou alguém com `projects.edit` corrige a qualquer momento com motivo (até 200), **então** grava a correção (`corrected_at`, `corrected_by`, `correction_reason`), a razão `project_costs` recebe a diferença (fonte `TimeEntryCorrection`) e o histórico guarda antes e depois. Projeto `Closed` ou contrato `Cancelled`: 409 "Projeto encerrado: as horas não mudam."

**Custo**
7. **Dado** um lançamento fechado, **quando** grava, **então** na **mesma transação** entra a linha em `commercial.project_costs` (`cost_type` `Labor`, fonte `TimeEntry`, `source_id` = lançamento, `seq` 1), e o Orçado × realizado, a margem atual (COM-05) e o Resultado por projeto (DSH-01b) passam a mostrar o valor.
8. **Dado** um cargo sem custo-hora (`IHourlyCost` nulo ou zero), **quando** lanço, **então** as horas gravam com custo 0 e o selo "Sem custo-hora"; quem tem `hr.edit` vê na "Minha pauta" "Cargo {cargo} sem custo-hora: {n} lançamentos sem custo." e, ao preencher o custo do cargo, o `HourlyCostChanged` recompõe os lançamentos sem custo dos projetos não encerrados (fonte `TimeEntryCorrection`, recomposição idempotente).
9. **Dado** o custo-hora de um cargo que muda, **quando** chega o `HourlyCostChanged`, **então** só os lançamentos **sem custo** são recompostos (os com snapshot ficam como estão, ADR 0025: o custo é o do momento); o histórico de custo do lançamento guarda origem e valores (`time_entry_cost_history`, só inserção, lido com `projects.view_cost`).
10. **[Segurança]** **Dado** um usuário sem `projects.view_cost`, **quando** vê horas (aba, Minhas horas, API), **então** só vê horas; custo-hora e valores **não** saem no DTO (teste de contrato do JSON). Nunca sai salário.

**Visão**
11. **Dado** um projeto com horas, **quando** abro a aba **Horas**, **então** vejo por etapa: previstas (do modelo/etapa), lançadas, saldo e barra de consumo (âmbar acima de 90%, vermelha acima de 100%, sempre com o número), e por pessoa; com `projects.view_cost`, também o custo; filtro por período e pessoa; "Exportar Excel" (padrão EXP-01).
12. **Dado** `projects.log_time`, **quando** abro **Minhas horas** (`/app/minhas-horas`), **então** vejo a semana (seg–dom) com meus lançamentos por dia, o total, "Lançar horas" com projeto e etapa sugeridos pelos últimos usados, o cronômetro e a navegação entre semanas; vazio "Nenhuma hora lançada nesta semana." com "Lançar horas".
13. **Dado** um projeto com horas lançadas acima das previstas na etapa, **quando** o responsável do projeto abre a "Minha pauta", **então** vê "Etapa {letra} {nome} de {projeto} passou do previsto ({lançadas} de {previstas} h)." com "Ver horas".

**RH para quem tem projeto de serviço (adendo à ADR 0046)**
14. **Dado** um tenant com o recurso `projects.service_execution` e o módulo `hr` **não** liberado pelo plano, **quando** acesso RH → Cargos e RH → Funcionários (e os contratos `IEmployees`, `IPositions`, `IHourlyCost`), **então** funcionam como liberados (recurso `hr.people_registry`); ponto, folha, afastamentos, férias e as demais telas do RH continuam com cadeado e 403 `module_not_released`. No menu, a área "Pessoas" mostra só "Funcionários" e "Cargos".
15. **[Segurança]** **Dado** o cadastro liberado pelo recurso, **quando** um usuário acessa, **então** as camadas 2 e 3 valem como sempre (grupo com `hr` habilitado e `hr.view`/`hr.create`/`hr.edit`); dado sensível continua exigindo `hr.view_sensitive` e `hr.view_health`. O Proprietário tem tudo; nenhum seed patch concede `hr.*` a outros grupos.
16. **Dado** a Konebras, **quando** o deploy roda, **então** nada muda no RH (ela já tem `hr` liberado e não tem o recurso).

## Regras de negócio e técnicas
- **Fonte única de mão de obra no projeto de serviço:** os lançamentos desta story. O projeto `Fabrication` segue só pela PRD-04 (nenhum lançamento de horas de serviço é aceito nele: 409 `not_a_service_project`).
- **Previsto:** `project_stages.standard_hours` (copiado do modelo, editável antes de começar, EXE-03). O orçado em reais continua o `project_budget` `Labor` (COM-04a, COM-09).
- **Arredondamento:** custo por lançamento em 2 casas (`decimal(18,2)`); custo-hora snapshot em `decimal(18,6)`.
- **Recurso `hr.people_registry`:** declarado em `BusinessProfileCatalog.Features` = `[services]`, com a regra "módulo `hr` liberado **ou** o tenant tem o recurso" aplicada **só** aos grupos de rota de cargos e funcionários (`RequireModuleOrProfileFeature("hr", "hr.people_registry")`). Não muda `tenant_modules`.

## Permissões, eventos e tabelas
- **Ação nova:** `projects.log_time` — "Lançar horas: lançar e corrigir as próprias horas nas etapas dos projetos de serviço, no prazo retroativo." (ADR 0003: necessidade real, o consultor lança sem poder editar o projeto). Entra no `ModuleCatalog` (módulo `projects`). **Seed patch** `projects-log-time`: concede `projects.log_time` aos grupos que já têm `projects.edit` (regra de delegação da ADR 0008 respeitada).
- **Recursos por perfil novos:** `hr.people_registry` = `[services]`.
- **Eventos:** consome `HourlyCostChanged` (HR). Não publica evento novo (o custo é do próprio Commercial).
- **Tabelas (schema `commercial`, novas):** `project_time_entries` (project_id, stage_id, employee_id, position_id, work_date `date`, started_at, ended_at (cronômetro), minutes, description, hourly_cost_snapshot `numeric(18,6)` `[ProtectedSecret]` no diff, amount `numeric(18,2)`, cost_missing, origin `Manual/Timer`, recorded_by, corrected_at, corrected_by, correction_reason, is_deleted, created_at, xmin; único parcial `(employee_id) WHERE ended_at IS NULL AND origin = 'Timer'`), `time_entry_cost_history` (só inserção, fora da auditoria); `project_costs` + fontes `TimeEntry` e `TimeEntryCorrection`. **Parâmetro** `time.retroactive_days` (schema `commercial`, padrão 7). Migration aditiva.

## Telas e estados
- **Aba Horas** do projeto `Service` e **Minhas horas** (item novo no menu da área Comercial, só com `projects.log_time` e o recurso).
- **Diálogo "Lançar horas"**: etapa, data, duração, descrição; próximo passo depois de salvar: "Lançar outra" ou "Concluir etapa" quando o checklist da etapa estiver completo.
- **Estados:** carregando; vazio com ação; erro com "Tentar de novo"; sem funcionário vinculado: aviso com o motivo e o atalho (CA 3).
- **400 px:** Minhas horas por dia em lista; cronômetro como barra fixa.
- **Ajuda:** `Ajuda/projetos-projeto-horas.md` (`aba: horas`), `Ajuda/minhas-horas.md`; `Ajuda/rh-*.md` de cargos e funcionários ganham a nota "Disponível também para quem tem projeto de serviço"; `<Dica>` em "Sem custo-hora" e "Previstas".

## Plano de testes
- **Unidade:** cálculo do custo e arredondamento; regras de data, 24 h, cronômetro único; DTO sem custo sem `view_cost`.
- **Integração (Testcontainers, obrigatória: movimenta custo do projeto):** lançamento → `project_costs` na mesma transação; correção gera diferença; exclusão gera estorno; cargo sem custo + `HourlyCostChanged` recompõe uma vez (idempotente); Orçado × realizado e margem do projeto com as horas; recurso `hr.people_registry` libera só cargos e funcionários (ponto/folha continuam 403); seed patch `projects-log-time`.
- **Regressão Konebras:** RH e custos idênticos (cópia do `konebras`).
- **Front (Vitest):** aba Horas (barra e cores), Minhas horas (semana, vazio), cronômetro após recarregar.

## Tarefas
- [ ] ADR 0046: adendo de 27/11/2026 (RH no perfil Serviços) — já registrado neste PR de documentação; implementar `RequireModuleOrProfileFeature`.
- [ ] `ModuleCatalog`: `projects.log_time`; seed patch `projects-log-time`; `BusinessProfileCatalog.Features`: `hr.people_registry`.
- [ ] Domain/Application: `ProjectTimeEntry`, `ProjectTimeService` (lançar, cronômetro, corrigir, excluir), `HourlyCostChangedConsumer` (Commercial), fontes do `ProjectCostSource`.
- [ ] Infrastructure: migration aditiva; parâmetro; consultas da aba Horas.
- [ ] Endpoints: `/api/v1/projects/{id}/time-entries` (GET, POST), `/time-entries/{id}` (PUT, DELETE), `/me/time-entries?week=`, `/me/timer` (POST start/stop); OpenAPI e Orval.
- [ ] HR: grupos de rota de cargos e funcionários com a regra nova; menu da área Pessoas pelo recurso.
- [ ] Front: aba Horas, Minhas horas, cronômetro global, diálogo, ajuda.
- [ ] Docs: `03-modelo-dados`, `04-fluxos-estados` (lançamento de horas), CLAUDE.md não muda.

## Dev Notes
- **Custo-hora e razão:** `src/Modules/HR/BemAqui.HR.Contracts/HrContracts.cs` (`IHourlyCost`, `IEmployees`, `HourlyCostChanged`), `src/Modules/Commercial/BemAqui.Commercial.Application/LaborCostConsumer.cs` e `MaterialCostConsumer.cs` (como gravar diferença na `project_costs`), `Domain/ProjectCost.cs` (`ProjectCostSource`), `src/Modules/Production/BemAqui.Production.Application/LaborCosting.cs` e `LaborCostBackfill.cs` (regra de snapshot e recomposição, PRD-04).
- **Apontamento:** padrão da PRD-03a (`labor_entries`: um aberto por funcionário, retroativo, "Apontado por").
- **Vínculo usuário ↔ funcionário:** USR-01 (Identity).
- **Recurso por perfil:** `BuildingBlocks/Web/ModuleEndpoints.cs` (`RequireProfileFeature`), `BusinessProfileCatalog.HasFeature`.
- **Margem e resultado:** `ProjectMarginService*.cs` (já lê a razão), `src/Web/src/pages/projects/ProjectCostsTab.tsx`.

## Fora do escopo
- Faturar por horas (EXE-09). Aprovação de horas antes de entrar no custo. Horas na assinatura (suporte mensal; com a REC-01/EXE-07). Custo-hora por funcionário (decisão: por cargo). Horas em projeto `Fabrication`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir da proposta aprovada pelo PO (27/11/2026), com o adendo da ADR 0046 sobre o RH | Claude |
