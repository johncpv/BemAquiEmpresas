# MIG-01c — Carga de unidades, categorias, itens, códigos do fornecedor e locais do Dolibarr

| Campo | Valor |
|---|---|
| ID | MIG-01c |
| Título | Importar `llx_product` (com `elev_garantia`), `llx_categorie`, `llx_c_units`, `llx_product_fournisseur_price` e `llx_entrepot`, com de-para de unidade e de tipo de item |
| Tamanho | M (quebra da MIG-01) |
| Status | Draft |
| Requisitos | R-TER-5 (produtos) · R-EST-1 · R-EST-2 · R-EST-3 · R-PRJ-7 (garantia, P-11) |
| Dependências | MIG-01a, MIG-01b (fornecedores para os códigos), CAD-02a, CAD-02b, CAD-03 |
| Bloqueia | MIG-01d |
| Ordem de execução | 13 |

## User story
Como **almoxarife**, quero **trazer do Dolibarr os produtos com unidade, NCM, garantia e o código de cada fornecedor, e os depósitos**, para **começar o estoque e as compras com o mesmo catálogo de hoje, sem redigitar**.

## Contexto e referências
- PRD R-TER-5 (produtos), R-EST-1, R-EST-2, R-EST-3.
- MIG-01a (mapeamento, staging, chaves externas, simulação, relatório).
- `docs/06-pendencias-riscos.md` P-07 (34 produtos; estoque zerado), P-11 (o campo `elev_garantia` do Dolibarr entra na migração).
- CAD-02a (regras do item: NCM obrigatório fora de Serviço, tipos, lote e série, garantia), CAD-02b (códigos do fornecedor), CAD-03 (locais, local padrão).

## Critérios de aceite
**Unidades e categorias**
1. **Dado** as unidades usadas pelos produtos (`fk_unit` → `llx_c_units`), **quando** a carga roda, **então** cada uma é ligada a uma unidade do BemAqui pelo de-para do mapeamento (ex.: `U`/`unit` → UN, `KG`, `M`, `M2`…); unidade sem de-para vira erro no relatório e o item não entra.
2. **Dado** categorias de produto (`llx_categorie` do tipo produto, com `fk_parent`), **quando** a carga roda, **então** viram a árvore de categorias; um produto em várias categorias fica na primeira pela ordem do mapeamento, e o relatório lista as demais (ponto em aberto 3).

**Itens**
3. **Dado** um `llx_product`, **quando** a carga roda, **então** vira item com código = `ref`, nome = `label`, unidade, categoria, NCM (do campo definido no mapeamento, normalmente `customcode`), estoque mínimo = `seuil_stock_alerte` e situação ativa quando `tosell` ou `tobuy` está ligado.
4. **Dado** `fk_product_type` = 1 (serviço), **quando** importado, **então** o tipo é Serviço; com 0 (produto), o tipo vem do de-para por produto definido com o PO no mapeamento (Matéria-prima, Componente, Produto acabado ou Material de consumo); sem de-para, o padrão proposto é Componente, com aviso no relatório.
5. **Dado** `tobatch` = 1 ou 2, **quando** importado, **então** liga "Controla lote" (1) ou "Controla número de série" (2), conforme o Dolibarr.
6. **Dado** o extrafield `elev_garantia`, **quando** importado, **então** vira os meses de garantia do item; vazio usa o padrão do tipo (CAD-06); valor não numérico vira aviso no relatório.
7. **Dado** um produto de tipo diferente de Serviço sem NCM válido (8 dígitos), **quando** a carga roda, **então** segue a regra do ponto em aberto 1 (proposta: importar inativo, com o aviso "sem NCM: ativar depois de preencher").
8. **Dado** dois produtos com o mesmo `ref`, **quando** a carga roda, **então** o segundo vai para erro "código repetido no Dolibarr".
9. **Dado** o custo médio (`pmp`), **quando** a carga de itens roda, **então** ele **não** é gravado aqui (o custo médio nasce com o saldo inicial, MIG-01d) e fica guardado na staging para a MIG-01d.

**Códigos do fornecedor**
10. **Dado** `llx_product_fournisseur_price` de um fornecedor importado na MIG-01b, **quando** a carga roda, **então** vira código do fornecedor (CAD-02b) com `ref_fourn`, a unidade e o fator do mapeamento; fornecedor não importado vai para "ignorado: fornecedor não importado"; o mesmo `ref_fourn` do mesmo fornecedor em dois produtos vira erro (regra da CAD-02b CA 10).

**Locais**
11. **Dado** `llx_entrepot`, **quando** a carga roda, **então** cada depósito vira local com código = `ref` (ou `label`, conforme a versão), nome e situação (`statut`); sub-depósitos (`fk_parent`) viram locais independentes com o nome "{pai} / {filho}" (ponto em aberto 4).
12. **Dado** um único depósito ativo, **quando** a carga termina, **então** o relatório sugere torná-lo o local padrão e o PO confirma no BemAqui (Estoque → Locais); a carga não troca o padrão sozinha.

**Reexecução e relatório**
13. **Dado** a carga feita, **quando** rodo de novo sem mudança, **então** tudo sai como "sem alteração" e nada é duplicado; um item editado no BemAqui depois da carga não é sobrescrito (conflito, MIG-01a CA 9).
14. **Dado** o relatório, **quando** confiro, **então** os 34 produtos do Dolibarr (P-07) estão todos contados entre importados, ignorados e erros, com o motivo de cada um.

## Regras de negócio e técnicas
- Grava só por `IItemImport` e `ILocationImport` (`Inventory.Contracts`), com as validações da CAD-02a, CAD-02b e CAD-03.
- O de-para de unidades e de tipos fica no mapeamento (`docs/operacao/migracao-dolibarr.md`) e numa tabela de configuração da execução (arquivo JSON passado ao comando, **proposta**), para o PO ajustar sem mudar código.
- Quantidades (`seuil_stock_alerte`) em `numeric(18,4)`; fatores pela regra da CAD-02b.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma (CLI, MIG-01a).
- **Eventos:** nenhum novo.
- **Tabelas:** grava em `inventory.*` pelos Contracts; registra em `migration.*`.

## Telas e estados
- Sem tela. Itens e locais importados aparecem em Estoque; o histórico de cada um mostra a importação.
- **Ajuda:** pergunta frequente "Os produtos do Dolibarr já estão aqui?" em `Ajuda/estoque-itens.md`.

## Plano de testes
- **Unidade:** de-para de unidades e tipos (CA 1, 4); `tobatch` (CA 5); `elev_garantia` (CA 6); nome de sub-depósito (CA 11).
- **Integração (Testcontainers PostgreSQL + MariaDB, fixture anonimizada):** categorias com produto em duas (CA 2); item completo (CA 3); sem NCM (CA 7); `ref` repetido (CA 8); `pmp` só na staging (CA 9); códigos do fornecedor com fornecedor ausente e repetido (CA 10); locais e sugestão de padrão (CA 11, 12); reexecução e conflito (CA 13); soma do relatório (CA 14).
- **Ensaio manual:** simulação contra a cópia da base real, conferindo os 34 produtos com o PO.

## Fora do escopo
- Saldos e custo médio: MIG-01d.
- Preço de venda, preço de compra e histórico de preços.
- BOM e roteiros do Dolibarr (`llx_bom_bom`), que não migram (MIG-01a ponto 4).

## Pontos em aberto
1. **Produto sem NCM:** importar inativo (proposta), importar ativo sem NCM (contraria a CAD-02a) ou não importar.
2. **Tipo do produto:** de-para por produto feito com o PO (proposta) ou regra automática (`tobuy` sem `tosell` → Matéria-prima; com BOM → Produto acabado).
3. **Produto em várias categorias:** primeira pela ordem (proposta) ou escolha no de-para.
4. **Sub-depósitos:** achatar com o nome do pai (proposta) ou ignorar.
5. **Campo do NCM** na base real (`customcode` ou extrafield): confirmar no inventário da MIG-01a.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e integração (Testcontainers PostgreSQL + MariaDB) passando.
- [ ] `IItemImport` e `ILocationImport` nos Contracts.
- [ ] De-para de unidades e tipos documentado e aceito pelo PO.
- [ ] Simulação contra a cópia da base real conferida com o PO.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `UnitsLoader`, `CategoriesLoader`, `ItemsLoader`, `SupplierCodesLoader`, `LocationsLoader`.
- [ ] Arquivo de de-para da execução (unidades e tipos) com validação.
- [ ] Fixture com serviço, produto com lote, produto com série, sem NCM e sub-depósito.

## Quality gates
- Agentes previstos: @dev, @data-engineer, @qa.
- Gate: CA 13 (reexecução e conflito) obrigatório.

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
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (sem tela; ajuda só com a pergunta frequente) |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** MIG-01a pronta; pontos em aberto 1 (produto sem NCM) e 2 (de-para de tipos).
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da MIG-01) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
