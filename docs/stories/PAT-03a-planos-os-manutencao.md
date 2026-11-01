# PAT-03a — Patrimônio: planos de manutenção preventiva e OS de manutenção

| Campo | Valor |
|---|---|
| ID | PAT-03a |
| Título | Plano de manutenção preventiva por bem (periodicidade em dias ou em horas de uso pelo horímetro digitado, checklist), job diário que abre as OS devidas e avisa antes; OS de manutenção preventiva e corretiva (`MAN-2026-0001`, dona o Assets, `assets.maintenance_orders`) com agendamento, execução interna ou por prestador, checklist, conclusão, bem em "Em manutenção" enquanto parado; próximo vencimento contado da conclusão; histórico de manutenção por bem |
| Tamanho | M (quebra da PAT-03 em 03a planos e OS, 03b custo, CP do prestador e capacidade) |
| Status | Done (pré-condição externa: lista das máquinas com plano e horímetro da Konebras) |
| Requisitos | R-PAT-4 (plano preventivo por tempo ou horas, OS de manutenção, histórico) · R-COM-2 · R-COM-3 · R-COM-4 (numeração MAN) · R-PRP-2 · R-PRP-5 · R-SEC-5 |
| Dependências | PAT-01a (bens, pipeline de foto sem EXIF), PAT-01b (atalho pela leitura), PAT-02 (baixa do bem), DSH-04 (`IAgendaSource`), FND-07 (jobs, notificações, anexos), FND-09e |
| Bloqueia | PAT-03b |
| Ordem de execução | 14 (Marco 6.4 "Patrimônio") |

## User story
Como **supervisor de produção**, quero **cadastrar a manutenção preventiva de cada máquina e receber a OS quando ela vencer, e abrir uma corretiva quando algo quebrar**, para **a máquina não parar sem aviso e eu ter o histórico do que foi feito**.

## Contexto e referências
- PRD R-PAT-4 ("Plano de manutenção preventiva por bem (periodicidade por tempo ou horas), OS de manutenção, histórico e custo"); R-PAT-5 (capacidade, PAT-03b).
- `03-modelo-dados` → `assets.maintenance_plans` (`asset_id`, `description`, `frequency_type` Days/Hours, `frequency_value`, `last_done_at`, `next_due_at`, `checklist`), `assets.maintenance_orders` (`number`, `asset_id`, `plan_id`, `type` Preventive/Corrective, `status`, `scheduled_at`, `done_at`, `cost`, `supplier_party_id`, `notes`, `payable_id`).
- `04-fluxos` §4: `MAN-AAAA-0001`; §2: `MaintenanceScheduled` (Assets) → Production grava a parada que reduz a capacidade (PAT-03b). A máquina de estados da OS de manutenção entra no `04-fluxos` por esta story.
- O tipo `Maintenance` de `production.production_orders` fica sem uso: a OS de manutenção pertence ao Assets (ADR 0038).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): tabela 3.3 pontos 50 a 54, §4.3 (transversal: jobs com `tenantId` e conferência do módulo liberado; anexos autorizados por registro), §4.4 padrão 8 do `/fab` (alvos de 56 px); DSH-04 (Minha pauta) já entregue na Fase 4.
- **ADR 0038** (`docs/adr/0038-patrimonio-manutencao-capacidade.md`: dono da OS de manutenção, paradas na capacidade); ADR 0019 §2, §3, §7, §12 (concorrência, idempotência, anexos, jobs com `dedup_key`).

## Critérios de aceite
**Plano preventivo**
1. **Dado** `assets.edit` e um bem `Active`, **quando** crio o plano com descrição, periodicidade (a cada N dias, ou a cada N horas de uso), antecedência do aviso (padrão 7 dias), executor padrão (interno ou prestador, terceiro com papel Fornecedor), checklist (itens de texto) e a data ou o horímetro da última execução, e clico "Salvar plano", **então** o plano calcula a próxima data (`next_due_at` = última + N dias) ou o próximo horímetro, e a ficha do bem mostra "Próxima preventiva: {data}".
2. **Dado** um plano por horas de uso, **quando** registro a leitura do horímetro no bem ("Registrar horímetro": valor e data, maior que a anterior; 422 "A leitura precisa ser maior que a anterior ({valor}).") digitada à mão, **então** a previsão do próximo vencimento é estimada pela média das últimas leituras e o plano vence quando a leitura alcança o horímetro previsto.
3. **Dado** o job diário de manutenção (recorrente global, fuso de São Paulo, `TenantJob` com `tenantId`, só para tenant com o módulo `assets` liberado), **quando** um plano entra na antecedência, **então** quem tem `assets.edit` recebe "Preventiva {plano} de {bem} vence em {data}." com link (`dedup_key` `maint-due:{planId}:{data}`); e, no vencimento, o job abre a OS preventiva `Open` com o checklist do plano, uma única vez por vencimento (idempotente; teste de execução dupla e de dois tenants).

**OS de manutenção**
4. **Dado** `assets.create`, **quando** clico "Nova OS de manutenção" (na ficha, na lista ou pela leitura do QR, PAT-01b), escolho o tipo (Corretiva ou Preventiva avulsa), descrevo o problema, indico se a máquina está parada, agendo a data e o executor, e clico "Abrir OS de manutenção", **então** a OS nasce `Open` com o número `MAN-2026-0001` (`INumberSequence`), exige `Idempotency-Key`; com "máquina parada", o bem passa a `Maintenance` na hora.
5. **Dado** uma OS `Open`, **quando** clico "Iniciar", **então** ela vai a `InProgress` com a data e hora de início, e o bem passa a `Maintenance` (se ainda não estava).
6. **Dado** uma OS `InProgress`, **quando** marco os itens do checklist (feito, não se aplica; alvos de 56 px no celular), descrevo o que foi feito, anexo fotos ou o relatório do prestador (PNG, JPG, PDF até 10 MB, dono `MaintenanceOrder`, fotos sem EXIF/GPS pelo pipeline da PAT-01a) e clico "Concluir manutenção", **então** a OS fica `Done` com a data de conclusão, o bem volta a `Active` (se não houver outra OS em andamento), e o plano da preventiva grava a última execução e recalcula o próximo vencimento **a partir da data de conclusão**; com checklist incompleto, o botão fica desabilitado com "Marque os {n} itens que faltam".
7. **Dado** uma OS `Open` ou `InProgress`, **quando** clico "Cancelar OS" e informo o motivo, **então** ela fica `Cancelled`, o bem volta a `Active` (se não houver outra em andamento) e a preventiva cancelada não mexe na última execução do plano.
8. **[Segurança]** **Dado** dois usuários concluindo a mesma OS, **quando** chegam juntos, **então** só um grava (`xmin`), o outro recebe 409 `concurrency_conflict`.

**Histórico e lista**
9. **Dado** `assets.view`, **quando** abro a aba Manutenção do bem, **então** vejo os planos (próximo vencimento, chip "Vencida" crítico quando passou) e as OS do bem (número, tipo, datas, executor, situação), da mais recente à mais antiga.
10. **Dado** Patrimônio → Manutenção, **quando** abro, **então** vejo as OS abertas e em andamento, as preventivas a vencer nos próximos 30 dias e as vencidas, com filtros por bem, centro, tipo e situação na URL; faixa lateral vermelha na OS de máquina parada e âmbar na preventiva a vencer.
11. **Dado** a Minha pauta (DSH-04), **quando** o usuário tem `assets.edit`, **então** o Assets implementa o `IAgendaSource` com as categorias "Preventivas vencidas" e "Máquinas paradas", cada item levando à OS ou ao plano.

**Baixa do bem (PAT-02)**
12. **Dado** um bem com OS de manutenção `Open` ou `InProgress`, **quando** alguém tenta baixar o bem (PAT-02 CA 6), **então** 409 "Conclua ou cancele as OS de manutenção abertas." com o link para elas; baixado o bem, os planos dele ficam inativos (sem abrir OS nova) e voltam a ativos na reativação só se alguém os reativar.

**Permissões e auditoria**
13. **Dado** o módulo `assets` não liberado, **quando** chamo, **então** 403 `module_not_released`; abrir OS com `assets.create`, plano, horímetro, execução e conclusão com `assets.edit`; sem a ação, 403 `forbidden` e o botão some. `RequireModule` e `perm:`; IDOR em `/assets/{id}/plans/{planId}` e `/maintenance-orders/{id}/attachments/{attId}` (o anexo precisa ser da OS da rota).
14. **Dado** qualquer alteração em plano, horímetro ou OS, **quando** abro o histórico, **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Máquina de estados (para o `04-fluxos`):** OS de manutenção `Open → InProgress → Done`; `Open|InProgress → Cancelled`. Bem: `Active ↔ Maintenance` pelas OS; `Disposed` pela PAT-02 (com OS aberta, a baixa é recusada, CA 12).
- **Plano:** `next_due_at` (`date`) ou `next_due_meter` (`numeric(18,4)`); recalculado na conclusão a partir da data de conclusão.
- **Horímetro:** digitado; tabela `assets.asset_meter_readings` (`asset_id`, `read_on`, `value` `numeric(18,4)`, `created_by`).
- **Peças:** a baixa de peças do estoque na OS de manutenção fica fora do MVP; peças entram como custo digitado na PAT-03b.
- **Tabelas:** `assets.maintenance_plans` (03 + `lead_days`, `default_supplier_party_id`, `next_due_meter`, `is_active`, `xmin`), `assets.maintenance_orders` (03 + `machine_stopped`, `problem`, `work_done`, `started_at`, `cancel_reason`, `xmin`; `cost` e `payable_id` usados na PAT-03b), `assets.maintenance_order_checklist` (linha a linha, como a COM-06).
- Datas de negócio em `date`; instantes em `timestamptz`; Guid v7; `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões:** `assets.view`, `assets.create` (abrir OS), `assets.edit`.
- **Eventos:** `MaintenanceOrderOpened`, `MaintenanceOrderCompleted` (`{ orderId, assetId, workCenterId?, machineStopped, scheduledOn }`), sem texto livre; `MaintenanceScheduled` fica para a PAT-03b.
- **Contratos:** implementa `IAgendaSource` (DSH-04).
- **Tabelas:** as acima; tipo de numeração `MAN`; dono de anexo `MaintenanceOrder`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 2 (cada item da pauta leva à origem).
- **Patrimônio → Manutenção** (`/app/patrimonio/manutencao`), **OS de manutenção** (`/app/patrimonio/manutencao/$id`, abas Resumo, Checklist, Documentos, Histórico), aba **Manutenção** na ficha do bem, **OS no app** (`/fab/manutencao/$id`, checklist com 56 px).
- **Verbos:** "Salvar plano" / "Plano salvo"; "Registrar horímetro" / "Horímetro registrado"; "Abrir OS de manutenção" / "OS de manutenção aberta"; "Iniciar" / "Manutenção iniciada"; "Concluir manutenção" / "Manutenção concluída"; "Cancelar OS" / "OS cancelada".
- **Estados:** carregando; vazio ("Nenhuma manutenção aberta. Crie planos preventivos para as máquinas." + "Novo plano"); erro.
- **Propositivo:** OS corretiva pela leitura do QR já com o bem; executor padrão do plano preenchido; conclusão oferece "Lançar o custo da manutenção" (PAT-03b) no `NextSteps`.
- **400 px:** OS e planos em cartões; checklist com alvos de 56 px; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** `Ajuda/patrimonio-manutencao.md`, `Ajuda/patrimonio-manutencao-os.md` (uma por aba), `Ajuda/fab-manutencao.md`; `<Dica>` em "Horímetro" e "Antecedência".

## Plano de testes
- **Unidade:** próxima data e próximo horímetro a partir da conclusão (CA 1, 2, 6); transições da OS e do bem (CA 4–7).
- **Integração (Testcontainers):** horímetro menor recusado (CA 2); job com aviso, abertura única, execução dupla, dois tenants e tenant sem o módulo (CA 3); criação com numeração e `Idempotency-Key` (CA 4); bem em manutenção e volta, foto sem EXIF (CA 5–7); concorrência (CA 8); listas e filtros (CA 9, 10); itens da pauta (CA 11); baixa recusada com OS aberta e planos inativos (CA 12); três camadas e IDOR (CA 13); histórico (CA 14).
- **E2E (Playwright):** ler o QR, abrir corretiva, concluir no celular a 400 px.

## Fora do escopo
- Custo da manutenção, CP do prestador e redução de capacidade: PAT-03b.
- Baixa de peças do estoque consumidas na manutenção: fora do MVP.
- OS de manutenção aberta pelo operador no tablet por PIN: fora (abre quem tem `assets.create`).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.3, pontos 50–54):
1. **OS de manutenção no Assets** (`assets.maintenance_orders`), ADR 0038; o tipo `Maintenance` da Produção fica sem uso (regras).
2. **Plano por horas com horímetro digitado** (CA 2).
3. **Próximo vencimento a partir da conclusão** (CA 6).
4. **Baixa de peças do estoque** fora do MVP.
5. **Abre a OS quem tem `assets.create`** (CA 4).

Decisões da revisão aplicadas: §4.3 (job com `tenantId` e módulo liberado, CA 3; anexo autorizado pelo registro, CA 13); pauta pelo `IAgendaSource` da DSH-04 (CA 11); a regra da baixa com OS aberta saiu da PAT-02 (CA 12).

**Pré-condição externa:** a Konebras lista as máquinas com plano preventivo, a periodicidade e as que têm horímetro, para o cadastro inicial; não bloqueia o `Ready`.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade e integração (Testcontainers) passando; front com Vitest. E2E não escrito (regra do PO de 31/10).
- [x] Migration criada (não destrutiva).
- [x] Permissões só do `ModuleCatalog`; dono de anexo com `OwnerExists`; job no `RecurringJobsRegistrar`.
- [x] Telas com estados; ajuda de cada rota e aba; teste de cobertura passando.
- [x] Alertas acionáveis e próximos passos aplicados.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (máquina da OS de manutenção) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `MaintenancePlan`, `MaintenanceOrder`, `AssetMeterReading` (Domain).
- [x] `MaintenanceDueJob`; `IAgendaSource` do Assets; regra da baixa; endpoints `/api/v1/assets/maintenance-plans`, `/maintenance-orders`, `/assets/{id}/meter-readings`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, design (checklist no celular), @qa.
- Gate: CA 3, 4, 6, 8 e 12 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depois da PAT-02) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** quebra da PAT-03 (custo, CP e capacidade na 03b); máquina de estados para o `04-fluxos`; job idempotente com `dedup_key`; dono da OS de manutenção como ponto em aberto.
- **Correções da revalidação (21/10/2026):** pontos 50 a 54 decididos: OS no Assets pela ADR 0038, horímetro digitado (CA 2), vencimento a partir da conclusão (CA 6), peças fora, `assets.create` abre (CA 4); job com `tenantId` e módulo liberado (CA 3); fotos sem EXIF (CA 6); pauta pelo `IAgendaSource` (CA 11), a DSH-04 já entregue; a recusa da baixa com OS aberta e a inativação dos planos vieram da PAT-02 (CA 12, novo); ordem 14 do Marco 6.4; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: lista das máquinas com plano e horímetro (Konebras).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PAT-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (tabela 3.3 pontos 50–54, ADR 0038, §4.3, §4.4 padrão 2); nova ordem (Marco 6.4, ordem 14); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 01/11/2026 | 1.0 | Implementada: `assets.maintenance_plans`, `assets.asset_meter_readings`, `assets.maintenance_orders` e `assets.maintenance_order_checklist` (migration `MaintenancePat03a`, só `CREATE TABLE`/`CREATE INDEX`); `MaintenancePlan`, `AssetMeterReading`, `MaintenanceOrder` (transições `Start`/`Complete`/`Cancel`), `MaintenanceRules` (próxima data e próximo horímetro a partir da última execução, estimativa pela média das últimas 5 leituras, chave do vencimento, o bem "Em manutenção" enquanto alguma OS o segura); `MaintenanceService` e as rotas `GET /api/v1/assets/assets/{id}/maintenance`, `GET/PUT /assets/assets/{id}/plans/{planId}` (pela rota do bem, IDOR), `POST .../plans`, `GET/POST .../meter-readings`, `GET/POST /assets/maintenance-orders` (abrir com `assets.create` e `Idempotency-Key`), `GET .../{id}`, `POST .../start`, `PUT .../checklist/{itemId}`, `POST .../complete`, `POST .../cancel`, `GET/POST .../attachments` e `GET .../attachments/{attId}/content` (o anexo é da OS da rota); `MaintenanceDueRecurringJob` (`assets-maintenance-due`, 06:00 de São Paulo) → `MaintenanceDueJob` (`TenantJob`) → `MaintenanceDueNotifier` (módulo liberado, aviso `maint-due:{planId}:{data}`, OS do vencimento num escopo próprio com índice único `(plan_id, due_key)`); `AssetsAgendaSource` (`assets.maintenance_overdue`, `assets.machines_stopped`, com `assets.edit`); baixa recusada com OS aberta (409 `asset_has_open_maintenance`, com os links) e planos inativos na baixa; `MaintenanceUsage` (bem com manutenção não é excluído) e o prestador nos usos do terceiro; ficha, aba e leitura da etiqueta com a próxima preventiva e a última manutenção; eventos `MaintenanceOrderOpened`/`MaintenanceOrderCompleted` (só ids, sem consumidor); problema, o que foi feito e motivo do cancelamento `[PersonalData]`, exportados pelo `AssetsPersonalDataProvider` (para quem escreveu e, sem os textos, para o prestador). Front: Patrimônio → Manutenção (`/app/patrimonio/manutencao`, filtros na URL, faixa vermelha na máquina parada e âmbar/vermelha na preventiva), OS de manutenção (`/app/patrimonio/manutencao/$id`, abas Resumo, Checklist, Documentos e Histórico), aba Manutenção do bem (planos, horímetro, OS), OS no celular (`/fab/manutencao/$id`, alvos de 56 px), atalho da leitura da etiqueta abrindo a corretiva com o bem e voltando para o celular; ajuda de cada rota e aba. **Recomendações aceitas (PO 24/10), pela opção mais conservadora:** (1) `cost` e `payable_id` não foram criados agora: entram na PAT-03b, que os usa (migration não destrutiva lá); (2) "Lançar o custo da manutenção" no `NextSteps` da conclusão também fica para a PAT-03b; a conclusão oferece "Ver o bem" e "Voltar à manutenção"; (3) a preventiva cancelada não reabre sozinha pelo job (uma OS por vencimento); o plano continua "Vencida" na lista e na pauta, e "Abrir OS agora" no cartão do plano abre a preventiva do plano com o checklist (recusada se o plano já tem OS aberta); (4) "Vencida" no plano por dias é a data que passou (no dia do vencimento é "A vencer" e o job abre a OS); no plano por horas, a leitura que alcançou o previsto; a data estimada nunca vence o plano; (5) sem leitura do horímetro na conclusão da preventiva por horas, vale a última leitura (ou o previsto, se não houver nenhuma); (6) a OS segura o bem "Em manutenção" quando está em andamento ou aberta com a máquina parada; (7) além do aviso da antecedência, o job avisa a abertura da OS (`maint-open:{orderId}`); (8) o checklist só é marcado com a OS em andamento; (9) o bem baixado não recebe plano, leitura nem OS (409 `asset_disposed`), e o plano dele só é reativado com o bem reativado; (10) a OS do celular não cancela (o cancelamento fica na OS completa); (11) a anonimização (`PlanAnonymizationAsync`/`AnonymizeAsync`) ainda não existe no `IPersonalDataProvider` da `main`: a cobertura é a exportação; a LGPD-01b inclui as OS. E2E não escrito (regra do PO de 31/10). Pendência externa: a lista das máquinas com plano e horímetro (Konebras). Status `Done` | Dex (dev) |
