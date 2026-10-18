# CAD-02a — Itens, unidades e categorias

| Campo | Valor |
|---|---|
| ID | CAD-02a |
| Título | Cadastro de itens (matéria-prima, componente, produto acabado, consumo, serviço) com NCM, CEST, origem, unidade de estoque, estoque mínimo, controle por lote e série e garantia padrão; unidades e categorias |
| Tamanho | M (quebra da CAD-02: conversões de unidade e códigos do fornecedor foram para a CAD-02b) |
| Status | Draft |
| Requisitos | R-EST-1 · R-EST-8 (lote na matéria-prima e série no produto acabado: só os indicadores; a rastreabilidade é a EST-04) · R-PLT-3 (seed de unidades) · R-PRJ-7 (garantia, P-11) · R-PRP-1 · R-PRP-3 · R-SEC-5 · PRD §6 Desempenho |
| Dependências | CAD-06 (garantia padrão por tipo); Fase 0 (FND-06c, FND-07, FND-09c, FND-14) |
| Bloqueia | CAD-02b, MIG-01c, COM-01, ENG-02, EST-01, CMP-01 |
| Ordem de execução | 7 |

## User story
Como **almoxarife e engenharia**, quero **cadastrar cada item com tipo, unidade, NCM e regras de lote e série**, para **que estoque, compras, produção e nota fiscal usem o mesmo item, com os dados certos desde o primeiro movimento**.

## Contexto e referências
- PRD R-EST-1 (itens: matéria-prima, componente, produto acabado, consumo, serviço; NCM, unidade, estoque mínimo, controle por lote e/ou série), R-EST-5 (estoque negativo bloqueado, EST-01), R-EST-7 (custo médio, EST-01), R-EST-8.
- `docs/03-modelo-dados.md` → `inventory.units` (`code`: UN, KG, M, M2, M3, L, PC; `name`), `inventory.item_categories` (`name`, `parent_id`), `inventory.items` (`code`, `name`, `type` RawMaterial/Component/Finished/Consumable/Service, `category_id`, `unit_id` de estoque, `ncm`, `cest`, `origin`, `min_stock`, `tracks_lot`, `tracks_serial`, `average_cost`, `is_active`).
- `docs/06-pendencias-riscos.md` P-11 (meses de garantia padrão por tipo de item, sobrescrevível no item; o `elev_garantia` do Dolibarr entra na migração).
- Backlog CAD-02 (CA): item com movimento não pode trocar a unidade de estoque. A conversão por item está na CAD-02b.
- `ModuleCatalog`: `inventory` com `view`, `create`, `edit`, `approve` e `adjust`, **sem `delete`** (ponto em aberto 1). Grupos do seed: Almoxarifado (`view`, `create`, `edit`), Supervisão (`view`, `create`), Financeiro e Engenharia (`view`).
- Protótipo: tela Estoque (colunas Item, Local, Físico, Reservado, Disponível, Mínimo, Custo médio, Situação). Saldos são do EST-01; esta story entrega o cadastro que alimenta essas colunas.

## Critérios de aceite
**Unidades**
1. **Dado** um tenant novo, **quando** é provisionado, **então** existem as unidades UN, KG, M, M2, M3, L e PC (seed idempotente, R-PLT-3).
2. **Dado** `inventory.create`, **quando** cadastro uma unidade com código (até 6 caracteres, maiúsculas, único) e nome, **então** ela é gravada; uma unidade em uso por item não pode ser inativada (409 "A unidade está em uso por {n} itens.").

**Categorias**
3. **Dado** `inventory.create`, **quando** cadastro categorias em árvore (nome e categoria pai), **então** o nome é único entre as irmãs (409) e a árvore não aceita ciclo (400).

**Itens**
4. **Dado** `inventory.create`, **quando** cadastro um item com código (único), nome, tipo, unidade de estoque e categoria, e clico "Salvar item", **então** ele é gravado, auditado e a confirmação diz "Item salvo".
5. **Dado** um item de tipo diferente de Serviço, **quando** salvo, **então** o NCM é obrigatório com 8 dígitos ("NCM precisa ter 8 dígitos."); CEST é opcional com 7 dígitos; a origem da mercadoria é um código de 0 a 8 com a descrição oficial (padrão 0 — Nacional).
6. **Dado** o tipo Serviço, **quando** salvo, **então** NCM, estoque mínimo, lote e série não são pedidos e `tracks_lot`/`tracks_serial` ficam falsos.
7. **Dado** o tipo Matéria-prima, **quando** abro "Novo item", **então** "Controla lote" vem marcado; no tipo Produto acabado, "Controla número de série" vem marcado (R-PRP-1); posso desmarcar.
8. **Dado** um estoque mínimo, **quando** salvo, **então** ele é `numeric(18,4)` na unidade de estoque, maior ou igual a 0.
9. **Dado** um item novo de um tipo com garantia padrão em Configurações → Parâmetros (CAD-06), **quando** abro "Novo item", **então** "Garantia (meses)" vem com o padrão do tipo e posso sobrescrever no item.
10. **Dado** o custo médio, **quando** abro o item, **então** ele aparece só para leitura (`numeric(18,6)`, exibido com `Money`), mantido pelo EST-01 e pela MIG-01d; a API não aceita custo médio na criação nem na edição.
11. **Dado** um item com movimento de estoque, **quando** tento trocar a unidade de estoque ou desligar "Controla lote" ou "Controla número de série", **então** a API responde 409 "Item com movimentação não pode trocar a unidade de estoque." (ou a mensagem do indicador); a regra usa `IItemUsage`, que o EST-01 liga aos movimentos reais; nesta story, o teste usa um provedor de uso de teste.
12. **Dado** um código já usado, **quando** salvo, **então** a API responde 409 "Já existe o item {código}."; o índice único no banco garante também em gravação simultânea.
13. **Dado** `inventory.edit`, **quando** inativo um item, **então** ele some dos seletores de novos documentos e continua visível nos documentos que o usam e na lista com o filtro "Inativos"; item com saldo diferente de zero não pode ser inativado (regra ligada pelo EST-01, testada com provedor de teste).
14. **Dado** um item existente, **quando** clico "Duplicar item", **então** abre "Novo item" com todos os dados copiados, menos código e custo médio (R-PRP-3).

**Lista e busca**
15. **Dado** `inventory.view`, **quando** abro Estoque → Itens, **então** vejo a DataTable server-side com código, nome, tipo, categoria, unidade, NCM, lote/série (ícones com rótulo para leitor de tela), mínimo e situação, com filtros por tipo, categoria e situação e busca por código ou nome; com 50 mil itens, a primeira página responde em menos de 1 s (PRD §6).

**Contrato**
16. **Dado** Commercial, Engineering, Purchasing, Production ou Fiscal, **quando** precisam de itens, **então** usam `IItems` de `Inventory.Contracts` (resumo com unidade, tipo, NCM, indicadores de lote e série, garantia; busca por tipo e termo; só ativos nos seletores).

**Permissões (3 camadas)**
17. **Dado** o módulo `inventory` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released`; sem grupo que habilite `inventory`, 403 `forbidden`; com `inventory.view` sem `create`/`edit`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`.

**Auditoria**
18. **Dado** qualquer alteração de unidade, categoria ou item, **quando** abro "Histórico", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Tipos** gravados como texto: `RawMaterial`, `Component`, `Finished`, `Consumable`, `Service`; rótulos: Matéria-prima, Componente, Produto acabado, Material de consumo, Serviço.
- **Quantidades** `numeric(18,4)`; **custo unitário** `numeric(18,6)` (CLAUDE.md).
- **NCM** guardado só com dígitos e exibido `9999.99.99`; nesta story só formato (validação contra a tabela oficial no ponto em aberto 3).
- **Garantia:** `warranty_months` (**proposta**, não está no 03), inteiro ≥ 0, nulo quando o tipo não tem garantia.
- **Código do item:** digitado (proposta); geração automática depende da CAD-06, ponto em aberto 9.
- **Busca:** `pg_trgm` em `code` e `name`.
- **Inativação** com `is_active` (já no 03). Sem exclusão nesta story (ponto em aberto 1).
- **`IItemUsage`** (Inventory.Application): "tem movimento?" e "tem saldo?"; nasce com implementação que responde "não" e o EST-01 troca pela consulta real.
- **Ids:** Guid v7. **Auditoria:** `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `inventory.view`, `inventory.create`, `inventory.edit`. Nenhuma ação nova.
- **Eventos:** nenhum nesta story.
- **Tabelas:** `inventory.units` (+ `is_active`, **proposta**), `inventory.item_categories`, `inventory.items` (+ `warranty_months`, **proposta**).

## Telas e estados
- **Estoque → Itens** (`/app/estoque/itens`): lista com "Novo item"; detalhe (`/app/estoque/itens/$id`) com abas Dados, Conversões e Fornecedores (as duas últimas da CAD-02b) e Histórico.
- **Estoque → Unidades** e **Estoque → Categorias** (`/app/estoque/unidades`, `/app/estoque/categorias`): listas simples com edição em painel.
- **Estados:** carregando (skeleton); vazio ("Cadastre o primeiro item: matéria-prima, componente ou produto acabado." + "Novo item"); erro com motivo e "Tentar de novo".
- **Propositivo:** indicadores de lote e série e garantia sugeridos pelo tipo (CA 7, 9); "Duplicar item" (CA 14); depois de salvar um item de compra, oferece "Cadastrar conversão" e "Vincular código do fornecedor" (CAD-02b).
- **Ajuda:** `Ajuda/estoque-itens.md` (lista, `padrao: true`), `Ajuda/estoque-item.md` (abas `dados`, `historico`), `Ajuda/estoque-unidades.md`, `Ajuda/estoque-categorias.md`; `<Dica>` em "NCM", "CEST", "Origem", "Controla lote" e "Controla número de série".

## Plano de testes
- **Unidade:** regras por tipo (CA 5–7); NCM e CEST (CA 5); validação de árvore de categorias (CA 3).
- **Integração (Testcontainers):** seed de unidades (CA 1); unidade em uso (CA 2); item com e sem garantia padrão (CA 9); custo médio fora da API de escrita (CA 10); unidade e lote travados com provedor de uso de teste (CA 11); código único concorrente (CA 12); inativação (CA 13); lista com 50 mil itens abaixo de 1 s (CA 15); `IItems` (CA 16); três camadas (CA 17); auditoria (CA 18).
- **Arquitetura:** só `Inventory.Contracts` é referenciado por outros módulos.
- **E2E (Playwright):** cadastrar matéria-prima com lote e produto acabado com série; duplicar item.

## Fora do escopo
- Conversões de unidade e códigos do fornecedor: CAD-02b.
- Saldos, movimentos, custo médio, reservas e estoque negativo: EST-01 e EST-02.
- Lotes, séries e rastreabilidade: EST-04.
- Preço de venda e tabela de preços (o PRD não prevê preço no item).
- Imagem do item.

## Pontos em aberto
1. **Excluir × inativar** (pré-condição): `inventory` não tem `delete`. Proposta: só inativar, com `inventory.edit`. Alternativa: incluir `delete` no catálogo (ADR 0003).
2. **Combinações de lote e série:** permitir em qualquer tipo menos Serviço (proposta) ou só lote em matéria-prima/componente/consumo e série em produto acabado/componente.
3. **Validação do NCM** contra a tabela oficial (TIPI/Siscomex): só formato (proposta) ou lista oficial importada e atualizada.
4. **NCM obrigatório** para Material de consumo, que talvez nunca saia em nota.
5. **Código do item:** digitado (proposta) ou gerado por sequência (`ITM-00001`), ver CAD-06 ponto 9.
6. **Itens fabricados por projeto (elevador sob encomenda):** cadastrar um item genérico por modelo (proposta) ou um item por projeto. Afeta COM-01 e PRD-06.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (schema `inventory`, `pg_trgm`, não destrutiva) e seed de unidades.
- [ ] Permissões usadas só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda de cada tela e aba escrita; teste de cobertura passando.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (`warranty_months`, `units.is_active`); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `Unit`, `ItemCategory`, `Item` com as regras por tipo.
- [ ] `IInventoryDb`, configurações `internal`, migration, seed de unidades.
- [ ] `IItemUsage` (implementação inicial) e `IItems` em `Inventory.Contracts`.
- [ ] Endpoints `/api/v1/inventory/items`, `/units`, `/categories` com `RequireModule("inventory")`.
- [ ] Telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 11 (unidade travada com movimento) e CA 15 (desempenho) obrigatórios.

## Validação do PO
Checklist do @po (Pax), 18/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** Ponto em aberto 1 (excluir × inativar, sem `delete` em `inventory`).
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-02) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
