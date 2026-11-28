# EXE-05a — Marco "ao concluir a etapa": gatilho `StageCompleted` de ponta a ponta

| Campo | Valor |
|---|---|
| ID | EXE-05a |
| Título | `TriggerEvent.StageCompleted` com `trigger_stage_code` no orçamento, contrato, aditivo, snapshot e Finance; `IProjectMilestoneReached` ganha `StageCode`; registro de gatilhos e índice único por etapa; liberação fora de ordem; reabertura de etapa desfaz a liberação pelo mesmo modelo do desfazer aceite (versão, lock, tudo ou nada, bloqueio por baixa, boleto ou NF); `MilestoneTriggerPolicy` com `StageCompleted`; tipo de execução sempre calculado |
| Tamanho | M |
| Status | Draft (revisão combinada de 28/11/2026) |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §7.1 · PRD COML-6, PRJ-6 · COM-01b, COM-04b, COM-07 · ADR 0019 §1 · EXE-01a (CA 15, 18, 27, 28) · R-SEC-5 |
| Dependências | EXE-03b (`ProjectStageCompleted` com versão) |
| Bloqueia | EXE-03c (reabrir com parcela), EXE-05b, REC-01 (início da assinatura por etapa) |
| Ordem de execução | 6 |

## User story
Como **vendedor de serviço**, quero **amarrar uma parcela à conclusão de uma etapa**, para **o cliente pagar conforme o projeto anda e o financeiro não precisar lembrar de liberar nada.**

## Contexto e referências (conferido no código em 28/11/2026)
- `TriggerEvent` (SharedKernel/Milestones.cs): `Signature`, `FabricationDone`, `Delivered`, `TechnicalAcceptance`. `IProjectMilestoneReached` (Commercial.Contracts) tem `ProjectId`, `ContractId`, `OccurredOn`, `Event`.
- `MilestoneReachedConsumer` busca o gatilho por `(ProjectId, Event)` e libera `Receivables` `Planned` por `TriggerEvent`; trata corrida pelo índice `ReceivableService.TriggerEventIndex` = `ux_project_trigger_events_project_event` com `DbErrors.IsUniqueViolation`. `ContractSignedConsumer` consulta o registro ao criar títulos.
- `AcceptanceRevertedConsumer` (EXE-01a) é o modelo de reversão: versão, `reverted`, `revert_note`, `FOR UPDATE`, só volta título `Open` sem pagamento e sem boleto; senão notifica `finance.edit`.
- `MilestoneTriggerPolicy` (EXE-01a) valida gatilho por tipo no orçamento, aprovação e aditivo. `ContractSnapshot.Milestones` guarda `TriggerEvent` como texto.
- **Clientes externos:** a API de integração v1 e os avisos de saída (INT-02) **não** expõem `TriggerEvent` (conferido no `openapi-integration-v1.json`); só o front do próprio ERP (cliente gerado pelo Orval) lê.

## Critérios de aceite

**Vocabulário e validação**
1. **Dado** a mudança, **quando** entra, **então** `TriggerEvent.StageCompleted` vai no fim do enum (texto no banco); `trigger_stage_code` (`char(1)`, anulável, `CHECK` "preenchido se e só se `StageCompleted`") entra em `quote_milestones`, `contract_milestones`, `contract_amendment_milestones` e `finance.receivables`; `SnapshotMilestone` ganha `StageCode` (aditivo).
2. **[Segurança]** **Dado** `MilestoneTriggerPolicy`, **quando** recebe `StageCompleted`, **então**: em `Fabrication`, 422 `milestone_trigger_not_allowed` (no orçamento, na aprovação, no aditivo e também na API direta); em `Service`, exige `trigger_stage_code` de etapa de **Execução** existente nas etapas de referência (orçamento: do modelo escolhido; contrato e aditivo: do projeto) — Entrega e Aceite usam os gatilhos próprios; 422 no campo da linha.
3. **Dado** o tipo de execução do orçamento, **quando** é usado, **então** é **sempre calculado** pela `ExecutionKindRule` (não há coluna `quotes.execution_kind`); só o snapshot do contrato o grava. A aprovação recalcula e revalida os marcos.
4. **Dado** a aprovação, **quando** o contrato nasce, **então** o snapshot guarda as etapas congeladas (EXE-03a) e os marcos com `StageCode`, e o projeto copia as mesmas etapas (letras iguais às do snapshot).

**Liberação**
5. **Dado** `IProjectMilestoneReached` com `StageCode` (nulo para os eventos antigos) e `ProjectStageCompleted` implementando-o, **quando** o Finance processa, **então** o lookup do gatilho e o filtro dos títulos `Planned` usam `(project_id, event, stage_code)`; outro `stageCode` não libera nada.
6. **Dado** o índice, **quando** a migration roda, **então** cria `ux_project_trigger_events_project_event_stage` `(project_id, event, coalesce(stage_code, ''))`, remove o antigo na mesma migration e `ReceivableService.TriggerEventIndex` passa a apontar para o novo (o `IsUniqueViolation` do consumidor também); nenhuma linha é apagada (teste sobre cópia do `konebras`).
7. **[Segurança]** **Dado** o `ProjectStageCompleted` processado antes do `ContractSigned` ou do aditivo aprovado, **quando** o título nasce, **então** nasce `Open` pelo registro (`ContractSignedConsumer` e o consumidor do aditivo olham `trigger_stage_code`); evento repetido ou com versão menor ou igual à gravada é descartado.
8. **Dado** um contrato com dois marcos em duas etapas diferentes, **quando** as etapas concluem em qualquer ordem, **então** cada uma libera só o seu (teste).

**Reabertura (desfazer a liberação)**
9. **[Segurança]** **Dado** o `ProjectStageReopened` versão n (publicado pela EXE-03c), **quando** o Finance processa, **então** usa o consumidor de reversão generalizado (o `AcceptanceRevertedConsumer` vira `MilestoneRevertedConsumer` para aceite e etapa): com os títulos do marco travados (`FOR UPDATE`), **tudo ou nada** — só reverte se **todos** estiverem `Open`, sem pagamento (nem parcial), sem baixa e sem boleto; marca o gatilho `reverted` com a versão; senão não reverte nada, grava `revert_note` e notifica quem tem `finance.edit` ("A etapa {letra} do projeto {número} foi reaberta, mas a parcela {título} já está em cobrança. Decida: cancelar, manter ou fazer um aditivo.").
10. **Dado** o Commercial antes de reabrir (EXE-03c), **quando** checa, **então** usa o mesmo `LockedReasonAsync` da EXE-01a generalizado por marco: `HasBillingSlip`, baixa, NF de saída do projeto emitida a partir da conclusão da etapa (`IOutgoingInvoices.ByProjectAsync`); bloqueado: 409 `stage_locked` "A parcela desta etapa já foi recebida ou cobrada. Para corrigir, use um aditivo."
11. **Dado** o `IStageMilestoneGuard` (EXE-03a/03b), **quando** esta story entra, **então** passa a responder pelo contrato: `HasMilestone(stage)` e a lista de marcos que apontam para uma letra (usada na edição antes de começar e na exigência de `projects.approve`).

**Aditivo**
12. **Dado** um aditivo (COM-07) com marco `StageCompleted` em etapa **já concluída**, **quando** é aprovado, **então** o título nasce `Open` pelo registro na hora (vencimento = conclusão + prazo); a tela do aditivo avisa antes "A etapa {letra} já foi concluída em {data}: esta parcela nasce valendo para cobrança."

## Permissões, eventos e tabelas
- **Permissões:** as de hoje (`quotes.edit`, `projects.approve` no aditivo). Nenhuma nova.
- **Eventos:** `IProjectMilestoneReached.StageCode` (padrão nulo, aditivo); `ProjectStageCompleted` passa a implementá-lo; Finance consome `ProjectStageCompleted` e `ProjectStageReopened`.
- **Tabelas:** colunas do CA 1; `finance.project_trigger_events` + `stage_code` e troca do índice (CA 6). Migrations aditivas nos dois módulos.

## Telas e estados
- Editor de marcos (orçamento e aditivo): opção "Ao concluir a etapa…" com as etapas de Execução pela letra e nome; rótulo "Ao concluir a etapa {letra} — {nome}" no orçamento, contrato, PDFs e chip "Aguarda: …" do título. Telas de "Como será executado" e preencher: EXE-05b.
- **Ajuda:** `comercial-orcamento-condicoes.md` e `comercial-contrato.md` ganham "Marco por etapa"; `<Dica>` em "Ao concluir a etapa".

## Plano de testes
- **Unidade:** `MilestoneTriggerPolicy` com `StageCompleted` (tipos, letras, papéis); rótulo.
- **Integração (Testcontainers, obrigatória: libera e devolve recebível):** liberação por etapa; fora de ordem (antes do contrato e do aditivo); duas etapas e dois marcos; evento velho descartado; reabertura tudo ou nada com baixa parcial, boleto e NF (bloqueia e notifica); aditivo com etapa já concluída; troca do índice sem perder linha (cópia do `konebras`, por causa do enum e do índice).
- **Regressão:** Konebras: marcos, títulos, PDFs iguais.

## Tarefas
- [ ] SharedKernel `StageCompleted`; colunas e `CHECK`; `SnapshotMilestone.StageCode`; `MilestoneTriggerPolicy`.
- [ ] Commercial: orçamento, aprovação (tipo calculado), aditivo; `IStageMilestoneGuard` real; `LockedReasonAsync` generalizado.
- [ ] Finance: `IProjectMilestoneReached.StageCode`; `MilestoneReachedConsumer`, `ContractSignedConsumer`, consumidor do aditivo; `MilestoneRevertedConsumer`; índice e `TriggerEventIndex`.
- [ ] Front: editor de marcos e rótulos (`quotes/milestones.ts`, `AmendmentMilestones.tsx`, `receivables.ts`); PDFs; ajuda.
- [ ] Docs `03-modelo-dados`, `04-fluxos-estados` ("Título a receber").

## Dev Notes
- `src/BuildingBlocks/BemAqui.SharedKernel/Milestones.cs`; `src/Modules/Commercial/BemAqui.Commercial.Contracts/ProjectContracts.cs` (`IProjectMilestoneReached`); `Application/ContractSnapshots.cs`, `QuoteService.Conditions.cs`, `AmendmentService.Validate.cs`, `ProjectExecutionService.cs` (`LockedReasonAsync`); `src/Modules/Finance/BemAqui.Finance.Application/MilestoneReachedConsumer.cs`, `ContractSignedConsumer.cs`, `AcceptanceRevertedConsumer.cs`, `ReceivableService.cs` (`TriggerEventIndex`), `Infrastructure/ReceivableConfigurations.cs`.

## Fora do escopo
- "Como será executado", preencher marcos e mão de obra (EXE-05b). Reabrir na tela (EXE-03c). Faturar por horas (EXE-09).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story EXE-05 escrita a partir da proposta aprovada pelo PO | Claude |
| 28/11/2026 | 0.2 | **Revisão combinada de 28/11/2026:** EXE-05 dividida em 05a (gatilho de ponta a ponta) e 05b (orçamento propositivo). `StageCode` no `IProjectMilestoneReached`; lookup, filtro e `ContractSignedConsumer` pela letra; troca do índice com `TriggerEventIndex` e `IsUniqueViolation`; reabertura pelo modelo do desfazer aceite, generalizado, tudo ou nada; aditivo com etapa já concluída; etapas congeladas no snapshot; `quotes.execution_kind` não é persistido (sempre calculado); `StageCompleted` recusado em Fabricação também na API; duas etapas e dois marcos; teste na cópia do `konebras`; confirmado que nenhum cliente externo lê `TriggerEvent`. | Claude |
