# EST-04 — Lote, série e rastreabilidade

| Campo | Valor |
|---|---|
| ID | EST-04 |
| Título | Consulta de lotes e séries e rastreabilidade nos dois sentidos: da série do produto acabado para a OS, o projeto, o cliente e os lotes de matéria-prima consumidos (com fornecedor e recebimento); do lote para as OS, séries, projetos e clientes onde foi usado; exportação do resultado |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-EST-8 (lote na matéria-prima e série no produto acabado, com rastreabilidade nos dois sentidos) · R-PRD-7 (lotes consumidos registrados) · R-PRJ-4 · R-SEC-5 · PRD §6 (Desempenho) |
| Dependências | CMP-04 (lote com fornecedor e recebimento), EST-03 (lote consumido por OS), PRD-06a (série e vínculo `serial_components`), PRD-06b (série expedida para o projeto) |
| Bloqueia | — |
| Ordem de execução | 21 (Marco 3.5 "Fechar a OS") |

## User story
Como **supervisor de qualidade**, quero **saber, a partir de uma série, de quais lotes e fornecedores veio o material, e a partir de um lote, em quais equipamentos e clientes ele foi parar**, para **responder rápido a uma reclamação ou a um recall de fornecedor**.

## Contexto e referências
- PRD §5.9 R-EST-8; §5.7 R-PRD-7; backlog EST-04: "Consulta série → lotes de MP usados; lote → séries/projetos onde foi usado".
- `03-modelo-dados` → `inventory.lots` (`item_id`, `lot_number`, `supplier_party_id`, `received_at`, `expiry`), `inventory.serials`, `stock_movements` (`lot_id`, `serial_id`, `source_type`/`source_id`, `project_id`); `inventory.serial_components` (proposta da PRD-06a).
- Fronteiras (ADR 0006): o Inventory resolve nomes de projeto, cliente, OS e fornecedor pelos Contracts dos donos (`IProjects`, `IParties`, `IProductionOrders`, `IPurchaseOrders`), por página.

## Critérios de aceite
**Série → origem**
1. **Dado** `inventory.view`, **quando** busco uma série em "Rastreabilidade" (`/app/estoque/rastreabilidade`) ou abro a série no item, **então** vejo: item, situação (Em estoque, Expedido), local, OS de origem (link), data de conclusão, projeto e cliente (link), romaneio de expedição (PRD-06b), e a árvore dos **lotes de matéria-prima consumidos** pela OS (item, lote, quantidade, fornecedor, recebimento com número e data, validade), pelo `IStockTraceability`.
2. **Dado** uma OS com quantidade > 1 e várias séries, **quando** consulto uma série, **então** a lista de lotes mostra os lotes consumidos pela OS inteira com a observação "Lotes da OS {número} ({n} unidades): a OS não separa o material por unidade." (Pontos em aberto 1).

**Lote → destino**
3. **Dado** `inventory.view`, **quando** busco um lote (item + número), **então** vejo: fornecedor, recebimentos de entrada, saldo atual por local, e cada consumo (OS, projeto, cliente, data, quantidade) com as séries produzidas por essas OS e o romaneio de cada série expedida; o total consumido + saldo + outras saídas bate com o total recebido (com a diferença explicada por ajuste ou saída avulsa, linha a linha).
4. **Dado** um lote, **quando** clico "Exportar", **então** baixo um CSV (separador `;`, UTF-8 com BOM, sem injeção de fórmula: célula que começa com `=`, `+`, `-` ou `@` recebe `'`) com o destino completo, e a exportação é auditada (quem, quando, qual lote).

**Lotes e séries do item**
5. **Dado** o item com `tracks_lot`, **quando** abro a aba "Lotes", **então** vejo cada lote com fornecedor, entrada, validade (chip alerta "Vence em {n} dias" até 30 dias, crítico "Vencido"), saldo e local; e o item com `tracks_serial` tem a aba "Séries" (PRD-06a).
6. **Dado** `inventory.edit`, **quando** corrijo a validade ou o número de um lote digitado errado (sem movimento de saída), **então** grava com motivo e auditoria; lote com saída só tem a validade corrigida (proposta).

**Desempenho, permissões e segurança**
7. **Dado** um lote consumido em 200 OS, **quando** consulto, **então** responde em menos de 1 s (consulta por índices de `lot_id` e `source_id`, nomes por página).
8. **Dado** o módulo `inventory` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `inventory.view`, 403. O nome do cliente aparece com `inventory.view` (Pontos em aberto 2); o custo do lote só com `inventory.view` e o custo do projeto nunca aparece aqui. `RequireModule` e `perm:`; IDOR em `/items/{itemId}/lots/{lotId}` e `/items/{itemId}/serials/{serialId}`.
9. **[Segurança]** **Dado** a busca por série ou lote, **quando** o termo tem caracteres de SQL ou curinga, **então** a consulta só usa parâmetros e a busca é exata ou por prefixo (até 50 resultados), nunca por `LIKE` montado com texto do usuário.

## Regras de negócio e técnicas
- **Sem tabela nova** além do `serial_components` (PRD-06a); a consulta é do Inventory, com nomes resolvidos pelos Contracts.
- **Rastreabilidade da OS, não da unidade:** o material é baixado por OS (EST-03), então a série herda os lotes da OS inteira (CA 2).
- CSV pelo utilitário de exportação seguro da MIG-01a (sem injeção de fórmula).

## Permissões, eventos e tabelas
- **Permissões:** `inventory.view` (consultar, exportar), `inventory.edit` (corrigir lote).
- **Eventos:** nenhum.
- **Contratos:** `IStockTraceability` (Inventory), `IProjects`, `IParties`, `IProductionOrders`, `IPurchaseOrders`.
- **Tabelas:** nenhuma nova (índices em `stock_movements (lot_id)` e `serial_components (lot_id)` se a medição pedir).

## Telas e estados
- **Fábrica → Estoque → Rastreabilidade** (`/app/estoque/rastreabilidade`, aba nova no `modules.ts`): busca única "Série ou lote", resultado em árvore acessível (`treegrid`, como o padrão comum 11 da Fase 1); **Item → abas Lotes e Séries**.
- **Estados:** carregando; vazio inicial ("Digite uma série ou um lote para ver de onde veio e para onde foi."); sem resultado ("Nenhuma série ou lote com '{termo}'."); erro.
- **Propositivo:** a busca reconhece se o termo é série ou lote; do resultado, links diretos para a OS, o projeto, o recebimento e o romaneio.
- **400 px:** árvore vira lista aninhada com expandir/recolher; exportar no menu.
- **Ajuda:** `Ajuda/estoque-rastreabilidade.md`, `Ajuda/estoque-item-lotes.md` (aba `lotes`); `<Dica>` em "Lotes da OS".

## Plano de testes
- **Unidade:** montagem da árvore e conciliação do lote (CA 3); proteção do CSV (CA 4).
- **Integração (Testcontainers):** dataset fixo com 2 fornecedores, 3 lotes, 2 OS, 3 séries e 1 romaneio: série → lotes e lote → séries/clientes (CA 1–3); exportação auditada (CA 4); abas do item e correção de lote (CA 5, 6); desempenho com 200 OS (CA 7); três camadas e IDOR (CA 8); busca parametrizada (CA 9).
- **E2E (Playwright):** buscar uma série, navegar até o fornecedor do lote e voltar ao cliente; tela a 400 px.

## Fora do escopo
- Recall automático (notificar clientes): fora do MVP.
- Rastreabilidade por unidade (material separado por série): fora (Pontos em aberto 1).

## Pontos em aberto
1. **Lotes por série** (PO): proposta: a série herda os lotes da OS inteira (CA 2); separar por unidade exigiria baixa por unidade na EST-03.
2. **Cliente na consulta do estoque** (PO/LGPD): proposta: mostrar o nome do cliente com `inventory.view` (é o que torna a rastreabilidade útil); cliente PF aparece com o nome, sem documento.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), desempenho e E2E passando.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px e a árvore acessível.
- [ ] Ajudas escritas; teste de cobertura passando.
- [ ] Auditoria da exportação ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `TraceabilityQuery` (série → origem, lote → destino) sobre `IStockTraceability`.
- [ ] Endpoints `/api/v1/inventory/traceability`, `/items/{id}/lots`; exportação CSV.
- [ ] Tela de rastreabilidade (`treegrid`), abas do item e ajudas.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1, 3 e 9 obrigatórios.

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

- **Correções feitas na validação:** a story foi para depois da CMP-04, da EST-03, da PRD-06a e da PRD-06b (todas as pontas da rastreabilidade); limite de rastrear por OS explícito (CA 2); CSV sem injeção de fórmula (CA 4).
- **Pré-condições para `Ready`:** nenhuma bloqueante.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Draft` até a aprovação do PO.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
