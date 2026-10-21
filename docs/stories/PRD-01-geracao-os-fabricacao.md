# PRD-01 — Geração da OS de fabricação

| Campo | Valor |
|---|---|
| ID | PRD-01 |
| Título | OS de fabricação gerada a partir do item do projeto (evento `ProjectItemCreated` e botão "Gerar OS" para os itens que já existem), com o roteiro copiado do template; etapas, centros, horas padrão e checklist editáveis na OS antes do início; lista e detalhe da OS; cancelamento antes do início |
| Tamanho | G (re-estimada de M: estende a `production_orders` da COM-06 para o tipo Fabricação, cria o consumidor do primeiro evento do Commercial na Produção e a tela da OS de fabricação, base de ENG-02, EST-02, PRD-02 e PRD-03a) |
| Status | Draft |
| Requisitos | R-PRD-1 (OS gerada do item do projeto e do template, editável na OS) · R-PRJ-2 (cada item gera uma ou mais OS) · R-ENG-1 · R-PRP-1 (roteiro do tipo de produto) · R-PRP-2 · R-COM-4 (numeração OS) · R-COM-3 (anexos técnicos, R-ENG-6) · R-SEC-5 |
| Dependências | ENG-01 (`IRoutingTemplates`), COM-04a (`ProjectItemCreated`, `IProjects`), COM-06 (`production_orders`, numeração `OS`), CAD-02a (`IItems`), CAD-03 (`IWorkCenters`), FND-09e |
| Bloqueia | ENG-02 (BOM na etapa Projeto da OS), EST-02, EST-03, PRD-02, PRD-03a, PRD-06a, PLN-01 |
| Ordem de execução | 5 (Marco 3.2 "Da engenharia à OS") |

## User story
Como **supervisor de produção**, quero **que cada equipamento vendido vire uma OS de fabricação com o roteiro certo, que eu possa ajustar antes de começar**, para **a fábrica ter a fila do que produzir sem digitar nada de novo**.

## Contexto e referências
- PRD §4 (Itens do projeto → OS de Fabricação com roteiro de etapas, Kanban); §5.7 R-PRD-1; §5.5 R-PRJ-2; R-ENG-6 (anexos técnicos por OS).
- `04-fluxos-estados` §1 "OS (fabricação)": `Open ─(BOM liberada e primeira etapa iniciada)─► InProgress ◄─► Paused`, `Quality`, `Completed`, `Cancelled` (libera reservas; material baixado exige devolução ou apropriação como perda); etapa `Waiting → Ready → InProgress ↔ Paused → Done | Rejected`, sequencial. §2: `ProjectItemCreated` → "Production cria OS de fabricação (status Open) a partir do template".
- `03-modelo-dados` → `production.production_orders` (`type` Fabrication, `project_id`, `project_item_id`, `product_id`, `quantity`, `status`, `priority`, `planned_start/end`, `actual_start/end`, `serial_numbers`), `production_order_steps` (`sequence`, `name`, `work_center_id`, `standard_hours`, `status`, `planned_*`, `actual_*`, `checklist_result`), `commercial.project_items.routing_template_id` ("PRD-01").
- **Código atual (COM-06):** `ProductionOrder` só tem o necessário da instalação (`InstallerId`, `ContractValue` obrigatórios, `ProductionOrderStatus` = Open/InProgress/Completed/Cancelled); `ProductionOrderStep` sem situação, horas nem checklist; o checklist é a tabela `production_order_checklist` por OS. O `ProjectItemCreated` (`ProjectId`, `ProjectItemId`, `QuoteItemId`, `ItemId?`, `Quantity`) é publicado na aprovação do orçamento desde a COM-04a e **não tem consumidor**: os projetos da Fase 2 não têm OS de fabricação.
- `ModuleCatalog`: `production` (`view`, `create`, `edit`, `approve` = "liberar na qualidade", `delete`). Seed: Supervisão `view/create/edit/approve`, Engenharia `view/edit/create` (COM-06), Almoxarifado `view`.
- P-04 (decidida): etapas Projeto → Corte → Montagem → Qualidade → Expedição.

## Critérios de aceite
**Geração automática**
1. **Dado** um orçamento aprovado (COM-04a) com itens do tipo que gera fabricação (ver Pontos em aberto 1; proposta: `Equipment`), **quando** o consumidor do `ProjectItemCreated` processa, **então** nasce uma OS `Open` do tipo `Fabrication` por item do projeto (Pontos em aberto 2), com número `OS-2026-00001` (mesma sequência `OS` da instalação), projeto, item do projeto, produto (`ItemId`, quando houver), quantidade do item, prioridade 0 e as etapas copiadas do roteiro sugerido (CA 3); o consumidor é idempotente pela inbox e por `(project_item_id)` (uma OS de fabricação não cancelada por item, índice único parcial).
2. **Dado** um item sem roteiro sugerido, **quando** a OS nasce, **então** ela vem sem etapas, com a faixa "Escolha o roteiro desta OS" e o chip de alerta "Sem roteiro", e os membros com `production.edit` recebem a notificação "OS {número} de {projeto} sem roteiro." com link (R-PRP-5, `dedup_key` `os-no-routing:{id}`).
3. **Dado** a sugestão do roteiro, **quando** a OS nasce ou o usuário abre "Trocar roteiro", **então** a ordem é: o roteiro do item do projeto (`project_items.routing_template_id`, herdado do item do orçamento quando existir), o roteiro vinculado ao produto (ENG-01, Pontos em aberto 1 da ENG-01), o último roteiro usado com o mesmo produto, e, se houver um único roteiro ativo, ele (R-PRP-1).

**Itens que já existem (Fase 2)**
4. **Dado** um projeto aprovado antes desta story (sem OS de fabricação), **quando** abro Projeto → Itens e OS com `production.create`, **então** cada item que gera fabricação mostra "Gerar OS de fabricação"; ao confirmar, a OS nasce como no CA 1 (`Idempotency-Key`); a lista "OS de fabricação" tem o filtro "Itens de projeto sem OS" com "Gerar OS" em lote (R-PRP-6).

**Etapas editáveis antes do início**
5. **Dado** `production.edit` e uma OS `Open` sem nenhuma etapa iniciada, **quando** edito as etapas (incluir, remover, reordenar, trocar centro, horas padrão, etapa de qualidade, exige aprovação, checklist) e clico "Salvar etapas", **então** gravam na OS (o template não muda) com as mesmas validações da ENG-01 (CA 5 de lá), e a primeira etapa fica `Ready` e as demais `Waiting`.
6. **Dado** uma etapa já iniciada, **quando** tento editar etapas, **então** 409 "A OS já começou: as etapas não mudam." (o ajuste de horas restantes é do planejamento, Fase 4).
7. **Dado** "Trocar roteiro" numa OS `Open` sem etapa iniciada, **quando** confirmo, **então** as etapas atuais são substituídas pelas do roteiro escolhido, com o diálogo dizendo quantas etapas e horas mudam.

**Detalhe, anexos e lista**
8. **Dado** `production.view`, **quando** abro a OS (`/app/producao/os/$id`), **então** vejo cabeçalho (número, chip de situação, projeto, cliente, produto, quantidade) com o `StageIndicator` (letra da etapa atual em âmbar e segmentos), "facts" (prometido do projeto, horas padrão totais, etapa atual, BOM — "Sem BOM" até a ENG-02) e as abas na URL: Etapas, BOM (a partir da ENG-02, com `engineering.view`), Material (a partir da EST-02), Apontamentos (a partir da PRD-03a), Documentos, Histórico de alterações.
9. **Dado** a aba Documentos, **quando** anexo desenhos e PDFs técnicos (dono `ProductionOrder`, que passa a aceitar também dwg, dxf e até 25 MB para a OS de fabricação — Pontos em aberto 5), **então** o anexo passa pelo ClamAV e fica com quem e quando (R-ENG-6); leitura com `production.view`, escrita com `production.edit`.
10. **Dado** "Fábrica → Produção → OS de fabricação" (`/app/producao/os`), **quando** abro, **então** vejo número, projeto, cliente, produto, quantidade, etapa atual (`StageIndicator`), prometido, situação e chips ("Sem roteiro", "Sem BOM"); filtros na URL por projeto, cliente, linha (do centro), situação e atraso (prometido do projeto < hoje e OS não concluída, com faixa lateral vermelha); a primeira coluna leva ao detalhe; 5 mil OS em menos de 1 s.

**Cancelamento**
11. **Dado** `production.edit` e uma OS `Open` sem etapa iniciada e sem material baixado, **quando** clico "Cancelar OS", informo o motivo e confirmo, **então** a OS fica `Cancelled` e publica `ProductionOrderCancelled` (`{ productionOrderId, projectId }`) no outbox, para a EST-02 liberar as reservas; OS iniciada só cancela com `production.approve`, e o diálogo lista o material já baixado com as saídas "Devolver ao estoque" (EST-03) ou "Manter no projeto como perda" (`04-fluxos` §1).
12. **Dado** o `ContractCancelled` (COM-04b), **quando** o consumidor da Produção processa, **então** cancela as OS de fabricação `Open` sem etapa iniciada do projeto e lista as iniciadas para decisão manual, notificando o responsável do projeto (mesma regra da COM-06 CA 14).

**Contrato para os outros módulos**
13. **Dado** o `IProductionOrders` em `Production.Contracts` (novo), **quando** Engenharia, Estoque, Compras ou Commercial chamam `FindAsync(id)`, `FindManyAsync(ids)`, `ByProjectAsync(projectId)` e `StatusAsync(id)`, **então** recebem número, tipo, projeto, item do projeto, produto, quantidade, situação e etapa atual, sem dado de custo; o Projeto 360 (COM-05, aba Itens e OS) passa a listar as OS de fabricação com a etapa.

**Permissões e segurança**
14. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; gerar sem `production.create`, editar sem `production.edit`, cancelar iniciada sem `production.approve`: 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/steps/{stepId}` e `/attachments/{attId}`; projeto e item validados por `IProjects`, produto por `IItems`, centro por `IWorkCenters`, roteiro por `IRoutingTemplates`.
15. **[Segurança]** **Dado** a OS, **quando** duas edições ou um cancelamento e uma edição chegam juntos, **então** só um grava (`xmin`, 409 `concurrency_conflict` "A OS mudou desde que você abriu. Recarregue.").
16. **Dado** qualquer alteração da OS ou das etapas, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Extensão da `production_orders`** (migration expand, N-1): `product_id`, `project_item_id`, `quantity` (`numeric(18,4)`), `priority`, `routing_template_id` (de onde veio a cópia), `actual_start`, `actual_end`; `installer_id` e `contract_value` passam a anuláveis (obrigatórios só para `Installation`, por `CHECK` por tipo). `ProductionOrderStatus` ganha `Paused` e `Quality` (usados a partir da PRD-03a e PRD-05).
- **Extensão da `production_order_steps`:** `work_center_id`, `standard_hours` (`numeric(18,4)`), `status` (Waiting/Ready/InProgress/Paused/Done/Rejected), `is_quality_step`, `requires_approval`, `planned_start/end`, `actual_start/end`; as etapas da instalação continuam sem centro e sem horas.
- **Checklist da etapa** (proposta): `production_order_checklist` ganha `step_id` (anulável para a instalação) e passa a guardar também o checklist das etapas de fabricação, linha a linha, como a COM-06 já faz (desvio registrado na COM-06).
- **Cópia do roteiro:** nada da OS aponta para a etapa do template; o template é só a origem (`routing_template_id` informativo).
- **Projeto:** o item do projeto ganha `routing_template_id` (03, "PRD-01") pelo Commercial (`IProjects` ganha `ItemsAsync` com o `ItemId`, o tipo do item do orçamento e o roteiro).
- **Situação do projeto** não muda nesta story (`InProgress` nasce com a primeira etapa iniciada, PRD-03a).
- Guid v7; numeração `OS` (5 dígitos) na mesma transação; `xmin`; auditoria `IAuditable` em OS, etapa e checklist; datas `date` no planejado, `timestamptz` no real.

## Permissões, eventos e tabelas
- **Permissões:** `production.view`, `production.create` (gerar), `production.edit` (etapas, cancelar não iniciada, anexos), `production.approve` (cancelar iniciada).
- **Eventos:** **consome** `ProjectItemCreated` (Commercial) e `ContractCancelled` (COM-04b, consumidor já existe para a instalação); **publica** `ProductionOrderCancelled` (`Production.Contracts`).
- **Contratos:** novo `IProductionOrders`; usa `IRoutingTemplates`, `IProjects` (+ `ItemsAsync` ampliado), `IItems`, `IWorkCenters`; implementa `IRoutingTemplateUsageProvider` (ENG-01).
- **Tabelas:** `production.production_orders` e `production.production_order_steps` (colunas acima), `production.production_order_checklist` (+ `step_id`), `commercial.project_items.routing_template_id`.

## Telas e estados
- **Fábrica → Produção → OS de fabricação** (`/app/producao/os`, primeira aba do `production` no `modules.ts`, antes de "Instalações"); detalhe `/app/producao/os/$id`.
- **Verbos:** "Gerar OS de fabricação" / "OS gerada"; "Salvar etapas" / "Etapas salvas"; "Trocar roteiro" / "Roteiro trocado"; "Cancelar OS" / "OS cancelada".
- **Estados:** carregando (skeleton por aba); vazio ("As OS de fabricação nascem com a aprovação do orçamento." + "Ver projetos" e "Itens de projeto sem OS"); erro; modo leitura (OS iniciada: faixa "A OS já começou. As etapas seguem o roteiro gravado.").
- **Propositivo:** roteiro sugerido (CA 3) com a `Tag` "Sugerido"; depois de gerar, `NextSteps`: "Criar a BOM" (a partir da ENG-02, com `engineering.create`), "Ver no Kanban" (PRD-02) e "Ver projeto".
- **400 px:** lista com número, produto, etapa e situação; etapas em cartões; `StageIndicator` mantém a letra e o rótulo.
- **Ajuda:** `Ajuda/producao-os-lista.md` (rota `/app/producao/os`), `Ajuda/producao-os.md` (abas `etapas` padrão, `documentos`, `historico`; as abas `bom`, `material` e `apontamentos` ganham ajuda nas stories delas); `<Dica>` em "Horas padrão", "Etapa de qualidade" e "Sem roteiro".

## Plano de testes
- **Unidade:** sugestão do roteiro (CA 3) com tabela de casos; validação das etapas (CA 5); máquina de estados (CA 6, 11).
- **Integração (Testcontainers):** consumidor do `ProjectItemCreated` idempotente (mensagem duplicada e reentregue), com e sem roteiro (CA 1, 2); itens antigos e geração em lote (CA 4); edição antes e depois do início (CA 5–7); anexos com ClamAV falso (CA 9); lista com filtros e desempenho (CA 10); cancelamento e evento (CA 11); `ContractCancelled` (CA 12); `IProductionOrders` (CA 13); três camadas, IDOR e concorrência (CA 14, 15); auditoria (CA 16); a OS de instalação da COM-06 continua passando nos testes dela (migration N-1).
- **Arquitetura:** Production só referencia `*.Contracts`; o `CHECK` por tipo impede OS de instalação sem instalador.
- **E2E (Playwright):** aprovar orçamento de teste, ver a OS nascer, trocar o roteiro, editar etapas e salvar; tela a 400 px.

## Fora do escopo
- BOM: ENG-02. Reservas: EST-02. Kanban: PRD-02. Apontamento e início da etapa: PRD-03a. Qualidade: PRD-05. Conclusão: PRD-06a.
- Datas previstas (agendamento): PLN-01 (Fase 4); prioridade arrastável: PLN-02.
- Roteiro no item do orçamento (tela do orçamento): Pontos em aberto 4 da ENG-01.

## Pontos em aberto
1. **Quais itens geram OS de fabricação** (PO): proposta: itens do orçamento do tipo `Equipment`; `Product` (revenda) e `Service` não geram. Alternativa: pelo tipo do item do estoque (`Finished`).
2. **Uma OS por item ou por unidade** (PO): proposta: uma OS por item do projeto com a quantidade do item (a série de cada unidade nasce na conclusão, PRD-06a); "Dividir OS" fica fora do MVP.
3. **OS para projetos da Fase 2** (PO): proposta: sem geração automática retroativa; o botão "Gerar OS" e o filtro "Itens de projeto sem OS" (CA 4).
4. **Cancelar OS iniciada** (PO): proposta igual à instalação (COM-06): `production.approve`.
5. **Anexos técnicos** (PO/arquiteto): o dono `ProductionOrder` hoje aceita png, jpg e pdf até 10 MB (ADR 0019 §7, instalação). Proposta: aceitar também dwg e dxf, até 25 MB, para a OS de fabricação (mesma regra do `Project`), atualizando a tabela da ADR 0019 §7.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando; testes da COM-06 intactos.
- [ ] Migration criada (expand, N-1, não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; regras do dono de anexo atualizadas.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da lista e de cada aba; teste de cobertura passando.
- [ ] Padrões preenchidos (roteiro sugerido) e próximos passos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Migration expand das tabelas de OS e etapas; enum de situação ampliado.
- [ ] `FabricationOrderService` (gerar, editar etapas, trocar roteiro, cancelar) e consumidor `ProjectItemCreatedHandler`.
- [ ] `IProductionOrders` em `Production.Contracts`; `IProjects.ItemsAsync` ampliado no Commercial.
- [ ] Endpoints `/api/v1/production/production-orders`; telas, `modules.ts` e ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (extensão da OS sem quebrar a instalação), @data-engineer (migration expand), @qa.
- Gate: CA 1, 4, 5, 11 e 15 obrigatórios.

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

- **Correções feitas na validação:** re-estimada de M para G (extensão da OS da COM-06 e consumidor novo); itens da Fase 2 sem OS cobertos pelo botão e filtro (CA 4), sem geração retroativa; migration N-1 que preserva a instalação; anexos dwg/dxf como ponto em aberto (5), sem mudar a ADR 0019 por conta própria.
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; re-estimada de M para G | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
