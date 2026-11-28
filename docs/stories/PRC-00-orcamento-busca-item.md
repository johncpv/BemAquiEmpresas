# PRC-00 — Orçamento busca item cadastrado

| Campo | Valor |
|---|---|
| ID | PRC-00 |
| Título | "Adicionar item" do orçamento com busca única por código, nome e GTIN (`IItems` e, onde houver Catálogo, o código de barras do produto); escolhido o item, a linha nasce com o vínculo (`item_id`), a descrição, a unidade e o tipo; "Digitar à mão" continua |
| Tamanho | P |
| Status | Done (28/11/2026) |
| Requisitos | [Proposta](../produto/produto-custo-preco.md) §1.3 (lacuna 1), §11 e §14 · COM-01a CA 7 (correção) · ADR 0006 (só por contratos) · D7 (custo só com `quotes.view_cost`) |
| Dependências | COM-01a (orçamento), CAD-02a (itens, `IItems`), CAT-01 (código de barras do produto), EST-01 (custo médio, `IItemCosts`) |
| Bloqueia | PRC-01 (ficha padrão do item), UX-02 (cadastro rápido de item) |
| Ordem de execução | 1 do bloco "Pós-go-live: melhorias do produto" |

## User story
Como **vendedor**, quero **escolher no orçamento um item que já está cadastrado**, para **não redigitar o nome, a unidade e o tipo, e para o item do orçamento ficar ligado ao estoque.**

## Contexto e referências (conferido no código em 28/11/2026)
- `ItemsTab.tsx` manda sempre `itemId: null`; o backend já aceita o vínculo (`QuoteService.ItemErrorAsync` com `IItems.FindAsync`) e a linha guarda `item_id` (`commercial.quote_items`).
- Já existe o seletor `GET /api/v1/quotes/lookups/items` (`ListQuoteItems`, `quotes.view`, `IItems.SearchAsync`, 20 resultados), usado pela linha de material da composição. A story **reusa** essa rota e amplia a resposta (`QuoteItemOption`).
- O item de estoque não tem GTIN: o código de barras é do produto do Catálogo (`catalog.product_barcodes`). `ICatalogLookup.ByGtinAsync` lê o preço da tabela padrão e **cria a "Balcão" no primeiro uso**: efeito colateral inaceitável na Konebras, que não usa Catálogo. Por isso entra um contrato só de leitura, `IProductBarcodes.ItemIdsByGtinAsync`.
- O custo médio vem do `IItemCosts.AverageCostsAsync` (Estoque), o mesmo da BOM (ENG-02).

## Critérios de aceite
1. **Dado** um orçamento editável, **quando** abro "Adicionar item", **então** o padrão é **buscar o item**: uma busca única por código, nome ou GTIN (o GTIN acha o item ligado ao produto do Catálogo, onde houver Catálogo); cada resultado mostra código, nome, tipo e unidade.
2. **Dado** `quotes.view_cost`, **quando** busco, **então** cada resultado mostra também o custo médio do item; **sem** a permissão, o custo **não sai da API** (campo nulo), não só da tela.
3. **Dado** um resultado escolhido, **quando** adiciono, **então** a linha grava o vínculo (`item_id`), a descrição (o nome do item, editável antes de adicionar) e o tipo pelo tipo do item: Serviço → **Serviço**, Produto acabado → **Equipamento**, os demais → **Produto**. A unidade aparece na linha e no campo de quantidade.
4. **Dado** "Digitar à mão", **quando** escolho, **então** volto ao formulário de antes (tipo, descrição e quantidade), sem vínculo.
5. **[Segurança]** **Dado** o servidor, **quando** recebe um `itemId` inexistente (inclusive de outro tenant, que mora noutro banco), inativo ou excluído ao adicionar, **então** responde **422** com o erro no campo `itemId` da linha ("Escolha um item ativo do estoque."). Ao **salvar** uma linha que já estava ligada a um item depois inativado, o vínculo antigo é aceito (o documento antigo continua editável); trocar para outro item inativo dá 422.
6. **[Segurança]** **Dado** um item ligado, **quando** o servidor grava, **então** o tipo da linha sai do tipo do item (regra do CA 3), qualquer que seja o tipo enviado.
7. **Dado** a busca, **quando** consulto, **então** o servidor devolve **no máximo 20** itens ativos, ordenados pelo código, com o termo limitado a 100 caracteres; a tela avisa o teto ("Mostra até 20 itens; digite mais do código ou do nome para refinar").
8. **Dado** o celular (400 px), **quando** adiciono item, **então** o formulário fica em uma coluna, sem rolagem horizontal, e a lista da busca cabe na largura.
9. **Regressão (Konebras e todos os perfis):** a composição de custo **não** muda; o seletor de material da composição continua funcionando com a resposta ampliada; nada é criado no Catálogo pela busca.

## Permissões, eventos e tabelas
- Sem permissão nova: a busca segue `quotes.view`; o custo médio na resposta exige `quotes.view_cost` (checado no servidor pelo `IPermissionChecker`).
- Sem evento novo e **sem migration**: `quote_items` já tem `item_id`; a unidade é lida do item (o Estoque é o dono, ADR 0006).
- Contrato novo, só leitura: `BemAqui.Catalog.Contracts.IProductBarcodes`.

## Telas e estados
- Aba Itens do orçamento: "Adicionar item" com a busca (carregando, vazio "Nada encontrado", erro "Não foi possível buscar", pelo `Combobox`) e o botão "Digitar à mão" / "Buscar item cadastrado".
- Cartão do item: unidade ao lado da quantidade e o item ligado ("Item: código nome").
- Próximo passo propositivo: escolhido o item, a descrição vem preenchida e o foco vai para a quantidade.

## Plano de testes
- Integração (`QuoteTests.ItemLookup`): busca por código e nome (tipo e unidade), por GTIN do produto do Catálogo, custo só com `quotes.view_cost`, limite de 20; adicionar com item (vínculo, tipo pelo item, unidade na resposta); 422 no `itemId` inexistente e inativo; linha com item inativado continua editável; busca por GTIN não cria tabela de preço.
- Vitest: mapeamento tipo do item → tipo da linha e o texto do resultado.
- Varreduras: `EndpointAuthorizationTests`, `Architecture.Tests`, `OpenApiDocumentTests`. Sem E2E (é por feature).

## Tarefas
- [x] Contrato `IProductBarcodes` no Catálogo (implementado no `CatalogReadContract`).
- [x] `QuoteItemOption` com tipo e custo médio; `QuoteLookups.ItemsAsync` com GTIN, limite e custo por permissão.
- [x] `QuoteService`: tipo pelo item, vínculo inativo aceito só se já gravado; `QuoteItemView.UnitCode`.
- [x] Front: `ItemsTab` com busca e "Digitar à mão"; unidade no `ItemCard`; `quotes.ts` com o mapeamento.
- [x] OpenAPI e cliente gerado; ajuda `comercial-orcamento-itens.md`.
- [x] Testes de integração e Vitest.

## Dev Notes
- Commercial passa a referenciar `BemAqui.Catalog.Contracts` (só Contracts, ADR 0006).
- O GTIN só é consultado quando o termo tem 8, 12, 13 ou 14 dígitos (após tirar espaços e hífens); o item achado pelo GTIN entra no topo da lista se ainda não veio pela busca de texto.
- `IItems.SearchAsync` limita em 100; a rota fixa 20.

## Fora do escopo
- Composição de custo a partir do item (PRC-01), cadastro rápido de item (UX-02), preço do Catálogo/tabela no orçamento (PRC-02).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 28/11/2026 | 0.1 | Story escrita a partir do backlog (bloco "Pós-go-live: melhorias do produto") e da proposta §14. **Revisão curta (arquitetura, segurança e UX)**: (a) **arquitetura** — reusa `ListQuoteItems` em vez de rota nova; GTIN por contrato novo só de leitura, porque o `ICatalogLookup` cria a tabela "Balcão" no primeiro uso (efeito na Konebras); sem migration; (b) **segurança** — IDOR: o `itemId` é revalidado no servidor pelo `IItems` do banco do tenant (outro tenant = inexistente), ativo e não excluído, 422 no campo; o tipo da linha sai do item; custo médio só com `quotes.view_cost`, filtrado na API; busca com limite de 20 e termo de até 100 caracteres; (c) **UX** — buscar é o padrão e "Digitar à mão" fica a um clique; descrição preenchida e editável; unidade visível; celular a 400 px em uma coluna. Vale para **todos os perfis, Konebras inclusive** (decisão do PO, §14). | Claude |
| 28/11/2026 | 1.0 | **Implementada.** **Decisões tomadas sem o PO (a mais segura; o PO pode rever):** (1) **paginação**: a busca é de digitação (top 20 por código, como todos os seletores `Combobox` da FND-09d), não paginada por página; o limite fixo e o aviso "refine a busca" protegem o servidor e a tela; (2) **unidade não é gravada na linha**: é lida do item ligado (o Estoque é o dono; sem migration, que concorreria com a EXE-03b no mesmo `TenantDbContext`); (3) o tipo da linha ligada é **imposto pelo servidor** pelo tipo do item; (4) a linha já ligada a um item que foi inativado continua editável com o mesmo vínculo (como o cargo inativo da EXE-02); duplicá-la dá 422; (5) "Mercadoria para revenda" vira **Produto** (regra "os demais"). | Claude |
