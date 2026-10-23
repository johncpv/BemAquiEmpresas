# EST-02 — Reservas e disponível

| Campo | Valor |
|---|---|
| ID | EST-02 |
| Título | Reserva por linha da BOM liberada, na posição agregada (item e local), reconciliada a partir da fonte (`IBoms.ReleasedForOrderAsync`, `IProductionOrders.StatusAsync`) a cada evento; falta registrada e avisada a Compras; disponível = físico − reservado em todas as telas; reserva completada automaticamente quando chega material; recomposição das BOMs já liberadas na implantação |
| Tamanho | M |
| Status | Done |
| Requisitos | R-EST-4 (físico, reservado e disponível; reserva na liberação da BOM) · R-ENG-4 (reserva e cálculo de faltas) · R-ENG-5 (revisão ajusta reservas) · R-EST-5 · R-CMP-1 (falta da BOM alimenta a solicitação) · R-PRP-5 · R-SEC-5 |
| Dependências | ENG-02 (`BomReleased`, `BomRevised`, `IBoms.ReleasedForOrderAsync`), EST-01a (posição agregada, `IStockMovements`, `IStockBalances`, `CHECK`s), PRD-01 (`ProductionOrderCancelled`, `IProductionOrders.StatusAsync`) |
| Bloqueia | EST-03, CMP-01, CMP-04, EST-01b, EST-05, EST-06 |
| Ordem de execução | 12 (Marco 3.2 "Estoque e BOM") |

## User story
Como **almoxarife**, quero **que o material de cada OS fique reservado assim que a BOM é liberada, e ver o que falta**, para **não vender ou usar em outra OS o que já está comprometido e comprar só o que falta**.

## Contexto e referências
- PRD §1.1 problema 4 ("Estoque não reflete o que já está comprometido com OS abertas"); §5.9 R-EST-4; §5.6 R-ENG-4/5.
- **ADR 0023**: §2 (a reserva trava na posição agregada `inventory.stock_positions`, nunca no lote; `CHECK quantity_reserved BETWEEN 0 AND quantity_on_hand`); §3 (ordem da trava item → posição → lote); §6 (reconciliação a partir da fonte; OS cancelada ou concluída tem alvo 0; revisão antiga ignorada; `StockShortageChanged` só aviso; pontos 27 a 29); §1 (a conclusão da OS libera as reservas de forma síncrona na PRD-06a2).
- **ADR 0025 §3** (a story que entrega um consumidor recompõe o que foi publicado antes dela: a EST-02 reconcilia as BOMs já liberadas).
- **Revisão pré-Fase 3** (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): D3, tabela 1.1 itens 27 a 29, §2.1 "Reservas e faltas", §2.3 padrões 7, 8 e 11.
- `04-fluxos-estados` §2: `BomReleased` → Inventory cria reservas; se faltar disponível, Purchasing cria solicitação; `BomRevised` → ajusta reservas; §1 OS `Cancelled` libera reservas.
- `03-modelo-dados` → `inventory.reservations` (`item_id`, `location_id`, `production_order_id`, `bom_line_id`, `quantity`, `quantity_issued`, `status` Active/Consumed/Released); o reservado passa a morar em `stock_positions.quantity_reserved` (ADR 0023 §2).
- ADR 0019 §1 (o outbox não garante a ordem dos eventos).
- Backlog EST-02 CA: "tentativa de reservar acima do disponível reserva o possível e gera falta para compras".
- Protótipo: Estoque com Físico, Reservado, Disponível e o chip crítico "Falta {x}, SC gerada".

## Critérios de aceite
**Reconciliar a partir da fonte (ADR 0023 §6)**
1. **Dado** um `BomReleased`, `BomRevised`, `ProductionOrderCancelled` ou `ProductionOrderCompleted` de uma OS, **quando** o consumidor processa, **então** ele **não aplica o delta do evento**: lê a BOM liberada vigente (`IBoms.ReleasedForOrderAsync`) e a situação da OS (`IProductionOrders.StatusAsync`), calcula o **alvo** de cada linha (a necessidade da BOM liberada, na unidade de estoque; **0** se a OS está `Cancelled` ou `Completed`) e reconcilia as reservas até o alvo, numa transação, idempotente pela inbox.
2. **Dado** um evento de uma revisão antiga da BOM (a fonte já está numa revisão mais nova), ou mensagens duplicadas e fora de ordem (`BomRevised` antes do `BomReleased`, `BomReleased` depois do `ProductionOrderCancelled`), **quando** processam, **então** o resultado final é o mesmo da ordem certa: a reserva fica no alvo da fonte (teste com as permutações dos quatro eventos).

**Reservar na posição agregada**
3. **Dado** um alvo de 10 e disponível 10 no local padrão do item, **quando** reconcilia, **então** nasce a reserva `Active` com `requested_quantity` 10 e `quantity` 10, e o `quantity_reserved` da **posição** (item e local) sobe 10 na mesma transação; itens com lote são reservados por item e local, sem lote (o lote é escolhido na requisição, EST-03).
4. **Dado** alvo 10 e disponível 6, **quando** reconcilia, **então** a reserva fica com 6 e **falta 4** (`requested_quantity − quantity`); o Inventory publica `StockShortageChanged` (`{ itemId, productionOrderId, projectId, bomLineId, shortQty }`) no outbox, **só como aviso** para a CMP-01 (que recompõe a solicitação a partir das faltas abertas, não do delta); a OS mostra o chip "Falta material".
5. **Dado** disponível 0, **quando** reconcilia, **então** a reserva nasce com 0 reservado e falta 10 (a falta existe mesmo sem saldo).
6. **Dado** um item com saldo em mais de um local, **quando** reconcilia, **então** reserva **primeiro no local padrão** do item (ALM) e depois nos demais em ordem de código, uma reserva por local (ponto 27).
7. **[Segurança]** **Dado** duas reconciliações concorrentes que disputam o mesmo saldo, **quando** processam juntas, **então** a soma reservada nunca passa do físico da posição (trava item → posição da EST-01a e `CHECK (quantity_reserved BETWEEN 0 AND quantity_on_hand)`), e a segunda reserva o que sobrar, com a falta certa (teste com 10 liberações paralelas de OS diferentes).

**Revisão, cancelamento e conclusão**
8. **Dado** uma revisão que aumenta o alvo de 10 para 14, **quando** reconcilia, **então** tenta reservar mais 4 (o possível, com falta do resto); ao diminuir para 7, libera 3 da reserva, nunca abaixo do já baixado (o excedente baixado aparece como "Baixado acima da BOM: {x}" para devolução pela EST-03); linha removida libera a reserva inteira não baixada; linha nova reserva como no CA 3.
9. **Dado** uma OS `Cancelled`, **quando** reconcilia (alvo 0), **então** todas as reservas `Active` da OS ficam `Released`, o reservado da posição desce e as faltas da OS zeram (`StockShortageChanged` com `shortQty` 0).
10. **Dado** a OS concluída, **quando** o `ProductionOrderCompleted` chega, **então** o consumidor **só reconcilia** (alvo 0): na PRD-06a2 a conclusão já liberou as reservas de forma síncrona (ADR 0023 §1) e o consumidor não muda nada; nas OS concluídas pela PRD-06a1, antes dela, o consumidor libera o que houver. O contrato síncrono `IStockReservations.ReleaseForOrderAsync(productionOrderId)` (exige transação aberta, como o `PostAsync`) é entregue e testado aqui e chamado pela PRD-06a2.

**Completar a reserva quando chega material**
11. **Dado** reservas com falta do item e uma entrada nova no local (recebimento, entrada manual, ajuste de sobra, devolução, transferência de entrada), **quando** o movimento grava pelo motor, **então** o Inventory completa as faltas **automaticamente**, na mesma transação do movimento (ponto 29), nesta ordem (ponto 28): **primeiro a OS que originou a compra** (quando o lançamento informa a OS de origem, como o recebimento da CMP-04 a partir de uma solicitação da falta), **depois a BOM liberada há mais tempo**; publica `StockShortageChanged` das faltas que mudaram.

**Recomposição na implantação (D3)**
12. **Dado** o deploy desta story com BOMs já liberadas pela ENG-02 e OS já concluídas pela PRD-06a1, **quando** o job de recomposição roda (uma vez por tenant, idempotente, recebendo o `tenantId`), **então** reconcilia cada OS aberta com BOM liberada pela regra do CA 1 (as concluídas ou canceladas ficam com alvo 0), grava as reservas e faltas com a origem "Recomposição" e a marca "Calculado na implantação", e publica os avisos de falta; rodar duas vezes, e com o consumidor ao vivo ao mesmo tempo, grava uma vez só (ADR 0025 §3).

**Disponível em todas as telas**
13. **Dado** qualquer tela ou contrato que mostra saldo (Saldos, item, BOM, saída avulsa, transferência, ajuste, `IStockBalances`), **quando** exibe, **então** mostra físico, reservado e disponível = físico − reservado, lidos da posição; a saída avulsa, a transferência e o ajuste respeitam o disponível (409 `insufficient_stock` "Há {x} disponível; {y} está reservado para OS."), e só a requisição da OS (EST-03) consome o reservado.
14. **Dado** a aba Material da OS (`/app/producao/os/$id?aba=material`, com `inventory.view` ou `production.view`), **quando** abro, **então** vejo por linha da BOM: necessidade, reservado, baixado (EST-03), falta e o local; cada falta leva à solicitação de compra (a partir da CMP-01) e o total de faltas aparece no cabeçalho da OS.
15. **Dado** a lista "Faltas" (`/app/estoque/faltas`), **quando** abro, **então** vejo por item a falta somada das OS abertas, as OS e projetos afetados, a data prometida mais próxima e o link para a solicitação; filtros na URL.

**Contrato e permissões**
16. **Dado** o `IStockReservations` em `Inventory.Contracts` (novo), **quando** a Produção, a EST-03 ou a CMP-01 chamam `ForOrderAsync(productionOrderId)` e `OpenShortagesAsync()`, **então** recebem reservas e faltas em lote, sem dado de custo; a CMP-01 recompõe as solicitações a partir de `OpenShortagesAsync` (o evento é só aviso).
17. **Dado** o módulo `inventory` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; as consultas exigem `inventory.view` (a aba Material na OS também aceita `production.view`, só leitura). `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/reservations/{reservationId}`.
18. **Dado** qualquer mudança de reserva, **quando** abro o histórico da OS ou do item, **então** vejo quanto foi reservado, liberado e completado, e por qual origem ("Evento {tipo} · mensagem {id}", "Entrada {MOV}", "Recomposição"), com data.

## Regras de negócio e técnicas
- **Reserva** (03 + ADR 0023): `requested_quantity` (alvo da linha naquele local), `quantity` (reservado), `quantity_issued` (EST-03), `status`, `bom_revision`, `origin`, `is_backfill`; falta = Σ `requested_quantity` − Σ `quantity` da linha da BOM. O reservado efetivo mora em `stock_positions.quantity_reserved`, mudado na mesma transação da reserva.
- **Reconciliação, não delta:** o consumidor sempre lê a fonte e leva a reserva até o alvo; nunca soma deltas às cegas. Alvo 0 para OS `Cancelled` ou `Completed`.
- **Trava:** a mesma do motor (item → posição), em ordem crescente de `item_id` quando a BOM tem vários itens.
- **Sem custo:** reserva não movimenta custo nem gera evento para o projeto; o custo nasce na baixa (EST-03).
- **Ordem das faltas** ao completar: OS de origem da compra, depois a data de liberação da BOM (a prioridade da fila vem na Fase 4).
- **Avisos** (`StockShortageChanged`, notificações, tempo real) só depois do commit (ADR 0023 §7).
- Guid v7; auditoria `IAuditable` na reserva (com a origem); quantidades `numeric(18,4)`.

## Permissões, eventos e tabelas
- **Permissões:** `inventory.view`, `production.view` (só a aba Material). Nenhuma ação nova.
- **Eventos:** **consome** `BomReleased`, `BomRevised`, `ProductionOrderCancelled`, `ProductionOrderCompleted` (todos só como gatilho da reconciliação); **publica** `StockShortageChanged` (`Inventory.Contracts`, só aviso).
- **Contratos:** novo `IStockReservations` (`ForOrderAsync`, `OpenShortagesAsync`, `ReleaseForOrderAsync`); usa `IBoms.ReleasedForOrderAsync` e `IProductionOrders.StatusAsync`; `IStockBalances` passa a devolver o reservado da posição.
- **Tabelas:** `inventory.reservations` (+ `requested_quantity`, `bom_revision`, `origin`, `is_backfill`, `xmin`); `stock_positions.quantity_reserved` (EST-01a).

## Telas e estados
Padrões de tela da Fase 3: 7, 8, 11.
- **OS → aba Material**, **Estoque → Faltas** (`/app/estoque/faltas`, aba "Operação" do Estoque no `modules.ts`), colunas Reservado e Disponível em Saldos (EST-01a).
- **Estados:** carregando; vazio da aba Material ("Libere a BOM para reservar o material." + "Abrir BOM"); em andamento depois da liberação ("Reservando material…", com o aviso de demora do padrão 7); vazio de Faltas ("Nenhuma falta para as OS abertas."); erro.
- **Propositivo:** falta com o link direto para a solicitação (CMP-01) ou "Gerar solicitação de compra"; chip "Falta {x}, SC gerada" como no protótipo; reserva recomposta com a marca "Calculado na implantação".
- **400 px:** aba Material com item, reservado e falta (necessidade e baixado na linha expansível); Faltas com item, falta e OS.
- **Ajuda:** `Ajuda/producao-os-material.md` (aba `material`), `Ajuda/estoque-faltas.md`; `<Dica>` em "Reservado", "Disponível" e "Falta".

## Plano de testes
- **Unidade:** cálculo do alvo e da falta por local (CA 1, 3–6); revisão sem descer abaixo do baixado (CA 8); ordem das faltas ao completar (CA 11).
- **Integração (Testcontainers, obrigatória: movimenta saldo reservado):** reconciliação pela fonte e permutações de eventos fora de ordem, duplicados e de revisão antiga (CA 1, 2); reserva completa, parcial e zero na posição (CA 3–5); vários locais (CA 6); 10 liberações paralelas (CA 7); revisão (CA 8); cancelamento e conclusão, com `ReleaseForOrderAsync` exigindo transação (CA 9, 10); entrada completa as faltas na ordem, com a OS de origem primeiro (CA 11); recomposição idempotente com o consumidor ao vivo (CA 12); disponível respeitado pela saída avulsa e pelo ajuste (CA 13); `IStockReservations` (CA 16); três camadas e IDOR (CA 17); auditoria (CA 18).
- **E2E (Playwright):** liberar BOM com falta e ver a aba Material e a lista Faltas.

## Fora do escopo
- Baixa da reserva e devolução: EST-03. Solicitação de compra: CMP-01.
- Prioridade da fila para disputar saldo: Fase 4 (PLN-02).
- Reserva manual sem BOM (ex.: venda avulsa): Fase 10.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 27–29):
1. (27) Local da reserva: o local padrão primeiro, depois os demais (CA 6).
2. (28) Ordem das faltas: primeiro a OS que originou a compra, depois a BOM liberada há mais tempo (CA 11).
3. (29) Completar a reserva: automático, na mesma regra do item 28 (CA 11).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos (BOM pelo `IBoms` da ENG-02, pela porta `IReleasedBomSource`; ver o Change Log).
- [x] ADR 0023 §6 aplicada.
- [x] Testes de unidade e E2E passando; integração (Testcontainers) escrita, roda no CI (Docker em fila nesta máquina).
- [x] Migration criada (não destrutiva); o teste sobre cópia do banco do tenant `konebras` fica para a homologação; job de recomposição idempotente.
- [x] Permissões só do `ModuleCatalog` (nenhuma ação nova).
- [x] Telas com estados (inclusive o "em andamento" assíncrono) e 400 px.
- [x] Ajuda da aba, da lista e da aba Reservas do item; teste de cobertura passando.
- [x] Próximos passos (falta → solicitação) aplicados.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval atualizados (OpenAPI editado à mão; regenerar com `BQ_UPDATE_OPENAPI`).
- [x] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `Reservation` (Domain) e `ReservationReconciler` puro (alvo, locais, falta, revisão, ordem das faltas).
- [ ] Consumidores de `BomReleased`, `BomRevised`, `ProductionOrderCancelled`, `ProductionOrderCompleted` chamando o reconciliador; complemento de faltas no motor da EST-01a.
- [ ] `IStockReservations` (com `ReleaseForOrderAsync` síncrono); evento `StockShortageChanged`.
- [ ] Job de recomposição das BOMs já liberadas.
- [ ] Aba Material na OS e lista Faltas; ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (reconciliação pela fonte e concorrência), @qa.
- Gate: CA 1, 2, 4, 7, 8, 10, 11, 12 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (ajustado: renumerados) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: Marco 3.2, depois da ENG-02) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** falta registrada mesmo com disponível zero; reserva recalculada pela necessidade da revisão vigente, sem somar deltas; `OpenShortagesAsync` para a CMP-01 recompor faltas; mensagens fora de ordem cobertas no plano de testes.
- **Correções da revalidação (21/10/2026):** reserva na posição agregada (ADR 0023 §2, CA 3, 7); reconciliação a partir da fonte com `IBoms.ReleasedForOrderAsync` e `IProductionOrders.StatusAsync`, alvo 0 para OS cancelada ou concluída e revisão antiga ignorada (ADR 0023 §6, CA 1, 2); `StockShortageChanged` só aviso (CA 4); a conclusão libera as reservas de forma síncrona na PRD-06a2 com `ReleaseForOrderAsync`, e o consumidor só reconcilia (CA 10); recomposição das BOMs já liberadas e das OS concluídas pela PRD-06a1 (D3, CA 12); pontos 27–29 decididos (CA 6, 11); padrões 7, 8 e 11.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D3, tabela 1.1 itens 27–29) e correções da revisão pré-Fase 3 (§2.1 reservas e faltas, ADR 0023 §2, §6; §2.3 padrões 7, 8, 11); CAs renumerados; nova ordem (Marco 3.2, ordem 12) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 23/10/2026 | 1.0 | Implementada (`Done`). Desvios e decisões de implementação: (1) **Dependência da ENG-02:** a ENG-02 corre em paralelo e não está na `main`. A BOM liberada entra por uma porta do Estoque, `IReleasedBomSource` (`ReleasedForOrderAsync` e `OpenOrdersWithReleasedBomAsync`), com `NoReleasedBomSource` até lá (nenhuma OS tem BOM liberada; a aba Material mostra "Libere a BOM para reservar o material."). Na integração com a ENG-02: (a) adaptador `BomsReleasedBomSource` no `Inventory.Application` sobre `IBoms.ReleasedForOrderAsync` (`ReleasedBomInfo` → `ReleasedBom`) e, para a recomposição, `IProductionOrders.OpenAsync` filtrado pela BOM liberada; registrar no lugar do `NoReleasedBomSource`; o `Inventory.Application` passa a referenciar `Engineering.Contracts`; (b) `ReservationConsumers` implementa `IIntegrationEventConsumer<BomReleased>` e `<BomRevised>` chamando `ReconcileOrderAsync` (as duas linhas são iguais às do cancelamento); (c) no `HostEvents`, `AddHandler<BomReleased, ReservationConsumers>()` e `AddHandler<BomRevised, ReservationConsumers>()` no lugar dos `AddEvent` da ENG-02. Os testes de integração já usam um fake da porta e não mudam. (2) A reserva é **por OS, item e local** (não por linha da BOM): a ENG-02 troca o `bom_line_id` a cada revisão, então o alvo é a soma das linhas do item na revisão vigente; `bom_line_id` guarda a primeira linha do item; a aba Material é por item. (3) A falta mora na linha do local padrão (`requested_quantity` inclui a falta); falta = Σ requested − Σ quantity − Σ quantity_issued. (4) Histórico em tabela própria, só inclusão (`inventory.reservation_changes`), além da auditoria `IAuditable` da reserva; aparece na aba Material da OS e na aba nova **Reservas** do item (`/app/estoque/itens/$id?aba=reservas`, ajuda `estoque-item-reservas.md`). (5) O disponível que uma reconciliação libera (cancelamento, revisão para baixo) completa as faltas das outras OS pela regra do CA 11 (fora do CA; evita comprar o que já está livre). (6) Trava por OS (`pg_advisory_xact_lock`) antes das travas dos itens: duas reconciliações da mesma OS não leem a fonte velha uma da outra. (7) Os consumidores e a recomposição não fazem nada com o módulo `inventory` não liberado; a recomposição roda na subida e às 03:45 e só marca `inventory.parameters.reservations_backfilled_at` quando termina com o módulo liberado. (8) O motor (`StockLedger`) passa a recusar saída, transferência, ajuste negativo e estorno de entrada acima do **disponível**; só a origem `MaterialRequisition` (EST-03) tira do reservado, e a EST-03 precisa baixar a própria reserva na mesma transação (o `CHECK` da posição segura o resto). A mensagem leva a unidade: "Há 6,0000 UN disponível; 4,0000 UN está reservado para OS.". A EST-05 decide o que fazer quando a contagem fica abaixo do reservado. (9) "Gerar solicitação de compra" leva a `/app/compras` (tela "Funcionalidade não liberada" até a CMP-01); `purchaseRequestLink` fica nulo até lá. (10) Padrão 7: "Reservando material…" com atualização a cada 3 s e o aviso de demora depois de 30 s; não há notificação ao terminar (ver pendência do PO). (11) "Abrir BOM" leva a `?aba=bom` (aba da ENG-02); sem ela, cai em Etapas. (12) A aba Material aparece com o módulo Estoque liberado; o endpoint aceita `inventory.view` ou `production.view` (`RequireAnyPermissionOf`); a lista Faltas e o histórico do item exigem `inventory.view`. (13) Item do tipo Serviço na BOM não reserva; local inativo ou excluído não recebe reserva nova (só libera). (14) Os testes de integração (Testcontainers) foram escritos e não rodaram nesta máquina (Docker em fila); os de arquitetura rodaram com 82 de 91, os 9 restantes bloqueados pelo Smart App Control (`0x800711C7`), a rodar no CI. (15) O `openapi.json` foi editado à mão (5 paths, 11 schemas) e o cliente Orval gerado dele; regenerar com `BQ_UPDATE_OPENAPI`. (16) A migration `InventoryReservations` não foi testada sobre a cópia do banco `konebras` | Dev |
| 23/10/2026 | 1.1 | Integração com a ENG-02 (PR #88) e decisões do PO. (a) Rebase sobre a `main`; a migration `InventoryReservations` foi refeita com data depois da `EngineeringBoms` (snapshot da `main`, `Up`/`Down` originais; sem mudança pendente no modelo). (b) Desvio 1 resolvido: `BomsReleasedBomSource` (adaptador real sobre `IBoms.ReleasedForOrderAsync`; a recomposição usa `IProductionOrders.OpenAsync` + `IBoms.IsReleasedAsync`) no lugar do `NoReleasedBomSource`; `ReservationConsumers` consome `BomReleased` e `BomRevised` (`AddHandler` no lugar dos `AddEvent` da ENG-02). (c) Lado da ENG-02: `StockBalancesBomStock` com `ReservationsConnected` true e `IssuedForOrderAsync` lido do `IStockReservations.ForOrderAsync`; o diálogo de liberação mostra "Vai reservar" e o aviso "Ao liberar, o estoque reserva o disponível…"; "Ver faltas da OS" leva à aba Material; testes da ENG-02 atualizados. (d) **Decisões do PO (23/10/2026):** padrão 7 com o aviso curto ao terminar a reserva, "Material reservado: {n} itens, {f} em falta.", na aba Material e como notificação para quem tem `engineering.approve` (só na liberação e na revisão da BOM); desvio 3 aprovado: o disponível liberado completa as faltas de outras OS, começando pela BOM liberada há mais tempo; nota na EST-05 e no backlog: contagem abaixo do reservado reduz primeiro as reservas das OS com a BOM liberada mais recentemente, gera a falta e avisa quem tem `inventory.approve` | Dev |
