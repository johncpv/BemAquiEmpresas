# CAD-04a — Cargos, jornadas, sindicatos e custo-hora

| Campo | Valor |
|---|---|
| ID | CAD-04a |
| Título | Cargos com CBO, faixa salarial e custo-hora calculado (salário × (1 + % encargos) ÷ horas mensais) ou ajustado; jornadas de trabalho e sindicatos |
| Tamanho | M (quebra da CAD-04: o cadastro de funcionários, com LGPD e histórico, foi para a CAD-04b) |
| Status | Draft |
| Requisitos | R-RH-2 · R-RH-1 (jornada e sindicato como cadastros de apoio) · R-PRD-4 (preparação: custo-hora usado no apontamento) · R-SEC-5 · PRD §6 LGPD |
| Dependências | CAD-06 (% de encargos e evento `PayrollChargesChanged`), CAD-03 (centro de trabalho padrão do cargo); Fase 0. **Pré-condição:** pontos em aberto 1 e 2 |
| Bloqueia | CAD-04b, PRD-04, COM-01 (horas por centro de trabalho no custo do orçamento) |
| Ordem de execução | 9 |

## User story
Como **RH e proprietário**, quero **cadastrar os cargos com a faixa salarial e ver o custo-hora de cada um já com os encargos**, para **que toda hora apontada na fábrica vire custo real no projeto, sem planilha à parte**.

## Contexto e referências
- PRD R-RH-2 (cargos/funções com faixa salarial e custo-hora = salário + encargos estimados, usado no apontamento de produção), R-PRD-4 (horas apontadas × custo-hora do cargo).
- `docs/04-fluxos-estados.md` §3.2: `custo_hora_cargo = (salário_base × (1 + %encargos_estimados)) / horas_mensais_contratuais`; `%encargos` configurável por tenant; snapshot gravado em cada apontamento.
- `docs/06-pendencias-riscos.md` P-05 (% de encargos editável em Configurações, CAD-06).
- `docs/03-modelo-dados.md` → `hr.job_positions` (`name`, `cbo`, `salary_min`, `salary_max`, `hourly_cost` "calculado/ajustável", `work_center_id` padrão), `hr.work_schedules` (`name`, `weekly_hours`, `days` JSON com entradas e saídas); `hr.employees.union_id` (o 03 não tem a tabela de sindicatos).
- ADR 0003 e `ModuleCatalog`: `hr` com `view`, `create`, `edit`, `approve`, `delete` e `view_sensitive` ("Ver salário, documentos e dados pessoais protegidos pela LGPD"). O grupo RH do seed tem `view`, `create`, `edit` e `view_sensitive` (ADR 0010 item 3).

## Critérios de aceite
**Cargos**
1. **Dado** `hr.create`, **quando** cadastro um cargo com nome (único entre os não excluídos), CBO (6 dígitos, exibido `9999-99`), faixa salarial (mínimo ≤ máximo, `numeric(18,2)`), salário de referência e centro de trabalho padrão (opcional, ativo, via `IWorkCenters` da CAD-03), e clico "Salvar cargo", **então** ele é gravado e auditado.
2. **Dado** o % de encargos de 80,00% (CAD-06), salário de referência de R$ 3.300,00 e 220 horas mensais contratuais, **quando** salvo o cargo, **então** o custo-hora calculado é R$ 27,00 (`3.300 × 1,80 ÷ 220`), gravado em `numeric(18,6)` e exibido com 2 casas.
3. **Dado** `hr.edit`, **quando** informo um custo-hora manual, **então** o cargo fica marcado "Ajustado manualmente" (com `<Dica>` explicando que não acompanha mudança de encargos) e o botão "Voltar ao calculado" restaura a fórmula.
4. **Dado** o evento `PayrollChargesChanged` (CAD-06), **quando** é processado, **então** todos os cargos não ajustados manualmente têm o custo-hora recalculado; os ajustados não mudam; o processamento é idempotente (inbox por handler, FND-07) e cada recálculo fica no histórico do cargo.
5. **Dado** um tenant sem % de encargos configurado (CAD-06, ponto em aberto 4), **quando** abro "Novo cargo", **então** a tela avisa "Configure o % de encargos em Configurações → Parâmetros para calcular o custo-hora." com o link, e o cargo pode ser salvo só com custo-hora manual.
6. **Dado** Production (PRD-04) ou Commercial (COM-01), **quando** precisam do custo-hora, **então** usam `IHourlyCost` de `HR.Contracts` (por cargo e, na CAD-04b, por funcionário), sem exigir permissão de RH do usuário; o teste de arquitetura impede outra referência.
7. **Dado** um cargo usado por funcionário ativo (CAD-04b), **quando** tento excluir, **então** a API responde 409 "O cargo tem funcionários. Mude o cargo deles antes."; sem uso, `hr.delete` exclui logicamente.

**Dados de salário do cargo (LGPD)**
8. **[Segurança]** **Dado** um usuário com `hr.view` sem `hr.view_sensitive`, **quando** consulta cargos pela API, **então** a resposta não traz faixa salarial nem salário de referência (campos ausentes, não só ocultos na tela); o custo-hora aparece (ponto em aberto 3).
9. **[Segurança]** **Dado** `hr.edit` sem `hr.view_sensitive`, **quando** tento gravar faixa salarial ou salário de referência, **então** a API responde 403 `forbidden`.

**Jornadas**
10. **Dado** `hr.create`, **quando** cadastro uma jornada com nome e, para cada dia da semana, entrada, saída e intervalo, **então** as horas semanais são calculadas a partir dos dias (ex.: seg–sex 08:00–17:48 com 1 h de intervalo = 44,00 h) e gravadas em `weekly_hours`; dia com saída antes da entrada responde 400.
11. **Dado** uma jornada, **quando** a tela mostra as horas mensais contratuais, **então** usa a regra do ponto em aberto 2 (sugestão: semanais × 5, 44 h → 220 h).

**Sindicatos**
12. **Dado** `hr.create`, **quando** cadastro um sindicato com nome, CNPJ (validado, CAD-06) e mês da data-base, **então** ele é gravado e pode ser escolhido no funcionário (CAD-04b).

**Permissões (3 camadas)**
13. **Dado** o módulo `hr` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released`; sem grupo que habilite `hr`, 403 `forbidden`; com `hr.view` sem `create`/`edit`/`delete`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`.

**Auditoria**
14. **Dado** qualquer alteração de cargo, jornada ou sindicato, **quando** abro "Histórico", **então** vejo antes/depois, usuário e data; faixa salarial e salário de referência aparecem como `***` (`[PersonalData]`), para não vazar salário pelo histórico.

## Regras de negócio e técnicas
- **Fórmula** (04 §3.2) num só lugar (`HourlyCostCalculator`, Domain), com `decimal` em todas as etapas e arredondamento só no fim (6 casas).
- **Salário de referência** (`reference_salary`, **proposta**): o 03 não diz qual salário entra na fórmula do cargo; ver ponto em aberto 1.
- **Horas mensais contratuais** (`monthly_hours`, **proposta**): ver ponto em aberto 2.
- **Ajuste manual:** `hourly_cost_manual` (bool, **proposta**).
- **Jornada:** `days` JSON `[{ "weekday": 1, "start": "08:00", "end": "17:48", "break_minutes": 60 }]`; horas em `numeric(5,2)`.
- **Sindicato:** tabela `hr.unions` (**proposta**, não está no 03): `name`, `document`, `base_month`.
- **Soft delete** em cargos, jornadas e sindicatos; jornada ou sindicato em uso só inativa (`is_active`, **proposta**).
- **Ids:** Guid v7. **Auditoria:** `IAuditable`; `salary_min`, `salary_max` e `reference_salary` com `[PersonalData]`.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `hr.view`, `hr.create`, `hr.edit`, `hr.delete`, `hr.view_sensitive`. Nenhuma ação nova.
- **Eventos:** consome `PayrollChargesChanged` (CAD-06); publica `HourlyCostChanged` (**proposta**, `{ positionId, oldCost, newCost }`, sem consumidor nesta fase).
- **Tabelas:** `hr.job_positions` (03) + `reference_salary`, `monthly_hours`, `hourly_cost_manual` (**propostas**); `hr.work_schedules` (03) + `is_active` (**proposta**); `hr.unions` (**proposta**).

## Telas e estados
- **RH → Cargos** (`/app/rh/cargos`): DataTable com nome, CBO, centro padrão, custo-hora (`Money`, `tabular-nums`, chip "Ajustado" quando manual), funcionários no cargo e situação; faixa salarial só com `hr.view_sensitive`.
- **RH → Jornadas** (`/app/rh/jornadas`) e **RH → Sindicatos** (`/app/rh/sindicatos`): listas com editor em painel; o editor da jornada mostra o total semanal e mensal ao vivo.
- **Estados:** carregando; vazio ("Cadastre os cargos da empresa para calcular o custo da hora trabalhada." + "Novo cargo"); erro com motivo e "Tentar de novo"; aviso de encargos não configurados (CA 5).
- **Propositivo:** salário de referência sugerido como a média da faixa; prévia do custo-hora enquanto digita; depois de salvar o cargo, oferece "Cadastrar funcionário neste cargo" (CAD-04b).
- **Ajuda:** `Ajuda/rh-cargos.md` (aba `cargos`), `Ajuda/rh-jornadas.md` (aba `jornadas`), `Ajuda/rh-sindicatos.md` (aba `sindicatos`); `<Dica>` em "Custo-hora", "CBO" e "Horas mensais".

## Plano de testes
- **Unidade:** fórmula do custo-hora com o exemplo do CA 2 e casos de borda (encargos 0%, salário 0); horas da jornada (CA 10, 11); faixa salarial (CA 1).
- **Integração (Testcontainers):** cargo calculado e manual (CA 2, 3); recálculo idempotente pelo evento (CA 4, mesmo evento entregue duas vezes); sem encargos (CA 5); `IHourlyCost` (CA 6); exclusão com uso (CA 7); campos de salário ausentes sem `hr.view_sensitive` e 403 na escrita (CA 8, 9); três camadas (CA 13); máscara no histórico (CA 14).
- **Arquitetura:** HR só referencia `Identity.Contracts` (parâmetros) e `Production.Contracts` (centros); outros módulos só `HR.Contracts`.
- **E2E (Playwright):** cadastrar cargo e jornada, ver o custo-hora; usuário sem `hr.view_sensitive` não vê a faixa.

## Fora do escopo
- Funcionários, dependentes, histórico e afastamentos: CAD-04b.
- Uso do custo-hora no apontamento: PRD-04.
- Folha lançada ou calculada: RH-02 e Fase 7.

## Pontos em aberto
1. **Salário que entra na fórmula do cargo** (pré-condição): o 04 §3.2 usa "salário_base", e o cargo tem faixa. Sugestão: "salário de referência" do cargo, com padrão na média da faixa. Alternativa: média dos salários dos funcionários ativos no cargo (expõe salário individual por dedução quando o cargo tem uma pessoa).
2. **Horas mensais contratuais** (pré-condição): sugestão de semanais × 5 (44 h → 220 h, prática da CLT), guardada no cargo e editável.
3. **Custo-hora é sensível?** O custo-hora permite estimar o salário. Proposta: visível com `hr.view` (a fábrica e o orçamento precisam dele) e a faixa salarial só com `hr.view_sensitive`. Confirmar com o PO.
4. **Seed de jornada** (sugestão: "44 h — seg a sex 08:00–17:48") e de cargos (sugestão: nenhum).
5. **Sindicato como tabela** (proposta) ou texto livre no funcionário.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (schema `hr`, não destrutiva).
- [ ] Permissões usadas só do `ModuleCatalog` (inclui `hr.view_sensitive`, ADR 0003).
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda das três telas escrita; teste de cobertura passando.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada, com `[PersonalData]` nos campos de salário.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (campos e `hr.unions`); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `JobPosition`, `WorkSchedule`, `LaborUnion`; `HourlyCostCalculator`.
- [ ] `IHrDb`, migration, handler de `PayrollChargesChanged` com inbox.
- [ ] `IHourlyCost` em `HR.Contracts`.
- [ ] Serialização condicional dos campos de salário por `hr.view_sensitive`.
- [ ] Endpoints `/api/v1/hr/positions`, `/schedules`, `/unions`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (LGPD), @qa.
- Gate: CA 2 (fórmula), CA 4 (recálculo idempotente) e CA 8 (campo ausente na API) obrigatórios.

## Validação do PO
Checklist do @po (Pax), 18/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** Pontos em aberto 1 (salário que entra na fórmula) e 2 (horas mensais contratuais); valor inicial do % de encargos (CAD-06).
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-04) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
