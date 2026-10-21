# EST-03 — Requisição de material para a OS e devolução

| Campo | Valor |
|---|---|
| ID | EST-03 |
| Título | Requisição de material da OS (baixa das reservas) pelo almoxarife, ou pedida pelo operador e confirmada pelo almoxarife; escolha do lote; custo do material apropriado à OS e ao projeto (`StockIssued` → custo realizado de Material no Projeto 360); devolução da OS ao estoque que reverte o custo |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-PRD-5 (requisição da OS, baixa das reservas, pelo almoxarife ou pelo operador com confirmação) · R-EST-4 (baixa na requisição) · R-EST-6 (saída por requisição, devolução de OS) · R-EST-8 (lote consumido) · R-PRJ-5 (realizado de material) · R-PRJ-4 · R-PRP-7 · R-SEC-5 |
| Dependências | EST-02 (reservas, `IStockReservations`), EST-01a (`IStockMovements`, `StockIssued`/`StockReturned`), PRD-01 (`IProductionOrders`), COM-05 (aba Custos do projeto) |
| Bloqueia | PRD-06a (custo de material da OS), EST-04 (lote consumido por OS), PRD-03a (pedido de material pelo operador) |
| Ordem de execução | 13 (Marco 3.4 "Fábrica rodando") |

## User story
Como **almoxarife**, quero **separar e baixar o material reservado para cada OS, escolhendo o lote**, e como **proprietário**, quero **que o custo desse material caia no projeto na hora**, para **o estoque ficar certo e o orçado × realizado de material ser real**.

## Contexto e referências
- PRD §4 ("Etapas produtivas: requisição (baixa) + apontamento de horas (custo)"); §5.7 R-PRD-5; §5.5 R-PRJ-5; objetivo central (custo real do projeto).
- `04-fluxos-estados` §2: `StockIssued` (Inventory) → Commercial/Projects registra custo de material do projeto; §3.3 (realizado de material = movimentos de saída para OS do projeto + compras diretas ao projeto, ver CMP-03a ponto 4); §1 OS `Cancelled` (material baixado exige devolução ou apropriação como perda).
- `03-modelo-dados` → `inventory.reservations.quantity_issued`, `stock_movements` (`type` Issue/Return, `source_type`/`source_id`, `project_id`), `commercial.project_costs` (`project_id`, `cost_type`, `source` StockIssue/Labor/Purchase/Payable/Payroll, `source_id`, `amount`, `date`; "view/tabela materializada").
- COM-05 CA 8: a aba Custos diz "Material e horas entram com a produção (Fase 3)."; o realizado hoje vem só dos títulos a pagar com projeto.
- Protótipo: Estoque → "Requisição para OS"; operador: "Faltas de material avisam o almoxarifado".

## Critérios de aceite
**Requisição pelo almoxarife**
1. **Dado** `inventory.create` e uma OS com reservas `Active`, **quando** abro a aba Material da OS e clico "Requisitar material", **então** o diálogo traz as linhas da BOM com o reservado a baixar (padrão: todo o reservado), o local e, para item com lote, o **lote sugerido** (o mais antigo com saldo, pela data de entrada; validade mais próxima primeiro quando houver; Pontos em aberto 3), com a opção de dividir a linha em vários lotes.
2. **Dado** a requisição preenchida, **quando** clico "Baixar material" e confirmo no diálogo com o impacto (por item: quantidade, lote, custo médio do momento, total; total da requisição; "Entra no custo do projeto {número}" para quem tem `projects.view_cost`), **então** na mesma transação nasce a requisição `Completed` (número proposto `REQ-AAAA-0001`, Pontos em aberto 1), cada linha vira um `Issue` pelo `IStockMovements` (origem `MaterialRequisition`, `project_id` da OS, `source_id` da linha) que baixa o físico **e** o reservado, a reserva soma `quantity_issued` (e fica `Consumed` quando baixa tudo), e o `StockIssued` vai para o outbox; com `Idempotency-Key`; a mensagem diz "Material baixado".
3. **Dado** uma quantidade acima do reservado da linha, **quando** confirmo, **então** só é aceita até o disponível do item, com motivo obrigatório ("Consumo acima da BOM: {motivo}"), e a parte acima da reserva baixa o disponível (Pontos em aberto 2); acima do disponível, 409 `insufficient_stock`.
4. **Dado** um item fora da BOM, **quando** incluo na requisição ("Material extra"), **então** é aceito até o disponível, com motivo, e o custo vai para a OS e o projeto do mesmo jeito.

**Pedido do operador com confirmação**
5. **Dado** um operador com `shopfloor.create` na etapa da OS (PWA, PRD-03a), **quando** toca "Pedir material" e escolhe os itens da BOM e as quantidades, **então** nasce a requisição `Requested` (sem baixa), e quem tem `inventory.create` recebe a notificação "Material pedido para a {OS}, etapa {etapa}, por {operador}." com link (R-PRP-5); o almoxarife abre, ajusta o lote e confirma pelo CA 2, ou recusa com motivo (o operador vê "Recusado: {motivo}") (proposta, Pontos em aberto 4). Esta story entrega o endpoint e o lado do almoxarife; o botão no PWA entra na PRD-03a (teste aqui pela API).

**Custo no projeto**
6. **Dado** o `StockIssued` com projeto, **quando** o consumidor do Commercial processa, **então** grava em `commercial.project_costs` a linha `{ project_id, cost_type: Material, source: StockIssue, source_id: movementId, amount: totalCost, date }`, idempotente pela inbox e por `(source, source_id)`.
7. **Dado** a aba Custos do Projeto 360 (COM-05), **quando** abro com `projects.view_cost`, **então** o **Realizado de Material** = Σ `project_costs` de `StockIssue`/`StockReturn` do projeto + títulos a pagar com projeto e natureza Material que não sejam de origem `Purchase` de item estocável (CMP-03a ponto 4), e a legenda deixa de dizer "Material e horas entram com a produção (Fase 3)" para o material; a margem projetada do Resumo usa esse valor; teste com dataset fixo: BOM liberada, baixa de R$ 1.200,00, devolução de R$ 200,00 → realizado de Material R$ 1.000,00.
8. **Dado** a aba Material da OS e a nova aba "Custos" da OS (com `projects.view_cost`), **quando** abro, **então** vejo o custo de material baixado por item e o total da OS (a mão de obra entra na PRD-04).

**Devolução**
9. **Dado** `inventory.create` e material baixado para a OS, **quando** clico "Devolver ao estoque", escolho as linhas (item, lote, quantidade até o baixado e ainda não devolvido) e o local, e confirmo, **então** nasce o movimento `Return` (entrada pelo **custo da baixa original**, que não muda o custo médio quando é o mesmo custo — Pontos em aberto 5), a reserva volta a crescer pelo devolvido se a BOM ainda precisa (senão o saldo fica livre), e o `StockReturned` faz o Commercial gravar o custo negativo (`source: StockReturn`), reduzindo o realizado do projeto.
10. **Dado** uma OS `Cancelled` com material baixado (PRD-01 CA 11), **quando** escolho "Devolver ao estoque", **então** segue o CA 9; quando escolho "Manter no projeto como perda", o custo continua no projeto e a OS registra a perda com motivo.

**Estorno, permissões e auditoria**
11. **Dado** uma requisição `Completed` lançada errada, **quando** clico "Estornar requisição" (`inventory.edit`, com motivo; Pontos em aberto 4 da EST-01a), **então** nascem os movimentos inversos pelo motor, a reserva volta, e o `StockReturned` tira o custo do projeto; estorno sem saldo (material já consumido em outro lugar) → 409 `insufficient_stock`.
12. **Dado** o módulo `inventory` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; baixar, devolver e confirmar pedido sem `inventory.create`, estornar sem `inventory.edit`, pedir pelo PWA sem `shopfloor.create`: 403. `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/requisitions/{reqId}` e `/requisitions/{id}/lines/{lineId}`; lote de outro item ou local: 422.
13. **[Segurança]** **Dado** duas baixas simultâneas da mesma reserva, **quando** chegam juntas, **então** a soma baixada nunca passa do reservado + disponível (lock do saldo e da reserva, `xmin`), e o custo não entra duas vezes no projeto (idempotência por movimento).
14. **Dado** qualquer requisição, devolução ou estorno, **quando** abro o histórico da OS, do item ou do projeto (linha do tempo da COM-05: "Material baixado para a {OS}: {valor}", só com `projects.view_cost`), **então** vejo quem, quando, lote e valor.

## Regras de negócio e técnicas
- **Requisição** (proposta): `inventory.material_requisitions` (`number`, `production_order_id`, `project_id`, `step_id` (pedido do operador), `status` Requested/Completed/Rejected/Reversed, `requested_by`, `completed_by`, `reject_reason`, `xmin`) e `material_requisition_lines` (`reservation_id`, `item_id`, `lot_id`, `location_id`, `quantity`, `over_reason`, `movement_id`).
- **Custo** pelo custo médio do momento da baixa (EST-01a); devolução pelo custo da baixa original (Pontos em aberto 5).
- **`commercial.project_costs`** (proposta: **tabela**, alimentada por eventos; o 03 a deixava como "view/tabela materializada"): `project_id`, `cost_type`, `source` (+ `StockReturn`), `source_id` (único com `source`), `amount` (com sinal), `date`, `production_order_id`. Só o Commercial grava; nada lê `inventory.*`.
- **Movimentos `Production` e `Shipment`** (PRD-06a/06b) **não** entram no custo do projeto (o custo já entrou pela baixa e pelas horas).
- Guid v7; `xmin`; auditoria `IAuditable`; payload dos eventos só com ids, quantidades e valores.

## Permissões, eventos e tabelas
- **Permissões:** `inventory.create` (baixar, devolver, confirmar pedido), `inventory.edit` (estornar), `shopfloor.create` (pedir pelo PWA), `projects.view_cost` (custo na OS e no projeto).
- **Eventos:** **publica** `StockIssued`, `StockReturned` (já definidos na EST-01a); **consumidor novo no Commercial**: grava `project_costs`.
- **Contratos:** `IMaterialRequisitions` em `Inventory.Contracts` (pedido do operador e consulta por OS); `IProjectCosts` em `Commercial.Contracts` (leitura por projeto e por OS, para a PRD-06a e a Fase 4).
- **Tabelas:** `inventory.material_requisitions`, `inventory.material_requisition_lines` (propostas), `commercial.project_costs` (tabela).

## Telas e estados
- **OS → aba Material** ganha "Requisitar material" e "Devolver ao estoque"; **OS → aba Custos** (nova, `projects.view_cost`); **Estoque → Requisições** (`/app/estoque/requisicoes`: pedidos dos operadores aguardando, requisições do dia; detalhe `/app/estoque/requisicoes/$id`).
- **Verbos:** "Baixar material" / "Material baixado"; "Devolver ao estoque" / "Material devolvido"; "Recusar pedido" / "Pedido recusado"; "Estornar requisição" / "Requisição estornada".
- **Estados:** carregando; vazio da lista ("Nenhum pedido de material aguardando." + "Ver OS com material reservado"); erro dentro do diálogo; modo leitura.
- **Propositivo:** quantidades e lote sugeridos (CA 1); a lista "Para separar hoje" (OS cuja etapa seguinte começa hoje, a partir da Fase 4; nesta fase, OS com reserva e etapa `Ready`); depois de baixar, `NextSteps` "Ver custo do projeto" (com `projects.view_cost`).
- **400 px:** linhas em cartões (item, lote, quantidade); diálogo em tela cheia; o almoxarife usa no celular.
- **Ajuda:** `Ajuda/estoque-requisicoes.md`, `Ajuda/estoque-requisicao.md`, `Ajuda/producao-os-custos.md` (aba `custos`), atualização de `Ajuda/producao-os-material.md` e `Ajuda/projetos-projeto-custos.md`; `<Dica>` em "Consumo acima da BOM" e "Devolver ao estoque".

## Plano de testes
- **Unidade:** sugestão de lote (CA 1); divisão em lotes; cálculo do excedente sobre a reserva (CA 3).
- **Integração (Testcontainers, obrigatória: movimenta estoque e custo):** baixa com movimento, reserva consumida e evento na mesma transação, com rollback (CA 2); excedente e material extra (CA 3, 4); pedido do operador, confirmação e recusa (CA 5); consumidor do Commercial idempotente (CA 6); Custos do projeto com o dataset fixo (CA 7); devolução reverte o custo (CA 9); OS cancelada (CA 10); estorno (CA 11); três camadas e IDOR (CA 12); concorrência (CA 13); linha do tempo e histórico (CA 14).
- **Arquitetura:** só o Commercial grava `commercial.project_costs`; o Inventory não referencia o Commercial além de `Commercial.Contracts`.
- **E2E (Playwright):** liberar BOM, baixar material escolhendo lote, ver o custo no projeto, devolver parte; tela a 400 px.

## Fora do escopo
- Leitura de código de barras/QR na separação: fora do MVP.
- Custo de mão de obra: PRD-04. Custo de não conformidade: PRD-05.
- Requisição para venda avulsa: Fase 10.

## Pontos em aberto
1. **Numeração da requisição** (PO): o `04-fluxos` §4 não tem o tipo. Proposta: `REQ-AAAA-0001`.
2. **Consumo acima da BOM** (PO): proposta: aceito até o disponível, com motivo (CA 3); alternativa: exigir revisão da BOM.
3. **Sugestão de lote** (PO): proposta: o mais antigo pela entrada, validade mais próxima primeiro quando houver (PEPS só para a escolha do lote; o custo continua médio).
4. **Pedido do operador** (PO): R-PRD-5 diz "pelo almoxarife ou pelo operador com confirmação". Proposta: o operador pede e o almoxarife confirma a baixa (CA 5); alternativa: o operador baixa direto, com confirmação no próprio PWA.
5. **Custo da devolução** (contadora): proposta: pelo custo da baixa original; alternativa: pelo custo médio do momento.
6. **Contar duas vezes** (PO): depende da decisão do ponto 4 da CMP-03a (CA 7).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajudas novas e atualizadas no mesmo commit; teste de cobertura passando.
- [ ] Confirmação com impacto no custo do projeto e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` (`project_costs` como tabela, requisição) atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `MaterialRequisition` (Domain) e `MaterialRequisitionService` (baixar, pedir, confirmar, recusar, devolver, estornar) sobre o `IStockMovements`.
- [ ] `ProjectCost` no Commercial, consumidor de `StockIssued`/`StockReturned`, `IProjectCosts`; aba Custos do projeto lendo o realizado novo.
- [ ] Endpoints `/api/v1/inventory/requisitions` e `/production-orders/{id}/requisitions`; telas e ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (custo do projeto por evento, fronteira Inventory × Commercial), @qa.
- Gate: CA 2, 6, 7, 9 e 13 obrigatórios.

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

- **Correções feitas na validação:** custo do projeto por evento numa tabela do Commercial (`project_costs`), sem o Commercial ler o Inventory; dataset fixo do realizado de material (CA 7); `Production` e `Shipment` fora do custo do projeto (regras); pedido do operador como proposta (ponto 4), sem decidir.
- **Pré-condições para `Ready`:** pontos em aberto 1 e 4, e o ponto 4 da CMP-03a.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
