# EXE-04b — Horas: cronômetro, "Minhas horas", recomposição do custo e avisos

| Campo | Valor |
|---|---|
| ID | EXE-04b |
| Título | Cronômetro com a duração pelo relógio do servidor; tela "Minhas horas"; recomposição dos lançamentos sem custo quando o custo-hora do cargo muda (`HourlyCostChanged`); avisos na Minha pauta; Excel da aba Horas respeitando `projects.view_cost` |
| Tamanho | M |
| Status | Draft (revisão combinada de 28/11/2026) |
| Requisitos | EXE-04a · PRD-03a (um apontamento aberto por funcionário) · PRD-04 (recomposição) · EXP-01 (Excel) · DSH-04 · R-SEC-5 |
| Dependências | EXE-04a |
| Bloqueia | — |
| Ordem de execução | 9 |

## User story
Como **consultor**, quero **ligar o cronômetro quando começo a trabalhar numa etapa e ver minhas horas da semana**, para **não esquecer de lançar.**

## Critérios de aceite

**Cronômetro**
1. **Dado** `projects.log_time`, **quando** clico "Iniciar cronômetro" numa etapa que aceita horas (EXE-04a CA 1), **então** nasce um lançamento aberto (`origin = Timer`, um aberto por funcionário, índice único parcial) com `started_at` do **relógio do servidor**; "Parar" fecha com `ended_at` do servidor; a duração nunca vem do navegador; `work_date` = data do início (São Paulo).
2. **Dado** um cronômetro que passa de 24 h, **quando** paro, **então** não grava: pede "Corrigir à mão" (lançamento manual da EXE-04a com motivo) e o aberto é descartado.
3. **Dado** um cronômetro aberto, **quando** o projeto vai a `Closed` ou `Cancelled`, o contrato é cancelado ou o funcionário é desligado, **então** ele para sozinho no momento do evento (lançamento fechado até ali, com custo) e o funcionário recebe o aviso "Seu cronômetro em {projeto} foi parado: {motivo}."
4. **Dado** o cronômetro em várias abas ou aparelhos, **quando** a tela fica visível ou a cada 60 s, **então** consulta `GET /me/timer` e mostra o estado do servidor (sem `localStorage` como fonte); aberto há mais de 12 h mostra "Cronômetro aberto há {n} h. Pare ou corrija."
5. **[Segurança]** **Dado** o cronômetro, **quando** inicia ou para, **então** exige `Idempotency-Key`; parar o de outro funcionário só com `projects.edit`.

**Minhas horas**
6. **Dado** `projects.log_time`, **quando** abro **Minhas horas** (`/app/minhas-horas`), **então** vejo a semana (seg–dom) por dia com meus lançamentos e o total, "Lançar horas" com projeto e etapa sugeridos pelos últimos usados, o cronômetro e a navegação entre semanas; vazio: "Nenhuma hora lançada nesta semana." com "Lançar horas". Valores em reais só com `projects.view_cost`.

**Recomposição**
7. **Dado** o `HourlyCostChanged` (HR), **quando** chega, **então** o consumidor busca o custo novo por `IHourlyCost.ForPositionAsync` e recompõe **só** os lançamentos com custo 0 ("Sem custo-hora") daquele cargo em projetos não `Closed`/`Cancelled` (razão: `seq` n+1 com a diferença), idempotente; os que têm snapshot não mudam (ADR 0025). Histórico de custo do lançamento (`time_entry_cost_history`, só inserção, lido com `projects.view_cost`).

**Avisos e Excel**
8. **Dado** a Minha pauta, **quando** é montada, **então**: quem tem `hr.edit` vê "Cargo {cargo} sem custo-hora: {n} lançamentos sem custo."; o responsável do projeto vê "Etapa {letra} {nome} de {projeto} passou do previsto ({lançadas} de {previstas} h)." com "Ver horas".
9. **[Segurança]** **Dado** "Exportar Excel" na aba Horas, **quando** exporto, **então** as colunas seguem a visibilidade da EXE-04a (por pessoa só com `projects.edit`/`view_cost`; reais só com `view_cost`) e a descrição **não** sai.

## Permissões, eventos e tabelas
- Sem ação nova. Consome `HourlyCostChanged`; consome `ContractCancelled` e o fechamento do projeto para parar cronômetros; desligamento do funcionário pelo evento do RH já existente.
- **Tabelas:** `time_entry_cost_history` (nova, fora da auditoria); `project_time_entries` + `started_at`, `ended_at` e índice único parcial `(employee_id) WHERE origin = 'Timer' AND ended_at IS NULL`.

## Telas e estados
- Barra do cronômetro no topo de qualquer tela; Minhas horas (item no menu Comercial com `projects.log_time` e o recurso); estados de vazio, carregando e erro; celular: semana em lista, cronômetro como barra fixa.
- **Ajuda:** `Ajuda/minhas-horas.md`; seção "Cronômetro" em `projetos-projeto-horas.md`.

## Plano de testes
- **Integração:** duração pelo servidor; teto de 24 h; parada automática nos três eventos; um aberto por funcionário; recomposição idempotente só dos sem custo e não em encerrado; pauta; Excel por permissão.
- **Front (Vitest):** cronômetro sincronizado por consulta; Minhas horas.

## Tarefas
- [ ] Cronômetro (start/stop/consulta), consumidores de parada, `HourlyCostChangedConsumer`, histórico de custo, fontes de pauta, Excel.
- [ ] Endpoints `/api/v1/me/timer` (GET, POST start/stop), `/api/v1/me/time-entries?week=`; OpenAPI e Orval; front e ajuda.

## Dev Notes
- `src/Modules/HR/BemAqui.HR.Contracts/HrContracts.cs` (`HourlyCostChanged`, `IHourlyCost`); `src/Modules/Production/BemAqui.Production.Application/LaborCostBackfill.cs` (padrão de recomposição); `CommercialAgendaSources.cs`; `Exports/CommercialExports.cs`.

## Fora do escopo
- Aprovação de horas; faturar por horas (EXE-09); horas na assinatura (REC-01/EXE-07).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 28/11/2026 | 0.1 | **Revisão combinada de 28/11/2026:** parte de cronômetro, Minhas horas e recomposição da antiga EXE-04. Duração só pelo relógio do servidor, `work_date` do início, teto de 24 h com correção manual, parada automática (encerrado, cancelado, desligado), situações da etapa que aceitam horas, sincronização por consulta, recomposição pelo `IHourlyCost.ForPositionAsync`, Excel respeitando `view_cost`. | Claude |
