# EST-02 — Reservas e disponível

| Campo | Valor |
|---|---|
| ID | EST-02 |
| Título | Reserva por linha da BOM liberada (consumidor de `BomReleased` e `BomRevised`), reservando o possível e registrando a falta; disponível = físico − reservado em todas as telas; liberação das reservas no cancelamento da OS; reserva completada quando chega material; falta publicada para Compras |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-EST-4 (físico, reservado e disponível; reserva na liberação da BOM) · R-ENG-4 (reserva e cálculo de faltas) · R-ENG-5 (revisão ajusta reservas) · R-EST-5 · R-CMP-1 (falta da BOM alimenta a solicitação) · R-PRP-5 · R-SEC-5 |
| Dependências | ENG-02 (`BomReleased`, `BomRevised`), EST-01a (saldo, `IStockMovements`, `IStockBalances`, `CHECK`s), PRD-01 (`ProductionOrderCancelled`, `IProductionOrders`) |
| Bloqueia | EST-03 (a requisição baixa a reserva), CMP-01 (falta da BOM), EST-06, PRD-03a (aviso de falta) |
| Ordem de execução | 7 (Marco 3.2 "Da engenharia à OS") |

## User story
Como **almoxarife**, quero **que o material de cada OS fique reservado assim que a BOM é liberada, e ver o que falta**, para **não vender ou usar em outra OS o que já está comprometido e comprar só o que falta**.

## Contexto e referências
- PRD §1.1 problema 4 ("Estoque não reflete o que já está comprometido com OS abertas"); §5.9 R-EST-4; §5.6 R-ENG-4/5.
- `04-fluxos-estados` §2: `BomReleased` → Inventory cria reservas; se faltar disponível, Purchasing cria solicitação (`BOMShortage`); `BomRevised` → ajusta reservas; §1 OS `Cancelled` libera reservas.
- `03-modelo-dados` → `inventory.reservations` (`item_id`, `location_id`, `production_order_id`, `bom_line_id`, `quantity`, `quantity_issued`, `status` Active/Consumed/Released); `stock_balances.quantity_reserved`.
- Backlog EST-02 CA: "tentativa de reservar acima do disponível reserva o possível e gera falta para compras".
- Protótipo: Estoque com Físico, Reservado, Disponível e o chip crítico "Falta {x}, SC gerada".

## Critérios de aceite
**Reservar na liberação**
1. **Dado** um `BomReleased` com uma linha de necessidade 10 e disponível 10 no local de reserva (CA 4), **quando** o consumidor processa, **então** nasce a reserva `Active` com `quantity` 10, `requested_quantity` 10 (proposta) e o `quantity_reserved` do saldo sobe 10, na mesma transação, idempotente pela inbox e por `bom_line_id`.
2. **Dado** necessidade 10 e disponível 6, **quando** processa, **então** a reserva fica com 6 e **falta 4** (`requested_quantity − quantity`), e o Inventory publica `StockShortageChanged` (`{ itemId, productionOrderId, projectId, bomLineId, shortQty }`) no outbox para a CMP-01; a OS mostra o chip "Falta material".
3. **Dado** disponível 0, **quando** processa, **então** a reserva nasce com 0 reservado e falta 10 (a falta precisa existir mesmo sem saldo).
4. **Dado** um item com saldo em mais de um local, **quando** reserva, **então** reserva primeiro no local padrão do item (ALM) e depois nos demais em ordem de código, uma reserva por local (proposta, Pontos em aberto 1); itens com lote são reservados por item e local, sem lote (o lote é escolhido na requisição, EST-03).
5. **[Segurança]** **Dado** duas liberações concorrentes que disputam o mesmo saldo, **quando** processam juntas, **então** a soma reservada nunca passa do físico (lock da linha de saldo e `CHECK (quantity_reserved <= quantity_on_hand)` da EST-01a) e a segunda reserva o que sobrar, com a falta correta (teste com 10 liberações paralelas).

**Revisão, cancelamento e conclusão**
6. **Dado** um `BomRevised` que aumenta a necessidade de 10 para 14, **quando** processa, **então** tenta reservar mais 4 (o possível, com falta do resto); ao diminuir para 7, libera 3 da reserva (nunca abaixo do já baixado: o excedente baixado aparece como "Baixado acima da BOM: {x}" para devolução pela EST-03); linha removida libera a reserva inteira não baixada; linha nova reserva como no CA 1.
7. **Dado** um `ProductionOrderCancelled`, **quando** processa, **então** todas as reservas `Active` da OS ficam `Released`, o reservado do saldo desce e as faltas da OS zeram (`StockShortageChanged` com `shortQty` 0).
8. **Dado** a OS concluída (PRD-06a) com reserva não baixada, **quando** o `ProductionOrderCompleted` chega, **então** o resto é liberado (`Released`), com o registro na aba Material. O evento nasce na PRD-06a; aqui o consumidor é entregue e testado com o evento publicado pelo próprio teste.

**Completar a reserva quando chega material**
9. **Dado** reservas com falta do item e uma entrada nova (recebimento, entrada manual, ajuste de sobra, devolução), **quando** o movimento grava, **então** o Inventory completa as faltas na ordem de liberação da BOM (mais antiga primeiro; Pontos em aberto 2), na mesma transação do movimento, e publica `StockShortageChanged` das faltas que mudaram.

**Disponível em todas as telas**
10. **Dado** qualquer tela ou contrato que mostra saldo (Saldos, item, BOM, saída avulsa, transferência, ajuste, `IStockBalances`), **quando** exibe, **então** mostra físico, reservado e disponível = físico − reservado; a saída avulsa, a transferência e o ajuste respeitam o disponível (409 `insufficient_stock` "Há {x} disponível; {y} está reservado para OS."), e só a requisição da OS (EST-03) consome o reservado.
11. **Dado** a aba Material da OS (`/app/producao/os/$id?aba=material`, com `inventory.view` ou `production.view`), **quando** abro, **então** vejo por linha da BOM: necessidade, reservado, baixado (EST-03), falta e o local; cada falta leva à solicitação de compra (a partir da CMP-01) e o total de faltas aparece no cabeçalho da OS.
12. **Dado** a lista "Faltas" (`/app/estoque/faltas`), **quando** abro, **então** vejo por item a falta somada das OS abertas, as OS e projetos afetados, a data prometida mais próxima e o link para a solicitação; filtros na URL.

**Contrato e permissões**
13. **Dado** o `IStockReservations` em `Inventory.Contracts` (novo), **quando** a Produção, a EST-03 ou a CMP-01 chamam `ForOrderAsync(productionOrderId)` e `OpenShortagesAsync()`, **então** recebem reservas e faltas em lote, sem dado de custo; a CMP-01 usa `OpenShortagesAsync` para recompor as faltas se algum evento se perder.
14. **Dado** o módulo `inventory` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; as consultas exigem `inventory.view` (a aba Material na OS também aceita `production.view`, só leitura). `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/reservations/{reservationId}`.
15. **Dado** qualquer mudança de reserva, **quando** abro o histórico da OS ou do item, **então** vejo quanto foi reservado, liberado e por qual evento (origem "Evento {tipo} · mensagem {id}"), com data.

## Regras de negócio e técnicas
- **Reserva** (03 + proposta): `requested_quantity` (necessidade da linha naquele local), `quantity` (reservado), `quantity_issued` (EST-03), `status`; falta = Σ `requested_quantity` − Σ `quantity` da linha da BOM.
- **Consumidor idempotente** pela inbox e por `(bom_line_id, revision)`; a reserva é sempre recalculada para chegar à necessidade da revisão vigente (não soma deltas às cegas).
- **Sem custo:** reserva não movimenta custo nem gera evento para o projeto; o custo nasce na baixa (EST-03).
- **Lock e CHECK** do saldo (EST-01a); o reservado nunca passa do físico; a reserva nunca é negativa.
- **Ordem das faltas** ao completar: data de liberação da BOM (proposta; a prioridade da fila vem na Fase 4).
- Guid v7; auditoria `IAuditable` na reserva (com a origem do evento); quantidades `numeric(18,4)`.

## Permissões, eventos e tabelas
- **Permissões:** `inventory.view`, `production.view` (só a aba Material). Nenhuma ação nova.
- **Eventos:** **consome** `BomReleased`, `BomRevised`, `ProductionOrderCancelled`, `ProductionOrderCompleted` (PRD-06a); **publica** `StockShortageChanged` (`Inventory.Contracts`).
- **Contratos:** novo `IStockReservations`; `IStockBalances` passa a devolver o reservado.
- **Tabelas:** `inventory.reservations` (+ `requested_quantity`, `bom_revision`, `xmin`).

## Telas e estados
- **OS → aba Material**, **Estoque → Faltas** (`/app/estoque/faltas`, aba nova no `modules.ts`), coluna Reservado e Disponível em Saldos (EST-01a).
- **Estados:** carregando; vazio da aba Material ("Libere a BOM para reservar o material." + "Abrir BOM"); em andamento depois da liberação ("Reservando material…"; depois de cerca de 30 s, "A reserva está demorando. Avisaremos quando terminar."); vazio de Faltas ("Nenhuma falta para as OS abertas."); erro.
- **Propositivo:** falta com o link direto para a solicitação (CMP-01) ou "Gerar solicitação de compra"; chip "Falta {x}, SC gerada" como no protótipo.
- **400 px:** aba Material com item, necessidade, reservado e falta; Faltas com item, falta e OS.
- **Ajuda:** `Ajuda/producao-os-material.md` (aba `material`), `Ajuda/estoque-faltas.md`; `<Dica>` em "Reservado", "Disponível" e "Falta".

## Plano de testes
- **Unidade:** cálculo de reserva e falta por local (CA 1–4); ajuste por revisão sem descer abaixo do baixado (CA 6).
- **Integração (Testcontainers, obrigatória: movimenta saldo reservado):** reserva completa, parcial e zero (CA 1–3); vários locais (CA 4); 10 liberações paralelas (CA 5); revisão (CA 6); cancelamento e conclusão (CA 7, 8); entrada completa as faltas na ordem (CA 9); disponível respeitado pela saída avulsa e pelo ajuste (CA 10); mensagens duplicadas e fora de ordem (`BomRevised` antes do `BomReleased`: grava e aplica quando a reserva existir, registro como na ADR 0019 §1); `IStockReservations` (CA 13); três camadas e IDOR (CA 14); auditoria (CA 15).
- **E2E (Playwright):** liberar BOM com falta e ver a aba Material e a lista Faltas.

## Fora do escopo
- Baixa da reserva e devolução: EST-03. Solicitação de compra: CMP-01.
- Prioridade da fila para disputar saldo: Fase 4 (PLN-02).
- Reserva manual sem BOM (ex.: venda avulsa): Fase 10.

## Pontos em aberto
1. **Local da reserva** (PO): proposta do CA 4 (local padrão primeiro, depois os demais); alternativa: só o local padrão e a falta para os outros.
2. **Quem recebe o material que chega** (PO): proposta: a OS com a BOM liberada há mais tempo (CA 9); na Fase 4, pela prioridade da fila.
3. **Reservar automaticamente ao chegar** (PO): proposta: sim (CA 9); alternativa: o almoxarife reserva à mão.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados (inclusive o "em andamento" assíncrono) e 400 px.
- [ ] Ajuda da aba e da lista; teste de cobertura passando.
- [ ] Próximos passos (falta → solicitação) aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `Reservation` (Domain) e `ReservationAllocator` puro (locais, falta, revisão).
- [ ] Consumidores de `BomReleased`, `BomRevised`, `ProductionOrderCancelled`, `ProductionOrderCompleted`; complemento de faltas no motor da EST-01a.
- [ ] `IStockReservations`; evento `StockShortageChanged`.
- [ ] Aba Material na OS e lista Faltas; ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (consumidores fora de ordem e concorrência), @qa.
- Gate: CA 2, 5, 6, 9 e 10 obrigatórios.

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

- **Correções feitas na validação:** falta registrada mesmo com disponível zero (CA 3); reserva recalculada pela necessidade da revisão vigente, sem somar deltas (regras); `OpenShortagesAsync` para a CMP-01 recompor faltas de eventos publicados antes de existir o consumidor (CA 13); mensagens fora de ordem cobertas no plano de testes.
- **Pré-condições para `Ready`:** pontos em aberto 1 e 3.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
