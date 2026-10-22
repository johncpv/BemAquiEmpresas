# PRD-04 — Custo de mão de obra e custo real do projeto

| Campo | Valor |
|---|---|
| ID | PRD-04 |
| Título | Apontamento fechado × custo-hora do cargo do funcionário (snapshot no fechamento) = custo de mão de obra da OS e do projeto; cria a razão de diferenças `commercial.project_costs` (ADR 0025); troca o cálculo do Projeto 360 (realizado, "Comprometido" e projetado, D2) com teste de regressão; recompõe os apontamentos já fechados (D3) |
| Tamanho | M (re-estimada de P) |
| Status | Done |
| Requisitos | R-PRD-4 (horas apontadas × custo-hora do cargo = custo de mão de obra da OS e do projeto) · R-RH-2 (custo-hora do cargo) · R-PRJ-5 (orçado × realizado do projeto) · R-SEC-5 · PRD §6 (LGPD: salário não aparece) |
| Dependências | PRD-03a, CAD-04a, COM-05; **ADR 0025** |
| Bloqueia | PRD-06a1, EST-03 |
| Ordem de execução | 6 (Marco 3.1 "Fábrica visível") |

## User story
Como **proprietário**, quero **que cada hora apontada vire custo no projeto pelo custo-hora do cargo de quem trabalhou, e que o Projeto 360 separe o que já foi gasto do que está só comprometido**, para **ver quanto a mão de obra está custando em cada equipamento e confiar na margem projetada**.

## Contexto e referências
- PRD §5.7 R-PRD-4; §5.3 R-RH-2; §5.5 R-PRJ-5; protótipo do operador: "Custo real por hora. Cada início e fim de etapa vira horas apontadas, multiplicadas pelo custo-hora do cargo e lançadas no projeto."
- `04-fluxos-estados` §3.2 (`custo_hora_cargo = salário_base × (1 + %encargos) ÷ horas mensais`; snapshot gravado em cada apontamento); §2 (`LaborEntryClosed` → registra custo de mão de obra no projeto); §3.3 (orçado × realizado).
- `03-modelo-dados` → `labor_entries.hourly_cost_snapshot` (`numeric(18,6)`), `labor_entries.cost`; `hr.job_positions.hourly_cost` (CAD-04a).
- `06` **P-05 (decidida):** % de encargos editável em Configurações (80,00%).
- CAD-04a: "custo-hora só por cargo" (o salário do funcionário é `[PersonalData]` e não entra).
- **Revisão pré-Fase 3:** `docs/revisoes/2026-10-21-revisao-pre-fase3.md` (D2, D3, D17 (a), tabela 1.1 itens 73–75, §2.1 "Custo real do projeto", §2.3 padrão 11).
- **ADR 0025** (custo real do projeto: §1 três colunas, §2 razão de diferenças, §3 recompor o passado, §4 regressão da COM-05); ADR 0019 §20 (`projects.view_cost`); ADR 0006 (outbox e inbox).
- **Código atual (COM-05, já no ar):** `ProjectCosts.Compute` em `src/Modules/Commercial/BemAqui.Commercial.Application/ProjectViews.cs` soma como realizado todo título a pagar não cancelado (`payables.Where(p => !p.Cancelled)`), inclusive os `Planned`; os títulos vêm de `IPayables.ByProjectAsync` (`ProjectPayable`: `Origin`, `CostType`, `Amount`, `Balance`, `Status`), em `src/Modules/Finance/BemAqui.Finance.Contracts/PayablesContracts.cs`. Teste atual: `tests/BemAqui.Integration.Tests/Commercial/ProjectCostsTests.cs`.

## Critérios de aceite
**Custo do apontamento**
1. **Dado** um apontamento de 2 h 30 min líquidos (150 min) de um funcionário cujo cargo tem custo-hora R$ 48,000000, **quando** o apontamento fecha (PRD-03a), **então** na mesma transação do fechamento (reação dentro do módulo é síncrona, ADR 0006) o apontamento grava `hourly_cost_snapshot` = 48,000000 (lido por `IHourlyCost.ForPositionAsync` do cargo do funcionário **no fechamento**, ponto 73), `cost` = 150 ÷ 60 × 48,000000 = R$ 120,00 (2 casas, `AwayFromZero`) e `cost_version` = 1, e o `LaborEntryClosed` que vai para o outbox passa a levar `version`, `previousAmount` (0), `newAmount` (120,00) e `occurredOn` (data do fechamento em São Paulo) (ADR 0025 §2).
2. **Dado** qualquer pausa do apontamento (falta de material, máquina parada, intervalo, fim do turno, outro), **quando** o custo é calculado, **então** os minutos líquidos excluem **todas** as pausas (ponto 75): nenhuma pausa entra no custo da OS; o tempo parado fica guardado nas pausas para o indicador da Fase 4.
3. **Dado** o custo-hora do cargo mudou depois do fechamento (novo % de encargos, reajuste do cargo), **quando** consulto o apontamento antigo, **então** ele mantém o snapshot; só os apontamentos fechados depois usam o valor novo.
4. **Dado** um `LaborEntryCorrected` (correção de apontamento da PRD-03a), **quando** a correção grava, **então** na mesma transação o custo é refeito com o **mesmo snapshot** e as horas novas, `cost_version` sobe 1, e o evento passa a levar `version`, `previousAmount` (custo antes), `newAmount` (custo depois) e `occurredOn`; nada é apagado.
5. **Dado** um cargo sem custo-hora (nulo) ou funcionário sem cargo, **quando** o apontamento fecha, **então** grava o custo 0 com a marca `cost_missing` ("Sem custo-hora"), e quem tem `hr.edit` recebe a notificação "Cargo {nome} sem custo-hora: {n} apontamentos sem custo." com link para o cargo (`dedup_key` `labor-no-cost:{positionId}`).
6. **Dado** `production.approve` e o cargo já com custo-hora, **quando** clico "Recalcular apontamentos sem custo" e confirmo no diálogo de impacto ("{n} apontamentos · R$ 0,00 → R$ {total}"), **então** só os apontamentos marcados `cost_missing` recebem o custo-hora atual como snapshot, perdem a marca, sobem `cost_version` e publicam `LaborEntryCorrected` com a origem `Recalculation` e a diferença (ponto 74); os apontamentos com snapshot nunca são recalculados; a mensagem diz "Apontamentos recalculados".

**Razão de diferenças `commercial.project_costs` (ADR 0025 §2)**
7. **Dado** a migration desta story, **quando** roda, **então** nasce a tabela `commercial.project_costs` (`id`, `project_id`, `production_order_id` anulável, `cost_type`, `source` (`StockIssue`/`StockReturn`/`Labor`/`LaborCorrection`), `source_id`, `seq`, `amount` com sinal `numeric(18,2)`, `occurred_on` `date`, `recorded_at`, `is_backfill`), com índice único `(source, source_id, seq)`; só o Commercial grava nela (teste de posse de tabela); `StockIssue` e `StockReturn` ficam previstos para a EST-03.
8. **Dado** um `LaborEntryClosed` ou `LaborEntryCorrected`, **quando** o consumidor do Commercial processa, **então** grava uma linha `cost_type = Labor` (`source` `Labor` no fechamento, `LaborCorrection` na correção e no recálculo, `source_id` = id do apontamento, `seq` = `version` do evento) com `amount` = `newAmount` − Σ das linhas já gravadas para o apontamento; evento repetido (mesmo `seq`) não grava de novo (inbox e índice único); evento com `seq` menor ou igual ao maior já aplicado é ignorado, porque a linha mais nova já levou o apontamento ao valor certo. Teste: fechamento (120,00), correção para 3 h (144,00) e recálculo chegando fora de ordem e repetidos deixam Σ = valor final do apontamento.

**Projeto 360: realizado, comprometido e projetado (D2, ADR 0025 §1 e §4)**
9. **Dado** a aba Custos do Projeto 360 com `projects.view_cost`, **quando** abro, **então** cada natureza mostra **Orçado**, **Realizado**, **Comprometido** (coluna nova), **Projetado** e **Desvio**, com:
   - **Realizado** = títulos a pagar com o projeto **confirmados** (fora de `Planned` e de `Cancelled`) de serviço (instalador, frete, outros; natureza diferente de `Material` de compra de item estocável) + Σ `project_costs` da natureza (horas agora; baixa de material com a EST-03); títulos `Opening` com projeto entram como realizado pelo saldo da data de corte (sem mudança);
   - **Comprometido** = títulos a pagar `Planned` com o projeto (previsto do instalador, previsto do pedido) + títulos de compra de item estocável com o projeto, confirmados ou não, enquanto o material não foi baixado para a OS (as origens de compra chegam na CMP-03a e na CMP-04, ADR 0025 Consequências; o cálculo já recebe a regra com teste de unidade);
   - **Projetado** = realizado + max(orçado − realizado, comprometido);
   - **Desvio** = projetado − orçado; a margem projetada do Resumo usa o projetado.
   Os movimentos `Production` e `Shipment` nunca entram (ADR 0025 §1). Dataset fixo: 3 apontamentos de 2 cargos, 1 título previsto do instalador e 1 confirmado de frete → totais conferidos com a planilha.
10. **[Segurança]** **Dado** um usuário sem `projects.view_cost`, **quando** abre o projeto, **então** o DTO não traz orçado, realizado, comprometido, projetado nem margem (DTO separado, não campo nulo; ADR 0019 §20); teste de arquitetura.
11. **Dado** uma cópia dos dados do tenant `konebras` (ADR 0020), **quando** roda o teste de regressão (ADR 0025 §4), **então** para cada projeto da Fase 2: o realizado cai exatamente pelo valor dos títulos `Planned` do projeto, esse valor aparece no comprometido, e o projetado total não muda quando o projeto não tem título previsto; o resultado do teste fica anexado ao PR (correção D17 (a)).

**Recompor o passado (D3, ADR 0025 §3)**
12. **Dado** apontamentos fechados antes desta story (sem custo), **quando** o job de recomposição roda uma vez por tenant depois do deploy, **então** cada apontamento recebe o snapshot do custo-hora atual do cargo (o único disponível; valor do dia da recomposição), o custo e `cost_version` = 1 com a origem "Implantação", e o Commercial grava as linhas em `project_costs` com `is_backfill = true`; o job é idempotente por tenant (rodar duas vezes e com o consumidor ao vivo ao mesmo tempo grava uma vez só, teste); apontamento de cargo sem custo-hora segue o CA 5.
13. **Dado** um valor recomposto, **quando** aparece na aba Apontamentos da OS ou no custo do projeto, **então** mostra a marca "Calculado na implantação" (padrão de tela 11) com `<Dica>` "Custo calculado na implantação, com o custo-hora do cargo daquele dia."

**Telas, segurança e auditoria**
14. **Dado** a OS (abas Apontamentos e Custos), **quando** abro com `projects.view_cost`, **então** vejo horas e custo por etapa e por apontamento, e a aba Custos da OS nasce aqui com a linha Mão de obra (a EST-03 acrescenta Material); sem `projects.view_cost`, a aba Apontamentos mostra só horas e a aba Custos não aparece (o DTO não tem custo).
15. **[Segurança]** **Dado** o custo de mão de obra, **quando** é exibido ou trafega, **então** nunca mostra salário nem dado do funcionário além do nome; o evento leva só ids, valores e data; nenhum DTO do `/fab` (`shopfloor`) traz custo (teste por reflexão, ADR 0023 §5); um usuário com `hr.view` sem `projects.view_cost` não vê custo de apontamento.
16. **Dado** os módulos `production` ou `projects` não liberados, **quando** chamo as consultas de custo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `projects.view_cost`, 403 na API de custos; recalcular sem `production.approve`: 403 e o botão some. `RequireModule` e `perm:`.
17. **Dado** qualquer custo gravado, corrigido, recalculado ou recomposto, **quando** abro o histórico do apontamento, **então** vejo o snapshot, as horas, o valor, a versão, a origem ("Fechamento", "Correção", "Recálculo", "Implantação") e quem fez.

## Regras de negócio e técnicas
- **Snapshot no fechamento** (ponto 73): o fechamento é o momento em que o apontamento passa a existir com horas; o `04-fluxos` §3.2 ganha essa precisão.
- **Horas líquidas:** fim − início − Σ pausas de qualquer motivo (ponto 75).
- **Custo** = minutos ÷ 60 × custo-hora, arredondado só no total do apontamento (ADR 0019 §9).
- **Razão de diferenças** (ADR 0025 §2): a soma das linhas de um apontamento é sempre o custo atual dele; nada é apagado nem editado; `seq` = versão do custo do apontamento.
- **Cálculo do Projeto 360** numa função pura (`ProjectCosts.Compute`) que recebe títulos (com `Status` e `Origin`) e as linhas da razão; títulos continuam lidos por `IPayables.ByProjectAsync`, nunca copiados para a razão.
- **Recomposição** num job Hangfire com `tenantId`, idempotente por tenant (marca de execução) e pelo índice único da razão.
- Dinheiro `numeric(18,2)`, custo-hora `numeric(18,6)`; auditoria `IAuditable` no apontamento.

## Permissões, eventos e tabelas
- **Permissões:** `projects.view_cost` (ver custo), `production.approve` (recalcular sem custo), `hr.edit` (recebe o aviso). Nenhuma permissão nova.
- **Eventos:** `LaborEntryClosed` e `LaborEntryCorrected` (`Production.Contracts`, da PRD-03a) ganham `version`, `previousAmount`, `newAmount` e `occurredOn` (e `origin` no corrigido: `Correction`/`Recalculation`), com valores padrão para mensagens antigas no outbox; **consumidor novo no Commercial** grava `project_costs`. Nenhum evento novo.
- **Contratos:** `IHourlyCost`, `IEmployees` (cargo), `IPayables.ByProjectAsync` (sem mudança de assinatura).
- **Tabelas:** `production.labor_entries` (`hourly_cost_snapshot`, `cost`, `cost_missing`, `cost_version`, `cost_backfilled`); `commercial.project_costs` (nova).

## Telas e estados
- Padrões de tela da Fase 3: 4, 10, 11.
- **Projeto → Custos** (colunas Orçado, Realizado, Comprometido, Projetado, Desvio), **OS → Custos** (nova, linha Mão de obra) e **OS → Apontamentos** (coluna Custo com `projects.view_cost`).
- **Estados:** "Sem custo-hora" com chip alerta e ação "Abrir cargo"; vazio ("Nenhuma hora apontada ainda."); a legenda antiga "Material e horas entram com a produção (Fase 3)" sai.
- **Propositivo:** aviso acionável do CA 5; "Recalcular apontamentos sem custo" aparece no cargo e na OS quando há apontamento marcado.
- **400 px:** custos por etapa em cartões; no projeto, Comprometido e Desvio vão para a linha expansível.
- **Ajuda:** atualização de `Ajuda/projetos-projeto-custos.md` (as três colunas: o que é realizado, comprometido e projetado, e por que o previsto do instalador saiu do realizado), `Ajuda/producao-os-apontamentos.md`; nova `Ajuda/producao-os-custos.md` (aba `custos`); `<Dica>` em "Custo-hora" ("Custo do cargo por hora, com encargos; o salário de cada pessoa não aparece.") e em "Comprometido" ("Já contratado, mas ainda não gasto: título previsto ou material comprado que não foi baixado para a OS.").

## Plano de testes
- **Unidade:** cálculo do custo e arredondamento (CA 1, 2); correção e recálculo com versão (CA 4, 6); diferença da razão com eventos repetidos e fora de ordem (CA 8); `ProjectCosts.Compute` com títulos `Planned`, confirmados, `Opening` e de compra de item estocável (CA 9).
- **Integração (Testcontainers, obrigatória: lança custo):** snapshot e evento no fechamento (CA 1); pausas fora (CA 2); custo-hora alterado depois (CA 3); correção (CA 4); sem custo-hora, aviso e recálculo com diálogo (CA 5, 6); migration e posse da tabela (CA 7); consumidor idempotente e fora de ordem (CA 8); Custos do projeto com o dataset fixo (CA 9); DTO sem custo (CA 10, 14, 15); regressão sobre cópia do `konebras` (CA 11); recomposição duas vezes e com o consumidor ao vivo (CA 12, 13); três camadas (CA 16); histórico (CA 17).
- **Arquitetura:** DTOs de apontamento e de projeto sem propriedade de custo para quem não tem `projects.view_cost`; DTOs do `shopfloor` sem custo; só o Commercial grava `commercial.project_costs`.
- **E2E (Playwright):** apontar uma etapa no `/fab` e ver as horas e o custo no Projeto 360 (validação do PO no Marco 3.1).

## Fora do escopo
- Baixa de material na razão e recomposição das saídas com projeto: EST-03.
- Títulos de compra como comprometido: CMP-03a e CMP-04 (a regra já está no cálculo).
- Folha e custo real por funcionário: Fase 6/7. Hora extra com adicional: fora do MVP (custo-hora único por cargo). Custo de máquina/centro por hora: fora do MVP.
- Indicador de tempo parado: Fase 4.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 73–75):
1. Custo-hora com snapshot no fechamento do apontamento (CA 1, 3).
2. Recalcular apontamentos sem custo-hora permitido com `production.approve`, publicando a diferença (CA 6).
3. Nenhuma pausa entra no custo; o tempo parado vira indicador na Fase 4 (CA 2).

Também aplicadas: D2 (realizado, comprometido e projetado, CA 9 a 11), D3 (recomposição, CA 12 e 13) e D17 (a) (correção do realizado da COM-05, CA 11), pela ADR 0025.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos (desvios no Change Log).
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando, inclusive a regressão sobre cópia do tenant `konebras`. **Unidade e E2E passando; integração escrita e não executada nesta máquina (Docker em uso); 9 testes de arquitetura bloqueados pelo Smart App Control (rodam no CI); regressão com dados sintéticos que imitam o `konebras` (a cópia real fica para o PO/CI).**
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`. **Criada (`ProjectCostsLedger`, só cria coluna, tabela e índice); teste sobre a cópia pendente.**
- [x] ADR 0025 aplicada (§1 a §4).
- [x] Permissões só do `ModuleCatalog`.
- [x] Telas com estados e 400 px.
- [x] Ajudas atualizadas (Projeto 360 com as três colunas); teste de cobertura passando.
- [x] Alerta acionável (sem custo-hora) e diálogo de impacto do recálculo aplicados.
- [x] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados. **`openapi.json` editado à mão e cliente gerado com `pnpm gen:api`; regenerar com `BQ_UPDATE_OPENAPI=1` no Docker.**
- [x] `docs/03-modelo-dados.md` atualizado (`project_costs` como tabela, colunas novas de `labor_entries`); `docs/04-fluxos-estados.md` §3.2 (snapshot no fechamento) e §3.3 (três colunas); story marcada no backlog.

## Antes → depois do Projeto 360 (regressão da COM-05, CA 11)
Teste `ProjectCostsRegressionTests` (dados sintéticos que imitam os projetos da Fase 2 do `konebras`). Em todo projeto: o realizado cai exatamente pelos títulos `Planned`, esse valor aparece no **Comprometido** e o **projetado total não muda** (a troca só move o previsto de coluna; vale também quando o previsto passa do orçado, porque o comprometido entra no max).

| Projeto (sintético) | Antes: realizado | Depois: realizado | Depois: comprometido | Projetado (antes = depois) |
|---|---|---|---|---|
| Previsto do instalador R$ 16.500,00 + frete confirmado R$ 2.200,00, orçado R$ 116.500,00 | R$ 18.700,00 | R$ 2.200,00 | R$ 16.500,00 | R$ 116.500,00 (margem projetada 35,3%) |
| Previsto do instalador R$ 11.000,00 acima do orçado (R$ 9.000,00) | R$ 12.400,00 | R$ 1.400,00 | R$ 11.000,00 | igual |
| Instalação confirmada e paga, sem previsto | igual | igual | R$ 0,00 | igual |
| Título de implantação R$ 4.000,00 + previsto sem natureza R$ 300,00 | R$ 4.300,00 | R$ 4.000,00 | R$ 300,00 | igual |

Depois do deploy, a recomposição (D3) acrescenta ao **Realizado** de Mão de obra as horas já apontadas na homologação, com a marca "Calculado na implantação"; o projetado de Mão de obra só muda se as horas passarem do orçado.

## Tarefas técnicas sugeridas
- [ ] `LaborCostCalculator` puro, chamado no fechamento, na correção, no recálculo e na recomposição.
- [ ] Campos de valor e versão no `LaborEntryClosed`/`LaborEntryCorrected`; consumidor no Commercial (`project_costs`).
- [ ] Migration da `commercial.project_costs`; troca do `ProjectCosts.Compute` e do DTO de custos; coluna "Comprometido" no front.
- [ ] Job de recomposição por tenant; teste de regressão sobre a cópia do `konebras`.
- [ ] Colunas de custo nas abas da OS; aba Custos da OS; aviso e recálculo.

## Quality gates
- Agentes previstos: @dev, @architect (razão de diferenças e regressão da COM-05), @qa.
- Gate: CA 1, 4, 8, 9, 11, 12 e 15 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK (ajustada: inclui o comprometido) |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (renumerados) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: sem EST-03, Marco 3.1) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** sem evento novo: o `LaborEntryClosed` do `04-fluxos` passa a levar o custo (a primeira versão criava um `LaborCostRecorded`); custo pelo custo-hora do cargo, nunca pelo salário (LGPD); correção por estorno e novo valor; o momento do snapshot ficou como ponto em aberto (1) com proposta.
- **Correções da revalidação (21/10/2026):** re-estimada de P para M; deixa de depender da EST-03 e passa ao Marco 3.1; cria a `commercial.project_costs` como razão de diferenças com `seq` (ADR 0025 §2; a correção grava a diferença, não o par estorno + novo); troca o cálculo do Projeto 360 com a coluna "Comprometido" e teste de regressão sobre o `konebras` (D2, D17 (a)); recomposição dos apontamentos já fechados com a marca "Calculado na implantação" (D3, padrão 11); pontos 73–75 decididos; nenhum DTO do `/fab` com custo; aba Custos da OS nasce aqui. CAs renumerados (17).
- **Pré-condições para `Ready`:** nenhuma de decisão de story. ADR 0025 aceita.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D2, D3, D17 (a), tabela 1.1 itens 73–75) e correções da revisão pré-Fase 3 (§2.1 custo real do projeto, ADR 0025; §2.3 padrões 4, 10, 11); re-estimada para M; nova ordem (Marco 3.1) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 22/10/2026 | 1.1 | Decisões do PO (22/10/2026): (a) parâmetro novo da Produção "Exibir custo-hora em cada apontamento" (padrão desligado, `settings.edit`, auditado): desligado, o servidor manda só o total por etapa e por OS (`OrderLaborCostView.Entries` vazio, `ShowsEntryCost` false, histórico do custo responde 403 `entry_cost_hidden`); ligado, quem tem `projects.view_cost` vê o custo-hora e o custo por apontamento; o total de Mão de obra do Projeto 360 continua com `projects.view_cost`; (b) o limite do apontamento aberto (PRD-03a CA 22) sai da configuração do servidor `Production:OpenLaborAlertHours` e vira parâmetro da Produção em Configurações → Parâmetros (padrão 12, de 1 a 72, `settings.edit`, auditado); tabela `production.parameters`, migration `ProductionParameters`; (c) confirmado: o dono do apontamento é o funcionário. | Dex (dev) |
| 22/10/2026 | 1.0 | Implementada (`Done`). Desvios: (1) tabela nova `production.labor_cost_history` (uma linha por versão do custo) para o histórico do CA 17, e coluna `labor_entries.cost_position_id` para o aviso e o recálculo por cargo; (2) os valores de custo do apontamento ficam fora do diff da auditoria (`[ProtectedSecret]`, só "mudou", como o orçado do projeto), porque o histórico da OS é de `production.view` (CA 15); (3) `LaborEntryCorrected` também leva `ProductionOrderId`, `ProjectId` e `Minutes`, e os dois eventos levam `Origin` (`Closing`/`Correction`/`Recalculation`/`Backfill`), para o consumidor gravar sem ler a Produção e marcar `is_backfill`; (4) `source` pela versão: `Labor` na versão 1 (fechamento e implantação) e `LaborCorrection` nas seguintes, para o índice único barrar duas linhas da mesma versão; (5) a marca de execução da recomposição é a própria versão do custo (versão 0 = pendente): o job é recorrente (03:30) e disparado na subida do processo, e termina sem fazer nada depois da primeira vez; corrida com uma correção cai no xmin e o Hangfire repete; (6) correção de apontamento fechado antes da PRD-04 (versão 0) usa o custo-hora atual e ganha "Calculado na implantação"; (7) `occurred_on`: data do fechamento, da correção ou do recálculo em São Paulo; na recomposição, a data do fim do apontamento; (8) no diálogo do recálculo, o total em R$ só aparece com `projects.view_cost` (sem ela, só a quantidade); (9) título de compra de item estocável = origem `Purchase` com natureza `Material` (a origem `GoodsReceipt` chega na CMP-04); (10) o aviso "Sem custo-hora" usa `NotifyOnce` com `labor-no-cost:{positionId}`: depois do primeiro aviso de um cargo, um novo apontamento sem custo do mesmo cargo não avisa de novo; (11) regressão do CA 11 com dados sintéticos; a cópia real do `konebras` e o anexo no PR ficam para o PO; (12) OpenAPI editado à mão (regenerar). | Dex (dev) |
