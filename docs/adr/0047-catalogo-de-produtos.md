# ADR 0047: Catálogo de produtos — fronteira com o Estoque, contrato de leitura e tabela de preço versionada

- Status: Aceito (17/11/2026). Escrita na story [CAT-01](../pdv/stories/CAT-01-produto-vendavel-barras-preco.md). Segue a decisão do PO PP-04 (Catálogo próprio; `Sales` + `Pos`) e a ADR 0046 (perfis e módulo oculto).
- Contexto: o PDV precisa de produto vendável com código de barras e preço. `inventory.items` não tem nenhum dos dois, e o Estoque é dono de unidade, NCM, custo e saldo. A Konebras (perfil só `factory`) não pode ver nem mudar nada.

## Decisão

### 1. Módulo e schema
- Projeto `src/Modules/Catalog` nas cinco camadas, código de módulo `catalog` no `ModuleCatalog` (ações padrão `view`, `create`, `edit`, `delete` e as específicas `edit_price` e `view_cost`).
- Schema `catalog_products`: o nome `catalog` já é do banco da plataforma (`catalog.tenants`, `catalog.plan_modules`), e o teste de posse de schema não aceita dois donos.
- O `catalog` está nos perfis `retail`, `services` e `ecommerce`; **não** está em `factory` (oculto para a Konebras). A migration do banco da plataforma `CatalogModuleInPlans` põe o módulo nos planos existentes (só acréscimo em `plan_modules`); `tenant_modules` não muda.
- Área do dock: "Comercial" até a PERF-03 criar a área "Loja"; na integração da PERF-03, o módulo passa para `ModuleAreas.Store`.

### 2. Fronteira com o Estoque
- Um produto por item (`products.item_id`, índice único entre os não excluídos). O Estoque continua dono de unidade, NCM, custo e saldo; o Catálogo é dono do nome de venda, da foto, dos códigos de barras e do preço.
- Item novo no mesmo formulário: contrato novo `IItemRegistration` em `Inventory.Contracts` (`UnitsAsync`, `CreateAsync(NewItemData)`), que passa pelas regras da tela de itens e roda na transação de quem chama (como o `IStockMovements`). Sem código, o Estoque gera `MER-000001`.
- `ItemType.Merchandise` (Mercadoria para revenda) entra no enum, gravado como texto (aditivo). NCM não é obrigatório nele. Na tela de itens, o tipo só aparece para tenant com perfil de comércio (ou no item que já é mercadoria).
- Item com produto não é excluído no Estoque: o Catálogo registra um `IItemUsageProvider` ("produto do catálogo").

### 3. Grade (variação)
- O pai tem item próprio (todo produto tem item); cada variação é um produto com item, código de barras e preço próprios e `variant_of_product_id` apontando para o pai. `has_variants` marca o pai, que não é vendido nem achado pela consulta rápida.
- Nome (o do pai com os valores dos atributos), categoria e foto descem do pai. A foto é um anexo do dono `ProductPhoto` no produto raiz; a variação lê a do pai.

### 4. Preço
- `price_lists` com a "Balcão" (padrão, todas as lojas, canal `Pos`), criada no primeiro uso do módulo (tenant que não usa o Catálogo não ganha linha). `price_list_items` com `numeric(18,2)` e `min_quantity` (atacado, Fase 12).
- Mudar preço trava a linha da tabela (`SELECT … FOR UPDATE`), sobe `version` uma vez por gravação, grava `price_changes` (anterior, novo, versão, quem, quando) e publica `PriceChanged`, tudo na mesma transação.
- Preço só com `catalog.edit_price`: a rota do preço exige a ação; criar ou editar produto com preço sem ela responde 403 `forbidden` e nada muda. Custo e margem só com `catalog.view_cost`, e ausentes (não zerados) sem ela.

### 5. Contrato de leitura (`Catalog.Contracts`)
- `ISellableProducts` (`FindAsync`, `FindManyAsync`) devolve `SellableProduct`: id do produto (a variação, na grade), id do pai, descrição, unidade, item, preço vigente da tabela padrão, tabela e versão, ativo, por peso e PLU. A resolução por id inclui inativos e excluídos (venda antiga mostra o produto); o pai de grade fica fora.
- `ICatalogLookup` (`ByGtinAsync`, `ByPluAsync`, `SearchAsync`): consulta rápida do caixa; o GTIN de embalagem devolve a quantidade.
- `IProductUsageProvider`: a venda (VND-01) implementa; produto vendido não é excluído, só desativado.
- Eventos `ProductChanged` e `PriceChanged`, para o modelo de leitura do `Pos` (OFF-01).

### 6. Versão de permissões
- Os ocultos entram no hash da versão de permissões só quando o tenant tem perfil além da Fábrica (ajuste do `PermissionService`). Na Fábrica sozinha, o oculto é só módulo novo do PDV, que nunca aparece para ela: a Konebras mantém a versão, o menu e as permissões de antes.

## Consequências
- A VND-01 consome o Catálogo só pelos contratos acima. A CAT-02 (importação) e a PERF-04 (dados iniciais) gravam pelos serviços do Catálogo.
- Tabelas por loja e canal, promoções, etiquetas, complementos e ficha técnica ficam para CAT-03 a CAT-06.
