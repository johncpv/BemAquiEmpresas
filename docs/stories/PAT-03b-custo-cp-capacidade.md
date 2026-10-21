# PAT-03b — Patrimônio: custo da manutenção, CP do prestador e redução da capacidade

| Campo | Valor |
|---|---|
| ID | PAT-03b |
| Título | Valor combinado com o prestador gera o título a pagar previsto (origem `Maintenance`) e é confirmado na conclusão, para ordem de pagamento; custo da manutenção por bem e por centro de trabalho (mão de obra interna, prestador, peças informadas); manutenção programada e máquina parada reduzem a capacidade do centro de trabalho no planejamento (`MaintenanceScheduled`, paradas) |
| Tamanho | M (quebra da PAT-03) |
| Status | Draft |
| Requisitos | R-PAT-4 (parte: custo, CP do prestador) · R-PAT-5 (D: manutenção programada reduz a capacidade) · R-FIN-4 · R-FIN-10 · R-PRD-9 (D, paradas) · R-PRP-7 · R-SEC-5 |
| Dependências | PAT-03a (OS de manutenção), FIN-02 (`IPayables`, origem `Maintenance`), FIN-03a (OP), FIN-05 (estimativa no fluxo de caixa), CAD-03 (capacidade do centro); Fase 4 PLN-01 (motor de agendamento que consome a redução) |
| Bloqueia | — |
| Ordem de execução | 14 (Marco 6.4 "Patrimônio"; a parte de capacidade só tem efeito visível com a PLN-01) |

## User story
Como **proprietário**, quero **ver quanto cada máquina custa em manutenção e que o pagamento ao prestador passe por ordem de pagamento**, e como **planejador**, quero **que a máquina em manutenção reduza a capacidade da fábrica**, para **o custo e o prazo refletirem a realidade**.

## Contexto e referências
- PRD R-PAT-4 ("histórico e custo"), R-PAT-5 (D), R-PRD-9 (D: "Paradas de máquina/centro (manutenção, falta de material) registradas e refletidas na capacidade"); backlog PAT-03 ("custo, CP do prestador; integração com capacidade (PAT-5)").
- `04-fluxos` §2: `MaintenanceScheduled` (Assets) → "Planning reduz capacidade do centro de trabalho no período"; §3.4 (planejamento recalcula na mudança de calendário/manutenção); §1 (nenhum pagamento sem OP aprovada).
- `03-modelo-dados` → `assets.maintenance_orders.cost`, `payable_id`, `supplier_party_id`; `production.downtimes` (`work_center_id`, `asset_id`, `reason`, `started_at`, `ended_at`).
- `SharedKernel.DocumentOrigin.Maintenance` (FIN-02); COM-06 como padrão de CP previsto com data estimada e confirmação (ADR 0019 §17).
- ADR 0016 (`IWorkCenterCapacity` por id; o planejamento lê o calendário por chamada síncrona).

## Critérios de aceite
**CP do prestador**
1. **Dado** uma OS de manutenção com prestador (terceiro com papel Fornecedor) e `assets.edit`, **quando** informo o valor combinado (`numeric(18,2)`, > 0) e salvo, **então** na mesma transação o Assets chama `IPayables.CreateFromOriginAsync(Maintenance, osId, prestador, sem projeto, valor, data estimada = data agendada + a primeira parcela da condição padrão do prestador, estimada, "Manutenção {MAN} — {bem}", planned: true)` e o título nasce `Planned` com a `Tag` "Estimado" no fluxo de caixa (FIN-05), conta e centro de custo pela regra do ponto em aberto 1.
2. **Dado** a mudança do valor combinado com a OS aberta, **quando** salvo, **então** o título `Planned` é atualizado (`UpdatePlannedAsync`) e quem tem `finance.edit` é notificado ("Valor da manutenção {MAN} mudou de {antes} para {depois} por {usuário}.").
3. **Dado** a conclusão da OS (PAT-03a CA 6) com prestador, **quando** confirmo no diálogo (`size="lg"`) "Confirma o título a pagar de {valor} ao prestador {nome}, com vencimento em {data}, que segue para ordem de pagamento.", **então** na mesma transação o `IPayables.ConfirmAsync` grava o valor final e o vencimento real e o título passa a `Open`; sem confirmação, a OS não conclui (R-PRP-7).
4. **Dado** o cancelamento da OS, **quando** o título ainda está `Planned`, **então** é cancelado pela origem; já `Open`, fica para o financeiro decidir, com a marca "Origem cancelada" (ADR 0019 §4).

**Custo**
5. **Dado** a OS concluída, **quando** informo o custo (prestador vem do título; mão de obra interna em horas × custo-hora do cargo do executor pelo `IHourlyCost`, proposta; peças e outros como valor digitado com descrição), **então** a OS grava o custo total por natureza e a ficha do bem mostra "Custo de manutenção: {mês} · {ano} · total".
6. **Dado** `assets.view` + `projects.view_cost` (proposta: custo é informação gerencial, Pontos em aberto 2), **quando** abro Patrimônio → Custo de manutenção, **então** vejo o custo por bem, por centro de trabalho e por mês, com o total de preventiva × corretiva e drill-down até a OS; sem a permissão de custo, os valores não existem no JSON.

**Capacidade**
7. **Dado** uma OS de manutenção agendada de um bem com centro de trabalho, **quando** é aberta ou reagendada, **então** o Assets publica `MaintenanceScheduled` (`{ orderId, workCenterId, from, to, hoursPerDay }`, proposta: a duração prevista informada na OS) e o consumidor do Production grava a parada (`production.downtimes`, motivo Manutenção, origem a OS), que reduz o `IWorkCenterCapacity` do período; cancelar ou concluir antes publica `MaintenanceUnscheduled`/`MaintenanceCompleted` e a parada é encerrada ou removida (proposta: removida se não começou, encerrada no fim real se começou).
8. **Dado** a OS corretiva com "máquina parada" (PAT-03a CA 4), **quando** é aberta, **então** a parada começa na hora da abertura e termina na conclusão; a capacidade do centro no período cai pela proporção do bem no centro (Pontos em aberto 3).
9. **Dado** a capacidade reduzida, **quando** a PLN-01 (Fase 4) recalcula, **então** usa o `IWorkCenterCapacity` já com a parada, sem código novo no planejamento; antes da PLN-01, a capacidade do centro na tela de centros de trabalho (CAD-03) mostra a redução do período.
10. **Dado** o consumidor do Production, **quando** o evento é reentregue ou chega fora de ordem, **então** a parada não duplica (inbox e `(source, source_id)` único) e o reagendamento substitui o período anterior.

**Permissões e auditoria**
11. **Dado** os módulos `assets`, `finance` ou `production` não liberados, **quando** chamo, **então** 403 `module_not_released`; valor combinado e custo com `assets.edit`; relatório de custo pela regra do CA 6. `RequireModule` e `perm:`; prestador validado pelo `IParties` (papel Fornecedor).
12. **[Segurança]** **Dado** dois usuários concluindo com confirmação do CP ao mesmo tempo, **quando** chegam, **então** só um grava e o título é confirmado uma vez (`xmin` e idempotência do `IPayables` por origem).
13. **Dado** valor, custo, título e paradas, **quando** abro o histórico da OS, **então** vejo antes/depois, usuário e data, e o link para o título (com `finance.view`).

## Regras de negócio e técnicas
- **Título** por `IPayables` (padrão da COM-06): `Planned` estimado → `Open` na conclusão; o pagamento só por OP aprovada.
- **Custo** em `assets.maintenance_order_costs` (proposta: `order_id`, `cost_type` Labor/Service/Parts/Other, `description`, `amount` `numeric(18,2)`, `hours` `numeric(18,4)`, `hourly_cost_snapshot` `numeric(18,6)`); o `cost` da OS é a soma.
- **Paradas:** dono Production (`production.downtimes` + `source_type`, `source_id`); o Assets não escreve tabela do Production (ADR 0006).
- Nenhum custo de manutenção vai para projeto (é custo indireto do centro, Pontos em aberto 1).

## Permissões, eventos e tabelas
- **Permissões:** `assets.edit`; relatório de custo pela decisão do ponto em aberto 2.
- **Eventos:** `MaintenanceScheduled` (`04-fluxos`), `MaintenanceUnscheduled` e `MaintenanceCompleted` (propostos), sem texto livre; consumidor no Production.
- **Contratos:** `IPayables`, `IHourlyCost`, `IWorkCenterCapacity`.
- **Tabelas:** `assets.maintenance_order_costs` (nova), `assets.maintenance_orders` (+ `contract_value`, `expected_hours`), `production.downtimes` (+ origem).

## Telas e estados
- Na OS (PAT-03a): campo "Valor combinado" com a `Tag` "Estimado" no título; aba **Custo**; diálogo de conclusão com o impacto financeiro.
- **Patrimônio → Custo de manutenção** (`/app/patrimonio/custo`), com o `Chart` (tokens da FND-09e) e a tabela equivalente acessível.
- **Estados:** carregando; vazio ("Nenhum custo de manutenção lançado."); erro; "Dados protegidos" sem a permissão de custo.
- **Propositivo:** prestador sugerido pelo executor padrão do plano; valor sugerido pelo da última OS do mesmo plano.
- **400 px:** custos em cartões; gráfico com a tabela abaixo.
- **Ajuda:** `Ajuda/patrimonio-custo.md`, atualizar `Ajuda/patrimonio-manutencao-os.md` (aba `custo`) e `Ajuda/producao-centros.md` ("Paradas por manutenção"); `<Dica>` em "Valor combinado".

## Plano de testes
- **Unidade:** data estimada (CA 1); custo por natureza (CA 5); período da parada (CA 7, 8).
- **Integração (Testcontainers, obrigatória: move financeiro):** título previsto, atualização, confirmação e cancelamento (CA 1–4); custo e relatório por permissão (CA 5, 6); evento, parada, capacidade reduzida, reentrega e reagendamento (CA 7–10); três camadas (CA 11); concorrência (CA 12); histórico (CA 13).
- **E2E (Playwright):** OS com prestador, concluir com confirmação e ver o título na lista de títulos a pagar.

## Fora do escopo
- Depreciação: Fase 9 (R-PAT-6).
- Rateio do custo de manutenção no custo-hora ou no projeto: pós-MVP.

## Pontos em aberto
1. **Conta e centro de custo** do título e do custo da manutenção (contadora): proposta conta 2.6 "Outros custos" para máquina de centro de trabalho e 3.3 "Administrativas" para os demais, centro de custo pelo centro do bem.
2. **Quem vê o custo da manutenção** (PO): `projects.view_cost` (proposta, para não criar ação nova) ou uma ação `assets.view_cost` (ADR 0003).
3. **Redução da capacidade** (PO): a máquina parada zera o centro inteiro ou só a parte dela (centro com várias máquinas); proposta: a duração e as horas por dia informadas na OS.
4. Paradas que não são manutenção (falta de material) no mesmo registro (R-PRD-9): ficam para a Produção.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, cadeia até o título e até a capacidade) e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados; ajuda atualizada; teste de cobertura passando.
- [ ] Confirmação com impacto financeiro aplicada.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (eventos) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Integração com `IPayables`; `MaintenanceOrderCost`.
- [ ] Eventos em `Assets.Contracts` e consumidor no Production (paradas e `IWorkCenterCapacity`).
- [ ] Relatório de custo; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (eventos e capacidade), @qa.
- Gate: CA 1, 3, 7 e 10 obrigatórios.

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

- **Correções feitas na validação:** CP no padrão da COM-06 (previsto estimado, confirmado na conclusão com diálogo de impacto); a parada é gravada pelo Production a partir do evento, sem o Assets escrever tabela alheia (CA 7); a dependência da PLN-01 (Fase 4) ficou explícita (CA 9).
- **Pré-condições para `Ready`:** pontos em aberto 1, 2 e 3; PAT-03a concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PAT-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
