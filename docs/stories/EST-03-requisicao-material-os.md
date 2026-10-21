# EST-03 — Requisição de material para a OS e devolução

| Campo | Valor |
|---|---|
| ID | EST-03 |
| Título | Requisição de material da OS (baixa síncrona das reservas pelo motor) pelo almoxarife ou pelo supervisor, ou pedida pelo operador no `/fab` ("Pedir material") e confirmada por eles; lote sugerido; consumo acima da BOM com motivo e aprovação acima de R$ 1.000,00; custo do material na razão `commercial.project_costs` (ADR 0025); devolução pelo custo da baixa original; recomposição das saídas com projeto feitas antes desta story |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-PRD-5 (requisição da OS, baixa das reservas, pelo almoxarife ou pelo operador com confirmação) · R-EST-4 (baixa na requisição) · R-EST-6 (saída por requisição, devolução de OS) · R-EST-8 (lote consumido) · R-PRJ-5 (realizado de material) · R-PRJ-4 · R-PRP-5 · R-PRP-7 · R-SEC-5 |
| Dependências | EST-02 (reservas, `IStockReservations`; sobre o motor da EST-01a: `IStockMovements`, `StockIssued`/`StockReturned`), PRD-03a (kit chão de fábrica e tela da etapa no `/fab`), PRD-04 (`commercial.project_costs`, aba Custos da OS e cálculo novo do Projeto 360) |
| Bloqueia | PRD-06a2, EST-04 |
| Ordem de execução | 13 (Marco 3.2 "Estoque e BOM") |

## User story
Como **almoxarife**, quero **separar e baixar o material reservado para cada OS, escolhendo o lote, e atender os pedidos dos operadores**, e como **proprietário**, quero **que o custo desse material caia no projeto na hora**, para **o estoque ficar certo e o orçado × realizado de material ser real**.

## Contexto e referências
- PRD §4 ("Etapas produtivas: requisição (baixa) + apontamento de horas (custo)"); §5.7 R-PRD-5; §5.5 R-PRJ-5; objetivo central (custo real do projeto).
- **ADR 0023**: §1 (a baixa e a devolução da requisição são chamadas síncronas ao `IStockMovements`, na transação do documento; o custo do projeto é evento); §3 (trava); §4 (estorno e idempotência); §5 (regra de valor e `SoleOwnerRule`).
- **ADR 0025**: §1 (realizado = baixa de material para a OS, líquida de devolução, + horas + títulos confirmados de serviço; o título de compra de item estocável é comprometido até a baixa); §2 (`commercial.project_costs` criada na PRD-04, razão de diferenças com valor anterior e novo); §3 (recompor o que foi publicado antes do consumidor).
- **ADR 0026 §4** (o pedido de material do operador está na lista permitida da sessão por PIN).
- **Revisão pré-Fase 3** (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): D2, D3, D7, D15, tabela 1.1 itens 54 a 59, §2.2 (EST-03: o pedido do operador nunca baixa estoque; nenhum DTO do PWA traz custo), §2.3 padrões 1, 4, 5, 6, 8 e 11.
- `04-fluxos-estados` §2: `StockIssued` (Inventory) → Commercial registra custo de material do projeto; §1 OS `Cancelled` (material baixado exige devolução ou apropriação como perda).
- `03-modelo-dados` → `inventory.reservations.quantity_issued`, `stock_movements` (`type` Issue/Return, `source_type`/`source_id`, `project_id`, `production_order_id`), `commercial.project_costs` (tabela, ADR 0025 §2).
- PRD-03a: a pausa "Falta de material" continua avisando o almoxarifado por notificação; o botão "Pedir material" no `/fab` é entregue aqui (saiu da PRD-03a pela nova ordem da fase).
- Protótipo: Estoque → "Requisição para OS"; operador: "Faltas de material avisam o almoxarifado".

## Critérios de aceite
**Requisição pelo almoxarife ou pelo supervisor**
1. **Dado** `inventory.create` e uma OS com reservas `Active`, **quando** abro a aba Material da OS e clico "Requisitar material", **então** o diálogo traz as linhas da BOM com o reservado a baixar (padrão: todo o reservado), o local e, para item com lote, o **lote sugerido** com a tag "Sugerido": o de **validade mais próxima** e, sem validade ou empatado, o **mais antigo** pela entrada (ponto 56), com a opção de dividir a linha em vários lotes.
2. **Dado** a requisição preenchida, **quando** clico "Baixar material" e confirmo no diálogo com o impacto (por item: quantidade, lote, disponível antes → depois; total da requisição pelo custo médio do momento e "Entra no custo do projeto {número}" só para quem tem `projects.view_cost`), **então** na mesma transação nasce a requisição `Completed` (número `REQ-AAAA-0001`, D15), cada linha vira um `Issue` **síncrono** pelo `IStockMovements` (origem `MaterialRequisition`, `project_id` e `production_order_id` da OS, `source_id` da linha) que baixa o físico **e** o reservado da posição, a reserva soma `quantity_issued` (e fica `Consumed` quando baixa tudo), e o `StockIssued` vai para o outbox; com `Idempotency-Key`; a mensagem diz "Material baixado".
3. **Dado** uma quantidade acima do reservado da linha, ou um item fora da BOM ("Material extra"), **quando** confirmo, **então** é aceito **até o disponível** do item, com motivo obrigatório ("Consumo acima da BOM: {motivo}"; ponto 55), e a parte acima da reserva baixa o disponível; acima do disponível, 409 `insufficient_stock`. O custo vai para a OS e o projeto do mesmo jeito.
4. **[Segurança]** **Dado** uma requisição cujo consumo acima da BOM (excedente sobre a reserva + material extra, pelo custo médio do momento) passa de **R$ 1.000,00** (o mesmo parâmetro `inventory.manual_approval_amount` da EST-01a), **quando** clico "Enviar para aprovação", **então** a requisição fica `PendingApproval` **sem baixar nada**, e os usuários com `inventory.approve`, diferentes do autor, recebem a notificação "Requisição {número} da {OS} aguardando aprovação: {valor} acima da BOM." com link; o aprovador decide sobre o que foi enviado, **sem editar** (aprovar baixa pelo CA 2 com o saldo do momento; reprovar exige comentário e notifica o autor); o autor não aprova a própria (exceção do `SoleOwnerRule`, com a marca "Autoaprovado").

**Pedido do operador no `/fab` (ponto 57)**
5. **Dado** um operador com `shopfloor.create` numa etapa em andamento ou pronta da fila dele (PRD-03a), **quando** toca "Pedir material" na tela da etapa, escolhe os itens da BOM e as quantidades e confirma na folha de tela cheia, **então** nasce a requisição `Requested` com a etapa, **sem baixa**, e quem tem `inventory.create` recebe a notificação "Material pedido para a {OS}, etapa {etapa}, por {operador}." com link direto (R-PRP-5, `dedup_key` `inventory-requisition:{id}`); o PWA mostra "Pedido enviado ao almoxarifado". Depois de pausar por "Falta de material", o PWA oferece "Pedir material" como próximo passo. O endpoint está na lista permitida da sessão por PIN (`[PinSessionAllowed]`, ADR 0026 §4).
6. **[Segurança]** **Dado** o pedido do operador, **quando** chega ao servidor, **então** ele **nunca baixa estoque**: o endpoint do `shopfloor` só cria a requisição `Requested` (teste de arquitetura: nenhum caminho do `shopfloor` chama o `IStockMovements`); o servidor recusa etapa que não está pronta ou em andamento, de OS cancelada ou de outro tenant (D11); nenhum DTO do pedido traz custo, valor ou lote com custo (teste por reflexão, EST-01a CA 29).
7. **Dado** um pedido `Requested`, **quando** o almoxarife ou o supervisor (com `inventory.create`) abre pela lista "Requisições" ou pelo link, **então** ajusta lote e quantidade e confirma pelo CA 2 (com o CA 3 e o CA 4 valendo), ou clica "Recusar pedido" com motivo; o operador vê no `/fab` "Material separado" ou "Recusado: {motivo}".

**Custo no projeto (ADR 0025 §2)**
8. **Dado** um `StockIssued` ou `StockReturned` com projeto, **quando** o consumidor do Commercial processa, **então** grava na `commercial.project_costs` (criada pela PRD-04) a diferença `newAmount − último NewAmount aplicado` daquela fonte (`source` `StockIssue`/`StockReturn`, `source_id` = movimento de origem, próximo `seq`, `cost_type` Material, `occurred_on`, `is_backfill = false`); evento repetido (mesmo `seq`) não grava de novo e evento fora de ordem grava a diferença certa (teste com o estorno chegando antes do lançamento).
9. **Dado** a aba Custos do Projeto 360 com `projects.view_cost`, **quando** abro, **então** o **Realizado de Material** soma as linhas `StockIssue`/`StockReturn` da razão do projeto (pelo cálculo da ADR 0025 §1 que a PRD-04 colocou no `ProjectViews`), a legenda deixa de dizer que o material entra com a produção, e a margem projetada usa esse valor; teste com dataset fixo: BOM liberada, baixa de R$ 1.200,00 e devolução de R$ 200,00 → realizado de Material R$ 1.000,00; os movimentos `Production` e `Shipment` nunca entram (ADR 0025 §1).
10. **Dado** a aba Custos da OS (criada na PRD-04) com `projects.view_cost`, **quando** abro, **então** vejo a linha Material com o custo baixado por item e o total, lido do `IStockMovements.CostByOrderAsync` (EST-01a CA 27), ao lado da mão de obra.
11. **Dado** o deploy desta story com saídas com projeto já lançadas (saídas avulsas da EST-01a e qualquer `Issue`/`Return` com projeto publicado antes do consumidor), **quando** o job de recomposição roda (uma vez por tenant, idempotente, recebendo o `tenantId`), **então** lê os movimentos pela fonte (`IStockMovements.ProjectMovementsAsync`) e grava na razão as diferenças que faltam com `is_backfill = true`, exibidas com a marca "Calculado na implantação"; rodar duas vezes, e com o consumidor ao vivo ao mesmo tempo, grava uma vez só (ADR 0025 §3).

**Devolução**
12. **Dado** `inventory.create` e material baixado para a OS, **quando** clico "Devolver ao estoque", escolho as linhas (item, lote, quantidade até o baixado e ainda não devolvido) e o local, e confirmo no diálogo com o impacto, **então** nasce o movimento `Return` síncrono pelo **custo da baixa original** (ponto 58), a reserva volta a crescer pelo devolvido se a BOM ainda precisa (reconciliação da EST-02; senão o saldo fica livre), e o `StockReturned` faz o Commercial gravar o valor negativo, reduzindo o realizado do projeto.
13. **Dado** uma OS `Cancelled` com material baixado (PRD-01), **quando** escolho "Devolver ao estoque", **então** segue o CA 12; quando escolho "Manter no projeto como perda", o custo continua no projeto e a OS registra a perda com motivo.

**Estorno, permissões e auditoria**
14. **[Segurança]** **Dado** uma requisição `Completed` lançada errada, **quando** clico "Estornar requisição" com motivo, **então** nascem os movimentos inversos pelo motor (pelo custo da baixa), a reserva volta pela reconciliação e o `StockIssued` do estorno (valor novo 0) tira o custo do projeto; quem estorna segue a regra da EST-01a CA 20: o autor, com `inventory.edit`, até R$ 1.000,00 e em até 24 h; acima disso, outra pessoa com `inventory.approve`; material já consumido em outro lugar → 409 `insufficient_stock`.
15. **Dado** o módulo `inventory` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; baixar, devolver e confirmar ou recusar pedido sem `inventory.create`, aprovar sem `inventory.approve`, estornar fora da regra do CA 14, pedir pelo PWA sem `shopfloor.create`: 403. `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/requisitions/{reqId}` e `/requisitions/{id}/lines/{lineId}`; lote de outro item ou local: 422.
16. **[Segurança]** **Dado** duas baixas simultâneas da mesma reserva, ou a confirmação simultânea do mesmo pedido, **quando** chegam juntas, **então** a soma baixada nunca passa do reservado + disponível (trava do motor e `xmin` da requisição e da reserva), e o custo não entra duas vezes no projeto (idempotência por perna e tipo no motor e por `seq` na razão).
17. **Dado** qualquer requisição, pedido, devolução ou estorno, **quando** abro o histórico da OS, do item ou do projeto (linha do tempo da COM-05: "Material baixado para a {OS}: {valor}", só com `projects.view_cost`), **então** vejo quem pediu, quem confirmou, quando, lote e valor.

## Regras de negócio e técnicas
- **Requisição:** `inventory.material_requisitions` (`number` REQ, `production_order_id`, `project_id`, `step_id` (pedido do operador), `status` Requested/PendingApproval/Completed/Rejected/Reversed, `requested_by`, `completed_by`, `decided_by`, `decision_comment`, `self_approved`, `reject_reason`, `over_bom_value`, `xmin`) e `material_requisition_lines` (`reservation_id`, `item_id`, `lot_id`, `location_id`, `quantity`, `over_reason`, `is_extra`, `movement_id`).
- **Síncrono (ADR 0023 §1):** baixa, devolução e estorno chamam o `IStockMovements` na transação da requisição; o custo do projeto é evento.
- **Custo:** baixa pelo custo médio do momento; devolução e estorno pelo custo da baixa original.
- **Razão do projeto:** só o Commercial grava a `commercial.project_costs`; a chave da fonte é o movimento de origem; o estorno é a mesma fonte com o próximo `seq` (valor novo 0). Nada no Commercial lê `inventory.*`.
- **Movimentos `Production` e `Shipment`** (PRD-06a2/06b) **não** entram no custo do projeto.
- **Comprometido** (título de compra de item estocável com projeto) é das CMP-03a e CMP-04 (ADR 0025 §1); esta story não muda essa coluna.
- **Sugestão de lote:** validade mais próxima, depois o mais antigo pela entrada (a escolha do lote não muda o custo, que continua médio).
- Guid v7; `xmin`; auditoria `IAuditable`; payload dos eventos só com ids, quantidades e valores.

## Permissões, eventos e tabelas
- **Permissões:** `inventory.create` (baixar, devolver, confirmar ou recusar pedido), `inventory.edit` (estornar dentro dos limites), `inventory.approve` (consumo acima da BOM acima de R$ 1.000,00, estorno acima dos limites), `shopfloor.create` (pedir pelo PWA), `projects.view_cost` (custo na OS e no projeto). Nenhum seed patch novo.
- **Eventos:** **publica** `StockIssued`, `StockReturned` (definidos na EST-01a, com valor anterior e novo); **consumidor novo no Commercial**: grava a razão `project_costs`.
- **Contratos:** `IMaterialRequisitions` em `Inventory.Contracts` (pedido do operador e consulta por OS, sem custo); `IStockMovements.ProjectMovementsAsync` (novo, para a recomposição); usa `IStockReservations` e `IProductionOrders`; nenhum contrato novo no Commercial.
- **Tabelas:** `inventory.material_requisitions`, `inventory.material_requisition_lines`; linhas novas em `commercial.project_costs`.

## Telas e estados
Padrões de tela da Fase 3: 1, 4, 5, 6, 8, 11.
- **OS → aba Material** ganha "Requisitar material" e "Devolver ao estoque"; **OS → aba Custos** ganha a linha Material; **Estoque → Requisições** (aba "Operação"; `/app/estoque/requisicoes` com o filtro padrão "Aguardando você" para pedidos dos operadores e requisições aguardando aprovação; detalhe `/app/estoque/requisicoes/$id`); **`/fab`** ganha "Pedir material" na tela da etapa, com o kit chão de fábrica da PRD-03a.
- **Verbos:** "Baixar material" / "Material baixado"; "Enviar para aprovação" / "Requisição enviada para aprovação"; "Aprovar requisição" / "Requisição aprovada"; "Pedir material" / "Pedido enviado ao almoxarifado"; "Recusar pedido" / "Pedido recusado"; "Devolver ao estoque" / "Material devolvido"; "Estornar requisição" / "Requisição estornada".
- **Estados:** carregando; vazio da lista ("Nenhum pedido de material aguardando." + "Ver OS com material reservado"); vazio do "Pedir material" no `/fab` ("Esta OS ainda não tem BOM liberada. Avise o supervisor."); erro dentro do diálogo; modo leitura.
- **Propositivo:** quantidades e lote sugeridos (CA 1); a lista "Para separar hoje" (OS com reserva e etapa `Ready`); depois de baixar, `NextSteps` "Ver custo do projeto" (com `projects.view_cost`); custo recomposto com a marca "Calculado na implantação".
- **400 px:** linhas em cartões (item, lote, quantidade, disponível); diálogo em tela cheia; o almoxarife usa no celular.
- **Ajuda:** `Ajuda/estoque-requisicoes.md`, `Ajuda/estoque-requisicao.md`, `Ajuda/fab-pedir-material.md`, atualização de `Ajuda/producao-os-material.md`, `Ajuda/producao-os-custos.md` e `Ajuda/projetos-projeto-custos.md`; `<Dica>` em "Consumo acima da BOM", "Sugerido" e "Devolver ao estoque".

## Plano de testes
- **Unidade:** sugestão de lote por validade e entrada (CA 1); divisão em lotes; excedente sobre a reserva e regra de valor (CA 3, 4); diferença da razão com valor anterior e novo (CA 8).
- **Integração (Testcontainers, obrigatória: movimenta estoque e custo):** baixa síncrona com movimento, reserva consumida e evento na mesma transação, com rollback (CA 2); excedente e material extra (CA 3); aprovação acima de R$ 1.000,00 sem edição e Proprietário único (CA 4); pedido do operador sem baixa, recusa de etapa fora da fila, confirmação e recusa (CA 5–7); consumidor do Commercial idempotente e fora de ordem (CA 8); Custos do projeto com o dataset fixo (CA 9); Custos da OS (CA 10); recomposição idempotente com o consumidor ao vivo (CA 11); devolução pelo custo original (CA 12); OS cancelada (CA 13); estorno com limites (CA 14); três camadas e IDOR (CA 15); concorrência (CA 16); linha do tempo e histórico (CA 17).
- **Arquitetura:** só o Commercial grava `commercial.project_costs`; o Inventory não referencia o Commercial; nenhum endpoint do `shopfloor` chama o `IStockMovements`; DTOs do `shopfloor` sem custo; o endpoint do pedido marcado `[PinSessionAllowed]`.
- **E2E (Playwright):** operador pede material no `/fab`, almoxarife confirma escolhendo o lote, custo no projeto, devolução de parte; tela a 400 px.

## Fora do escopo
- Leitura de código de barras/QR na separação: fora do MVP.
- Custo de mão de obra e a razão `project_costs`: PRD-04. Custo de não conformidade: PRD-05 (informativo).
- Coluna "Comprometido" das compras: CMP-03a e CMP-04.
- Requisição para venda avulsa: Fase 10.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 54–59):
1. (54) Numeração `REQ-AAAA-0001` (D15).
2. (55) Consumo acima da BOM: até o disponível, com motivo; acima de R$ 1.000,00, aprovação de outra pessoa (CA 3, 4).
3. (56) Sugestão de lote: validade mais próxima, depois o mais antigo, com a tag "Sugerido" (CA 1).
4. (57) O operador pede; o almoxarife ou o supervisor confirma e baixa; o pedido do operador nunca baixa estoque (CA 5–7).
5. (58) Custo da devolução: o da baixa original (CA 12).
6. (59) Contar duas vezes: resolvido pela D2 e pela ADR 0025 (material comprado para o projeto conta como realizado só na baixa; CA 9).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] ADR 0023 §1 e ADR 0025 §1 a §3 aplicadas.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`; job de recomposição idempotente.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px e o `/fab`.
- [ ] Ajudas novas e atualizadas no mesmo commit; teste de cobertura passando.
- [ ] Confirmação com impacto no custo do projeto e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` (requisição) atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `MaterialRequisition` (Domain) e `MaterialRequisitionService` (baixar, enviar para aprovação, aprovar, pedir, confirmar, recusar, devolver, estornar) sobre o `IStockMovements`.
- [ ] Consumidor de `StockIssued`/`StockReturned` no Commercial gravando a razão; job de recomposição por `ProjectMovementsAsync`.
- [ ] Endpoint do `shopfloor` para o pedido (`[PinSessionAllowed]`) e o botão "Pedir material" no `/fab`.
- [ ] Endpoints `/api/v1/inventory/requisitions` e `/production-orders/{id}/requisitions`; telas e ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (baixa síncrona, custo do projeto por evento, fronteira Inventory × Commercial), cybersecurity (pedido do operador e segregação), @qa.
- Gate: CA 2, 4, 6, 8, 9, 11, 12 e 16 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (ajustado: renumerados) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: §2.2) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: Marco 3.2, depois da PRD-04) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** custo do projeto por evento numa tabela do Commercial (`project_costs`), sem o Commercial ler o Inventory; dataset fixo do realizado de material; `Production` e `Shipment` fora do custo do projeto; pedido do operador como proposta, sem decidir.
- **Correções da revalidação (21/10/2026):** baixa síncrona pelo motor (ADR 0023 §1, CA 2); a razão `project_costs` é da PRD-04 e esta story grava nela com valor anterior e novo (ADR 0025 §2, CA 8); recomposição das saídas com projeto anteriores com "Calculado na implantação" (D3, CA 11); a EST-03 entrega a API e o botão "Pedir material" no `/fab`, na lista permitida do PIN (CA 5); o pedido do operador nunca baixa e o DTO não traz custo (§2.2, CA 6); consumo acima da BOM com aprovação acima de R$ 1.000,00 (CA 4); lote sugerido com a tag (CA 1); devolução pelo custo original (CA 12); Custos da OS pelo `CostByOrderAsync` (CA 10); `IProjectCosts` saiu (a PRD-06a2 lê o custo da OS pelas fontes síncronas); pontos 54–59 decididos; padrões 1, 4, 5, 6, 8 e 11.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D2, D3, D7, D15, tabela 1.1 itens 54–59) e correções da revisão pré-Fase 3 (§2.1 motor de estoque e custo real do projeto, ADR 0023 §1, ADR 0025 §1–§3; §2.2 EST-03; §2.3 padrões 1, 4, 5, 6, 8, 11); "Pedir material" no `/fab` vindo da PRD-03a; CAs renumerados; nova ordem (Marco 3.2, ordem 13) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
