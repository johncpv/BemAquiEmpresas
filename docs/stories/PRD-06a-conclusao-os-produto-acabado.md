# PRD-06a — Conclusão da OS: produto acabado com série e custo da OS

| Campo | Valor |
|---|---|
| ID | PRD-06a |
| Título | Conclusão da OS de fabricação com todas as etapas aprovadas: número de série por unidade, entrada do produto acabado no estoque pelo custo apurado da OS (material baixado + mão de obra), registro dos lotes de matéria-prima consumidos, liberação do que sobrou de reserva e, quando todas as OS de fabricação do projeto concluem, "fabricação concluída" automática (`ProjectFabricated`) |
| Tamanho | M (quebra da PRD-06: a expedição e o romaneio foram para a PRD-06b) |
| Status | Draft |
| Requisitos | R-PRD-7 (conclusão gera produto acabado em estoque com número de série e registra lotes consumidos) · R-EST-8 (série no produto acabado) · R-EST-7 (produto acabado pelo custo da OS, `04-fluxos` §3.1) · R-PRJ-6 (marco "fabricação concluída" libera parcela) · R-PRJ-7 (garantia a partir da entrega, D) · R-COM-4 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | PRD-05 (qualidade aprovada), EST-03 (material baixado por OS, lotes, `IProjectCosts`), PRD-04 (custo de mão de obra), EST-01a (`IStockMovements`, tipo `Production`), EST-02 (liberação das reservas), COM-04b (`ProjectFabricated` libera a parcela; registro de gatilhos) |
| Bloqueia | PRD-06b (expedição do produto acabado), EST-04 (série → lotes) |
| Ordem de execução | 19 (Marco 3.5 "Fechar a OS") |

## User story
Como **supervisor de produção**, quero **concluir a OS quando o equipamento passou na qualidade, gerando o número de série e dando entrada no produto acabado pelo custo real**, e como **financeiro**, quero **que a parcela "fabricação concluída" seja liberada sozinha quando todas as OS do projeto terminarem**, para **o estoque ter o equipamento pronto com o custo certo e o cliente ser cobrado na hora certa**.

## Contexto e referências
- PRD §4 ("Expedição: nº de série ─► NF-e"); §5.7 R-PRD-7; §5.9 R-EST-8; §5.5 R-PRJ-6.
- `04-fluxos-estados` §1 "OS (fabricação)" (`Completed: gera produto acabado com série`); §1 "Projeto" (`→ Fabricated` "todas OS de fabricação concluídas; até a PRD-06, registro manual 'Registrar fabricação concluída'"); §2: `ProductionOrderCompleted` → Inventory entra produto acabado com série; se todas OS do projeto → `ProjectFabricated` → Finance libera os recebíveis com gatilho `FabricationDone` (vencimento = data do evento + prazo); §3.1 ("Produto acabado entra pelo custo apurado da OS (materiais baixados + mão de obra apontada)").
- `03-modelo-dados` → `inventory.serials` (`item_id`, `serial_number`, `production_order_id`, `status` InStock/Shipped, `project_id`), `stock_movements.type = Production`, `production_orders.serial_numbers`.
- **Código atual:** o `ProjectFabricated` já existe em `Commercial.Contracts` e é publicado pelo endpoint manual `POST /projects/{id}/fabrication` (COM-04b); a COM-04b liberou os títulos `FabricationDone` com registro de gatilhos (ADR 0019 §1).
- CAD-02a: `tracks_serial` no item; garantia padrão por tipo (`warranty_defaults`, P-11).

## Critérios de aceite
**Concluir**
1. **Dado** `production.approve` e uma OS com todas as etapas `Done` (inclusive a qualidade aprovada, PRD-05) e nenhum apontamento aberto, **quando** clico "Concluir OS", **então** o diálogo grande mostra o impacto: quantidade a produzir (a da OS), os números de série que serão gerados (CA 2), o local de entrada (padrão do item), o **custo apurado da OS** (material baixado − devolvido + mão de obra, pelo `IProjectCosts.ForOrderAsync`) e o custo unitário (÷ quantidade, 6 casas), o que sobrou de reserva a liberar, e as parcelas que serão liberadas se esta for a última OS do projeto (com `finance.view`).
2. **Dado** o produto da OS controla série (`tracks_serial`), **quando** concluo, **então** cada unidade ganha um número de série único por item, no formato proposto `{código do item}-{AAAA}-{sequência de 5}` (Pontos em aberto 1), editável antes de confirmar (único, 422 se repetir); sem controle de série, a entrada é por quantidade.
3. **Dado** a confirmação, **quando** grava, **então** na mesma transação: a OS vai a `Completed` (`actual_end`), as séries são gravadas, e o evento `ProductionOrderCompleted` (`{ productionOrderId, projectId, productId, quantity, unitCost, serialNumbers, locationId, completedOn }`) vai para o outbox; a mensagem diz "OS concluída. Dando entrada no produto acabado…".
4. **Dado** o `ProductionOrderCompleted`, **quando** o consumidor do Inventory processa, **então** dá entrada no produto acabado pelo `IStockMovements` (tipo `Production`, origem `ProductionOrder`, custo unitário do evento, uma série por unidade em `inventory.serials` com `status = InStock`, `production_order_id` e `project_id`), recalcula o custo médio do produto e libera as reservas restantes da OS (EST-02 CA 8); idempotente pela inbox e por `(ProductionOrder, osId)`; o movimento `Production` **não** gera custo de projeto (EST-03, regras).
5. **Dado** a OS sem produto de estoque (`product_id` nulo, ENG-02 ponto 4), **quando** tento concluir, **então** 422 "Escolha o item de estoque do produto antes de concluir." com o atalho para escolher o item na OS.
6. **Dado** uma OS cujo custo apurado é 0 (sem material nem horas), **quando** concluo, **então** o diálogo mostra o aviso âmbar "Custo da OS zerado: o produto entra no estoque com custo 0." e pede confirmação explícita (R-PRP-7).

**Rastreabilidade (lotes consumidos)**
7. **Dado** a conclusão, **quando** grava, **então** fica registrado o vínculo série/OS → lotes de matéria-prima consumidos (os lotes dos movimentos `Issue` − `Return` da OS, por item e quantidade), consultável pela EST-04 (`IStockTraceability`, proposta) — sem copiar dado, só referências.

**Fabricação concluída do projeto**
8. **Dado** a última OS de fabricação não cancelada do projeto concluída, **quando** o consumidor do Commercial recebe o `ProductionOrderCompleted` e confere por `IProductionOrders.ByProjectAsync` que todas as OS de fabricação não canceladas estão `Completed`, **então** passa o projeto a `Fabricated` (`fabricated_on` = data da conclusão) e publica `ProjectFabricated` (`{ projectId, contractId, occurredOn }`); a COM-04b libera os títulos com gatilho `FabricationDone` (Planned → Open, vencimento = data + prazo), mesmo que o evento chegue antes do título (registro de gatilhos).
9. **Dado** um projeto com OS de fabricação, **quando** alguém tenta "Registrar fabricação concluída" à mão (COM-04b), **então** 409 "A fabricação deste projeto é concluída pelas OS ({n} abertas)." com o link para as OS; o registro manual continua para projeto **sem** OS de fabricação (Pontos em aberto 2).
10. **Dado** o projeto já `Fabricated` à mão antes desta story, **quando** a última OS conclui, **então** o Commercial não publica o evento de novo (o gatilho já está registrado, idempotente por `(project_id, event)`).

**Estorno da conclusão**
11. **Dado** `production.approve` e uma OS `Completed` cujo produto acabado ainda está todo em estoque (nenhuma série expedida), **quando** clico "Estornar conclusão" com motivo e confirmo no diálogo `destructive`, **então** a OS volta a `InProgress` com a última etapa `Ready`, o evento `ProductionOrderCompletionReversed` faz o Inventory lançar o movimento inverso (séries `Cancelled`, proposta) e o Commercial voltar o projeto de `Fabricated` a `InProgress` quando ele tinha sido fabricado por esta OS, sem estornar títulos já liberados (o financeiro decide; notificação a `finance.edit`); com série expedida, 409 "O produto já foi expedido."

**Consulta, permissões e auditoria**
12. **Dado** `production.view`, **quando** abro a OS concluída, **então** vejo as séries, o custo apurado (com `projects.view_cost`) e o link para o movimento de entrada; o item mostra a aba "Séries" (série, OS, projeto, situação, local), com `inventory.view`.
13. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; concluir e estornar sem `production.approve`: 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/serials/{serialId}`.
14. **[Segurança]** **Dado** duas conclusões simultâneas da mesma OS, **quando** chegam juntas, **então** só uma grava (`xmin`), e a entrada no estoque e o `ProjectFabricated` não duplicam (inbox e idempotência por origem).
15. **Dado** a conclusão ou o estorno, **quando** abro o histórico da OS, do item e a linha do tempo do projeto, **então** vejo quem, quando, quantas unidades, as séries e o custo (custo só com `projects.view_cost`).

## Regras de negócio e técnicas
- **Custo apurado da OS** = Σ `project_costs` da OS (Material de `StockIssue` e `StockReturn`, Labor) no momento da conclusão; custo unitário = custo ÷ quantidade, 6 casas; total do movimento 2 casas.
- **Série** (03 + proposta): `serials` + `status` (InStock/Shipped/Cancelled), `location_id`, `created_at`; único por `(item_id, serial_number)`.
- **Vínculo de rastreabilidade** (proposta): `inventory.serial_components` (`serial_id` ou `production_order_id`, `item_id`, `lot_id`, `quantity`), montado no consumo do `ProductionOrderCompleted` a partir dos movimentos da OS.
- **Garantia:** a data de início da garantia é a entrega (R-PRJ-7, COM-04a `warranty_until`); esta story só registra a série; a garantia por série fica para quando o PO pedir.
- **Projeto `Fabricated` automático** substitui o manual para projetos com OS de fabricação (`04-fluxos` §1 previa a troca "até a PRD-06").
- Guid v7; `xmin`; auditoria `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões:** `production.approve` (concluir, estornar), `production.view`, `inventory.view` (séries), `projects.view_cost` (custo), `finance.view` (parcelas no diálogo).
- **Eventos:** **publica** `ProductionOrderCompleted`, `ProductionOrderCompletionReversed` (`Production.Contracts`); **consumidores:** Inventory (entrada, séries, reservas), Commercial (projeto `Fabricated` e `ProjectFabricated`).
- **Contratos:** `IProjectCosts.ForOrderAsync` (Commercial), `IStockMovements`, `IProductionOrders.ByProjectAsync`; `IStockTraceability` (proposta, Inventory, usada pela EST-04).
- **Tabelas:** `inventory.serials`, `inventory.serial_components` (proposta), `production.production_orders` (`actual_end`).

## Telas e estados
- **OS → "Concluir OS"** (diálogo com impacto), **OS → aba Séries** (depois da conclusão), **Item → aba Séries**.
- **Verbos:** "Concluir OS" / "OS concluída"; "Estornar conclusão" / "Conclusão estornada".
- **Estados:** botão desabilitado com o motivo enquanto falta etapa ("Falta aprovar a Qualidade") ou há apontamento aberto; processamento assíncrono da entrada ("Dando entrada no produto acabado…", depois de cerca de 30 s o estado de demora); erro dentro do diálogo.
- **Propositivo:** séries geradas e editáveis; local padrão; depois de concluir, `NextSteps` "Montar romaneio" (PRD-06b), "Ver parcela liberada" (com `finance.view`, quando liberou) e "Ver projeto".
- **400 px:** diálogo em tela cheia; séries em lista com rolagem.
- **Ajuda:** `Ajuda/producao-os-series.md` (aba `series`), `Ajuda/estoque-item-series.md` (aba do item), atualização de `Ajuda/producao-os.md` ("Concluir OS") e de `Ajuda/projetos-projeto.md` ("Fabricação concluída automática"); `<Dica>` em "Custo apurado da OS" e "Número de série".

## Plano de testes
- **Unidade:** custo apurado e unitário (CA 1); geração e unicidade das séries (CA 2); regra "última OS do projeto" (CA 8).
- **Integração (Testcontainers, obrigatória: movimenta estoque e libera recebível):** concluir com evento na mesma transação (CA 3); entrada `Production` com séries, custo médio e reservas liberadas, sem custo de projeto (CA 4); sem produto e custo zero (CA 5, 6); vínculo de lotes (CA 7); última OS → `Fabricated` → título `FabricationDone` liberado ponta a ponta, inclusive com o evento antes do título (CA 8); manual recusado com OS e permitido sem OS (CA 9); já fabricado à mão (CA 10); estorno com e sem série expedida (CA 11); três camadas e IDOR (CA 13); conclusão concorrente (CA 14); histórico e linha do tempo (CA 15).
- **E2E (Playwright):** concluir a última OS de um projeto de teste e ver o projeto "Fabricado" e a parcela liberada.

## Fora do escopo
- Romaneio e baixa do produto acabado: PRD-06b. NF-e de saída: Fase 5.
- Conclusão parcial (parte das unidades): fora do MVP (Pontos em aberto 3).
- Garantia por série: fora (a garantia é do item entregue, R-PRJ-7).

## Pontos em aberto
1. **Formato da série** (PO): proposta `{código do item}-{AAAA}-{00001}`, sequência por item e ano; alternativa: tipo de documento configurável na CAD-06.
2. **Registro manual de fabricação** (PO): proposta: bloqueado para projeto com OS de fabricação (CA 9) e mantido para projeto sem OS.
3. **Conclusão parcial** (PO): proposta: fora do MVP; a OS conclui com a quantidade inteira.
4. **Estorno da conclusão e parcela já liberada** (PO/Financeiro): proposta: não estorna o título liberado; notifica o financeiro (CA 11).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, cadeia até o recebível) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados (inclusive o assíncrono) e 400 px.
- [ ] Ajudas novas e atualizadas; teste de cobertura passando.
- [ ] Confirmação com impacto e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (Projeto `Fabricated` automático) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ProductionOrderCompletionService` (concluir, estornar) e `SerialNumberGenerator`.
- [ ] Consumidores: Inventory (entrada `Production`, séries, vínculo de lotes, reservas) e Commercial (`Fabricated`, `ProjectFabricated`, bloqueio do manual).
- [ ] `IProjectCosts.ForOrderAsync`, `IStockTraceability`.
- [ ] Diálogo de conclusão, abas Séries, ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (cadeia Production → Inventory → Commercial → Finance), @qa.
- Gate: CA 3, 4, 8, 11 e 14 obrigatórios.

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

- **Correções feitas na validação:** quebra da PRD-06 (expedição na PRD-06b); movimento `Production` fora do custo do projeto (o custo já entrou pela baixa e pelas horas, CA 4); `ProjectFabricated` automático substituindo o manual só para projeto com OS (CA 9, 10); estorno da conclusão sem mexer na parcela liberada (CA 11, ponto 4).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PRD-06) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
