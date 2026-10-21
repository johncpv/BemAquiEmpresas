# PAT-03a — Patrimônio: planos de manutenção preventiva e OS de manutenção

| Campo | Valor |
|---|---|
| ID | PAT-03a |
| Título | Plano de manutenção preventiva por bem (periodicidade em dias ou em horas de uso, checklist), job diário que abre as OS devidas e avisa antes; OS de manutenção preventiva e corretiva (`MAN-2026-0001`) com agendamento, execução interna ou por prestador, checklist, conclusão, bem em "Em manutenção" enquanto parado; histórico de manutenção por bem |
| Tamanho | M (quebra da PAT-03 em 03a planos e OS, 03b custo, CP do prestador e capacidade) |
| Status | Draft |
| Requisitos | R-PAT-4 (plano preventivo por tempo ou horas, OS de manutenção, histórico) · R-COM-2 · R-COM-3 · R-COM-4 (numeração MAN) · R-PRP-2 · R-PRP-5 · R-SEC-5 |
| Dependências | PAT-01a (bens), PAT-01b (atalho pela leitura), FND-07 (jobs, notificações, anexos), FND-09e |
| Bloqueia | PAT-03b |
| Ordem de execução | 13 (Marco 6.4 "Patrimônio") |

## User story
Como **supervisor de produção**, quero **cadastrar a manutenção preventiva de cada máquina e receber a OS quando ela vencer, e abrir uma corretiva quando algo quebrar**, para **a máquina não parar sem aviso e eu ter o histórico do que foi feito**.

## Contexto e referências
- PRD R-PAT-4 ("Plano de manutenção preventiva por bem (periodicidade por tempo ou horas), OS de manutenção, histórico e custo"); R-PAT-5 (capacidade, PAT-03b).
- `03-modelo-dados` → `assets.maintenance_plans` (`asset_id`, `description`, `frequency_type` Days/Hours, `frequency_value`, `last_done_at`, `next_due_at`, `checklist`), `assets.maintenance_orders` (`number`, `asset_id`, `plan_id`, `type` Preventive/Corrective, `status`, `scheduled_at`, `done_at`, `cost`, `supplier_party_id`, `notes`, `payable_id`).
- `04-fluxos` §4: `MAN-AAAA-0001`; §2: `MaintenanceScheduled` (Assets) → Planning reduz a capacidade (PAT-03b). A máquina de estados da OS de manutenção não existe no `04-fluxos`: proposta abaixo.
- `production.production_orders.type` prevê `Maintenance`, mas a OS de manutenção pertence ao Assets (`assets.maintenance_orders`), como no `03` (Pontos em aberto 1).
- ADR 0019 §2, §3, §7, §12 (concorrência, idempotência, anexos, jobs com `dedup_key`).

## Critérios de aceite
**Plano preventivo**
1. **Dado** `assets.edit` e um bem `Active`, **quando** crio o plano com descrição, periodicidade (a cada N dias, ou a cada N horas de uso), antecedência do aviso (proposta: 7 dias), executor padrão (interno ou prestador, terceiro com papel Fornecedor), checklist (itens de texto) e a data ou o horímetro da última execução, e clico "Salvar plano", **então** o plano calcula a próxima data (`next_due_at` = última + N dias) ou o próximo horímetro, e a ficha do bem mostra "Próxima preventiva: {data}".
2. **Dado** um plano por horas de uso, **quando** registro a leitura do horímetro no bem ("Registrar horímetro": valor e data, maior que a anterior), **então** a previsão do próximo vencimento é estimada pela média das últimas leituras (proposta) e o plano vence quando a leitura alcança o horímetro previsto (Pontos em aberto 2).
3. **Dado** o job diário de manutenção (recorrente global, fuso de São Paulo, `TenantJob`), **quando** um plano entra na antecedência, **então** quem tem `assets.edit` recebe "Preventiva {plano} de {bem} vence em {data}." com link (`dedup_key` `maint-due:{planId}:{data}`); e, no vencimento, o job abre a OS preventiva `Open` com o checklist do plano, uma única vez por vencimento (idempotente; teste de execução dupla e de dois tenants).

**OS de manutenção**
4. **Dado** `assets.create` (proposta), **quando** clico "Nova OS de manutenção" (na ficha, na lista ou pela leitura do QR, PAT-01b), escolho o tipo (Corretiva ou Preventiva avulsa), descrevo o problema, indico se a máquina está parada, agendo a data e o executor, e clico "Abrir OS de manutenção", **então** a OS nasce `Open` com o número `MAN-2026-0001` (`INumberSequence`), exige `Idempotency-Key`; com "máquina parada", o bem passa a `Maintenance` na hora.
5. **Dado** uma OS `Open`, **quando** clico "Iniciar", **então** ela vai a `InProgress` com a data e hora de início, e o bem passa a `Maintenance` (se ainda não estava).
6. **Dado** uma OS `InProgress`, **quando** marco os itens do checklist (feito, não se aplica; alvos de 56 px no celular), descrevo o que foi feito, anexo fotos ou o relatório do prestador (PNG, JPG, PDF até 10 MB, dono `MaintenanceOrder`) e clico "Concluir manutenção", **então** a OS fica `Done` com a data de conclusão, o bem volta a `Active` (se não houver outra OS em andamento), e o plano da preventiva grava a última execução e recalcula o próximo vencimento; com checklist incompleto, o botão fica desabilitado com "Marque os {n} itens que faltam".
7. **Dado** uma OS `Open` ou `InProgress`, **quando** clico "Cancelar OS" e informo o motivo, **então** ela fica `Cancelled`, o bem volta a `Active` (se não houver outra em andamento) e a preventiva cancelada não mexe na última execução do plano.
8. **[Segurança]** **Dado** dois usuários concluindo a mesma OS, **quando** chegam juntos, **então** só um grava (`xmin`), o outro recebe 409 `concurrency_conflict`.

**Histórico e lista**
9. **Dado** `assets.view`, **quando** abro a aba Manutenção do bem, **então** vejo os planos (próximo vencimento, chip "Vencida" crítico quando passou) e as OS do bem (número, tipo, datas, executor, situação), da mais recente à mais antiga.
10. **Dado** Patrimônio → Manutenção, **quando** abro, **então** vejo as OS abertas e em andamento, as preventivas a vencer nos próximos 30 dias e as vencidas, com filtros por bem, centro, tipo e situação na URL; faixa lateral vermelha na OS de máquina parada e âmbar na preventiva a vencer.
11. **Dado** a Minha pauta (DSH-04, Fase 4), **quando** existir, **então** o detector do Assets lista "Preventivas vencidas" e "Máquinas paradas" para quem tem `assets.edit` (proposta).

**Permissões e auditoria**
12. **Dado** o módulo `assets` não liberado, **quando** chamo, **então** 403 `module_not_released`; abrir OS com `assets.create`, plano, execução e conclusão com `assets.edit`; sem a ação, 403 `forbidden` e o botão some. `RequireModule` e `perm:`; IDOR em `/assets/{id}/plans/{planId}` e `/maintenance-orders/{id}/attachments/{attId}`.
13. **Dado** qualquer alteração em plano, horímetro ou OS, **quando** abro o histórico, **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Máquina de estados (proposta para o `04-fluxos`):** OS de manutenção `Open → InProgress → Done`; `Open|InProgress → Cancelled`. Bem: `Active ↔ Maintenance` pelas OS; `Disposed` pela PAT-02 (com OS aberta, a baixa é recusada).
- **Plano:** `next_due_at` (`date`) ou `next_due_meter` (`numeric(18,4)`); recalculado na conclusão a partir da data de conclusão (proposta) ou da data prevista (Pontos em aberto 3).
- **Horímetro:** tabela `assets.asset_meter_readings` (`asset_id`, `read_on`, `value` `numeric(18,4)`, `created_by`), proposta.
- **Tabelas:** `assets.maintenance_plans` (03 + `lead_days`, `default_supplier_party_id`, `next_due_meter`, `is_active`, `xmin`), `assets.maintenance_orders` (03 + `machine_stopped`, `problem`, `work_done`, `started_at`, `cancel_reason`, `xmin`; `cost` e `payable_id` usados na PAT-03b), `assets.maintenance_order_checklist` (linha a linha, como a COM-06).
- Datas de negócio em `date`; instantes em `timestamptz`; Guid v7; `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões:** `assets.view`, `assets.create`, `assets.edit`.
- **Eventos:** `MaintenanceOrderOpened`, `MaintenanceOrderCompleted` (`{ orderId, assetId, workCenterId?, machineStopped, scheduledOn }`), sem texto livre; `MaintenanceScheduled` fica para a PAT-03b.
- **Tabelas:** as acima; tipo de numeração `MAN`; dono de anexo `MaintenanceOrder`.

## Telas e estados
- **Patrimônio → Manutenção** (`/app/patrimonio/manutencao`), **OS de manutenção** (`/app/patrimonio/manutencao/$id`, abas Resumo, Checklist, Documentos, Histórico), aba **Manutenção** na ficha do bem, **OS no app** (`/fab/manutencao/$id`, checklist com 56 px).
- **Verbos:** "Salvar plano" / "Plano salvo"; "Abrir OS de manutenção" / "OS de manutenção aberta"; "Iniciar" / "Manutenção iniciada"; "Concluir manutenção" / "Manutenção concluída"; "Cancelar OS" / "OS cancelada".
- **Estados:** carregando; vazio ("Nenhuma manutenção aberta. Crie planos preventivos para as máquinas." + "Novo plano"); erro.
- **Propositivo:** OS corretiva pela leitura do QR já com o bem; conclusão oferece "Lançar o custo da manutenção" (PAT-03b) no `NextSteps`.
- **400 px:** OS e planos em cartões; checklist com alvos de 56 px.
- **Ajuda:** `Ajuda/patrimonio-manutencao.md`, `Ajuda/patrimonio-manutencao-os.md` (uma por aba), `Ajuda/fab-manutencao.md`; `<Dica>` em "Horímetro" e "Antecedência".

## Plano de testes
- **Unidade:** próxima data e próximo horímetro (CA 1, 2); transições da OS e do bem (CA 4–7).
- **Integração (Testcontainers):** job com aviso, abertura única, execução dupla e dois tenants (CA 3); criação com numeração e `Idempotency-Key` (CA 4); bem em manutenção e volta (CA 5–7); concorrência (CA 8); listas e filtros (CA 9, 10); três camadas e IDOR (CA 12); histórico (CA 13).
- **E2E (Playwright):** ler o QR, abrir corretiva, concluir no celular a 400 px.

## Fora do escopo
- Custo da manutenção, CP do prestador e redução de capacidade: PAT-03b.
- Estoque de peças de reposição consumidas na manutenção: fora do MVP (Pontos em aberto 4).

## Pontos em aberto
1. **Dono da OS de manutenção** (arquiteto): `assets.maintenance_orders` (proposta, como no `03`) × o tipo `Maintenance` de `production.production_orders`; registrar no `03` e no `04-fluxos`.
2. **Plano por horas** (PO): horímetro digitado (proposta) ou horas calculadas pelo uso do centro de trabalho (apontamentos).
3. Próximo vencimento contado da conclusão (proposta) ou da data prevista.
4. Baixa de peças do estoque na OS de manutenção (requisição como a EST-03).
5. Quem abre OS de manutenção (PO): `assets.create` (proposta) ou também o operador pelo app (`shopfloor`).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; dono de anexo com `OwnerExists`; job no `RecurringJobsRegistrar`.
- [ ] Telas com estados; ajuda de cada rota e aba; teste de cobertura passando.
- [ ] Alertas acionáveis e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (máquina da OS de manutenção) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `MaintenancePlan`, `MaintenanceOrder`, `AssetMeterReading` (Domain).
- [ ] `MaintenanceDueJob`; endpoints `/api/v1/assets/maintenance-plans`, `/maintenance-orders`, `/assets/{id}/meter-readings`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, design (checklist no celular), @qa.
- Gate: CA 3, 4, 6 e 8 obrigatórios.

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

- **Correções feitas na validação:** quebra da PAT-03 (custo, CP e capacidade na 03b); máquina de estados como proposta para o `04-fluxos`; job idempotente com `dedup_key` (CA 3); dono da OS de manutenção como ponto em aberto 1.
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2; PAT-01a concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PAT-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
