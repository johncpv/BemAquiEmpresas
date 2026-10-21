# PRD-06a2 — Produto acabado: entrada no estoque, séries e lotes consumidos

| Campo | Valor |
|---|---|
| ID | PRD-06a2 |
| Título | Na conclusão da OS de fabricação, entrada síncrona do produto acabado (`Production`) pelo custo da OS lido por fontes síncronas (horas da Produção + `IStockMovements.CostByOrderAsync`), séries por unidade `{código}-{AAAA}-{seq}`, registro dos lotes de matéria-prima consumidos e liberação síncrona das reservas restantes; o estorno da conclusão reverte a entrada e as séries não expedidas |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-PRD-7 (conclusão gera produto acabado em estoque com número de série e registra lotes consumidos) · R-EST-8 (série no produto acabado) · R-EST-7 (produto acabado pelo custo da OS, `04-fluxos` §3.1) · R-COM-4 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | PRD-06a1, EST-03, EST-02, EST-01a |
| Bloqueia | PRD-06b, EST-04 |
| Ordem de execução | 19 (Marco 3.4 "Fechar e rastrear") |

## User story
Como **supervisor de produção**, quero **que, ao concluir a OS, o equipamento entre no estoque com o número de série de cada unidade e o custo real da OS**, para **o estoque ter o produto pronto com o custo certo e cada série saber de quais lotes de matéria-prima foi feita**.

## Contexto e referências
- PRD §4 ("Expedição: nº de série ─► NF-e"); §5.7 R-PRD-7; §5.9 R-EST-8.
- `04-fluxos-estados` §1 "OS (fabricação)" (`Completed: gera produto acabado com série`); §3.1 ("Produto acabado entra pelo custo apurado da OS (materiais baixados + mão de obra apontada)"); §4 (numeração, série D15).
- `03-modelo-dados` → `inventory.serials` (`item_id`, `serial_number`, `production_order_id`, `status` InStock/Shipped, `project_id`), `stock_movements.type = Production`, `production_orders.serial_numbers`.
- CAD-02a: `tracks_serial` no item; ENG-02 ponto 26 (BOM sem item de estoque do produto é permitida, mas a conclusão exige o produto).
- **Revisão pré-Fase 3:** `docs/revisoes/2026-10-21-revisao-pre-fase3.md` (D1 quebra e ordem, D15, tabela 1.1 itens 26 e 80, §2.1 "Conclusão e romaneio" e "Custo real do projeto", §2.3 padrões 4, 6, 7, 10, §4 Marco 3.4).
- **ADR 0023** §1 (efeito de estoque síncrono na conclusão), §3 (trava e custo médio), §4 (idempotência e estorno), §6 (alvo da reserva 0 na OS concluída), §7 (numeração D15); **ADR 0025** §1 (o movimento `Production` nunca entra no custo do projeto) e §2 (custo da OS por fontes síncronas, sem depender da permissão de quem conclui).
- Quebra da PRD-06a (D1): a conclusão, o projeto "Fabricado", a parcela e o estorno da conclusão estão na **PRD-06a1** (Marco 3.1); esta story acrescenta o estoque à mesma conclusão.

## Critérios de aceite
**Concluir com entrada no estoque**
1. **Dado** uma OS `ReadyToComplete` cujo produto não tem item de estoque (`product_id` nulo, ENG-02 ponto 26), **quando** tento concluir, **então** 422 "Escolha o item de estoque do produto antes de concluir." com o atalho para escolher o item na OS; a partir desta story, a conclusão sempre exige o produto e as entradas.
2. **Dado** `production.approve` e uma OS `ReadyToComplete` com produto, **quando** clico "Concluir OS", **então** o diálogo de impacto da PRD-06a1 passa a mostrar também: a quantidade que entra no estoque (a da OS), os números de série que serão gerados (CA 4), o local de entrada (padrão do item, com a tag "Sugerido", editável), o que sobrou de reserva a liberar e, com `projects.view_cost`, o **custo apurado da OS** e o custo unitário (÷ quantidade, 6 casas); a linha "O produto acabado ainda não entra no estoque" sai.
3. **Dado** o custo da OS, **quando** é calculado, **então** vem de **fontes síncronas** (ADR 0025 §2): Σ `labor_entries.cost` da OS (Produção) + `IStockMovements.CostByOrderAsync(osId)` (baixas − devoluções da OS, Inventory), nunca da razão do Commercial; o cálculo roda no servidor e **não depende da permissão de quem conclui** (sem `projects.view_cost`, o diálogo não mostra o custo, mas a entrada usa o mesmo valor).
4. **Dado** o produto controla série (`tracks_serial`), **quando** concluo, **então** cada unidade ganha um número de série único por item no formato **`{código do item}-{AAAA}-{seq}`** (D15, ponto 80), sequência por item e ano com 5 dígitos (`ELV-0001-2026-00001`; valor inicial, parâmetro), pelo serviço de numeração, editável antes de confirmar (único por item, 422 se repetir); sem controle de série, a entrada é por quantidade.
5. **Dado** a confirmação, **quando** grava, **então** na **mesma transação** da conclusão (ADR 0023 §1; a Produção é dona da transação e chama o `IStockMovements.PostAsync`): a OS vai a `Completed`; nasce a entrada `Production` (origem `ProductionOrder`, chave `(ProductionOrder, osId, 1, Production)`, custo unitário do CA 3) no local escolhido, com o custo médio do produto recalculado (ADR 0023 §3); as séries são gravadas em `inventory.serials` com `status = InStock`, `production_order_id`, `project_id` e local; os lotes consumidos ficam registrados (CA 8); as reservas restantes da OS são liberadas (alvo 0, ADR 0023 §6); e o `ProductionOrderCompleted` vai para o outbox com `productId`, `quantity`, `unitCost`, `serialNumbers` e `locationId` a mais. Qualquer falha desfaz tudo e a OS continua `ReadyToComplete`. A mensagem diz "OS concluída. Produto acabado no estoque."
6. **Dado** a entrada `Production`, **quando** o Projeto 360 calcula o custo, **então** o movimento **não** entra no custo do projeto (ADR 0025 §1: o custo já entrou pela baixa e pelas horas).
7. **Dado** uma OS cujo custo apurado é 0 (sem material nem horas), **quando** concluo, **então** o diálogo mostra o aviso âmbar "Custo da OS zerado: o produto entra no estoque com custo 0." e pede confirmação explícita (R-PRP-7).

**Rastreabilidade (lotes consumidos)**
8. **Dado** a conclusão, **quando** grava, **então** fica registrado o vínculo OS/série → lotes de matéria-prima consumidos (os lotes dos movimentos `Issue` − `Return` da OS, por item e quantidade) em `inventory.serial_components`, só com referências, consultável pela EST-04 (`IStockTraceability`); cada série herda os lotes da OS inteira (ponto 87, EST-04).

**OS concluídas antes desta story**
9. **Dado** as OS concluídas pela PRD-06a1 antes desta story (Marco 3.1, sem estoque), **quando** esta story entra, **então** elas continuam **sem entrada no estoque** e sem séries: não há recomposição de estoque; a OS mostra o texto informativo "Concluída antes do controle de estoque: o produto não entrou no estoque." e a ajuda explica que, se o produto ainda estiver na fábrica, a entrada é feita pelo estoque (EST-01a/EST-01b).

**Estorno da conclusão**
10. **Dado** `production.approve` e uma OS `Completed` com entrada no estoque, **quando** estorno a conclusão (PRD-06a1 CA 10), **então** na mesma transação o Inventory lança o movimento inverso da entrada (`reverses_movement_id`, ADR 0023 §4), as séries não expedidas vão a `Cancelled` e o vínculo de lotes fica marcado como estornado; com qualquer série `Shipped`, 409 "O produto já foi expedido."; produto sem série com saldo menor que a entrada: 409 `insufficient_stock`; a reserva da OS é reconciliada a partir da fonte (ADR 0023 §6).

**Consulta, permissões e auditoria**
11. **Dado** `production.view`, **quando** abro a OS concluída, **então** vejo a aba Séries (série, situação, local) e o link para o movimento de entrada; o custo apurado só com `projects.view_cost`; o item mostra a aba "Séries" (série, OS, projeto, situação, local) com `inventory.view`.
12. **Dado** o módulo `production` ou `inventory` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; concluir e estornar sem `production.approve`: 403. `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/serials/{serialId}` e `/items/{id}/serials`.
13. **[Segurança]** **Dado** duas conclusões simultâneas da mesma OS, **quando** chegam juntas, **então** só uma grava (`xmin`), a entrada no estoque não duplica (chave de idempotência do movimento, ADR 0023 §4) e as séries não repetem (índice único por item).
14. **[Segurança]** **Dado** os DTOs da OS, do item e do `/fab`, **quando** o usuário não tem `projects.view_cost` (OS) ou `inventory.view` (custo médio do item), **então** não trazem custo (DTO separado; nenhum DTO do `shopfloor` traz custo, ADR 0023 §5).
15. **Dado** a conclusão ou o estorno, **quando** abro o histórico da OS, do item e a linha do tempo do projeto, **então** vejo quem, quando, quantas unidades, as séries e o custo (custo só com `projects.view_cost`).

## Regras de negócio e técnicas
- **Custo da OS** = Σ custo das horas da OS + `CostByOrderAsync` no momento da conclusão; custo unitário = custo ÷ quantidade, 6 casas; total do movimento 2 casas `AwayFromZero`.
- **Série** (03 + colunas desta story): `serials` + `status` (InStock/Shipped/Cancelled), `location_id`, `created_at`; único por `(item_id, serial_number)`; tipo de numeração `Serial` com prefixo único no tenant (ADR 0023 §7).
- **Vínculo de rastreabilidade:** `inventory.serial_components` (`production_order_id`, `serial_id` anulável, `item_id`, `lot_id`, `quantity`, `reversed_at`), montado na conclusão a partir dos movimentos da OS.
- **Síncrono x evento** (ADR 0023 §1): entrada, séries, lotes e reservas na transação; o `ProductionOrderCompleted` continua para a projeção (Commercial, painéis).
- **Garantia:** a data de início da garantia é a entrega (R-PRJ-7); esta story só registra a série.
- Guid v7; `xmin`; auditoria `IAuditable`; saldos e custo médio fora do diff (ADR 0023 §4).

## Permissões, eventos e tabelas
- **Permissões:** `production.approve` (concluir, estornar), `production.view`, `inventory.view` (séries do item), `projects.view_cost` (custo). Nenhuma permissão nova.
- **Eventos:** `ProductionOrderCompleted` ganha `productId`, `unitCost`, `serialNumbers` e `locationId` (valores padrão para mensagens antigas no outbox); `ProductionOrderCompletionReversed` sem mudança.
- **Contratos:** `IStockMovements.PostAsync` e `CostByOrderAsync` (Inventory), `IStockSerials` (Inventory: criar, cancelar e consultar séries), `IStockReservations.ReleaseForOrderAsync` (EST-02: liberar o restante, síncrono), `IStockTraceability` (Inventory, usada pela EST-04).
- **Tabelas:** `inventory.serials`, `inventory.serial_components`, `production.production_orders` (`entry_movement_id`, `completed_without_stock`).

## Telas e estados
- Padrões de tela da Fase 3: 4, 6, 7, 10.
- **OS → "Concluir OS"** (diálogo com o impacto no estoque), **OS → aba Séries** (na ordem do padrão 10), **Item → aba Séries**.
- **Verbos:** "Concluir OS" / "OS concluída"; "Estornar conclusão" / "Conclusão estornada".
- **Estados:** botão desabilitado com o motivo (sem produto, etapa pendente); "Concluindo a OS…" com o aviso de demora depois de 30 s (padrão 7, se a transação demorar); erro dentro do diálogo; texto informativo das OS concluídas antes (CA 9); vazio da aba Séries ("Este produto não controla série.").
- **Propositivo:** séries geradas e editáveis; local sugerido; depois de concluir, `NextSteps` "Novo romaneio" (PRD-06b), "Ver parcela liberada" (com `finance.view`) e "Ver projeto".
- **400 px:** diálogo em tela cheia; séries em lista com rolagem.
- **Ajuda:** `Ajuda/producao-os-series.md` (aba `series`), `Ajuda/estoque-item-series.md` (aba do item), atualização de `Ajuda/producao-os.md` ("Concluir OS" com a entrada no estoque e as OS concluídas antes); `<Dica>` em "Custo apurado da OS" ("Horas apontadas mais o material baixado para a OS, menos o devolvido.") e "Número de série".

## Plano de testes
- **Unidade:** custo apurado e unitário (CA 3); geração e unicidade das séries no formato D15 (CA 4).
- **Integração (Testcontainers, obrigatória: movimenta estoque):** sem produto (CA 1); diálogo com e sem `projects.view_cost` e o mesmo custo na entrada (CA 2, 3); entrada `Production` com séries, custo médio, lotes e reservas liberadas na mesma transação, e falha desfazendo tudo (CA 5, 8); fora do custo do projeto (CA 6); custo zero (CA 7); OS concluídas antes sem entrada (CA 9); estorno com e sem série expedida e sem saldo (CA 10); três camadas e IDOR (CA 12); conclusão concorrente (CA 13); DTOs sem custo (CA 14); histórico e linha do tempo (CA 15).
- **E2E (Playwright):** concluir a OS de um projeto de teste com requisição e horas, ver as séries no item e o custo unitário.

## Fora do escopo
- Conclusão sem estoque, projeto "Fabricado", parcela e estorno do projeto: PRD-06a1.
- Romaneio e baixa do produto acabado: PRD-06b. NF-e de saída: Fase 5.
- Recomposição de estoque das OS concluídas antes desta story (CA 9).
- Conclusão parcial: fora do MVP (ponto 82). Garantia por série: fora (R-PRJ-7).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, D1, D15 e tabela 1.1, itens 26 e 80):
1. Formato da série `{código}-{AAAA}-{seq}`, sequência por item e ano (D15, ponto 80; CA 4).
2. A conclusão exige o item de estoque do produto (ENG-02 ponto 26; CA 1).
3. Quebra da PRD-06a (D1): esta story vai ao Marco 3.4; as OS concluídas antes dela ficam sem entrada no estoque, documentado (CA 9).
4. Custo da OS por fontes síncronas, sem depender da permissão de quem conclui (ADR 0025 §2; CA 3).

**Pré-condição externa:** conferir a numeração que a Konebras já usa nos elevadores (campos `elev_*` do Dolibarr, só leitura) antes de implementar a série; se houver um formato em uso, o PO decide se o prefixo ou a sequência inicial se ajustam ao D15. Não bloqueia: o padrão D15 vale se a conferência não mudar nada.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] ADR 0023 (§1, §3, §4, §6) e ADR 0025 §2 aplicadas.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados e 400 px.
- [ ] Ajudas novas e atualizadas; teste de cobertura passando.
- [ ] Diálogo de impacto e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` (`serials`, `serial_components`) e `docs/04-fluxos-estados.md` §1 e §4 (entrada síncrona na conclusão, série D15, OS concluídas antes sem estoque) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `SerialNumberGenerator` com o formato D15.
- [ ] Extensão do `ProductionOrderCompletionService` (PRD-06a1) com a chamada síncrona ao Inventory: entrada, séries, lotes, reservas; estorno inverso.
- [ ] `IStockMovements.CostByOrderAsync`, `IStockSerials`, `IStockTraceability` no Inventory.
- [ ] Diálogo de conclusão, abas Séries, ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (transação Production → Inventory), @qa.
- Gate: CA 3, 5, 10, 13 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (Marco 3.4) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação, na PRD-06a):** quebra da PRD-06 (expedição na PRD-06b); movimento `Production` fora do custo do projeto; `ProjectFabricated` automático substituindo o manual só para projeto com OS; estorno da conclusão sem mexer na parcela liberada.
- **Correções da revalidação (21/10/2026):** quebra da PRD-06a (D1): esta story fica com o estoque da conclusão; entrada, séries, lotes e reservas passam de evento a chamada síncrona na transação da conclusão (ADR 0023 §1, §2.1 "Conclusão e romaneio"); custo da OS pelas fontes síncronas, no lugar do `IProjectCosts.ForOrderAsync` (ADR 0025 §2); série no formato D15 com a pré-condição externa do Dolibarr; produto obrigatório (ponto 26); OS concluídas antes sem entrada, documentado; estorno reverte a entrada e as séries não expedidas.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condição externa explícita: conferir a numeração `elev_*` do Dolibarr (só leitura).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PRD-06, como PRD-06a) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções (PRD-06a) | Pax (PO) |
| 21/10/2026 | 0.3 | Quebra da PRD-06a (D1): produto acabado no Marco 3.4; decisões do PO (D15, tabela 1.1 itens 26 e 80) e correções da revisão pré-Fase 3 (§2.1 conclusão síncrona, ADR 0023 §1 e ADR 0025 §2; §2.3 padrões 4, 6, 7, 10); pré-condição externa `elev_*` | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` com pré-condição externa | Pax (PO) |
