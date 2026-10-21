# PRD-04 — Custo de mão de obra

| Campo | Valor |
|---|---|
| ID | PRD-04 |
| Título | Apontamento fechado × custo-hora do cargo do funcionário (snapshot no fechamento) = custo de mão de obra da OS e do projeto (`LaborEntryClosed` → realizado de Mão de obra no Projeto 360); correção de apontamento refaz o custo por estorno |
| Tamanho | P |
| Status | Draft |
| Requisitos | R-PRD-4 (horas apontadas × custo-hora do cargo = custo de mão de obra da OS e do projeto) · R-RH-2 (custo-hora do cargo) · R-PRJ-5 (realizado de mão de obra) · R-SEC-5 · PRD §6 (LGPD: salário não aparece) |
| Dependências | PRD-03a (`LaborEntryClosed`, `LaborEntryCorrected`), CAD-04a (`IHourlyCost.ForPositionAsync`), CAD-04b (cargo do funcionário, `IEmployees`), EST-03 (`commercial.project_costs`, aba Custos da OS) |
| Bloqueia | PRD-06a (custo da OS para o produto acabado), DSH-02 (Fase 4) |
| Ordem de execução | 17 (Marco 3.4 "Fábrica rodando") |

## User story
Como **proprietário**, quero **que cada hora apontada vire custo no projeto pelo custo-hora do cargo de quem trabalhou**, para **ver quanto a mão de obra está custando em cada equipamento, comparado com o orçado**.

## Contexto e referências
- PRD §5.7 R-PRD-4; §5.3 R-RH-2; §5.5 R-PRJ-5; protótipo do operador: "Custo real por hora. Cada início e fim de etapa vira horas apontadas, multiplicadas pelo custo-hora do cargo e lançadas no projeto."
- `04-fluxos-estados` §3.2 (`custo_hora_cargo = salário_base × (1 + %encargos) ÷ horas mensais`; **snapshot gravado em cada apontamento**); §2 (`LaborEntryClosed` → registra custo de mão de obra no projeto); §3.3 (realizado de mão de obra = apontamentos).
- `03-modelo-dados` → `labor_entries.hourly_cost_snapshot` (`numeric(18,6)`), `labor_entries.cost`; `hr.job_positions.hourly_cost` (calculado/ajustável, CAD-04a); `commercial.project_costs` (`source` Labor).
- `06` **P-05 (decidida):** % de encargos editável em Configurações (80,00%).
- CAD-04a: "custo-hora só por cargo" (o salário do funcionário é `[PersonalData]` e não entra).

## Critérios de aceite
1. **Dado** um `LaborEntryClosed` de 2 h 30 min líquidos (150 min) de um funcionário cujo cargo tem custo-hora R$ 48,000000, **quando** o apontamento fecha (PRD-03a CA 8), **então** na mesma transação do fechamento (reação dentro do módulo é síncrona, ADR 0006) o apontamento grava `hourly_cost_snapshot` = 48,000000 (lido por `IHourlyCost.ForPositionAsync` do cargo do funcionário **no fechamento**) e `cost` = 150 ÷ 60 × 48,000000 = R$ 120,00 (2 casas, `AwayFromZero`), e o `LaborEntryClosed` que vai para o outbox passa a levar `amount` e `date` (o evento da PRD-03a ganha os campos; `04-fluxos` §2: `LaborEntryClosed` → custo de mão de obra no projeto).
2. **Dado** o `LaborEntryClosed` com `amount`, **quando** o consumidor do Commercial processa, **então** grava em `commercial.project_costs` `{ cost_type: Labor, source: Labor, source_id: laborEntryId, amount, date, production_order_id }`, idempotente pela inbox e por `(source, source_id)`.
3. **Dado** a aba Custos do Projeto 360 (COM-05), **quando** abro com `projects.view_cost`, **então** o **Realizado de Mão de obra** = Σ `project_costs` de `Labor` do projeto (+ títulos a pagar com natureza Mão de obra, se houver), e a legenda "Material e horas entram com a produção (Fase 3)" sai; a margem projetada usa o valor; dataset fixo: 3 apontamentos de 2 cargos → total conferido com a planilha.
4. **Dado** a aba Custos da OS (EST-03) e a aba Apontamentos (PRD-03a), **quando** abro com `projects.view_cost`, **então** vejo horas e custo por etapa e por apontamento; sem `projects.view_cost`, a aba Apontamentos mostra só horas (o DTO não tem custo, ADR 0019 §20).
5. **Dado** o custo-hora do cargo mudou depois do apontamento (novo % de encargos, reajuste do cargo), **quando** consulto, **então** o apontamento antigo mantém o snapshot; só os apontamentos fechados depois usam o valor novo.
6. **Dado** um cargo sem custo-hora (nulo) ou funcionário sem cargo, **quando** o apontamento fecha, **então** grava o custo 0 com a marca "Sem custo-hora", e quem tem `hr.edit` recebe a notificação "Cargo {nome} sem custo-hora: {n} apontamentos sem custo." com link para o cargo (`dedup_key` `labor-no-cost:{positionId}`); ao preencher o custo-hora, "Recalcular apontamentos sem custo" (com `production.approve`) aplica o valor atual a esses apontamentos (proposta, Pontos em aberto 2).
7. **Dado** um `LaborEntryCorrected` (PRD-03a CA 14), **quando** processa, **então** o custo é refeito com o **mesmo snapshot** e as horas novas; o `LaborEntryCorrected` passa a levar o valor anterior e o novo, e o Commercial grava o estorno (valor negativo do custo antigo) e o novo valor (`source_id` com a versão da correção, proposta), de modo que o realizado bate com as horas corrigidas e nada é apagado.
8. **[Segurança]** **Dado** o custo de mão de obra, **quando** é exibido ou trafega, **então** nunca mostra salário nem dado do funcionário além do nome; o evento leva só ids, valor e data; um usuário com `hr.view` sem `projects.view_cost` não vê custo de apontamento.
9. **Dado** os módulos `production` ou `projects` não liberados, **quando** chamo as consultas de custo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `projects.view_cost`, 403 na API de custos. `RequireModule` e `perm:`.
10. **Dado** qualquer custo gravado, recalculado ou estornado, **quando** abro o histórico do apontamento, **então** vejo o snapshot, as horas, o valor, a origem ("Fechamento", "Correção", "Recálculo") e quem fez.

## Regras de negócio e técnicas
- **Snapshot no fechamento** (proposta sobre o "snapshot gravado em cada apontamento" do `04-fluxos` §3.2: o fechamento é o momento em que o apontamento passa a existir com horas; Pontos em aberto 1).
- **Horas líquidas:** sem as pausas (todas as pausas ficam fora do custo, inclusive falta de material e máquina parada; Pontos em aberto 3).
- **Custo** = minutos ÷ 60 × custo-hora, arredondado só no total do apontamento (ADR 0019 §9).
- **Estorno, não edição:** a correção gera o par negativo + novo no `project_costs` (soma imutável).
- Dinheiro `numeric(18,2)`, custo-hora `numeric(18,6)`; auditoria `IAuditable` no apontamento.

## Permissões, eventos e tabelas
- **Permissões:** `projects.view_cost` (ver custo), `production.approve` (recalcular sem custo), `hr.edit` (recebe o aviso).
- **Eventos:** `LaborEntryClosed` e `LaborEntryCorrected` (`Production.Contracts`, da PRD-03a) ganham `amount`/`previousAmount` e `date`; **consumidor novo no Commercial** grava `project_costs`. Nenhum evento novo.
- **Contratos:** `IHourlyCost`, `IEmployees` (cargo), `IProjectCosts` (Commercial).
- **Tabelas:** `production.labor_entries` (`hourly_cost_snapshot`, `cost`, `cost_missing`); `commercial.project_costs` (`source` Labor).

## Telas e estados
- **Projeto → Custos** (linha Mão de obra), **OS → Custos** e **OS → Apontamentos** (coluna Custo com `projects.view_cost`).
- **Estados:** "Sem custo-hora" com chip alerta e ação "Abrir cargo"; vazio ("Nenhuma hora apontada ainda.").
- **Propositivo:** aviso acionável do CA 6.
- **400 px:** custos por etapa em cartões.
- **Ajuda:** atualização de `Ajuda/projetos-projeto-custos.md`, `Ajuda/producao-os-custos.md` e `Ajuda/producao-os-apontamentos.md`; `<Dica>` em "Custo-hora" ("Custo do cargo por hora, com encargos; o salário de cada pessoa não aparece.").

## Plano de testes
- **Unidade:** cálculo do custo e arredondamento (CA 1); estorno e novo valor na correção (CA 7).
- **Integração (Testcontainers, obrigatória: lança custo):** snapshot e custo no fechamento, evento e consumidor idempotente (CA 1, 2); Custos do projeto com o dataset fixo (CA 3); DTO sem custo sem a permissão (CA 4, 8); custo-hora alterado depois (CA 5); sem custo-hora, aviso e recálculo (CA 6); correção (CA 7); três camadas (CA 9); histórico (CA 10).
- **Arquitetura:** DTO de apontamento sem propriedade de custo para quem não tem `projects.view_cost`.

## Fora do escopo
- Folha e custo real por funcionário: Fase 6/7.
- Hora extra com adicional no custo: fora do MVP (custo-hora único por cargo).
- Custo de máquina/centro por hora: fora do MVP.

## Pontos em aberto
1. **Momento do snapshot** (PO): proposta: no fechamento do apontamento; alternativa: no início.
2. **Recalcular apontamentos sem custo** (PO): proposta: permitido com `production.approve` para os marcados "Sem custo-hora" (CA 6); os demais nunca são recalculados.
3. **Pausa paga** (PO/contadora): proposta: nenhuma pausa entra no custo da OS; o tempo parado por falta de material aparece como indicador na Fase 4.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e arquitetura passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados e 400 px.
- [ ] Ajudas atualizadas; teste de cobertura passando.
- [ ] Alerta acionável (sem custo-hora) aplicado.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/04-fluxos-estados.md` §3.2 (momento do snapshot) e `docs/03-modelo-dados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `LaborCostCalculator` puro, chamado no fechamento e na correção do apontamento.
- [ ] Campos de valor no `LaborEntryClosed`/`LaborEntryCorrected`; consumidor no Commercial (`project_costs`).
- [ ] Colunas de custo nas abas e no projeto; aviso e recálculo.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1, 3, 7 e 8 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** sem evento novo: o `LaborEntryClosed` do `04-fluxos` passa a levar o custo (a primeira versão criava um `LaborCostRecorded`); custo pelo custo-hora do cargo, nunca pelo salário (CA 8, LGPD); correção por estorno e novo valor (CA 7); o momento do snapshot ficou como ponto em aberto (1) com proposta.
- **Pré-condições para `Ready`:** ponto em aberto 1.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
