# PRD-01 — Geração da OS de fabricação

| Campo | Valor |
|---|---|
| ID | PRD-01 |
| Título | OS de fabricação gerada a partir do item do projeto (automática para equipamento pelo `ProjectItemCreated`; botão "Gerar OS" e geração em lote para componente fabricado e para os itens que já existem), com o roteiro copiado do template; etapas, centros, horas padrão e checklist editáveis na OS antes do início; lista e detalhe da OS com `OrderHeader` e `FactsBar`; desenhos técnicos; cancelamento |
| Tamanho | G (re-estimada de M: estende a `production_orders` da COM-06 para o tipo Fabricação, cria o consumidor do primeiro evento do Commercial na Produção e a tela da OS de fabricação, base de ENG-02, EST-02, PRD-02 e PRD-03a) |
| Status | Ready |
| Requisitos | R-PRD-1 (OS gerada do item do projeto e do template, editável na OS) · R-PRJ-2 (cada item gera uma ou mais OS) · R-ENG-1 · R-PRP-1 (roteiro do tipo de produto) · R-PRP-2 · R-COM-4 (numeração OS) · R-COM-3 (anexos técnicos, R-ENG-6) · R-SEC-5 |
| Dependências | ENG-01 (`IRoutingTemplates`), COM-04a (`ProjectItemCreated`, `IProjects`), COM-06 (`production_orders`, numeração `OS`) |
| Bloqueia | PRD-02, PRD-03a, ENG-02 |
| Ordem de execução | 2 (Marco 3.1 "Fábrica visível") |

## User story
Como **supervisor de produção**, quero **que cada equipamento vendido vire uma OS de fabricação com o roteiro certo, que eu possa ajustar antes de começar**, para **a fábrica ter a fila do que produzir sem digitar nada de novo**.

## Contexto e referências
- PRD §4 (Itens do projeto → OS de Fabricação com roteiro de etapas, Kanban); §5.7 R-PRD-1; §5.5 R-PRJ-2; R-ENG-6 (anexos técnicos por OS).
- `04-fluxos-estados` §1 "OS (fabricação)": `Open ─(BOM liberada e primeira etapa iniciada)─► InProgress ◄─► Paused`, `Quality`, `Completed`, `Cancelled` (libera reservas; material baixado exige devolução ou apropriação como perda); etapa `Waiting → Ready → InProgress ↔ Paused → Done | Rejected`, sequencial. A D12 (ADR 0023 §7) muda a primeira transição: a primeira etapa inicia sem BOM e as seguintes exigem BOM liberada (bloqueio ligado pela ENG-02). §2: `ProjectItemCreated` → "Production cria OS de fabricação (status Open) a partir do template".
- `03-modelo-dados` → `production.production_orders` (`type` Fabrication, `project_id`, `project_item_id`, `product_id`, `quantity`, `status`, `priority`, `planned_start/end`, `actual_start/end`, `serial_numbers`), `production_order_steps` (`sequence`, `name`, `work_center_id`, `standard_hours`, `status`, `planned_*`, `actual_*`, `checklist_result`), `commercial.project_items.routing_template_id` ("PRD-01"), `commercial.quote_items.routing_template_id` ("Fase 3").
- **Revisão pré-Fase 3** (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): D1 (ordem), D12, D14 (quais itens geram OS), D16 (desenhos); tabela 1.1, itens 18 a 22; §2.1 "OS de fabricação e de instalação" e "Outros" (contratos que faltam); §2.2 PRD-01; §2.3 padrão 10.
- ADR 0023 §7 (`ProjectItemCreated` com `Kind`, `IsOnboarding` e `RoutingTemplateId`; D12); ADR 0019 §7 e o adendo de 21/10/2026 (dono `ProductionOrder` com dwg/dxf de 25 MB, ClamAV de 30 MB, limite de envios); ADR 0019 §1 (ordem dos eventos) e §2 (`xmin`); ADR 0006 (outbox); ADR 0020 (migration sobre cópia do `konebras`).
- **Código atual (COM-06):** `ProductionOrder` só tem o necessário da instalação (`InstallerId`, `ContractValue` obrigatórios, `ProductionOrderStatus` = Open/InProgress/Completed/Cancelled); `ProductionOrderStep` sem situação, horas nem checklist; o checklist é a tabela `production_order_checklist` por OS; `InstallationOrdersContract` (`IInstallationOrders.ByProjectAsync`/`StatusAsync`) não filtra por tipo. O `ProjectItemCreated` (`ProjectId`, `ProjectItemId`, `QuoteItemId`, `ItemId?`, `Quantity`) é publicado na aprovação do orçamento desde a COM-04a e **não tem consumidor**: os projetos da Fase 2 não têm OS de fabricação. `ProjectSummary` não tem a data prometida e `ProjectItemInfo` não tem tipo, produto nem roteiro.
- `ModuleCatalog`: `production` (`view`, `create`, `edit`, `approve` = "liberar na qualidade", `delete`). Seed: Supervisão `view/create/edit/approve`, Engenharia `view/edit/create` (COM-06), Almoxarifado `view`.
- P-04 (decidida): etapas Projeto → Corte → Montagem → Qualidade → Expedição ("preparar e embalar", D4).

## Critérios de aceite
**Quais itens geram OS (D14)**
1. **Dado** um orçamento aprovado (COM-04a) com itens do tipo `Equipment` e um contrato que **não** é de implantação, **quando** o consumidor do `ProjectItemCreated` processa, **então** nasce uma OS `Open` do tipo `Fabrication` por item do projeto, com a quantidade do item (a série de cada unidade nasce na conclusão, PRD-06a2), número `OS-2026-00001` (mesma sequência `OS` da instalação), projeto, item do projeto, produto (`ItemId`, quando houver), prioridade 0 e as etapas copiadas do roteiro sugerido (CA 4); o consumidor é idempotente pela inbox e por `(project_item_id)` (uma OS de fabricação não cancelada por item, índice único parcial).
2. **Dado** um item do tipo `Product` (componente fabricado), `Service`, ou qualquer item de contrato de implantação (`IsOnboarding`), **quando** o consumidor processa, **então** não nasce OS: `Product` fica para o "Gerar OS" manual (CA 5); `Service` e contrato de implantação nunca geram OS (o botão não aparece e a API responde 422 "Este item não gera OS de fabricação." ou "Contrato de implantação não gera OS: a fabricação aconteceu antes do sistema.").
3. **Dado** o `ProjectItemCreated` ampliado (`Kind`, `IsOnboarding`, `RoutingTemplateId`; ADR 0023 §7), **quando** o consumidor recebe uma mensagem antiga do outbox, sem esses campos, **então** ela é lida sem erro (campos com valor padrão) e **não** gera OS automática: o item fica na lista "Itens de projeto sem OS" para o "Gerar OS" (CA 5), porque o tipo e a implantação são conferidos pelo `IProjects.ItemsAsync`.

**Roteiro sugerido**
4. **Dado** a sugestão do roteiro, **quando** a OS nasce ou o usuário abre "Trocar roteiro", **então** a ordem é: o roteiro do item do projeto (`project_items.routing_template_id`, herdado do item do orçamento quando existir), o roteiro vinculado ao produto (`IRoutingTemplates.ForItemAsync`, ENG-01 CA 7), o último roteiro usado com o mesmo produto e, se houver um único roteiro ativo, ele (R-PRP-1), com a tag "Sugerido"; sem sugestão, a OS nasce sem etapas, com a faixa "Escolha o roteiro desta OS" e o chip de alerta "Sem roteiro", e os membros com `production.edit` recebem a notificação "OS {número} de {projeto} sem roteiro." com link (R-PRP-5, `dedup_key` `os-no-routing:{id}`).

**Gerar OS manual e itens que já existem (D14, sem geração retroativa)**
5. **Dado** `production.create` e um item `Equipment` de projeto aprovado antes desta story, ou um item `Product`, sem OS de fabricação não cancelada, **quando** abro Projeto → Itens e OS e clico "Gerar OS de fabricação", **então** a OS nasce como no CA 1 (`Idempotency-Key`); a lista "OS de fabricação" tem o filtro "Itens de projeto sem OS" com "Gerar OS" em lote (R-PRP-6), que mostra quantas OS vão nascer antes de confirmar. Não há geração automática retroativa.
6. **Dado** um projeto já `Fabricated`, `Installing`, `Delivered` ou `Cancelled` (inclusive com a fabricação registrada à mão na COM-04b), **quando** tento "Gerar OS", **então** o botão não aparece, o item fica fora do filtro do CA 5 e a API responde 409 "O projeto {número} já foi fabricado: não gera OS nova."

**Etapas editáveis antes do início**
7. **Dado** `production.edit` e uma OS `Open` sem nenhuma etapa iniciada, **quando** edito as etapas (incluir, remover, reordenar, letra, trocar centro, horas padrão, etapa de qualidade, exige aprovação, checklist) e clico "Salvar etapas", **então** gravam na OS (o template não muda) com as mesmas validações da ENG-01 (CA 5 de lá), e a primeira etapa fica `Ready` e as demais `Waiting`.
8. **Dado** uma etapa já iniciada, **quando** tento editar etapas, **então** 409 "A OS já começou: as etapas não mudam." (o ajuste de horas restantes é do planejamento, Fase 4).
9. **Dado** "Trocar roteiro" numa OS `Open` sem etapa iniciada, **quando** confirmo, **então** as etapas atuais são substituídas pelas do roteiro escolhido, com o diálogo de impacto dizendo quantas etapas e horas mudam (antes → depois).

**Detalhe, anexos e lista**
10. **Dado** `production.view`, **quando** abro a OS (`/app/producao/os/$id`), **então** vejo o `OrderHeader` (número, chip de situação, projeto, cliente, produto, quantidade, `StageIndicator` com a letra da etapa atual) e a `FactsBar` (prometido do projeto, horas padrão totais, etapa atual e, a partir da ENG-02, a BOM), e as abas na URL na ordem do padrão 10: Etapas, BOM (a partir da ENG-02, com `engineering.view`), Material (EST-02), Apontamentos (PRD-03a), Qualidade (PRD-05), Custos (PRD-04), Séries (PRD-06a2), Documentos, Histórico de alterações; aba de story ainda não entregue não aparece.
11. **Dado** o `OrderHeader` e a `FactsBar` (componentes comuns novos, padrão 10), **quando** a OS de instalação (COM-06) é aberta, **então** ela usa os mesmos componentes, sem mudar o comportamento nem os testes da COM-06.
12. **Dado** a aba Documentos com `production.edit`, **quando** anexo desenhos e documentos técnicos, **então** o dono `ProductionOrder` aceita pdf, png, jpg, dwg e dxf de até 25 MB na OS de fabricação e continua com png, jpg e pdf de até 10 MB na OS de instalação (`CanAttach` pelo tipo, ADR 0019 §7 adendo de 21/10/2026); o anexo passa pelo ClamAV (configurado para 30 MB; acima do limite do antivírus é recusado, nunca liberado sem varredura) e fica com quem e quando (R-ENG-6); leitura com `production.view`.
13. **[Segurança]** **Dado** um usuário que envia muitos anexos, **quando** passa de 30 envios em uma hora (parâmetro `attachments.max_uploads_per_hour`, ADR 0019 §7 adendo), **então** 429 "Muitos envios em pouco tempo. Tente de novo em alguns minutos." e nada é gravado.
14. **[Segurança]** **Dado** um operador com `shopfloor.view` sem `production.view`, **quando** pede um documento da OS pelo endpoint do chão de fábrica (`GET /api/v1/shopfloor/production-orders/{id}/attachments/{attId}`, `RequireModule("shopfloor")`), **então** o servidor confere que a OS está na fila do chão de fábrica (tem etapa `Ready` ou `InProgress`, OS não cancelada nem concluída, mesmo tenant; regra da fila pela D11) e que o anexo é dessa OS; fora disso, 404; o download é registrado. A tela no `/fab` é da PRD-03a; a entrada na lista permitida da sessão por PIN é da PRD-03b (ADR 0026 §4).
15. **Dado** "Fábrica → Produção → OS de fabricação" (`/app/producao/os`), **quando** abro, **então** vejo número, projeto, cliente, produto, quantidade, etapa atual (`StageIndicator`), prometido, situação e chips ("Sem roteiro" e, a partir da ENG-02, "Sem BOM"); filtros na URL por projeto, cliente, linha (do centro), situação e atraso (prometido do projeto < hoje e OS não concluída, com a faixa lateral vermelha e o texto "Atrasada {n} d", padrão 2); a primeira coluna leva ao detalhe; 5 mil OS em menos de 1 s.

**Cancelamento**
16. **Dado** `production.edit` e uma OS `Open` sem etapa iniciada, **quando** clico "Cancelar OS", informo o motivo e confirmo, **então** a OS fica `Cancelled` e publica `ProductionOrderCancelled` (`{ productionOrderId, projectId }`) no outbox (a EST-02 reconcilia as reservas pela fonte, ADR 0023 §6).
17. **Dado** uma OS com etapa iniciada, **quando** cancelo, **então** exige `production.approve` (sem ela, 403 e o botão some), e o diálogo de impacto lista as horas apontadas e, a partir da EST-03, o material já baixado com as saídas "Devolver ao estoque" (EST-03) ou "Manter no projeto como perda" (`04-fluxos` §1).
18. **Dado** o `ContractCancelled` (COM-04b), **quando** o handler **próprio da fabricação** processa (separado do handler da instalação da COM-06), **então** cancela as OS de fabricação `Open` sem etapa iniciada do projeto e lista as iniciadas para decisão manual, notificando o responsável do projeto (mesma regra da COM-06 CA 14).

**OS de fabricação e de instalação na mesma tabela (§2.1)**
19. **Dado** a mesma entidade `ProductionOrder` para os dois tipos, **quando** a migration roda, **então** `installer_id` e `contract_value` ficam anuláveis com um `CHECK` por tipo (obrigatórios para `Installation`, nulos para `Fabrication`), e `project_item_id`, `product_id` e `quantity` obrigatórios para `Fabrication`; o `IInstallationOrders` (`InstallationOrdersContract.ByProjectAsync` e `StatusAsync`) filtra `type = Installation` (OS de fabricação devolve `null`) e o novo `IProductionOrders` filtra `type = Fabrication`.
20. **Dado** a volta automática do deploy para a versão anterior, **quando** ela lê `production_orders`, **então** nunca carrega linha de fabricação: o deploy é em duas etapas (expand): a primeira leva as colunas anuláveis, o `CHECK` e o filtro por tipo em todas as consultas da instalação, sem gravar OS de fabricação; só a seguinte liga a geração. Teste: a versão com o filtro, sobre um banco com OS de fabricação, continua listando e abrindo as instalações.

**Contratos para os outros módulos**
21. **Dado** o `IProductionOrders` em `Production.Contracts` (novo), **quando** Engenharia, Estoque, Compras ou Commercial chamam `FindAsync(id)`, `FindManyAsync(ids)`, `ByProjectAsync(projectId)` e `StatusAsync(id)`, **então** recebem número, tipo, projeto, item do projeto, produto, quantidade, situação e etapa atual (nome e letra), sem dado de custo; o Projeto 360 (COM-05, aba Itens e OS) passa a listar as OS de fabricação com a etapa.
22. **Dado** os contratos do Commercial, **quando** a Produção chama, **então** `IProjects.FindAsync`/`FindManyAsync` trazem `PromisedDate` (data prometida do projeto) e `IProjects.ItemsAsync` traz, por item, `ItemId`, o tipo (`Kind`), `IsOnboarding` do contrato e `RoutingTemplateId`; `commercial.quote_items` e `commercial.project_items` ganham `routing_template_id` anulável (o item do projeto herda do item do orçamento), sem tela nesta fase (ENG-01 ponto 6), e o Commercial implementa o `IRoutingTemplateUsageProvider` (ENG-01 CA 10).

**Permissões e segurança**
23. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; gerar sem `production.create`, editar sem `production.edit`, cancelar iniciada sem `production.approve`: 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/steps/{stepId}` e `/attachments/{attId}`; projeto e item validados por `IProjects`, produto por `IItems`, centro por `IWorkCenters`, roteiro por `IRoutingTemplates`.
24. **[Segurança]** **Dado** a OS, **quando** duas edições ou um cancelamento e uma edição chegam juntos, **então** só um grava (`xmin`, 409 `concurrency_conflict` "A OS mudou desde que você abriu. Recarregue.").
25. **Dado** qualquer alteração da OS ou das etapas, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Extensão da `production_orders`** (migration expand, N-1, CA 19 e 20): `product_id`, `project_item_id`, `quantity` (`numeric(18,4)`), `priority`, `routing_template_id` (de onde veio a cópia), `actual_start`, `actual_end`; `installer_id` e `contract_value` anuláveis com `CHECK` por tipo. `ProductionOrderStatus` ganha `Paused` e `Quality` (usados a partir da PRD-03a e PRD-05).
- **Extensão da `production_order_steps`:** `code` (a letra, copiada do roteiro), `work_center_id`, `standard_hours` (`numeric(18,4)`), `status` (Waiting/Ready/InProgress/Paused/Done/Rejected), `is_quality_step`, `requires_approval`, `planned_start/end`, `actual_start/end`; as etapas da instalação continuam sem centro e sem horas.
- **Checklist da etapa:** `production_order_checklist` ganha `step_id` (anulável para a instalação) e passa a guardar também o checklist das etapas de fabricação, linha a linha, como a COM-06 já faz (desvio registrado na COM-06).
- **Cópia do roteiro:** nada da OS aponta para a etapa do template; o template é só a origem (`routing_template_id` informativo).
- **Uma OS por item (D14):** a OS tem a quantidade do item; "Dividir OS" fica fora do MVP.
- **Evento ampliado:** o Commercial publica o `ProjectItemCreated` com `Kind`, `IsOnboarding` e `RoutingTemplateId` (campos com valor padrão, compatível com as mensagens antigas, CA 3).
- **Contratos separados (§2.1):** `IInstallationOrders` só para instalação e `IProductionOrders` só para fabricação, sobre a mesma tabela.
- **Situação do projeto** não muda nesta story (`InProgress` nasce com a primeira etapa iniciada, PRD-03a).
- Guid v7; numeração `OS` (5 dígitos) na mesma transação; `xmin`; auditoria `IAuditable` em OS, etapa e checklist; datas `date` no planejado, `timestamptz` no real.

## Permissões, eventos e tabelas
- **Permissões:** `production.view`, `production.create` (gerar), `production.edit` (etapas, cancelar não iniciada, anexos), `production.approve` (cancelar iniciada); `shopfloor.view` (documento da OS pelo endpoint do chão de fábrica, CA 14).
- **Eventos:** **consome** `ProjectItemCreated` (Commercial, ampliado) e `ContractCancelled` (COM-04b, handler próprio da fabricação); **publica** `ProductionOrderCancelled` (`Production.Contracts`).
- **Contratos:** novo `IProductionOrders`; `IInstallationOrders` filtrado por tipo; usa `IRoutingTemplates` (+ `ForItemAsync`), `IProjects` (+ `PromisedDate`, `ItemsAsync` ampliado), `IItems`, `IWorkCenters`; implementa `IRoutingTemplateUsageProvider` (ENG-01) na Produção e no Commercial.
- **Tabelas:** `production.production_orders` e `production.production_order_steps` (colunas acima), `production.production_order_checklist` (+ `step_id`), `commercial.quote_items.routing_template_id` e `commercial.project_items.routing_template_id` (anuláveis).

## Telas e estados
- **Padrões de tela da Fase 3:** 2 (faixa lateral com texto), 3 (indicador de etapa), 4 (diálogo de impacto ao trocar roteiro e cancelar), 6 (roteiro "Sugerido"), 10 (`OrderHeader`/`FactsBar` e ordem das abas, criados aqui).
- **Fábrica → Produção → OS de fabricação** (`/app/producao/os`, primeira aba do `production` no `modules.ts`, antes de "Instalações"); detalhe `/app/producao/os/$id`.
- **Verbos:** "Gerar OS de fabricação" / "OS gerada"; "Gerar OS" em lote / "{n} OS geradas"; "Salvar etapas" / "Etapas salvas"; "Trocar roteiro" / "Roteiro trocado"; "Cancelar OS" / "OS cancelada".
- **Estados:** carregando (skeleton por aba); vazio ("As OS de fabricação nascem com a aprovação do orçamento." + "Ver projetos" e "Itens de projeto sem OS"); erro; modo leitura (OS iniciada: faixa "A OS já começou. As etapas seguem o roteiro gravado.").
- **Propositivo:** roteiro sugerido (CA 4); depois de gerar, `NextSteps`: "Criar a BOM" (a partir da ENG-02, com `engineering.create`), "Ver no Kanban" (PRD-02) e "Ver projeto"; item `Product` sem OS aparece no filtro "Itens de projeto sem OS".
- **400 px:** lista com número, produto, etapa e situação; etapas em cartões; `StageIndicator` no formato da ENG-01 (compacto acima de 8 etapas); `FactsBar` quebra em duas linhas.
- **Ajuda:** `Ajuda/producao-os-lista.md` (rota `/app/producao/os`), `Ajuda/producao-os.md` (abas `etapas` padrão, `documentos`, `historico`; as abas das outras stories ganham ajuda nelas); a ajuda explica quais itens geram OS (D14); `<Dica>` em "Horas padrão", "Etapa de qualidade", "Sem roteiro" e "Gerar OS" ("Equipamento gera OS sozinho. Componente fabricado, pelo botão. Serviço e contrato de implantação não geram.").

## Plano de testes
- **Unidade:** quais itens geram OS (CA 1, 2, 3) e sugestão do roteiro (CA 4) com tabela de casos; validação das etapas (CA 7); máquina de estados (CA 8, 16, 17); `CHECK` por tipo (CA 19).
- **Integração (Testcontainers):** consumidor do `ProjectItemCreated` idempotente (mensagem duplicada, reentregue e antiga sem os campos novos), com e sem roteiro, `Equipment`, `Product`, `Service` e implantação (CA 1–4); "Gerar OS" e geração em lote, e bloqueio em projeto fabricado (CA 5, 6); edição antes e depois do início (CA 7–9); anexos por tipo de OS com ClamAV falso e arquivo de 25 MB, limite de envios (CA 12, 13); documento pelo endpoint do chão de fábrica dentro e fora da fila (CA 14); lista com filtros e desempenho (CA 15); cancelamento, evento e `production.approve` na iniciada (CA 16, 17); `ContractCancelled` no handler da fabricação sem afetar o da instalação (CA 18); `IInstallationOrders` filtrado e `IProductionOrders` (CA 19, 21); versão com o filtro sobre banco com OS de fabricação (CA 20); `IProjects` ampliado (CA 22); três camadas, IDOR e concorrência (CA 23, 24); auditoria (CA 25); os testes da COM-06 continuam passando.
- **Componentes (Vitest):** `OrderHeader` e `FactsBar` (CA 10, 11).
- **Arquitetura:** Production só referencia `*.Contracts`; o `CHECK` por tipo impede OS de instalação sem instalador.
- **E2E (Playwright):** aprovar orçamento de teste com equipamento, ver a OS nascer, trocar o roteiro, editar etapas e salvar; gerar OS de um componente pelo botão; tela a 400 px.

## Fora do escopo
- BOM e o bloqueio por BOM: ENG-02. Reservas: EST-02. Kanban: PRD-02. Apontamento, início da etapa e a tela de documentos no `/fab`: PRD-03a. Qualidade: PRD-05. Conclusão: PRD-06a1; produto acabado e séries: PRD-06a2.
- Datas previstas (agendamento): PLN-01 (Fase 4); prioridade arrastável: PLN-02.
- Tela do roteiro no item do orçamento (só a coluna entra aqui).
- "Dividir OS" e geração automática retroativa para os projetos da Fase 2.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 18–22):
1. D14: `Equipment` gera OS automática; `Product` (componente fabricado) por "Gerar OS" manual; `Service` nunca; contrato de implantação nunca (CA 1, 2, 5).
2. D14: uma OS por item do projeto, com a quantidade do item; série por unidade na conclusão (PRD-06a2) (CA 1).
3. D14: sem geração retroativa para os projetos da Fase 2: botão "Gerar OS" e geração em lote; "Gerar OS" bloqueado em projeto já fabricado (CA 5, 6).
4. Cancelar OS iniciada com `production.approve` (CA 17).
5. D16: dwg/dxf de até 25 MB no dono `ProductionOrder` pelo adendo da ADR 0019 §7, com ClamAV de 30 MB e limite de envios; o operador lê pelo endpoint do chão de fábrica que confere a fila (CA 12–14).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), componentes, arquitetura e E2E passando; testes da COM-06 intactos.
- [ ] Migration criada (expand, N-1, não destrutiva), testada sobre cópia do banco do tenant `konebras`; deploy em duas etapas (CA 20).
- [ ] Permissões só do `ModuleCatalog`; regras do dono de anexo `ProductionOrder` pelo adendo da ADR 0019 §7 (D16).
- [ ] `OrderHeader` e `FactsBar` criados e usados também na OS de instalação.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da lista e de cada aba; teste de cobertura passando.
- [ ] Padrões preenchidos (roteiro sugerido) e próximos passos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Migration expand das tabelas de OS e etapas com o `CHECK` por tipo; enum de situação ampliado; filtro por tipo no `InstallationOrdersContract` (primeiro deploy).
- [ ] `FabricationOrderService` (gerar, gerar em lote, editar etapas, trocar roteiro, cancelar), consumidor `ProjectItemCreatedHandler` e handler de `ContractCancelled` da fabricação.
- [ ] `IProductionOrders` em `Production.Contracts`; `ProjectItemCreated` ampliado, `PromisedDate`, `ItemsAsync` ampliado e `routing_template_id` no Commercial.
- [ ] Endpoint do documento da OS para o chão de fábrica; limite de envios.
- [ ] `OrderHeader` e `FactsBar`; endpoints `/api/v1/production/production-orders`; telas, `modules.ts` e ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (extensão da OS sem quebrar a instalação, evento ampliado), @data-engineer (migration expand e deploy em duas etapas), cybersecurity (anexos e endpoint do chão de fábrica), @qa.
- Gate: CA 1, 2, 3, 5, 14, 17, 19, 20 e 24 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CA 13, 14) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: Marco 3.1, ordem 2) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** re-estimada de M para G (extensão da OS da COM-06 e consumidor novo); itens da Fase 2 sem OS cobertos pelo botão e filtro (CA 4), sem geração retroativa; migration N-1 que preserva a instalação; anexos dwg/dxf como ponto em aberto (5), sem mudar a ADR 0019 por conta própria.
- **Correções da revalidação (21/10/2026):** pontos 18 a 22 decididos (D14: `Equipment` automático, `Product` manual, `Service` e implantação nunca, CA 1, 2, 5; projeto fabricado bloqueia "Gerar OS", CA 6; cancelamento da iniciada com `production.approve`, CA 17; D16 com ClamAV de 30 MB, limite de envios e endpoint do chão de fábrica, CA 12–14); §2.1: mesma entidade com `CHECK` por tipo, contratos separados e filtro no `IInstallationOrders`, deploy em duas etapas para a versão anterior não ler fabricação, handler próprio de `ContractCancelled`, `ProjectItemCreated` ampliado e compatível (CA 3, 18–20); contratos que faltam (`PromisedDate`, tipo e roteiro em `ItemsAsync`, CA 22); `OrderHeader`/`FactsBar` e ordem das abas (padrão 10, CA 10, 11); chip "Sem BOM" só a partir da ENG-02 (Marco 3.1 sem BOM); dependências e bloqueios pela nova ordem; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; re-estimada de M para G | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D1, D12, D14, D16, tabela 1.1 itens 18–22) e correções da revisão pré-Fase 3 (§2.1 OS de fabricação e de instalação e contratos que faltam, §2.2 PRD-01, §2.3 padrões 2, 3, 4, 6 e 10); nova ordem (Marco 3.1, ordem 2); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
