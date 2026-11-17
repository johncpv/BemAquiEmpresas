# CAT-01 — Módulo Catálogo: produto vendável ligado ao item do estoque, categoria, variação (grade), vários códigos de barras por produto, tabela de preço padrão versionada e consulta rápida por código, PLU e busca

| Campo | Valor |
|---|---|
| ID | CAT-01 |
| Título | Módulo Catálogo: produto vendável ligado ao item do estoque, categoria, variação (grade), vários códigos de barras por produto, tabela de preço padrão versionada e consulta rápida por código, PLU e busca |
| Tamanho | G |
| Status | Done (17/11/2026; branch `feat/cat-01-catalogo`) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes |
| Requisitos | Pedido do PO de 15/11/2026 (itens 1 e 4) · [PRD do PDV](../01-PRD.md) seção 4.2 · [Modelo de dados](../03-modelo-dados.md#3-catalog--schema-catalog_products) · ADR 0006 (contratos entre módulos) |
| Dependências | PERF-01, PERF-03; PP-04 decidida |
| Bloqueia | CAT-02, VND-01, PERF-04, PDV-01, OFF-01, ECM-01 |
| Paridade com o mercado | Requisitos mínimos 22, 25 (tabela padrão), 33 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 4 (Fase 10) |

## User story
Como **gerente de loja**, quero **cadastrar o que eu vendo com código de barras e preço**, para **o caixa achar o produto na hora e eu saber a margem de cada um**.

## Contexto e referências
- `inventory.items` não tem código de barras nem preço de venda, e `ItemType` não tem "mercadoria para revenda".
- A BOM da Engenharia é por OS e não serve como ficha técnica de produto; a ficha técnica nasce no Catálogo, na Fase 15.
- Esta story e a PDV-01 escrevem a ADR das fronteiras dos módulos novos.

## Critérios de aceite
### Módulo e produto
1. **Dado** o projeto `src/Modules/Catalog/`, **quando** os testes de arquitetura rodam, **então** ele tem as cinco camadas, schema `catalog_products` e só referencia `*.Contracts`.
2. **Dado** o `ModuleCatalog`, **quando** a aplicação sobe, **então** existe o módulo `catalog` na área "Loja", com `view`, `create`, `edit`, `delete`, `edit_price` e `view_cost`.
3. **Dado** `catalog.create`, **quando** crio um produto, **então** ele nasce ligado a um item do Estoque: escolho um item existente ou crio um novo do tipo `Merchandise`, no mesmo formulário, sem digitar duas vezes nome, unidade e NCM.
4. **Dado** um item que já tem produto, **quando** tento criar outro produto para ele, **então** recebo 409 "Este item já tem um produto no catálogo.".

### Códigos de barras
5. **Dado** um produto, **quando** adiciono códigos de barras, **então** posso ter vários, cada um com a quantidade da embalagem (ex.: a caixa com 12).
6. **Dado** um GTIN com dígito verificador errado, **quando** salvo, **então** recebo 422 "Código de barras inválido. Confira os dígitos.".
7. **Dado** um GTIN que já pertence a outro produto, **quando** salvo, **então** recebo 409 com o nome do produto que já o usa.

### Preço
8. **Dado** a tabela de preço "Balcão", **quando** altero o preço de um produto com `catalog.edit_price`, **então** a versão da tabela sobe, o histórico guarda preço anterior, novo, quem e quando, e o evento `PriceChanged` vai ao outbox.
9. **[Segurança]** **Dado** quem tem `catalog.edit` e não tem `catalog.edit_price`, **quando** envia preço na edição do produto, **então** recebe 403 `forbidden` e nada muda.
10. **[Segurança]** **Dado** quem não tem `catalog.view_cost`, **quando** lista ou abre produtos, **então** custo e margem não vêm na resposta (ausentes, não zerados).
11. **Dado** `catalog.view_cost`, **quando** abro o produto, **então** vejo o custo médio (por `IItemCosts`) e a margem sobre o preço vigente.

### Consulta
12. **Dado** `ICatalogLookup`, **quando** consulto por GTIN, por PLU ou por trecho do nome, **então** recebo o produto com o preço vigente da tabela pedida; o GTIN de embalagem devolve também a quantidade.
13. **Dado** 20 mil produtos, **quando** consulto por GTIN e por trecho do nome, **então** a resposta vem em menos de 50 ms no p95 (teste com `[Trait(LoadTest.Trait, LoadTest.Value)]`).

### Categoria e variação
14. **Dado** `catalog.create`, **quando** cadastro categorias, **então** cada uma tem nome, categoria pai opcional, ordem e, opcionalmente, margem ou markup padrão; todo produto tem uma categoria (a "Geral" do seed é o padrão); categoria com produto não é excluída, só desativada.
15. **Dado** um produto com grade (ex.: camiseta em 3 cores e 4 tamanhos), **quando** crio as variações, **então** cada combinação vira um produto com item, código de barras, preço e saldo próprios, ligado ao produto pai; a lista mostra o pai com as variações agrupadas; o caixa e a consulta por código acham a variação.
16. **Dado** um produto pai, **quando** altero nome, categoria ou foto, **então** as variações herdam; preço pode ser o mesmo para todas ou por variação.

## Regras de negócio e técnicas
- O Estoque segue dono de unidade, NCM, custo e saldo. O Catálogo é dono de nome de venda, foto, códigos de barras e preço.
- Preço `decimal(18,2)`; quantidade da embalagem `decimal(18,4)`.
- Produto é cadastro: soft delete. Produto com venda não é apagado, só desativado.
- `ProductChanged` e `PriceChanged` alimentam o modelo de leitura do `Pos` (OFF-01).

## Permissões, eventos e tabelas
- **Permissões:** `catalog.view`, `create`, `edit`, `delete`, `edit_price`, `view_cost` (novas, por seed do `ModuleCatalog`).
- **Endpoints:** `GET|POST /api/v1/catalog/products`, `GET|PUT|DELETE /api/v1/catalog/products/{id}`, `PUT .../price`, `GET|POST|DELETE .../barcodes`, `GET /api/v1/catalog/products/export`, `GET /api/v1/catalog/price-lists`.
- **Eventos:** `ProductChanged`, `PriceChanged`.
- **Tabelas:** `categories`, `products` (com `category_id`, `variant_of_product_id` e `variant_attributes`), `product_barcodes`, `price_lists`, `price_list_items`; `ItemType.Merchandise` no Estoque (aditivo).
- **Auditoria:** produto e preço são `IAuditable`.

## Telas e estados
- **Produtos** (`/app/catalogo/produtos`): lista com busca por nome e código, filtros e exportação. Vazio: "Nenhum produto ainda." com "Novo produto" e "Importar planilha".
- **Produto** (formulário, padrão da ADR 0044): seções Dados (com a categoria), Variações, Códigos de barras, Preço. Painel lateral com custo e margem (só com `view_cost`).
- **Categorias** (`/app/catalogo/categorias`): árvore simples. Vazio: só a "Geral".
- Estados de carregando e de erro em todas.
- **Ajuda:** `Ajuda/catalogo-produtos.md`, `Ajuda/catalogo-produto.md` e `Ajuda/catalogo-categorias.md` (novas). `<Dica>` em "Quantidade da embalagem", em "PLU" e em "Variação".

## Plano de testes
- **Unidade:** dígito verificador de GTIN-8, 12, 13 e 14; cálculo da margem.
- **Integração (Testcontainers):** criar produto com item novo (CA 3); unicidade do GTIN (CA 7); versão e histórico do preço (CA 8); `edit_price` e `view_cost` (CA 9 e 10); consulta por GTIN de embalagem (CA 12).
- **Carga:** CA 13.
- **Vitest:** formulário e painel de margem.

## Fora do escopo
- Tabelas por loja e canal, atacado, promoções (CAT-03).
- Complementos, combos e ficha técnica (CAT-05 e CAT-06).
- Etiquetas (CAT-04).

## Pontos em aberto
- PP-04: se o PO preferir preço no `Sales` e código de barras no item do Estoque, esta story muda de módulo, não de critérios.

## Implementação (17/11/2026)
Decisões em [ADR 0047](../../adr/0047-catalogo-de-produtos.md).
- **Módulo:** `src/Modules/Catalog` nas cinco camadas, schema `catalog_products` (o nome `catalog` é do banco da plataforma). Código `catalog` no `ModuleCatalog` com `view`, `create`, `edit`, `delete`, `edit_price` e `view_cost`; nos perfis `retail`, `services` e `ecommerce`, **fora** de `factory` (oculto para a Konebras). A migration da plataforma `CatalogModuleInPlans` põe o módulo nos planos existentes.
- **Área do dock:** fica em "Comercial" até a PERF-03 criar a área "Loja" (desvio registrado; a troca é uma linha no `ModuleCatalog` e no `modules.ts`, na integração da PERF-03).
- **CA 3 e 4:** item novo pelo contrato novo `IItemRegistration` do Estoque (mesmas regras da tela de itens, na transação do produto; sem código, gera `MER-000001`) ou item existente sem produto; `ItemType.Merchandise` (aditivo, sem NCM obrigatório), oferecido na tela de itens só para perfis de comércio. 409 "Este item já tem um produto no catálogo." pela conferência e pelo índice único. Item com produto não é excluído no Estoque.
- **CA 5 a 7:** `product_barcodes` com `pack_quantity` `numeric(18,4)`; GTIN-8, 12, 13 e 14 com dígito verificador (422 com a mensagem do CA); GTIN único no tenant (409 `barcode_taken` com o nome do produto). O pai de grade não recebe código: vai na variação.
- **CA 8 e 9:** "Balcão" criada no primeiro uso do módulo; mudar preço trava a tabela (`FOR UPDATE`), sobe a versão, grava `price_changes` e publica `PriceChanged` na mesma transação. Rota do preço com `catalog.edit_price`; preço na criação ou edição sem a ação = 403 `forbidden`, nada muda.
- **CA 10 e 11:** `cost` (custo médio pelo `IItemCosts`, margem, markup e preço sugerido pela categoria) só com `catalog.view_cost`, com `JsonIgnore(WhenWritingNull)`: ausente, não zerado. A exportação omite as colunas sem a ação.
- **CA 12 e 13:** `ICatalogLookup` por GTIN (com a quantidade da embalagem), PLU e trecho do nome (pg_trgm), com o preço da tabela pedida; teste de carga com 20 mil produtos.
- **CA 14:** categorias com pai, ordem e margem ou markup padrão (um dos dois); "Geral" padrão, que não sai; com produto, 409 `category_in_use` e "Desativar".
- **CA 15 e 16:** grade por atributos (até 3, até 200 variações), cada combinação com item Mercadoria próprio (unidade e NCM do pai), preço do pai ou informado; lista agrupa no pai; nome, categoria e foto (anexo `ProductPhoto` no produto raiz) descem do pai; "Aplicar a todas as variações" no preço.
- **Contrato com a VND-01:** `ISellableProducts` (`FindAsync`, `FindManyAsync` → `SellableProduct`), `ICatalogLookup`, `IProductUsageProvider` (a venda implementa) e os eventos `ProductChanged` e `PriceChanged`.
- **Konebras:** catálogo oculto e não liberado; a versão de permissões não muda (os ocultos só entram no hash com perfil além da Fábrica); o tipo "Mercadoria para revenda" não aparece na tela de itens dela; categoria e tabela de preço não são criadas no banco dela (nascem no primeiro uso).
- **Fora desta story / pendente:** "Importar planilha" no vazio da lista fica para a CAT-02 (o botão não foi posto). Colunas das Fases 15 (`kitchen_station_id`, `prep_minutes`) e `fiscal_ready` (regra 10) não entraram. PP-04 está decidida; nenhuma pendência sem decisão foi implementada.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [x] Migration criada; permissões só do `ModuleCatalog`.
- [x] Telas com estados de carregando, vazio e erro; ajuda escrita ou atualizada em `Ajuda/` no mesmo commit.
- [x] Padrões e próximo passo propositivos avaliados; auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog; decisões em ADR (0047).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 17/11/2026 | 1.0 | Implementada (seção "Implementação"): módulo Catálogo, contrato `Catalog.Contracts` para a VND-01, `IItemRegistration` e `ItemType.Merchandise` no Estoque, telas Produtos, Produto e Categorias, ajuda, ADR 0047. Desvio: área "Comercial" até a PERF-03. | Claude (dev) |
