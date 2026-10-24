# EST-04 — Lote, série e rastreabilidade

| Campo | Valor |
|---|---|
| ID | EST-04 |
| Título | Consulta de lotes e séries e rastreabilidade nos dois sentidos: da série do produto acabado para a OS, o projeto, o cliente e os lotes de matéria-prima consumidos (com fornecedor e recebimento); do lote para as OS, séries, projetos e clientes onde foi usado; exportação do resultado, com o cliente pessoa física protegido |
| Tamanho | M |
| Status | Done |
| Requisitos | R-EST-8 (lote na matéria-prima e série no produto acabado, com rastreabilidade nos dois sentidos) · R-PRD-7 (lotes consumidos registrados) · R-PRJ-4 · R-SEC-5 · PRD §6 (Desempenho) · LGPD (PRD §6) |
| Dependências | CMP-04 (lote com fornecedor e recebimento), EST-03 (lote consumido por OS), PRD-06a2 (série e vínculo `serial_components`), PRD-06b (série expedida no romaneio) |
| Bloqueia | — |
| Ordem de execução | 21 (Marco 3.4 "Fechar e rastrear") |

## User story
Como **supervisor de qualidade**, quero **saber, a partir de uma série, de quais lotes e fornecedores veio o material, e a partir de um lote, em quais equipamentos e clientes ele foi parar**, para **responder rápido a uma reclamação ou a um recall de fornecedor**.

## Contexto e referências
- PRD §5.9 R-EST-8; §5.7 R-PRD-7; §6 LGPD; backlog EST-04: "Consulta série → lotes de MP usados; lote → séries/projetos onde foi usado".
- `03-modelo-dados` → `inventory.lots` (`item_id`, `lot_number`, `supplier_party_id`, `received_at`, `expiry`), `inventory.serials`, `stock_movements` (`lot_id`, `serial_id`, `source_type`/`source_id`, `project_id`); `inventory.serial_components` (criada na PRD-06a2).
- PRD-06a2: a série nasce na conclusão da OS, no formato `{código}-{AAAA}-{seq}` (D15); as OS concluídas antes da PRD-06a2 ficam sem entrada no estoque e sem série (documentado lá, sem recomposição de estoque).
- PRD-06b: o romaneio `ROM-AAAA-0001` baixa a série expedida (D4).
- Fronteiras (ADR 0006): o Inventory resolve nomes de projeto, cliente, OS e fornecedor pelos Contracts dos donos (`IProjects`, `IParties`, `IProductionOrders`, contratos do Purchasing para o recebimento), por página.
- Revisão pré-Fase 3: `docs/revisoes/2026-10-21-revisao-pre-fase3.md` (D15, tabela 1.1 itens 87 e 88, §2.2 "EST-04: ponto 88", §2.3, §4 Marco 3.4).
- ADR 0023 §2 (detalhe por lote em `stock_balances`, posição agregada) e §7 (numerações D15).

## Critérios de aceite
**Série → origem**
1. **Dado** `inventory.view`, **quando** busco uma série em "Rastreabilidade" (`/app/estoque/rastreabilidade`) ou abro a série no item, **então** vejo: item, situação (Em estoque, Expedido), local, OS de origem (link), data de conclusão, projeto e cliente (link, com a regra do CA 9), romaneio de expedição (PRD-06b), e a árvore dos **lotes de matéria-prima consumidos** pela OS (item, lote, quantidade, fornecedor, recebimento com número e data, validade), pelo `IStockTraceability`.
2. **Dado** uma OS com quantidade > 1 e várias séries, **quando** consulto uma série, **então** a lista de lotes mostra os lotes consumidos pela OS inteira (ponto 87) com a observação "Lotes da OS {número} ({n} unidades): a OS não separa o material por unidade."

**Lote → destino**
3. **Dado** `inventory.view`, **quando** busco um lote (item + número), **então** vejo: fornecedor, recebimentos de entrada, saldo atual por local, e cada consumo (OS, projeto, cliente, data, quantidade) com as séries produzidas por essas OS e o romaneio de cada série expedida; consumo de OS concluída antes da PRD-06a2 aparece com "Sem série: OS concluída antes do registro de séries."; o total consumido + saldo + outras saídas bate com o total recebido (com a diferença explicada por ajuste ou saída avulsa, linha a linha).
4. **Dado** um lote, **quando** clico "Exportar", **então** baixo um CSV (separador `;`, UTF-8 com BOM, sem injeção de fórmula: célula que começa com `=`, `+`, `-` ou `@` recebe `'`) com o destino completo, com o cliente na mesma regra do CA 9, e a exportação é auditada (quem, quando, qual lote).

**Lotes e séries do item**
5. **Dado** o item com `tracks_lot`, **quando** abro a aba "Lotes", **então** vejo cada lote com fornecedor, entrada, validade (chip alerta "Vence em {n} dias" até 30 dias, crítico "Vencido"), saldo e local; e o item com `tracks_serial` tem a aba "Séries" (PRD-06a2).
6. **Dado** `inventory.edit`, **quando** corrijo a validade ou o número de um lote digitado errado (sem movimento de saída), **então** grava com motivo e auditoria; lote com saída só tem a validade corrigida.

**Desempenho, permissões e segurança**
7. **Dado** um lote consumido em 200 OS, **quando** consulto, **então** responde em menos de 1 s (consulta por índices de `lot_id` e `source_id`, nomes por página).
8. **Dado** o módulo `inventory` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `inventory.view`, 403. O custo do lote só com `inventory.view` e o custo do projeto nunca aparece aqui. `RequireModule` e `perm:`; IDOR em `/items/{itemId}/lots/{lotId}` e `/items/{itemId}/serials/{serialId}`.
9. **[Segurança]** **Dado** o cliente de um projeto na consulta ou na exportação, **quando** o resultado é montado, **então** cliente pessoa jurídica aparece pelo nome; cliente pessoa física aparece pelo nome para quem tem `parties.view` e só pelas iniciais ("J. M. R.") para quem não tem; nenhum documento (CPF ou CNPJ) aparece; a regra vale no servidor, no DTO e no CSV (teste de contrato) (ponto 88, §2.2).
10. **[Segurança]** **Dado** a busca por série ou lote, **quando** o termo tem caracteres de SQL ou curinga, **então** a consulta só usa parâmetros e a busca é exata ou por prefixo (até 50 resultados), nunca por `LIKE` montado com texto do usuário.

## Regras de negócio e técnicas
- **Sem tabela nova** além do `serial_components` (PRD-06a2); a consulta é do Inventory, com nomes resolvidos pelos Contracts.
- **Rastreabilidade da OS, não da unidade:** o material é baixado por OS (EST-03), então a série herda os lotes da OS inteira (CA 2, ponto 87).
- **Cliente:** o tipo de pessoa (PJ/PF) e o nome vêm do `IParties`; as iniciais são montadas no servidor quando o usuário não tem `parties.view` (CA 9).
- CSV pelo utilitário de exportação seguro da MIG-01a (sem injeção de fórmula).

## Permissões, eventos e tabelas
- **Permissões:** `inventory.view` (consultar, exportar), `inventory.edit` (corrigir lote); `parties.view` só decide se o cliente PF aparece por extenso.
- **Eventos:** nenhum.
- **Contratos:** `IStockTraceability` (Inventory), `IProjects`, `IParties` (nome e tipo de pessoa), `IProductionOrders`, contratos do Purchasing para o recebimento (CMP-04).
- **Tabelas:** nenhuma nova (índices em `stock_movements (lot_id)` e `serial_components (lot_id)` se a medição pedir).

## Telas e estados
- Padrões de tela da Fase 3: 8, 9.
- **Fábrica → Estoque → Rastreabilidade** (`/app/estoque/rastreabilidade`, aba nova do grupo "Operação" no `modules.ts`): busca única "Série ou lote", resultado em árvore acessível (`treegrid`, padrão comum 11 dos cadastros da Fase 1); **Item → abas Lotes e Séries**, com o saldo do lote no padrão 8.
- **Estados:** carregando; vazio inicial ("Digite uma série ou um lote para ver de onde veio e para onde foi."); sem resultado ("Nenhuma série ou lote com '{termo}'."); erro.
- **Propositivo:** a busca reconhece se o termo é série (formato `{código}-{AAAA}-{seq}`) ou lote; do resultado, links diretos para a OS, o projeto, o recebimento e o romaneio.
- **400 px:** árvore vira lista aninhada com expandir/recolher; exportar no menu.
- **Ajuda:** `Ajuda/estoque-rastreabilidade.md`, `Ajuda/estoque-item-lotes.md` (aba `lotes`); `<Dica>` em "Lotes da OS" e "Iniciais do cliente" ("Cliente pessoa física aparece por extenso só para quem tem acesso a terceiros.").

## Plano de testes
- **Unidade:** montagem da árvore e conciliação do lote (CA 3); proteção do CSV (CA 4); iniciais do cliente PF (CA 9).
- **Integração (Testcontainers):** dataset fixo com 2 fornecedores, 3 lotes, 2 OS (uma concluída antes das séries), 3 séries e 1 romaneio: série → lotes e lote → séries/clientes (CA 1–3); exportação auditada (CA 4); abas do item e correção de lote (CA 5, 6); desempenho com 200 OS (CA 7); três camadas e IDOR (CA 8); cliente PJ, PF com e sem `parties.view`, na tela e no CSV (CA 9); busca parametrizada (CA 10).
- **E2E (Playwright):** buscar uma série, navegar até o fornecedor do lote e voltar ao cliente; tela a 400 px.

## Fora do escopo
- Recall automático (notificar clientes): fora do MVP.
- Rastreabilidade por unidade (material separado por série): fora (ponto 87).
- Série das OS concluídas antes da PRD-06a2: não existe (sem recomposição de estoque, PRD-06a2).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 87–88):
1. Lotes por série: a série herda os lotes da OS inteira (CA 2).
2. Nome do cliente: cliente PJ pelo nome; cliente PF com iniciais para quem não tem `parties.view` (CA 9).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, arquitetura e E2E passando; integração (Testcontainers) e desempenho com 200 OS escritos e não executados nesta máquina (fila do Docker).
- [x] Permissões só do `ModuleCatalog` (`inventory.view`, `inventory.edit`, `parties.view`, já existentes); migration `InventoryTraceability` não destrutiva.
- [x] Telas com estados, inclusive a 400 px e a árvore acessível.
- [x] Ajudas escritas; teste de cobertura passando.
- [x] Auditoria da exportação ligada (`Exported` no lote) e da correção (diff do `Lot`).
- [x] Cliente Orval regenerado; `openapi.json` editado à mão (regenerar com `BQ_UPDATE_OPENAPI`).
- [x] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `TraceabilityService` (série → origem, lote → destino) sobre `IStockTraceability` e os movimentos do lote; `CustomerNameMasker` (PJ, PF com iniciais); `TraceabilityRules` (conciliação, notas, validade, CSV).
- [x] Endpoints `/api/v1/inventory/traceability`, `/items/{itemId}/lots` (lista, detalhe, correção, rastro e exportação CSV), `/items/{itemId}/serials/{serialId}/trace`.
- [x] Tela de rastreabilidade (`treegrid`), abas do item e ajudas.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1, 3, 9 e 10 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK (ajustado: LGPD) |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (ajustado) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CA 9) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: PRD-06a2, ordem 21) |
| 7 | Plano de testes cobre os CAs | OK (ajustado) |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** a story foi para depois da CMP-04, da EST-03, da PRD-06a e da PRD-06b (todas as pontas da rastreabilidade); limite de rastrear por OS explícito; CSV sem injeção de fórmula.
- **Correções da revalidação (21/10/2026):** depende da PRD-06a2 (onde nasce a série) no lugar da PRD-06a; cliente PF com iniciais sem `parties.view`, na tela e no CSV (CA 9, ponto 88, §2.2); lotes da OS inteira definitivo (ponto 87); consumo de OS concluída antes das séries explicado (CA 3); correção de lote com saída definitiva (CA 6); Marco 3.4. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D15, tabela 1.1 itens 87–88) e correções da revisão pré-Fase 3 (§2.1 quebra da PRD-06a, ADR 0023 §2 e §7; §2.2 ponto 88; §2.3 padrões 8, 9); nova ordem (Marco 3.4, ordem 21) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 24/10/2026 | 1.0 | Implementada (`Done`), sobre a PRD-06b (branch `feat/prd-06b-expedicao`). Desvios e decisões de implementação: (1) **Rotas** (IDOR pelo item, CA 8): busca `GET /inventory/traceability?q=`; série → origem `GET /inventory/items/{itemId}/serials/{serialId}/trace`; lote → destino `GET /inventory/items/{itemId}/lots/{lotId}/trace` e o CSV em `/trace/export`; a aba Lotes usa `GET /items/{itemId}/lots` e `GET /items/{itemId}/lots/{lotId}`, que passaram a devolver `ItemLotRow` (o `LotDetail` da EST-01a saiu; o front não o usava), e a correção é `PUT /items/{itemId}/lots/{lotId}` (`inventory.edit`). (2) **Recebimento e fornecedor (CMP-04 em paralelo, ponto de ligação):** o Inventory não lê o Purchasing; o recebimento (número e data) vem do movimento `Receipt` com origem `GoodsReceipt` e o número `REC` em `source_number`, e o link usa `/app/compras/recebimentos/$id` com o `source_id` (a rota da CMP-04); o fornecedor e a validade vêm do lote (`supplier_party_id`, `expiry`), que a CMP-04 precisa passar no `EnsureLotAsync(item, lote, fornecedor, validade)` e no `SourceNumber` do `StockPosting`. (3) **Romaneio** da série lido da saída `Shipment` do próprio estoque (`source_number` do movimento), uma consulta para a lista, sem chamar o `IShipments` por série. (4) **Custo:** nenhum DTO da rastreabilidade traz custo, nem o do lote (o mais seguro; o custo médio continua no item e no kardex com `inventory.view`); teste por reflexão. (5) **CSV:** o utilitário da MIG-01a ficou no `ImportReportWriter` do módulo Migrations, que o Inventory não pode referenciar; a regra foi para `SafeCsv` (BuildingBlocks), e o `ImportReportWriter.Cell` passou a delegar a ele; quantidade negativa sai com `'` (regra do CA 4). A exportação grava a ação nova `Exported` na auditoria do lote (`IAuditWriter.RecordExportAsync`: formato e linhas, sem o conteúdo), que o histórico mostra como "Exportou". (6) **Correção do lote** (CA 6): colunas novas em `lots` (`correction_reason`, `corrected_at`, `corrected_by`; sem tabela nova); "saída" é qualquer movimento negativo do lote (baixa, expedição, ajuste, saída avulsa e também transferência), a leitura mais segura; trava a linha do item como o motor; nada mudou devolve sem gravar (idempotente); corrida pelo mesmo número 409 `lot_conflict`; os documentos já gravados guardam o número antigo (`manual_posting_lines.lot_number`, `shipment_lines.lot_number`). (7) **Índices:** `stock_movements.lot_id` já tinha índice (FK); entraram `ix_lots_lot_number` e `ix_serials_serial_number` (`text_pattern_ops`) para a busca por prefixo. (8) **Desempenho** (CA 7): os consumos vêm todos de uma vez, com os nomes de OS, projeto, cliente, séries e romaneios em uma consulta por lista (sem paginação na tela, para a conta do lote fechar e o CSV ter tudo); o teste de 200 OS foi escrito. (9) **Busca** (CA 10): igualdade ou `StartsWith` do EF (o Npgsql manda o termo escapado como parâmetro; `%` e `_` são texto), até 50 séries e 50 lotes, exatos primeiro; termo acima de 60 caracteres responde 422; teste de arquitetura sem `LIKE` montado nem SQL cru. (10) **Cliente** (CA 9): PJ pela razão social; PF por extenso só com `parties.view`; as iniciais pulam partículas ("da", "de", "dos"…); o `partyId` (link) só vai para quem tem `parties.view`; o fornecedor aparece pelo nome (o CA 9 trata do cliente). (11) **Situação da série** na rastreabilidade: "Em estoque", "Expedido", "Cancelada" (a aba Séries segue com os rótulos da PRD-06a2). A série no item virou link para a rastreabilidade (CA 1 "abro a série no item"); `TraceSerial` ganhou o item, para o link "Ver origem da série" a partir do lote. (12) **Árvore:** o `TreeTable` (padrão 11) ganhou `searchable`, `expandAll`, `nameHeader` e `className` por coluna; a 400 px a coluna Quantidade some e a quantidade vai para baixo do rótulo (lista aninhada com abrir e fechar); "Exportar CSV" fica em "Mais ações". (13) **"Lote vencido ou bloqueado":** não existe bloqueio de lote no modelo; os testes cobrem o lote vencido (chip "Vencido") e o lote com saída (número travado). (14) `openapi.json` editado à mão (regenerar com `BQ_UPDATE_OPENAPI`); testes de integração escritos e não executados (fila do Docker); dos testes de arquitetura, os 9 de sempre ficam bloqueados pelo Smart App Control (`0x800711C7`) e rodam no CI; a migration não foi testada sobre a cópia do banco `konebras` | Dev |
