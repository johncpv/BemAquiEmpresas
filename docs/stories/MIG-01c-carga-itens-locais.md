# MIG-01c — Carga de unidades, categorias, itens, códigos do fornecedor e locais do Dolibarr

| Campo | Valor |
|---|---|
| ID | MIG-01c |
| Título | Importar `llx_c_units`, `llx_categorie` (produto), `llx_product` (com `elev_garantia`), `llx_product_fournisseur_price` e `llx_entrepot`, com de-para de unidade e de tipo, sem saldo nem custo |
| Tamanho | M (quebra da MIG-01) |
| Status | Ready |
| Requisitos | R-TER-5 (produtos) · R-EST-1 · R-EST-2 · R-EST-3 · R-PRJ-7 (garantia, P-11) |
| Dependências | MIG-01a, MIG-01b (fornecedores dos códigos), CAD-03 (locais e ALM), CAD-02a (itens, unidades, categorias, garantia), CAD-02b (códigos do fornecedor) |
| Bloqueia | GOL-01 |
| Ordem de execução | 10 (Marco B — catálogo real) |

## User story
Como **almoxarife**, quero **trazer do Dolibarr os produtos com unidade, NCM, garantia e o código de cada fornecedor, e os depósitos**, para **começar o estoque e as compras com o mesmo catálogo de hoje, sem redigitar**.

## Contexto e referências
- PRD R-TER-5 (produtos), R-EST-1, R-EST-2, R-EST-3.
- MIG-01a (snapshot, staging, chaves externas, simulação, relatório, tela) e `docs/operacao/migracao-dolibarr.md`.
- **ADR 0017** (só cadastros: sem saldo de estoque, sem custo médio `pmp`, sem lotes; sem preço do fornecedor).
- `docs/06-pendencias-riscos.md` P-07 (estoque zerado no Dolibarr), P-11 (`elev_garantia` entra na migração).
- Revisão pré-Fase 1: tabela 1.1 (MIG-01c e CAD-03), §2.2 (NCM obrigatório só para item ativo; contagens pelo total lido).
- CAD-02a (invariante do NCM, tipos, lote e série, garantia padrão no Inventory), CAD-02b (códigos do fornecedor), CAD-03 (local padrão ALM).

## Critérios de aceite
**Unidades e categorias**
1. **Dado** as unidades usadas pelos produtos (`fk_unit` → `llx_c_units`), **quando** a carga roda, **então** cada uma é ligada a uma unidade do BemAqui pelo de-para (ex.: `U`/`unit` → UN, `KG`, `M`, `M2`); uma unidade marcada "criar" no de-para é criada; unidade sem de-para vira erro e o item não entra.
2. **Dado** categorias de produto (`llx_categorie` do tipo produto, com `fk_parent`), **quando** a carga roda, **então** viram a árvore de categorias; um produto em várias categorias fica na primeira pela ordem do `rowid`, e o relatório lista as demais como aviso.

**Itens**
3. **Dado** um `llx_product`, **quando** a carga roda, **então** vira item com código = `ref`, nome = `label`, unidade, categoria, NCM (da coluna do mapeamento, normalmente `customcode`, só dígitos), estoque mínimo = `seuil_stock_alerte` (`numeric(18,4)`) e situação ativa quando `tosell` ou `tobuy` está ligado.
4. **Dado** `fk_product_type` = 1, **quando** importado, **então** o tipo é Serviço; com 0, o tipo vem do de-para produto a produto feito com o PO (Matéria-prima, Componente, Produto acabado ou Material de consumo); sem de-para, entra como Componente com o aviso "tipo não informado no de-para".
5. **Dado** `tobatch` = 1 ou 2, **quando** importado, **então** liga "Controla lote" (1) ou "Controla número de série" (2); em Serviço, os dois ficam desligados.
6. **Dado** o extrafield `elev_garantia`, **quando** importado, **então** vira os meses de garantia do item; vazio usa a garantia padrão do tipo (CAD-02a); valor não numérico vira aviso e o item usa o padrão do tipo.
7. **Dado** um produto de Matéria-prima, Componente ou Produto acabado sem NCM válido (8 dígitos), **quando** a carga roda, **então** ele entra **inativo**, com o aviso "sem NCM: ativar depois de preencher" (mesmo invariante da CAD-02a); Material de consumo e Serviço entram ativos sem NCM.
8. **Dado** dois produtos com o mesmo `ref`, **quando** a carga roda, **então** o segundo vai para erro "código repetido no Dolibarr".
9. **Dado** o custo médio (`pmp`), saldos e lotes, **quando** a carga roda, **então** nada disso é lido (fora do snapshot, ADR 0017); o custo médio do item começa em zero e o saldo inicial é digitado na EST-01.

**Códigos do fornecedor**
10. **Dado** `llx_product_fournisseur_price` de um fornecedor importado na MIG-01b, **quando** a carga roda, **então** vira código do fornecedor (CAD-02b) com `ref_fourn`, a unidade do fornecedor em texto (`fk_unit` ou a do de-para) e o fator do de-para (padrão 1 quando a unidade é a de estoque); fornecedor não importado vai para "ignorado: fornecedor não importado"; o mesmo `ref_fourn` do mesmo fornecedor em dois produtos vira erro (CAD-02b CA 10); nenhum preço é lido.

**Locais**
11. **Dado** um único depósito ativo no Dolibarr, **quando** a carga roda, **então** ele é ligado ao local padrão "ALM — Almoxarifado principal" (chave externa no ALM), sem criar outro local.
12. **Dado** vários depósitos, **quando** a carga roda, **então** cada um vira local com código = `ref` (ou `label`, conforme a versão), nome e situação (`statut`); sub-depósitos (`fk_parent`) viram locais com o nome "{pai} / {filho}"; o relatório sugere qual tornar padrão e a carga não troca o padrão sozinha.

**Importado do Dolibarr**
13. **Dado** um item, categoria ou local importado, **quando** abro o detalhe, **então** vejo a `Tag` "Importado do Dolibarr" e a referência de origem (`import_source`).

**Reexecução e relatório**
14. **Dado** a carga feita, **quando** rodo de novo sem mudança, **então** tudo sai como "sem alteração"; um item editado no BemAqui depois da carga não é sobrescrito (conflito, MIG-01a CA 13).
15. **Dado** o relatório, **quando** confiro, **então** para `llx_c_units`, `llx_categorie`, `llx_product`, `llx_product_fournisseur_price` e `llx_entrepot` o total lido na extração = importados + atualizados + sem alteração + vinculados + ignorados + conflitos + erros, com o motivo de cada pendência.

## Regras de negócio e técnicas
- Grava só por `IItemImport` e `ILocationImport` (`Inventory.Contracts`, criadas nesta story), com as validações da CAD-02a, CAD-02b e CAD-03.
- `import_source` é acrescentado a `inventory.items`, `item_categories`, `units`, `item_supplier_codes` e `locations` nesta story.
- O de-para de unidades, tipos, unidades do fornecedor e fatores fica no arquivo `--mapping` da MIG-01a, para o PO ajustar sem mudar código.
- Ordem: unidades → categorias → locais → itens → códigos do fornecedor.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova (CLI, MIG-01a).
- **Eventos:** nenhum novo.
- **Tabelas:** grava em `inventory.*` pelos Contracts, com a coluna `import_source`; registra em `migration.*`.

## Telas e estados
- Sem tela nova. Itens e locais importados aparecem em Estoque com a `Tag` "Importado do Dolibarr"; os itens inativos sem NCM aparecem no filtro "Inativos" e nas pendências da tela da importação (MIG-01a).
- **Ajuda:** pergunta frequente "Os produtos do Dolibarr já estão aqui?" e "Por que o item importado está inativo?" em `Ajuda/estoque-itens.md`.

## Plano de testes
- **Unidade:** de-para de unidades e tipos (CA 1, 4); `tobatch` (CA 5); `elev_garantia` (CA 6); invariante do NCM na carga (CA 7); nome de sub-depósito (CA 12).
- **Integração (Testcontainers PostgreSQL + MariaDB, fixture anonimizada):** categorias com produto em duas (CA 2); item completo (CA 3); sem NCM inativo (CA 7); `ref` repetido (CA 8); nenhuma coluna de custo, saldo ou preço lida (CA 9, 10); códigos do fornecedor com fornecedor ausente e repetido (CA 10); depósito único no ALM e vários depósitos (CA 11, 12); `import_source` e `Tag` (CA 13); reexecução e conflito (CA 14); soma pelo total lido (CA 15).
- **Ensaio manual:** simulação e carga na homologação com o snapshot da base real, conferindo os produtos e o de-para de tipos com o PO.

## Fora do escopo
- Saldos, lotes, custo médio e movimentos de estoque (decisão do PO de 18/10/2026; MIG-01d cancelada; saldo inicial digitado na EST-01).
- Preço de venda, preço de compra e histórico de preços.
- BOM e roteiros do Dolibarr (`llx_bom_bom`), que não migram.

## Pontos em aberto
- Decidido em 18/10/2026 (tabela 1.1 e §2.2): produto sem NCM entra inativo; tipo pelo de-para produto a produto com o PO; produto em várias categorias fica com a primeira; sub-depósitos achatados com o nome do pai; depósito único ligado ao ALM.
- **Pré-condições externas (não bloqueiam o `Ready`, porque têm padrão):**
  - o de-para de tipo produto a produto feito com o PO (sem ele, Componente com aviso);
  - a coluna do NCM na base real (`customcode` ou extrafield), confirmada no inventário da MIG-01a.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e integração (PostgreSQL + MariaDB) passando.
- [ ] `IItemImport` e `ILocationImport` nos Contracts; coluna `import_source` por migration.
- [ ] De-para de unidades e tipos documentado e aceito pelo PO.
- [ ] Simulação e carga na homologação conferidas com o PO; ajuda atualizada.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `UnitsLoader`, `CategoriesLoader`, `LocationsLoader`, `ItemsLoader`, `SupplierCodesLoader`.
- [ ] `IItemImport`, `ILocationImport` e `import_source`.
- [ ] Fixture com serviço, produto com lote, com série, sem NCM, com `ref` repetido, depósito único e sub-depósito.

## Quality gates
- Agentes previstos: @dev, @data-engineer, @qa.
- Gate: CA 7 (invariante do NCM) e CA 14 (reexecução e conflito) obrigatórios.

## Validação do PO
Checklist do @po (Pax), 18/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK (sem endpoint novo; segurança da carga na MIG-01a) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (sem tela nova; `Tag` de importado e ajuda) |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (18/10/2026):** `pmp`, saldos e lotes fora (MIG-01d cancelada, ADR 0017); valores da tabela 1.1 nos CAs; NCM pelo invariante de item ativo; depósito único ligado ao ALM; `Tag` "Importado do Dolibarr"; contagens pelo total lido; ordem 10 do Marco B.
- **Pré-condições para `Ready`:** nenhuma bloqueante. As externas (de-para de tipos, coluna do NCM) têm padrão definido.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da MIG-01) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisões do PO: só cadastros (ADR 0017), tabela 1.1; correções da revisão pré-Fase 1; ordem 10 do Marco B | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
