# EXE-05 — Marcos ligados a etapas e orçamento que sugere a execução

| Campo | Valor |
|---|---|
| ID | EXE-05 |
| Título | Gatilho de marco "Ao concluir a etapa {letra}" (`TriggerEvent.StageCompleted` + `trigger_stage_code`) no orçamento, contrato, aditivo e Finance, com liberação tolerante a eventos fora de ordem; o orçamento mostra "Como será executado" (tipo e modelo de etapas sugeridos), "Preencher marcos pelo modelo" e "Preencher mão de obra pelas etapas"; rótulos por tipo nos PDFs |
| Tamanho | M |
| Status | Draft |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §5 e §7.1 · PRD COML-1, COML-6, PRJ-6 · COM-01a/b (composição de custo, marcos) · COM-04b e ADR 0019 §1 (registro de gatilhos) · COM-07 (aditivo) · R-SEC-5 |
| Dependências | EXE-03 (etapas e `ProjectStageCompleted`); EXE-04 para o CA 9 |
| Bloqueia | REC-01 (início da assinatura "ao concluir a etapa X") |
| Ordem de execução | 5 (Execução depois do contrato, antes da F2 do Conecta) |

## User story
Como **vendedor de serviço**, quero **montar o orçamento já dizendo como o trabalho será feito e amarrar cada parcela a uma etapa**, para **o cliente pagar conforme o projeto anda e o financeiro não precisar lembrar de liberar nada.**

## Contexto e referências
- `TriggerEvent` (SharedKernel) tem `Signature`, `FabricationDone`, `Delivered`, `TechnicalAcceptance`. O Finance grava cada gatilho em `finance.project_trigger_events` (único `(project_id, event)`) e libera o título `Planned → Open` com vencimento = data + `days_after_event` (COM-04b).
- A composição de custo do item do orçamento já tem linhas `Labor` com cargo e horas (COM-01a); hoje são digitadas à mão.

## Critérios de aceite

**Como será executado**
1. **Dado** um orçamento `Draft` num tenant com o recurso `projects.service_execution`, **quando** abro a aba "Condições" (ou a nova seção "Execução" do cabeçalho), **então** vejo "Como será executado": tipo sugerido pela regra da EXE-01 (Serviço quando todos os itens não recorrentes são `Service`) e o **modelo de etapas** sugerido (item principal → segmento → padrão do tenant), com "Trocar modelo"; o tipo não é editável à mão (vem dos itens) e muda sozinho quando os itens mudam, com o aviso "O tipo mudou para {tipo} porque {motivo}."
2. **Dado** a aprovação, **quando** o contrato nasce, **então** `quotes.execution_kind` e `quotes.stage_template_id` vão para o snapshot do contrato e o projeto copia **esse** modelo (EXE-03 CA 1). Copiar orçamento e nova revisão copiam o modelo.

**Marco por etapa**
3. **Dado** um orçamento `Service` com modelo, **quando** adiciono um marco com gatilho "Evento", **então** as opções são: Assinatura do contrato, **Ao concluir a etapa…** (lista as etapas do modelo pela letra e nome), Entrega, Aceite do cliente; o marco grava `trigger_event = StageCompleted` e `trigger_stage_code`.
4. **[Segurança]** **Dado** o servidor, **quando** recebe um marco `StageCompleted`, **então** exige `trigger_stage_code` de uma etapa existente no modelo do orçamento e que não seja a de papel Entrega ou Aceite (para essas, use "Entrega" e "Aceite do cliente"); recusa `trigger_stage_code` em outro gatilho; 422 no campo da linha.
5. **Dado** a troca do modelo no orçamento (ou no projeto `Planned`, EXE-03 CA 3) com marco apontando para letra que não existe no modelo novo, **quando** confirmo, **então** 409 "Os marcos {lista} apontam para etapas que não existem no modelo novo. Ajuste os marcos antes." (no orçamento, o front oferece "Ajustar marcos": os afetados passam a "Entrega").
6. **Dado** "Preencher marcos pelo modelo", **quando** clico, **então** os marcos sugeridos do modelo (proposta §6; campo `suggested_milestones` no modelo, editável na EXE-02 como lista simples de % + gatilho) substituem os atuais depois da confirmação "Substituir {n} marcos?"; a soma fica 100% e o usuário ajusta.

**Contrato, aditivo e Finance**
7. **Dado** um contrato com marco `StageCompleted` letra "G", **quando** o Commercial publica `ProjectStageCompleted` com `stageCode = G`, **então** o Finance grava em `project_trigger_events` (chave `(project_id, event, stage_code)`) e libera o título `Planned` daquele marco com vencimento = data + prazo; outro `stageCode` não libera nada; evento repetido não muda nada (idempotente).
8. **[Segurança]** **Dado** o `ProjectStageCompleted` processado **antes** do `ContractSigned` (ou do aditivo aprovado), **quando** o título nasce, **então** já nasce `Open` pelo registro (ADR 0019 §1); **dado** o `ProjectStageReopened` (EXE-03 CA 11) com o título ainda `Open` sem baixa, **então** volta a `Planned` e a linha do registro é removida; com baixa, nada muda (o Commercial já recusou a reabertura). O consumidor confere que projeto e contrato do título batem com o evento e ignora contrato `Cancelled` (COM-04b CA 17).
9. **Dado** um item `Service` no orçamento com modelo e `quotes.view_cost`, **quando** clico "Preencher mão de obra pelas etapas", **então** o sistema cria (depois da confirmação) uma linha `Labor` por etapa com cargo sugerido e horas previstas × quantidade do item, custo unitário = custo-hora do cargo (`IHourlyCost`); linhas `Labor` existentes do item são substituídas só com a confirmação "Substituir a mão de obra deste item?"; etapa sem cargo entra sem custo, com o selo "Defina o cargo".
10. **Dado** um aditivo (COM-07) de contrato `Service`, **quando** adiciono marcos, **então** valem as mesmas opções e validações (CA 3–4), com as etapas do **projeto** (não do modelo).

**Rótulos e documentos**
11. **Dado** marcos `StageCompleted`, **quando** aparecem no orçamento, contrato, aditivo, PDFs (`ContractPdfModel` e PDF do orçamento) e no chip "Aguarda: …" do título a receber, **então** o texto é "Ao concluir a etapa {letra} — {nome}" (nome do projeto quando existir, senão do modelo congelado no snapshot); "após {n} dias" quando houver prazo.
12. **Dado** um tenant sem o recurso (Konebras), **quando** usa orçamento, contrato e títulos, **então** nada muda (opções, PDFs e API iguais; teste de regressão); a API recusa `StageCompleted` com 422 "Gatilho não disponível."

## Regras de negócio e técnicas
- `TriggerEvent.StageCompleted` entra no fim do enum (aditivo, texto no banco). `trigger_stage_code` `char(1)` anulável, `CHECK` "preenchido se e só se `StageCompleted`" em `quote_milestones`, `contract_milestones`, `contract_amendment_milestones` e `finance.receivables`.
- Índice de `project_trigger_events`: o único `(project_id, event)` vira `(project_id, event, coalesce(stage_code, ''))` — criar o novo, depois remover o antigo, na mesma migration (troca de índice, nenhum dado apagado).
- A REC-01 reaproveita este vocabulário para o início da assinatura ("ao concluir a etapa X", "na entrega", "no aceite do cliente").

## Permissões, eventos e tabelas
- **Permissões:** as de hoje — `quotes.edit` (marcos e modelo no orçamento), `quotes.view_cost` (preencher mão de obra), `projects.approve` (aditivo, COM-07). Nenhuma ação nova.
- **Eventos:** Finance consome `ProjectStageCompleted` e `ProjectStageReopened` (publicados na EXE-03).
- **Tabelas:** `commercial.quotes` (+ `execution_kind`, `stage_template_id`); `quote_milestones`, `contract_milestones`, `contract_amendment_milestones` (+ `trigger_stage_code`); `commercial.stage_templates` (+ `suggested_milestones` jsonb); `finance.receivables` (+ `trigger_stage_code`); `finance.project_trigger_events` (+ `stage_code`, troca do índice único). Migrations aditivas nos dois módulos.

## Telas e estados
- **Orçamento → Condições:** bloco "Como será executado" acima dos marcos; editor de marcos com a opção de etapa; botões "Preencher marcos pelo modelo" e, na composição do item, "Preencher mão de obra pelas etapas". Confirmações "Marcos preenchidos", "Mão de obra preenchida".
- **Estados:** sem modelo cadastrado: "Nenhum modelo de etapas. Os marcos usam entrega e aceite." com "Criar modelo" (`projects.edit`); erro com "Tentar de novo".
- **400 px:** o gatilho de etapa aparece como "Etapa G — Go-live" no cartão do marco.
- **Ajuda:** `Ajuda/comercial-orcamento-condicoes.md` ganha "Como será executado" e "Marco por etapa"; `Ajuda/comercial-contrato.md` e a ajuda de A receber ganham o rótulo; `<Dica>` em "Ao concluir a etapa".

## Plano de testes
- **Unidade:** validação do marco por etapa; texto do rótulo; preencher marcos e mão de obra.
- **Integração (Testcontainers, obrigatória: libera recebível):** etapa concluída libera só o marco da letra; fora de ordem (evento antes do contrato e antes do aditivo); reabrir volta o título sem baixa; idempotência; contrato cancelado ignorado; troca de índice sem perder linha existente.
- **Regressão Konebras:** marcos, títulos e PDFs iguais (cópia do `konebras`).
- **Front (Vitest):** opções por tipo; aviso de troca de tipo; confirmação de substituição.

## Tarefas
- [ ] SharedKernel: `TriggerEvent.StageCompleted`.
- [ ] Commercial: colunas e validação no orçamento, contrato e aditivo; snapshot; `QuoteService.Conditions.cs`, `QuoteService.Costs.cs` (preencher mão de obra), `AmendmentService.Validate.cs`; `suggested_milestones` no modelo (EXE-02 ganha o campo na tela).
- [ ] Finance: `ContractSignedConsumer.cs`, `MilestoneReachedConsumer.cs` (chave com etapa), consumidor de `ProjectStageReopened`, migration do índice.
- [ ] Endpoints e OpenAPI/Orval; front: `quotes/milestones.ts`, editor de marcos, `AmendmentMilestones.tsx`, `ContractItems.tsx`, `finance/receivables/receivables.ts`.
- [ ] PDFs: `ContractPdfModel.cs`, PDF do orçamento.
- [ ] Docs: `03-modelo-dados`, `04-fluxos-estados` §1 "Orçamento" e "Título a receber", ajuda.

## Dev Notes
- **Vocabulário:** `src/BuildingBlocks/BemAqui.SharedKernel/Milestones.cs`.
- **Finance:** `src/Modules/Finance/BemAqui.Finance.Application/ContractSignedConsumer.cs`, `MilestoneReachedConsumer.cs`, `ReceivableService.cs`, `Domain/ProjectTriggerEvent.cs`.
- **Commercial:** `QuoteService.Conditions.cs`, `QuoteMilestoneDtos.cs`, `QuoteService.Costs.cs`, `QuoteLookups.cs` (já usa `IHourlyCost`), `AmendmentService.*.cs`, `Documents/ContractPdfModel.cs`.
- **Front:** `src/Web/src/pages/quotes/milestones.ts`, `src/Web/src/pages/contracts/ContractItems.tsx`, `contracts/amendments/AmendmentMilestones.tsx`, `finance/receivables/receivables.ts`.
- Testes: `--filter-class "*Milestone*"`, `"*Quote*"`, `"*Receivable*"`.

## Fora do escopo
- Faturar por horas (EXE-09). Marco por percentual de avanço. Início da assinatura (REC-01, que reaproveita este gatilho). Resultado do contrato (EXE-07).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir da proposta aprovada pelo PO (27/11/2026) | Claude |
