# EST-01a — Saldos, movimentos e custo médio

| Campo | Valor |
|---|---|
| ID | EST-01a |
| Título | Razão de estoque: saldo por item, local e lote; movimentos imutáveis de saldo inicial, entrada manual, saída avulsa e transferência, com estorno por movimento inverso; custo médio ponderado móvel global por item; estoque negativo bloqueado; contrato `IStockMovements` para requisição, recebimento, produção e expedição |
| Tamanho | G (quebra da EST-01: o ajuste com aprovação foi para a EST-01b) |
| Status | Draft |
| Requisitos | R-EST-4 (saldo físico; o reservado entra na EST-02) · R-EST-5 (negativo bloqueado) · R-EST-6 (entrada, saída, transferência, devolução: o motor) · R-EST-7 (custo médio ponderado móvel) · R-EST-2 · R-EST-3 (quantidade na unidade de estoque) · R-EST-8 (lote no saldo) · R-COM-4 · R-PRP-7 · R-SEC-5 |
| Dependências | CAD-02a (itens, `average_cost` só leitura, `tracks_lot`/`tracks_serial`), CAD-02b (`IUnitConversion`), CAD-03 (locais), CAD-06 (numeração), FND-09e (idempotência, `xmin`, diálogo com impacto) |
| Bloqueia | EST-01b, EST-02, EST-03, EST-05, CMP-04, PRD-06a, PRD-06b |
| Ordem de execução | 2 (Marco 3.1 "Estoque confiável"; primeira story que movimenta estoque) |

## User story
Como **almoxarife**, quero **registrar o saldo inicial, as entradas, as saídas e as transferências de cada item, com o custo de cada movimento**, e como **proprietário**, quero **que o custo médio seja calculado sozinho e que nenhum movimento seja apagado**, para **o estoque refletir o que existe e o custo do material usado nos projetos ser real**.

## Contexto e referências
- PRD §5.9 R-EST-4 a R-EST-8; §1.1 problema 4 ("estoque não reflete o comprometido"); objetivo central: custo real do projeto.
- `04-fluxos-estados` §3.1 (custo médio ponderado global entre locais; saídas usam o custo do momento; o movimento grava o custo, imutável; produto acabado pelo custo da OS); §2 (`StockIssued`, `GoodsReceived`, `ProductionOrderCompleted`, `InvoiceAuthorized` consomem este motor).
- `03-modelo-dados` → `inventory.stock_balances` (`item_id`, `location_id`, `lot_id`, `quantity_on_hand`, `quantity_reserved`), `inventory.stock_movements` (`number`, `type` com `OpeningBalance`, `item_id`, `location_id`, `lot_id`, `serial_id`, `quantity` com sinal, `unit_cost`, `total_cost`, `source_type`, `source_id`, `project_id`, `at`, `user_id`; imutável), `inventory.lots`, `inventory.items.average_cost`.
- **ADR 0016** ("Data de corte e `OpeningBalance`": tipo próprio de movimento "Saldo inicial", posição no fim do dia D, gravado em D 23:59:59 America/Sao_Paulo em UTC; MIG-01d cancelada, ADR 0017).
- `06-pendencias-riscos`: **P-01 aberta** (crédito de imposto no custo médio, ver Pontos em aberto 2); **P-16 decidida** (frete e IPI entram no custo de entrada, rateio proporcional ao valor dos itens: aplicado no recebimento, CMP-04).
- ADR 0019 §2 (`xmin`, transição pela entidade rastreada; SQL cru só com `IAuditWriter`), §3 (`Idempotency-Key`), §9 (arredondamento só no total da linha).
- `ModuleCatalog`: `inventory` (`view`, `create`, `edit`, `approve` = "ajuste de inventário", `delete`; específica `adjust`). No seed: Almoxarifado `view/create/edit`, Supervisão `view/create`, Financeiro/Engenharia `view`.
- Protótipo: tela Estoque (Item, Local, Físico, Reservado, Disponível, Mínimo, Custo médio, Situação) e "Disponível é o físico menos o que já está reservado para OS abertas".

## Critérios de aceite
**Saldo inicial (`OpeningBalance`)**
1. **Dado** `inventory.create` e um item ativo sem nenhum movimento no local, **quando** clico "Lançar saldo inicial", escolho a data de corte (≤ hoje), o local, a quantidade (> 0, na unidade de estoque; ou em outra unidade convertida por `IUnitConversion`, mostrando "= {qtd} {un. estoque}"), o custo unitário (`numeric(18,6)`, > 0) e, se o item controla lote, o número do lote, e confirmo no diálogo com o impacto ("Saldo {antes} → {depois} · Custo médio {antes} → {depois}"), **então** nasce o movimento `OpeningBalance` em D 23:59:59 (America/Sao_Paulo, gravado em UTC, ADR 0016), o saldo e o custo médio são atualizados na mesma transação e a mensagem diz "Saldo inicial lançado".
2. **Dado** um item que já tem movimento no local, **quando** tento lançar saldo inicial nele, **então** 409 "Este item já tem movimentos neste local. Use um ajuste de estoque." (com o `recovery` "Lançar ajuste" da EST-01b).
3. **Dado** a tela "Saldo inicial em lote" (propositivo, R-PRP-3), **quando** colo ou digito várias linhas (item, local, quantidade, custo, lote) e confirmo, **então** todas entram numa transação só com um `Idempotency-Key`; uma linha inválida devolve 422 com o caminho (`lines[3].unitCost`) e nada é gravado.

**Entrada manual, saída avulsa e transferência**
4. **Dado** `inventory.create`, **quando** lanço uma **entrada manual** (tipo `Receipt`, origem `Manual`) com item, local, quantidade, custo unitário, lote (obrigatório quando o item controla lote), motivo (obrigatório, até 200) e projeto opcional, **então** o saldo sobe e o custo médio é recalculado pela fórmula do `04-fluxos` §3.1 (CA 9). Entrada com pedido de compra só pela CMP-04.
5. **Dado** `inventory.create`, **quando** lanço uma **saída avulsa** (tipo `Issue`, origem `Manual`: consumo interno, amostra, perda) com item, local, lote (quando controla), quantidade, motivo obrigatório e projeto opcional, **então** o saldo desce, o movimento grava o custo médio do momento (`unit_cost`) e `total_cost` = quantidade × custo, arredondado em 2 casas `AwayFromZero` (ADR 0019 §9); com projeto, publica `StockIssued` (CA 14). A saída para OS é só pela requisição (EST-03).
6. **Dado** `inventory.create`, **quando** lanço uma **transferência** de um local para outro (item, lote, quantidade), **então** nascem dois movimentos `Transfer` (saída no de origem e entrada no de destino) com o mesmo número e o custo médio do momento; o custo médio do item não muda (é global, `04-fluxos` §3.1).

**Estoque negativo e concorrência**
7. **[Segurança]** **Dado** um saldo físico de 5 no local e lote, **quando** tento tirar 6 (saída, transferência ou estorno), **então** 409 `insufficient_stock` "Saldo insuficiente: há 5,0000 {un} de {item} em {local}." e nada é gravado (R-EST-5); a regra vale também contra o reservado a partir da EST-02 (saída avulsa não consome o que está reservado para OS).
8. **[Segurança]** **Dado** duas saídas simultâneas da última unidade, **quando** chegam juntas, **então** só uma grava: a linha de `stock_balances` é travada na transação (`SELECT … FOR UPDATE`) e o banco tem `CHECK (quantity_on_hand >= 0)`, `CHECK (quantity_reserved >= 0)` e `CHECK (quantity_reserved <= quantity_on_hand)`; a outra recebe 409 `insufficient_stock` (teste de integração com 20 saídas paralelas).

**Custo médio**
9. **Dado** a fórmula `novo = (saldo_qtd × custo_atual + qtd_entrada × custo_entrada) ÷ (saldo_qtd + qtd_entrada)`, global entre locais, **quando** entram movimentos, **então** `items.average_cost` (`numeric(18,6)`) bate com o cálculo de referência num dataset fixo de 30 movimentos (entradas, saídas, transferências e estornos), arredondando o custo médio em 6 casas `AwayFromZero` só ao gravar.
10. **Dado** uma entrada quando o saldo total do item é zero, **quando** grava, **então** o custo médio passa a ser o custo da entrada; uma saída nunca muda o custo médio.

**Estorno (movimento nunca é apagado)**
11. **Dado** `inventory.edit` (ver Pontos em aberto 4) e um movimento manual não estornado, **quando** clico "Estornar movimento", informo o motivo e confirmo no diálogo `destructive` com o impacto, **então** nasce o movimento inverso (mesmo tipo, quantidade com sinal oposto, mesmo custo unitário do original, `reverses_movement_id`), o original fica marcado "Estornado em {data} por {usuário}", e o custo médio é recalculado tirando a entrada (`(saldo × custo − qtd × custo_entrada) ÷ (saldo − qtd)`; com o saldo final zero, o custo médio fica o último calculado).
12. **[Segurança]** **Dado** um movimento, **quando** alguém tenta `PUT`/`DELETE` nele, ou um estorno de estorno, ou um estorno que deixaria saldo negativo, **então** 405/409 conforme o caso ("Movimento de estoque não muda nem é apagado; estorne-o." / "Este movimento já foi estornado." / `insufficient_stock`); um teste de arquitetura reprova `Update`/`Remove` de `StockMovement` fora do construtor, e um trigger no banco recusa UPDATE das colunas de quantidade e custo e DELETE em `stock_movements`.
13. **Dado** movimentos gerados por outro documento (requisição, recebimento, produção, expedição, ajuste), **quando** tento estornar pela tela de movimentos, **então** 409 "Estorne pelo documento de origem ({tipo} {número})." com o link; o estorno desses é feito pelo documento dono, pelo mesmo motor.

**Contrato para os outros módulos e custo do projeto**
14. **Dado** o `IStockMovements` em `Inventory.Contracts` (novo), **quando** outro módulo chama `PostAsync(StockPosting)` (tipo, item, local, lote, série, quantidade com sinal, custo unitário para entradas, `source_type`/`source_id`, projeto) na transação dele, **então** o movimento, o saldo e o custo médio gravam juntos, idempotente por `(source_type, source_id, linha)` (a segunda chamada devolve o mesmo movimento), e um `Issue`/`Return` com projeto publica no outbox `StockIssued`/`StockReturned` (`{ movementId, projectId, productionOrderId?, itemId, quantity, totalCost, at }`, sem dado pessoal) — a reação do Commercial (custo do projeto) entra na EST-03.
15. **Dado** `IStockBalances` em `Inventory.Contracts` (novo), **quando** chamado com uma lista de itens (e local opcional), **então** devolve físico, reservado e disponível por item e local, em lote (uma consulta), para a ENG-02, a EST-02, a CMP-01 e a EST-06.

**Telas, permissões e auditoria**
16. **Dado** `inventory.view`, **quando** abro "Saldos", **então** vejo por item e local: físico, reservado (0 até a EST-02), disponível, mínimo, custo médio (só com `inventory.view`, ver Pontos em aberto 5) e o chip de situação ("Normal", alerta "Abaixo do mínimo", crítico "Sem saldo"); filtros por local, categoria, tipo e situação na URL; 50 mil linhas de saldo em menos de 1 s (PRD §6).
17. **Dado** "Movimentos", **quando** filtro por item, local, tipo, período, projeto ou documento de origem, **então** vejo número, data, tipo, item, local, lote, quantidade com sinal, custo unitário, total, origem (com link) e quem lançou; o item mostra a aba "Movimentos" com o saldo acumulado linha a linha (kardex), conferido com o dataset do CA 9.
18. **Dado** o módulo `inventory` não liberado, **quando** chamo qualquer endpoint, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `inventory.create`, os botões de lançamento somem e a API responde 403. `RequireModule` e `perm:` em todo endpoint; todo comando que cria movimento exige `Idempotency-Key` (`.RequireIdempotencyKey()`, ADR 0019 §3).
19. **[Segurança]** **Dado** ids de item, local, lote ou projeto no corpo, **quando** não existem, estão inativos ou são de outro dono (lote de outro item), **então** 422 no campo; o projeto é validado por `IProjects.FindAsync` e precisa estar em andamento (não `Closed`/`Cancelled`); IDOR em `/items/{itemId}/lots/{lotId}`.
20. **Dado** qualquer movimento ou estorno, **quando** consulto a auditoria, **então** vejo quem, quando e o quê; o saldo e o custo médio mudam só pelo motor (teste: nenhuma outra classe escreve em `stock_balances` nem em `average_cost`).

## Regras de negócio e técnicas
- **Unidade:** toda quantidade gravada está na unidade de estoque do item (`numeric(18,4)`); a conversão acontece na borda pelo `IUnitConversion` (sem conversão cadastrada, 422 claro, nunca fator 1, CAD-02b).
- **Custo:** `unit_cost` `numeric(18,6)`; `total_cost` `numeric(18,2)`, arredondado só na linha; custo médio global por item (não por local); entrada usa o custo informado; saída, transferência e estorno de saída usam o custo médio do momento; estorno de entrada usa o custo da entrada original.
- **Saldo:** `stock_balances` por `(item_id, location_id, lot_id)` (lote nulo quando o item não controla lote), com índice único e as `CHECK`s do CA 8; disponível = físico − reservado (EST-02).
- **Lote:** `inventory.lots` (`item_id`, `lot_number`, `supplier_party_id`, `received_at`, `expiry`), único por `(item_id, lot_number)`; criado na primeira entrada com aquele número. Série (`serials`) entra na PRD-06a.
- **Número do movimento** (proposta, Pontos em aberto 1): tipo de documento novo `MOV` em `NumberFormats` (`MOV-AAAA-000001`), um número por lançamento (as duas pernas da transferência e as linhas de um lote de saldo inicial dividem o número).
- **Imutável:** `stock_movements` sem UPDATE/DELETE (trigger + teste de arquitetura); estorno por `reverses_movement_id` (único parcial, um estorno por movimento).
- **Datas:** `at` `timestamptz` (UTC); a data exibida é a de São Paulo; o saldo inicial em D 23:59:59 local.
- **Escrita de saldo** pela entidade rastreada com lock da linha (ADR 0019 §2); se algum caminho usar SQL cru (lote de saldo inicial), a auditoria é gravada pelo `IAuditWriter` na mesma transação.
- **Provedores de uso:** o Inventory implementa `IItemUsageProvider.HasMovementAsync/HasBalanceAsync` e `ILocationUsageProvider.HasBalanceAsync` (já declarados), que hoje respondem sempre falso; o item com movimento não troca de unidade (CAD-02a).

## Permissões, eventos e tabelas
- **Permissões:** `inventory.view` (saldos e movimentos), `inventory.create` (saldo inicial, entrada, saída avulsa, transferência), `inventory.edit` (estorno; proposta, Pontos em aberto 4). Nenhum seed patch novo nesta story.
- **Eventos (`Inventory.Contracts`):** `StockIssued` e `StockReturned` (publicados com projeto; consumidos pelo Commercial a partir da EST-03).
- **Contratos novos:** `IStockMovements` (lançar pelo motor), `IStockBalances` (consulta em lote).
- **Tabelas:** `inventory.stock_balances`, `inventory.stock_movements` (+ `reverses_movement_id`, `reason`, `origin` Manual/…, `transfer_group` — propostas), `inventory.lots`. O 03 ganha o `OpeningBalance` (ADR 0016).

## Telas e estados
- **Fábrica → Estoque → Saldos** (`/app/estoque/saldos`, nova aba padrão do `inventory` no `modules.ts`) e **Movimentos** (`/app/estoque/movimentos`, detalhe `/app/estoque/movimentos/$id`); no item, a aba "Movimentos" (kardex) antes do "Histórico de alterações".
- **Ações:** "Lançar entrada", "Lançar saída", "Transferir", "Lançar saldo inicial" e "Saldo inicial em lote", cada uma em diálogo grande (`size="lg"`) com o impacto (saldo e custo médio antes → depois) e o botão com o mesmo verbo; "Estornar movimento" com estilo `destructive`.
- **Verbos:** "Lançar entrada" / "Entrada lançada"; "Lançar saída" / "Saída lançada"; "Transferir" / "Transferência lançada"; "Estornar movimento" / "Movimento estornado".
- **Estados:** carregando (skeleton); vazio de Saldos ("Nenhum saldo ainda. Comece lançando o saldo inicial de cada local." + "Lançar saldo inicial"); vazio filtrado com "Limpar filtros"; erro; modo leitura sem `inventory.create`.
- **Propositivo:** local padrão do item (ALM) preenchido; custo da entrada manual sugerido pelo custo médio atual, com a `Tag` "Sugerido"; lote sugerido pelo último do item; unidade de digitação lembrada por item; depois de um saldo inicial, `NextSteps` "Lançar o próximo item" e "Ver saldos"; quantidade digitada em outra unidade mostra a conversão ao vivo. Todo lançamento exige confirmação (R-PRP-7).
- **400 px:** Saldos mostra item, local, disponível e situação (físico, reservado, mínimo e custo na linha expansível); diálogos em tela cheia; kardex com data, tipo, quantidade e saldo.
- **Ajuda:** `Ajuda/estoque-saldos.md`, `Ajuda/estoque-movimentos.md`, `Ajuda/estoque-movimento.md`, `Ajuda/estoque-item-movimentos.md` (aba do item); `<Dica>` em "Disponível", "Custo médio" ("Média ponderada do custo das entradas; as saídas usam o custo do momento."), "Saldo inicial" e "Estornar".

## Plano de testes
- **Unidade:** custo médio (CA 9, 10, 11) contra a planilha de referência; arredondamento; conversão de unidade na borda; regras de lote obrigatório.
- **Integração (Testcontainers, obrigatória: movimenta estoque):** saldo inicial em D 23:59:59 e bloqueio com movimento existente (CA 1, 2); lote de saldo inicial atômico e idempotente (CA 3); entrada, saída e transferência (CA 4–6); negativo (CA 7); 20 saídas paralelas da última unidade (CA 8); dataset de 30 movimentos (CA 9); estorno e bloqueios, trigger de imutabilidade (CA 11–13); `IStockMovements` idempotente por origem e eventos no outbox (CA 14); `IStockBalances` em lote (CA 15); três camadas, idempotência e validação de ids (CA 18, 19); auditoria (CA 20).
- **Arquitetura:** só o motor escreve em `stock_balances`/`average_cost`; nenhum `Update`/`Remove` de `StockMovement`; todo endpoint com `RequireModule`, `perm:` e, nos comandos, `RequireIdempotencyKey`.
- **Desempenho:** Saldos com 50 mil linhas < 1 s (CA 16); kardex de item com 10 mil movimentos paginado < 1 s.
- **E2E (Playwright):** saldo inicial, entrada, saída e estorno com o impacto no diálogo; Saldos a 400 px.

## Fora do escopo
- Ajuste de estoque com aprovação: EST-01b. Reserva e disponível: EST-02. Requisição para OS: EST-03. Inventário: EST-05.
- Entrada por pedido de compra: CMP-04; por NF-e: Fase 5. Produto acabado e série: PRD-06a.
- Custo por local, PEPS ou custo padrão: fora do MVP (o 04 fixa o custo médio global).

## Pontos em aberto
1. **Numeração do movimento** (PO): o 03 tem `stock_movements.number`, mas o `04-fluxos` §4 não tem o tipo. Proposta: tipo `MOV` (`MOV-AAAA-000001`), configurável na CAD-06.
2. **P-01 (aberta, contadora):** crédito de ICMS/IPI no custo médio depende do regime. Proposta enquanto aberta: o custo de entrada manual é o digitado; o recebimento (CMP-04) aplica a P-16 (frete e IPI no custo) e não desconta crédito. Não bloqueia esta story.
3. **Saldo inicial depois da data de corte** (PO): proposta: permitido enquanto o item não tiver outro movimento no local, com a data escolhida ≤ hoje; depois, só ajuste (EST-01b).
4. **Quem estorna** (PO/segurança): proposta `inventory.edit` para estornar movimento manual (com motivo, auditoria e notificação a quem tem `inventory.approve` quando o valor passar de R$ 1.000,00, proposta); alternativa: exigir `inventory.approve`.
5. **Custo médio visível** (PO): o custo unitário é informação de custo, mas não está no sigilo da D7 (que é de orçamento e projeto). Proposta: visível com `inventory.view`; se o PO quiser sigilo, vira ação específica (ADR 0003).
6. **Saída avulsa com projeto** (PO): proposta: permitida (ex.: material entregue direto na obra), com motivo, e entra no custo do projeto pela EST-03.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura, desempenho e E2E passando.
- [ ] Migration criada (não destrutiva, com as `CHECK`s e o trigger de imutabilidade), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro, inclusive a 400 px.
- [ ] Ajuda de cada tela e aba escrita no mesmo commit; teste de cobertura passando.
- [ ] Padrões preenchidos, confirmação com impacto e próximos passos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` (`OpeningBalance`, colunas propostas) atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `StockMovement`, `StockBalance`, `Lot` no Domain; `AverageCostCalculator` puro.
- [ ] `StockLedger` (motor único, com lock da linha de saldo) atrás de `IStockMovements`; `IStockBalances`.
- [ ] Migration com `CHECK`s, índice único do saldo, trigger de imutabilidade e índice `(item_id, at)` para o kardex.
- [ ] `NumberFormats` + `MOV` (se o PO aprovar o ponto 1).
- [ ] Endpoints `/api/v1/inventory/balances`, `/movements`, `/opening-balances`; páginas, `modules.ts`, ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (motor único e concorrência), @data-engineer (CHECKs, trigger, índices), cybersecurity (imutabilidade e concorrência), @qa.
- Gate: CA 7, 8, 9, 11, 12 e 14 obrigatórios.

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

- **Correções feitas na validação:** quebra da EST-01 (ajuste com aprovação na EST-01b); `CHECK`s e lock do saldo para a concorrência (CA 8); estorno de documento gerado só pelo documento de origem (CA 13); P-01 citada como aberta sem bloquear; numeração `MOV` e quem estorna como pontos em aberto, sem decidir.
- **Pré-condições para `Ready`:** pontos em aberto 1 e 4.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da EST-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
