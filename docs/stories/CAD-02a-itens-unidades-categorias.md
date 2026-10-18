# CAD-02a — Itens, unidades, categorias e garantia padrão

| Campo | Valor |
|---|---|
| ID | CAD-02a |
| Título | Cadastro de itens (matéria-prima, componente, produto acabado, consumo, serviço) com NCM, CEST, origem, unidade de estoque, estoque mínimo, lote e série e garantia; unidades, categorias em árvore e garantia padrão por tipo no Inventory |
| Tamanho | M (quebra da CAD-02: conversões e códigos do fornecedor foram para a CAD-02b) |
| Status | Ready |
| Requisitos | R-EST-1 · R-EST-8 (indicadores de lote e série; a rastreabilidade é a EST-04) · R-PLT-3 (seed de unidades) · R-PRJ-7 (garantia, P-11) · R-PRP-1 · R-PRP-3 · R-SEC-5 · PRD §6 Desempenho |
| Dependências | CAD-03 (schema `inventory` e `inventory.delete`), FND-09d (moldes, `NcmInput`, `QuantityInput`, `TreeTable`); Fase 0 (FND-06c, FND-07, FND-09c, FND-14) |
| Bloqueia | CAD-02b, MIG-01c, COM-01, ENG-02, EST-01, CMP-01 |
| Ordem de execução | 8 (Marco B — catálogo real) |

## User story
Como **almoxarife e engenharia**, quero **cadastrar cada item com tipo, unidade, NCM, regras de lote e série e garantia**, para **que estoque, compras, produção e nota fiscal usem o mesmo item, com os dados certos desde o primeiro movimento**.

## Contexto e referências
- PRD R-EST-1 (tipos, NCM, unidade, estoque mínimo, lote e/ou série), R-EST-7 (custo médio, EST-01), R-EST-8, R-PRJ-7.
- `docs/03-modelo-dados.md` → `inventory.units`, `item_categories`, `items`, `warranty_defaults` (campos aprovados nesta story).
- `docs/06-pendencias-riscos.md` P-11 (meses de garantia padrão por tipo, sobrescrevível no item; `elev_garantia` entra na migração). Decidido em 18/10/2026: a configuração fica no Inventory (ADR 0016).
- **ADR 0016** (garantia e `ItemType` no Inventory; `ITenantParameters` do Identity sem garantia; excluir × inativar com `inventory.delete`), revisão pré-Fase 1: D10, tabela 1.1 (CAD-06 garantia e CAD-02a), §2.2 (NCM obrigatório só para item ativo; busca Ctrl K), §2.4.
- Padrões comuns a todos os cadastros: `docs/stories/README.md`. Item usa **cadastro em seções** (criação) e **detalhe com abas**; unidades, categorias e garantia usam **painel lateral**.
- `ModuleCatalog`: `inventory` com `view`, `create`, `edit`, `approve`, `adjust` e `delete` (CAD-03). Grupos do seed: Almoxarifado (`view`, `create`, `edit`), Supervisão (`view`, `create`), Financeiro e Engenharia (`view`).

## Critérios de aceite
**Unidades**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed roda, **então** existem as unidades UN, KG, M, M2, M3, L e PC, com `seed_key`, sem recriar a que o tenant excluiu.
2. **Dado** `inventory.create`, **quando** cadastro uma unidade com código (até 6 caracteres, maiúsculas, único) e nome e clico "Salvar unidade", **então** ela é gravada; unidade em uso por item não pode ser excluída nem inativada (409 "A unidade está em uso por {n} itens.").

**Categorias**
3. **Dado** `inventory.create`, **quando** cadastro categorias na `TreeTable` (nome e categoria pai) e clico "Salvar categoria", **então** o nome é único entre as irmãs (409) e a árvore não aceita ciclo (400); categoria com itens ou filhas só inativa.

**Garantia padrão (Inventory)**
4. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed roda, **então** a garantia padrão é: Produto acabado 12 meses; Componente e Serviço 3 meses; Matéria-prima e Material de consumo sem garantia; o seed não sobrescreve valor já editado.
5. **Dado** `inventory.edit` em Estoque → Garantia padrão, **quando** altero os meses de um tipo e clico "Salvar garantia padrão", **então** o valor (inteiro de 0 a 120, ou vazio = sem garantia) é gravado com histórico e vale para os itens novos do tipo; itens já cadastrados não mudam.

**Itens**
6. **Dado** `inventory.create`, **quando** abro "Novo item", **então** a página em seções começa pelo tipo (o campo que comanda), seguido de código (digitado, único), nome, unidade de estoque e categoria; ao clicar "Salvar item", ele é gravado, auditado, o toast diz "Item salvo" e o detalhe abre com a faixa "Próximos passos" ("Cadastrar conversão", "Vincular código do fornecedor").
7. **Dado** um item **ativo** do tipo Matéria-prima, Componente ou Produto acabado, **quando** salvo sem NCM, **então** a API responde 400 "Preencha o NCM (8 dígitos) para o item ativo."; o NCM é validado só pelo formato; para Material de consumo e Serviço, o NCM é opcional; um item inativo pode ficar sem NCM, e ativá-lo exige o NCM (mesmo invariante na tela e na importação).
8. **Dado** o CEST (opcional, 7 dígitos) e a origem da mercadoria (código de 0 a 8 com a descrição oficial, padrão 0 — Nacional), **quando** salvo, **então** são gravados.
9. **Dado** o tipo Serviço, **quando** salvo, **então** estoque mínimo, lote e série não são pedidos e `tracks_lot`/`tracks_serial` ficam falsos.
10. **Dado** qualquer tipo exceto Serviço, **quando** abro "Novo item", **então** posso ligar "Controla lote" e "Controla número de série" (juntos ou separados); Matéria-prima vem com lote marcado e Produto acabado com série marcada (R-PRP-1), e o sugerido só muda enquanto o usuário não mexeu no campo.
11. **Dado** um estoque mínimo, **quando** salvo, **então** ele é `numeric(18,4)` na unidade de estoque, maior ou igual a 0, digitado no `QuantityInput`.
12. **Dado** um tipo com garantia padrão (CA 4), **quando** abro "Novo item", **então** "Garantia (meses)" vem com o padrão do tipo e posso sobrescrever no item.
13. **Dado** o custo médio, **quando** abro o item, **então** ele aparece só para leitura (`numeric(18,6)`, `Money`), mantido pelo EST-01; a API não aceita custo médio na criação nem na edição (`UnmappedMemberHandling.Disallow`).
14. **Dado** um item com movimento de estoque, **quando** tento trocar a unidade de estoque ou desligar "Controla lote" ou "Controla número de série", **então** a API responde 409 "Item com movimentação não pode trocar a unidade de estoque." (ou a mensagem do indicador); a regra usa `IItemUsage`, que o EST-01 liga aos movimentos reais; aqui, o teste usa um provedor de uso de teste.
15. **Dado** um código já usado, **quando** salvo, **então** a API responde 409 "Já existe o item {código}."; o índice único garante também em gravação simultânea.
16. **Dado** `inventory.edit`, **quando** clico "Inativar item" e confirmo, **então** ele some dos seletores, continua visível onde é usado e na lista com o filtro "Inativos" (chip neutro); item com saldo não pode ser inativado (regra do EST-01, testada com provedor de teste).
17. **Dado** `inventory.delete` e um item sem uso, **quando** clico "Excluir item" e confirmo "Excluir", **então** ele é excluído logicamente; com uso (movimento, BOM, código do fornecedor, documento), 409 "O item está em uso. Inative-o." e o diálogo oferece "Inativar".
18. **Dado** um item, **quando** clico "Duplicar item", **então** abre "Novo item" com os dados copiados, menos código e custo médio (R-PRP-3).

**Lista e busca**
19. **Dado** `inventory.view`, **quando** abro Estoque → Itens, **então** vejo a DataTable server-side com código (link), nome, tipo, categoria, unidade, NCM, lote/série (ícones com rótulo), mínimo e situação, com filtros por tipo, categoria e situação na URL, busca por código ou nome e o vazio filtrado; com 50 mil itens, a primeira página responde em menos de 1 s.
20. **Dado** `inventory.view` e a busca da topbar (Ctrl K), **quando** digito ao menos 2 caracteres, **então** a paleta mostra até 5 itens (código, nome e tipo) com link para o detalhe; sem `inventory.view`, nenhum item aparece (ADR 0010 item 9).

**Contrato**
21. **Dado** Commercial, Engineering, Purchasing, Production ou Fiscal, **quando** precisam de itens, **então** usam `IItems` de `Inventory.Contracts` (resumo com unidade, tipo `ItemType`, NCM, lote e série, garantia; busca por tipo e termo só com ativos; resolução por id inclusive de inativos e excluídos).

**Permissões (3 camadas)**
22. **Dado** o módulo `inventory` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released`; sem grupo que habilite `inventory`, 403 `forbidden`; com `inventory.view` sem `create`/`edit`/`delete`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`. Todo endpoint tem `RequireModule` e `perm:`.

**Auditoria**
23. **Dado** qualquer alteração de unidade, categoria, garantia padrão ou item, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **`ItemType`** (enum em `Inventory.Contracts`, criado aqui) gravado como texto: `RawMaterial`, `Component`, `Finished`, `Consumable`, `Service`; rótulos Matéria-prima, Componente, Produto acabado, Material de consumo, Serviço.
- **Quantidades** `numeric(18,4)`; **custo unitário** `numeric(18,6)`.
- **NCM** só com dígitos, exibido `9999.99.99`, validado pelo formato; invariante: obrigatório para item ativo de Matéria-prima, Componente e Produto acabado.
- **Garantia:** `items.warranty_months` (inteiro ≥ 0, nulo = sem garantia); padrão por tipo em `inventory.warranty_defaults` (`item_type` único, `months`).
- **Código do item:** digitado (mantém a `ref` do Dolibarr); sem numeração automática na Fase 1.
- **Item fabricado sob encomenda:** um item genérico por modelo (ex.: "Elevador de carga 1.000 kg"); a série identifica cada unidade (EST-04, PRD-06).
- **Busca:** `pg_trgm` + `unaccent` em `code` e `name`.
- **Excluir × inativar** (D10): `is_active` em itens, unidades e categorias; exclusão lógica só sem uso, com `inventory.delete`.
- **`IItemUsage`** (Application): "tem movimento?", "tem saldo?", "está em uso?"; nasce respondendo "não" e o EST-01 liga a consulta real.
- **Ids:** Guid v7. **Auditoria:** `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões (no `ModuleCatalog`):** `inventory.view`, `inventory.create`, `inventory.edit`, `inventory.delete`.
- **Eventos:** nenhum.
- **Tabelas:** `inventory.units` (+ `is_active`, `seed_key`), `inventory.item_categories` (+ `is_active`), `inventory.items` (+ `warranty_months`), `inventory.warranty_defaults` (nova).

## Telas e estados
- **Estoque → Itens** (`/app/estoque/itens`): h1 com a linha "Matérias-primas, componentes, produtos e serviços."; "Novo item" em `<Can>`.
- **Novo item** (`/app/estoque/itens/novo`): página em seções Identificação (tipo, código, nome, categoria, unidade), Fiscal (NCM, CEST, origem), Estoque (mínimo, lote, série, garantia).
- **Item** (`/app/estoque/itens/$id`): abas na URL Dados, Conversões e Fornecedores (CAD-02b), Histórico de alterações (última).
- **Estoque → Unidades**, **Estoque → Categorias** (`TreeTable`) e **Estoque → Garantia padrão** (`/app/estoque/unidades`, `/categorias`, `/garantia`): listas com painel lateral.
- **Verbos:** "Salvar item", "Duplicar item", "Inativar item", "Excluir item", "Salvar unidade", "Salvar categoria", "Salvar garantia padrão"; diálogos com "Voltar".
- **Estados:** carregando; vazio ("Cadastre o primeiro item: matéria-prima, componente ou produto acabado." + "Novo item"); vazio filtrado; erro com "Tentar de novo"; modo leitura.
- **Propositivo:** lote, série e garantia sugeridos pelo tipo; "Duplicar item"; faixa "Próximos passos" depois de criar.
- **400 px e teclado:** um CA de E2E para a criação do item.
- **Ajuda:** `Ajuda/estoque-itens.md` (lista, `padrao: true`), `Ajuda/estoque-item-novo.md`, `Ajuda/estoque-item.md` (abas `dados`, `historico`), `Ajuda/estoque-unidades.md`, `Ajuda/estoque-categorias.md`, `Ajuda/estoque-garantia.md`; `<Dica>` em "NCM", "CEST", "Origem", "Controla lote", "Controla número de série" e "Garantia".

## Plano de testes
- **Unidade:** regras por tipo e invariante do NCM (CA 7, 9, 10); CEST e origem (CA 8); árvore de categorias (CA 3); sugerido só muda enquanto não editado (CA 10).
- **Integração (Testcontainers):** seed de unidades e de garantia em tenant novo e existente (CA 1, 4); unidade em uso (CA 2); garantia padrão e no item (CA 5, 12); custo médio recusado (CA 13); unidade e lote travados com uso de teste (CA 14); código único concorrente (CA 15); inativar e excluir (CA 16, 17); duplicar (CA 18); 50 mil itens abaixo de 1 s (CA 19); busca da topbar (CA 20); `IItems` (CA 21); três camadas (CA 22); auditoria (CA 23).
- **Arquitetura:** só `Inventory.Contracts` é referenciado por outros módulos; `ITenantParameters` sem garantia; todo endpoint com `RequireModule` e `perm:`.
- **E2E (Playwright):** cadastrar matéria-prima com lote e produto acabado com série; duplicar; criação a 400 px e só pelo teclado.

## Fora do escopo
- Conversões e códigos do fornecedor: CAD-02b.
- Saldos, movimentos, custo médio, reservas: EST-01 e EST-02 (o saldo inicial é digitado na EST-01, `OpeningBalance`).
- Lotes, séries e rastreabilidade: EST-04.
- Preço de venda e imagem do item.

## Pontos em aberto
- Decidido em 18/10/2026 (D10, tabela 1.1, §2.2 e ADR 0016): excluir × inativar com `inventory.delete`; lote e série para qualquer tipo exceto Serviço; NCM só pelo formato, obrigatório para item ativo de Matéria-prima, Componente e Produto acabado e opcional para consumo e serviço; código digitado; item genérico por modelo; garantia padrão no Inventory (12/3/3 meses).
- Nenhum ponto em aberto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva) e seeds de unidades e garantia.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio, vazio filtrado e erro.
- [ ] Ajuda de cada tela e aba; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` conferido; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ItemType` em `Inventory.Contracts`; entidades `Unit`, `ItemCategory`, `Item`, `WarrantyDefault`.
- [ ] `IInventoryDb`, migration, seeds de unidades e garantia.
- [ ] `IItemUsage` (implementação inicial) e `IItems`; provedor da busca da topbar.
- [ ] Endpoints `/api/v1/inventory/items`, `/units`, `/categories`, `/warranty-defaults` com `RequireModule("inventory")`.
- [ ] Telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 7 (invariante do NCM), CA 14 (unidade travada) e CA 19 (desempenho) obrigatórios.

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

- **Correções feitas na validação (18/10/2026):** garantia padrão trazida da CAD-06 para o Inventory, com o enum `ItemType` criado aqui; NCM obrigatório só para item ativo; lote e série para qualquer tipo exceto Serviço; excluir × inativar com `inventory.delete`; busca Ctrl K; criação em seções; dependência da CAD-03 no lugar da CAD-06; custo médio sem a MIG-01d (cancelada); ordem 8 do Marco B.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-02) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisões do PO (D1, D10, tabela 1.1) e ADR 0016; garantia no Inventory; ordem 8 do Marco B | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
