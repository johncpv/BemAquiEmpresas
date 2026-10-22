# PAT-03b — Patrimônio: custo da manutenção, CP do prestador e redução da capacidade

| Campo | Valor |
|---|---|
| ID | PAT-03b |
| Título | Valor combinado com o prestador gera o título a pagar previsto (origem `Maintenance`) e é confirmado na conclusão, para ordem de pagamento, com conta e centro de custo pré-configurados "A confirmar com a contadora"; custo da manutenção por bem e por centro de trabalho (mão de obra interna, prestador, peças informadas), visível com `assets.edit`; manutenção programada e máquina parada viram parada no `IWorkCenterCapacity`, pelas horas por dia informadas na OS, e o planejamento recalcula |
| Tamanho | M (quebra da PAT-03) |
| Status | Ready (pré-condição externa) |
| Requisitos | R-PAT-4 (parte: custo, CP do prestador) · R-PAT-5 (D: manutenção programada reduz a capacidade) · R-FIN-4 · R-FIN-10 · R-PRD-9 (D, paradas por manutenção) · R-PRP-7 · R-SEC-5 |
| Dependências | PAT-03a (OS de manutenção), FIN-02 (`IPayables`, origem `Maintenance`), FIN-03a (OP), FIN-05 (estimativa no fluxo de caixa), CAD-03 (capacidade do centro, `IWorkCenterCapacity`), CAD-04a (`IHourlyCost`), PLN-01a (plano finito que consome a capacidade), PLN-01b (pedido de recálculo agrupado), PLN-04 (ocupação da fábrica) |
| Bloqueia | — |
| Ordem de execução | 15 (Marco 6.4 "Patrimônio") |

## User story
Como **proprietário**, quero **ver quanto cada máquina custa em manutenção e que o pagamento ao prestador passe por ordem de pagamento**, e como **planejador**, quero **que a máquina em manutenção reduza a capacidade da fábrica**, para **o custo e o prazo refletirem a realidade**.

## Contexto e referências
- PRD R-PAT-4 ("histórico e custo"), R-PAT-5 (D), R-PRD-9 (D: "Paradas de máquina/centro (manutenção, falta de material) registradas e refletidas na capacidade"); backlog PAT-03b ("custo, CP do prestador; integração com capacidade (PAT-5)").
- `04-fluxos` §2: `MaintenanceScheduled` (Assets) → Production grava a parada que reduz a capacidade do centro no período; §3.4 (o planejamento recalcula na mudança de calendário e de manutenção); §1 (nenhum pagamento sem OP aprovada).
- `03-modelo-dados` → `assets.maintenance_orders.cost`, `payable_id`, `supplier_party_id`; `production.downtimes` (`work_center_id`, `asset_id`, `reason`, `started_at`, `ended_at`).
- `SharedKernel.DocumentOrigin.Maintenance` (FIN-02); COM-06 como padrão de CP previsto com data estimada e confirmação (ADR 0019 §17).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D10 (capacidade por centro; manutenção vira parada no `IWorkCenterCapacity`), tabela 3.3 pontos 55 a 58, §4.2 (PAT-03b: custo da manutenção com `assets.edit`, não `projects.view_cost`), §4.4 padrões 1 e 2; novo requisito "tudo configurável em Configurações" (ADR 0042).
- **ADR 0038** (`docs/adr/0038-patrimonio-manutencao-capacidade.md`: dono da OS de manutenção, paradas na capacidade); **ADR 0029** (`docs/adr/0029-planejamento-finito.md`: escritor único do plano, recálculo); ADR 0016 (`IWorkCenterCapacity` por id; o planejamento lê o calendário por chamada síncrona); ADR 0024 §1 (chamada síncrona ao `IPayables`).

## Critérios de aceite
**CP do prestador**
1. **Dado** uma OS de manutenção com prestador (terceiro com papel Fornecedor) e `assets.edit`, **quando** informo o valor combinado (`numeric(18,2)`, > 0) e salvo, **então** na mesma transação o Assets chama `IPayables.CreateFromOriginAsync(Maintenance, osId, prestador, sem projeto, valor, data estimada = data agendada + a primeira parcela da condição padrão do prestador, estimada, "Manutenção {MAN} — {bem}", planned: true)` e o título nasce `Planned` com a `Tag` "Estimado" no fluxo de caixa (FIN-05), com a conta e o centro de custo do CA 5.
2. **Dado** a mudança do valor combinado com a OS aberta, **quando** salvo, **então** o título `Planned` é atualizado (`UpdatePlannedAsync`) e quem tem `finance.edit` é notificado ("Valor da manutenção {MAN} mudou de {antes} para {depois} por {usuário}.").
3. **Dado** a conclusão da OS (PAT-03a CA 6) com prestador, **quando** confirmo no diálogo (`size="lg"`) "Confirma o título a pagar de {valor} ao prestador {nome}, com vencimento em {data}, que segue para ordem de pagamento.", **então** na mesma transação o `IPayables.ConfirmAsync` grava o valor final e o vencimento real e o título passa a `Open`; sem confirmação, a OS não conclui (R-PRP-7).
4. **Dado** o cancelamento da OS, **quando** o título ainda está `Planned`, **então** é cancelado pela origem; já `Open`, fica para o financeiro decidir, com a marca "Origem cancelada" (ADR 0019 §4).
5. **Dado** os parâmetros do Patrimônio (Configurações → Parâmetros → Patrimônio, `settings.edit`), **quando** o título e o custo são gravados, **então** a conta vem pré-configurada como 2.6 "Outros custos" para bem com centro de trabalho e 3.3 "Administrativas" para os demais, e o centro de custo pelo centro do bem (padrão "Produção" e "Administrativo"), cada valor com o chip "A confirmar com a contadora" e "Marcar como conferido" (ADR 0042); nenhum custo de manutenção vai para projeto.

**Custo**
6. **Dado** a OS concluída, **quando** informo o custo (prestador vem do título; mão de obra interna em horas × custo-hora do cargo do executor pelo `IHourlyCost`; peças e outros como valor digitado com descrição), **então** a OS grava o custo total por natureza e a ficha do bem mostra "Custo de manutenção: {mês} · {ano} · total" para quem tem `assets.edit`.
7. **[Segurança]** **Dado** `assets.edit`, **quando** abro Patrimônio → Custo de manutenção, **então** vejo o custo por bem, por centro de trabalho e por mês, com o total de preventiva × corretiva e drill-down até a OS (o total do drill-down é igual ao da célula); sem `assets.edit` (só `assets.view`), os valores de custo e o custo-hora não existem no JSON e a tela mostra "Dados protegidos"; nenhum custo-hora com nome de funcionário sai sem `hr.view_sensitive` (só o total da mão de obra interna).

**Capacidade**
8. **Dado** uma OS de manutenção agendada de um bem com centro de trabalho, **quando** é aberta ou reagendada, **então** a OS pede as **horas paradas por dia** (preenchidas com as horas do centro por dia ÷ o número de máquinas ativas do centro, editáveis, > 0 e ≤ as horas do centro) e a duração prevista, e o Assets publica `MaintenanceScheduled` (`{ orderId, workCenterId, from, to, hoursPerDay }`); o consumidor do Production grava a parada (`production.downtimes`, motivo Manutenção, origem a OS) e o `IWorkCenterCapacity.GetAvailableHoursAsync` passa a descontar essas horas no período; cancelar a OS publica `MaintenanceUnscheduled` (parada removida se não começou, encerrada no fim real se começou) e concluir publica `MaintenanceCompleted` (parada encerrada na conclusão).
9. **Dado** a OS corretiva com "máquina parada" (PAT-03a CA 4), **quando** é aberta, **então** a parada começa na hora da abertura e termina na conclusão, com as horas por dia do CA 8.
10. **Dado** a capacidade reduzida, **quando** a parada é gravada, mudada ou encerrada, **então** o Production faz o pedido de recálculo agrupado do plano (PLN-01b: 1 rodando, 1 pendente), o plano finito (PLN-01a) usa o `IWorkCenterCapacity` já com a parada, sem código novo no motor, e a ocupação da fábrica (PLN-04) mostra a capacidade menor no período; a tela de centros de trabalho (CAD-03) mostra "Paradas por manutenção" no período.
11. **Dado** o consumidor do Production, **quando** o evento é reentregue ou chega fora de ordem, **então** a parada não duplica (inbox e `(source_type, source_id)` único) e o reagendamento substitui o período anterior; teste com dois tenants.

**Permissões e auditoria**
12. **Dado** os módulos `assets`, `finance` ou `production` não liberados, **quando** chamo, **então** 403 `module_not_released`; valor combinado, custo e relatório de custo com `assets.edit`; parâmetros com `settings.edit`; `RequireModule` e `perm:`; prestador validado pelo `IParties` (papel Fornecedor).
13. **[Segurança]** **Dado** dois usuários concluindo com confirmação do CP ao mesmo tempo, **quando** chegam, **então** só um grava e o título é confirmado uma vez (`xmin` e idempotência do `IPayables` por origem).
14. **Dado** valor, custo, título e paradas, **quando** abro o histórico da OS, **então** vejo antes/depois, usuário e data, e o link para o título (com `finance.view`).

## Regras de negócio e técnicas
- **Título** por `IPayables` (padrão da COM-06, chamada síncrona): `Planned` estimado → `Open` na conclusão; o pagamento só por OP aprovada.
- **Custo** em `assets.maintenance_order_costs` (`order_id`, `cost_type` Labor/Service/Parts/Other, `description`, `amount` `numeric(18,2)`, `hours` `numeric(18,4)`, `hourly_cost_snapshot` `numeric(18,6)`); o `cost` da OS é a soma.
- **Paradas (D10, ADR 0038):** dono Production (`production.downtimes` + `source_type`, `source_id`, `hours_per_day`); o Assets não escreve tabela do Production (ADR 0006); a capacidade é por centro, e a parada desconta só as horas por dia informadas.
- **Parâmetros do Patrimônio** (`assets.parameters`, PAT-01b): `assets.maintenance_account_machine` (2.6), `assets.maintenance_account_other` (3.3), `assets.maintenance_cost_center_machine` (Produção), `assets.maintenance_cost_center_other` (Administrativo).

## Permissões, eventos e tabelas
- **Permissões:** `assets.edit` (valor, custo e relatório de custo; não `projects.view_cost`), `settings.edit` (parâmetros).
- **Eventos:** `MaintenanceScheduled` (`04-fluxos`), `MaintenanceUnscheduled` e `MaintenanceCompleted`, em `Assets.Contracts`, sem texto livre; consumidor no Production.
- **Contratos:** `IPayables`, `IHourlyCost`, `IWorkCenterCapacity` (passa a descontar as paradas), pedido de recálculo do plano (PLN-01b).
- **Tabelas:** `assets.maintenance_order_costs` (nova), `assets.maintenance_orders` (+ `contract_value`, `expected_hours`, `hours_per_day`), `assets.parameters` (+ contas e centros), `production.downtimes` (+ origem e horas por dia).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 1 (gráfico de custo sobre o `ChartFrame`, série nunca só por cor, resumo textual acima e tabela equivalente), 2 (totais clicáveis até a OS, "Como calculamos", "Atualizado às").
- Na OS (PAT-03a): campo "Valor combinado" com a `Tag` "Estimado" no título; "Horas paradas por dia"; aba **Custo**; diálogo de conclusão com o impacto financeiro.
- **Patrimônio → Custo de manutenção** (`/app/patrimonio/custo`), com o `ColumnChart` e a tabela equivalente acessível.
- **Configurações → Parâmetros → Patrimônio:** contas e centros da manutenção, com os chips.
- **Estados:** carregando; vazio ("Nenhum custo de manutenção lançado."); erro; "Dados protegidos" sem `assets.edit`.
- **Propositivo:** prestador sugerido pelo executor padrão do plano; valor sugerido pelo da última OS do mesmo plano; horas paradas por dia sugeridas pelo centro.
- **400 px:** custos em cartões; gráfico com a tabela abaixo; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** `Ajuda/patrimonio-custo.md`, atualizar `Ajuda/patrimonio-manutencao-os.md` (aba `custo` e "Horas paradas por dia"), `Ajuda/producao-centros.md` ("Paradas por manutenção") e a seção Patrimônio de `Ajuda/configuracoes-parametros.md`; `<Dica>` em "Valor combinado" e "Horas paradas por dia".

## Plano de testes
- **Unidade:** data estimada (CA 1); conta e centro pelo bem (CA 5); custo por natureza (CA 6); período e horas da parada (CA 8, 9).
- **Integração (Testcontainers, obrigatória: move financeiro):** título previsto, atualização, confirmação e cancelamento (CA 1–4); parâmetros com o chip (CA 5); custo e relatório por permissão, sem valor no JSON com `assets.view` (CA 6, 7); evento, parada, capacidade reduzida, pedido de recálculo e ocupação (CA 8–10); reentrega, reagendamento e dois tenants (CA 11); três camadas (CA 12); concorrência (CA 13); histórico (CA 14).
- **E2E (Playwright):** OS com prestador e máquina parada, ver a capacidade menor na ocupação, concluir com confirmação e ver o título na lista de títulos a pagar.

## Fora do escopo
- Depreciação: Fase 9 (R-PAT-6).
- Rateio do custo de manutenção no custo-hora ou no projeto: pós-MVP.
- Paradas que não são manutenção (falta de material): ficam para a Produção, depois.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, D10 e tabela 3.3, pontos 55–58):
1. **Contas da manutenção:** 2.6 "Outros custos" para bem com centro de trabalho e 3.3 "Administrativas" para os demais, centro de custo pelo centro do bem, como parâmetros "A confirmar com a contadora" (CA 5).
2. **Custo de manutenção visível com `assets.edit`** (não `projects.view_cost`) (CA 6, 7).
3. **Horas por dia informadas na OS,** como parada no `IWorkCenterCapacity` (D10, ADR 0038) (CA 8–10).
4. **Outras paradas** ficam na Produção, depois.

Decisões da revisão aplicadas: §4.2 (custo com `assets.edit`, CA 7); D10 (parada no `IWorkCenterCapacity` e recálculo, CA 10; com a Fase 4 antes, o efeito no plano e na ocupação já é visível); §4.4 padrões 1 e 2.

**Pré-condição externa:** a contadora confere as contas e os centros da manutenção (pré-configurados em Configurações → Parâmetros → Patrimônio com o chip "A confirmar com a contadora"); a Konebras confere as horas paradas por dia sugeridas; não bloqueia o `Ready`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, cadeia até o título e até a capacidade) e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados; ajuda atualizada; teste de cobertura passando.
- [ ] Confirmação com impacto financeiro aplicada.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (eventos e paradas) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Integração com `IPayables`; `MaintenanceOrderCost`; parâmetros do Patrimônio.
- [ ] Eventos em `Assets.Contracts`; consumidor no Production (paradas, `IWorkCenterCapacity`, pedido de recálculo).
- [ ] Relatório de custo; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (eventos, capacidade e recálculo), @qa.
- Gate: CA 1, 3, 7, 8, 10 e 11 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: custo com `assets.edit`, CA 7) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: PLN-01a, PLN-01b e PLN-04 já entregues na Fase 4) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** CP no padrão da COM-06 (previsto estimado, confirmado na conclusão com diálogo de impacto); a parada é gravada pelo Production a partir do evento, sem o Assets escrever tabela alheia; a dependência do planejamento ficou explícita.
- **Correções da revalidação (21/10/2026):** pontos 55 a 58 decididos: contas pré-configuradas "a confirmar" (CA 5, novo), custo com `assets.edit` (§4.2, CA 7), horas paradas por dia na OS como parada no `IWorkCenterCapacity` (D10, CA 8, 9) e recálculo pelo PLN-01b com a ocupação da PLN-04 (CA 10); PLN-01 → PLN-01a/01b; família de gráficos e `Kpi` (padrões 1 e 2); ordem 15 do Marco 6.4; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência das contas pela contadora e das horas paradas pela Konebras.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PAT-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D10, tabela 3.3 pontos 55–58, ADR 0038, ADR 0029, ADR 0042, §4.2, §4.4 padrões 1 e 2); nova ordem (Marco 6.4, ordem 15); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
