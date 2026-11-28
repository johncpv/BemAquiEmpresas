# EXE-05b — Orçamento que sugere a execução: "Como será executado", preencher marcos e preencher mão de obra

| Campo | Valor |
|---|---|
| ID | EXE-05b |
| Título | No orçamento de serviço: bloco "Como será executado" (tipo calculado e modelo de etapas sugerido e trocável); "Preencher marcos pelo modelo" com escolha explícita por marco afetado; "Preencher mão de obra pelas etapas" (horas por unidade × custo-hora de hoje do cargo); reexecução quando o modelo muda, inclusive na aprovação |
| Tamanho | M |
| Status | Draft (revisão combinada de 28/11/2026) |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §5 · PRD COML-1, COML-6 · COM-01a/b · EXE-02 (`IStageTemplates`, marcos sugeridos) · EXE-05a (`StageCompleted`) · R-SEC-5 |
| Dependências | EXE-05a, EXE-02; EXE-04a para o aviso de custo realizado |
| Bloqueia | REC-01 (usa o bloco de execução) |
| Ordem de execução | 10 |

## User story
Como **vendedor de serviço**, quero **que o orçamento já venha com as etapas, os marcos e a mão de obra sugeridos pelo modelo**, para **montar a proposta rápido e sem esquecer nada, decidindo eu mesmo o que fica.**

## Critérios de aceite

**Como será executado**
1. **Dado** um orçamento `Draft` num tenant com o recurso, **quando** abro a aba "Condições", **então** o bloco "Como será executado" mostra o tipo **calculado** pelos itens (`ExecutionKindRule`, não editável; muda sozinho com o aviso "O tipo mudou para {tipo} porque {motivo}.") e, no `Service`, o modelo de etapas sugerido (item principal → segmento → padrão do tenant) com "Trocar modelo"; `quotes.stage_template_id` e `stage_template_revision` gravam a escolha. Cópia e nova revisão copiam a escolha.

**Preencher marcos**
2. **Dado** "Preencher marcos pelo modelo", **quando** clico, **então** o diálogo mostra os marcos sugeridos do modelo (EXE-02) ao lado dos atuais e só substitui depois de "Substituir {n} marcos?"; a soma fica 100%.
3. **Dado** a troca do modelo (ou mudança de itens que troca o modelo sugerido) com marcos `StageCompleted` apontando para letras que não existem no modelo novo, **quando** confirmo a troca, **então** o diálogo **lista cada marco afetado** e exige uma escolha explícita por marco (outra etapa do modelo novo, Entrega, Aceite do cliente, data ou remover); nunca troca sozinho. A escolha vai para a auditoria do orçamento.
4. **[Segurança]** **Dado** a aprovação, **quando** o servidor recalcula o tipo e lê o modelo na revisão gravada, **então**, se o modelo mudou de revisão e algum marco aponta para letra que não existe mais, 422 `milestone_stage_missing` com a lista e `tab: "condicoes"` (o usuário refaz a escolha do CA 3); com o modelo igual, aprova.

**Preencher mão de obra**
5. **Dado** um item `Service` com modelo e `quotes.view_cost`, **quando** clico "Preencher mão de obra pelas etapas", **então** cria (depois de confirmar) uma linha `Labor` por etapa com cargo sugerido, **horas por unidade** do modelo (a quantidade do item multiplica, como toda a composição por unidade) e custo unitário = custo-hora **de hoje** do cargo (`IHourlyCost`); linhas `Labor` existentes do item só são substituídas com "Substituir a mão de obra deste item?".
6. **Dado** etapas sem cargo ou cargo sem custo-hora, **quando** abro o botão, **então** ele fica desabilitado com o motivo ("A etapa {letra} não tem cargo. Defina no modelo." / "O cargo {cargo} não tem custo-hora.") com o link para o modelo ou para o cargo (com `hr.edit`).
7. **Dado** o diálogo, **quando** abre, **então** avisa: "O orçado usa o custo-hora de hoje. O realizado usa o custo do dia em que as horas forem lançadas."

## Permissões, eventos e tabelas
- **Permissões:** `quotes.edit` (bloco e marcos), `quotes.view_cost` (mão de obra). Nenhuma nova.
- **Tabelas:** `commercial.quotes` + `stage_template_id`, `stage_template_revision` (o tipo **não** é coluna: sempre calculado, EXE-05a CA 3).

## Telas e estados
- Bloco "Como será executado" acima dos marcos; diálogos de preencher com a lista de afetados; estados: sem modelo "Nenhum modelo de etapas. Os marcos usam entrega e aceite." com "Criar modelo" (`projects.edit`); erro com "Tentar de novo".
- Celular: marcos afetados em cartões, uma escolha por cartão.
- **Ajuda:** `comercial-orcamento-condicoes.md` ganha "Como será executado", "Preencher marcos" e "Preencher mão de obra"; `<Dica>` em "Horas por unidade".

## Plano de testes
- **Unidade:** tipo calculado; montagem dos marcos sugeridos; mão de obra por unidade.
- **Integração:** troca de modelo com marcos afetados exige escolha (sem escolha: 422); aprovação com revisão nova do modelo; auditoria da escolha; botão desabilitado sem cargo/custo; cópia e revisão do orçamento.
- **Front (Vitest):** bloco, diálogo de afetados, aviso de custo.

## Tarefas
- [ ] Colunas no orçamento; `QuoteService.Conditions.cs` (bloco, preencher marcos, escolha por marco), `QuoteService.Costs.cs` (mão de obra), revalidação na aprovação.
- [ ] Endpoints, OpenAPI e Orval; front e ajuda.

## Dev Notes
- `src/Modules/Commercial/BemAqui.Commercial.Application/QuoteService.Conditions.cs`, `QuoteService.Costs.cs`, `QuoteLookups.cs` (`IHourlyCost`), `QuoteApprovalService.Approve.cs`; `IStageTemplates` (EXE-02).

## Fora do escopo
- Gatilho de etapa no Finance (EXE-05a). Assinatura (REC-01).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 28/11/2026 | 0.1 | **Revisão combinada de 28/11/2026:** parte propositiva da antiga EXE-05. Escolha explícita por marco afetado (nunca troca silenciosa para Entrega), com auditoria; reexecução quando o modelo muda, inclusive na aprovação; botão de mão de obra desabilitado com motivo; aviso de custo de hoje × custo do lançamento. **Decisão do mantenedor:** horas por unidade do item. | Claude |
