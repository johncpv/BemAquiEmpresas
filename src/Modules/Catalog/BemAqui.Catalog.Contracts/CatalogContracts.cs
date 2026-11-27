using BemAqui.SharedKernel;

namespace BemAqui.Catalog.Contracts;

/// <summary>
/// Produto vendável para o núcleo de vendas (VND-01), o modelo de leitura do caixa (OFF-01) e a loja virtual (CAT-01, ADR 0006): o Catálogo é
/// o dono; os outros módulos só leem por aqui.
/// <list type="bullet">
/// <item><c>ProductId</c>: o produto que se vende. Numa grade, é a variação (cada variação é um produto com item, código de barras e preço
/// próprios).</item>
/// <item><c>ParentProductId</c>: o produto pai da grade; null quando o produto não é variação.</item>
/// <item><c>Description</c>: o nome de venda (na variação, o nome do pai com os atributos, ex.: "Camiseta básica Azul M").</item>
/// <item><c>UnitId</c> e <c>UnitCode</c>: a unidade de estoque do item (o Estoque é o dono).</item>
/// <item><c>ItemId</c>: o item de estoque ligado (<c>inventory.items</c>), por onde a venda baixa o saldo.</item>
/// <item><c>Price</c>: o preço vigente na tabela padrão (<c>numeric(18,2)</c>); null = produto ainda sem preço (não vendável no caixa).</item>
/// <item><c>PriceListId</c> e <c>PriceListVersion</c>: a tabela padrão e a versão em que o preço foi lido (o aparelho informa a versão em que
/// vendeu).</item>
/// <item><c>IsActive</c>: falso para produto desativado ou excluído (a resolução por id devolve mesmo assim: venda antiga continua mostrando o
/// produto).</item>
/// <item><c>IsWeighable</c> e <c>Plu</c>: vendido por peso na balança e o código curto da balança.</item>
/// </list>
/// </summary>
public sealed record SellableProduct(
    Guid ProductId,
    Guid? ParentProductId,
    string Description,
    Guid UnitId,
    string UnitCode,
    Guid ItemId,
    decimal? Price,
    Guid PriceListId,
    long PriceListVersion,
    bool IsActive,
    bool IsWeighable,
    string? Plu);

/// <summary>
/// Leitura dos produtos vendáveis (CAT-01, contrato com a VND-01). Só produto ligado a item de estoque: o pai de uma grade não é vendável (vende-se
/// a variação) e fica fora das respostas. A resolução por id inclui inativos e excluídos, com <c>IsActive</c> falso.
/// </summary>
public interface ISellableProducts
{
    /// <summary>O produto ou a variação pelo id; null se não existe ou é o pai de uma grade.</summary>
    Task<SellableProduct?> FindAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Lote de ids numa consulta só; os que não existem (ou são pais de grade) ficam fora da lista.</summary>
    Task<IReadOnlyList<SellableProduct>> FindManyAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Item de estoque de cada produto vendável (PDV-05: o saldo da loja desce ao caixa por produto). Pelos produtos (<paramref name="productIds"/>)
    /// ou pelos itens (<paramref name="itemIds"/>, os que tiveram movimento); o outro filtro fica nulo. Sem preço nem nome: só o par.
    /// </summary>
    Task<IReadOnlyList<ProductItemLink>> ItemLinksAsync(IReadOnlyCollection<Guid>? productIds, IReadOnlyCollection<Guid>? itemIds, CancellationToken cancellationToken = default);
}

/// <summary>Como o produto foi achado na consulta rápida (CAT-01 CA 12).</summary>
public enum CatalogLookupMatch
{
    Gtin,
    Plu,
    Name,
}

/// <summary>
/// Resultado da consulta rápida (CA 12): o produto vendável, como achou e, pelo GTIN de embalagem, a quantidade da embalagem
/// (<c>numeric(18,4)</c>; 1 na unidade). O preço é o da tabela pedida (ou da padrão).
/// </summary>
public sealed record CatalogLookupHit(SellableProduct Product, CatalogLookupMatch Match, string? Gtin, decimal PackQuantity);

/// <summary>
/// Consulta rápida do caixa e da retaguarda (CAT-01 CA 12 e 13): por GTIN (inclusive o da embalagem), por PLU da balança ou por trecho do
/// nome. Só produtos ativos e vendáveis. <paramref name="priceListId"/> nulo = tabela padrão.
/// </summary>
public interface ICatalogLookup
{
    Task<CatalogLookupHit?> ByGtinAsync(string gtin, Guid? priceListId = null, CancellationToken cancellationToken = default);

    Task<CatalogLookupHit?> ByPluAsync(string plu, Guid? priceListId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogLookupHit>> SearchAsync(string term, int limit = 20, Guid? priceListId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vitrine da API de integração (INT-03 CA 4 e 5): produtos ativos e vendáveis por página (a partir de 1, até 100 por página), filtrados pelo
    /// trecho do nome, pelo GTIN ou pelo código interno; preço da tabela padrão. Nunca custo nem margem.
    /// </summary>
    Task<CatalogListingPage> ListAsync(string? term, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Um produto ativo e vendável da vitrine (INT-03); inativo, excluído ou pai de grade: null.</summary>
    Task<CatalogListing?> ListingAsync(Guid productId, CancellationToken cancellationToken = default);
}

/// <summary>Produto da vitrine: o vendável, o código interno do item e o GTIN da unidade (sem embalagem), quando houver.</summary>
public sealed record CatalogListing(SellableProduct Product, string? Code, string? Gtin);

/// <summary>Página da vitrine e o total do filtro.</summary>
public sealed record CatalogListingPage(IReadOnlyList<CatalogListing> Items, int Total);

/// <summary>
/// Quem usa um produto (CAT-01, regra "produto com venda não é apagado, só desativado"): a venda (VND-01) implementa. Em uso, o produto não é
/// excluído; responde 409 <c>product_in_use</c> e a tela oferece "Desativar".
/// </summary>
public interface IProductUsageProvider
{
    /// <summary>Descrição curta do uso ("vendas") ou null se não usa.</summary>
    Task<string?> UsageAsync(Guid productId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Situação do catálogo para o cartão "Primeiros passos" (PERF-04, passo "Importar produtos"): o passo se conclui pelo dado, quando o
/// tenant já tem algum produto cadastrado (pela importação da CAT-02 ou à mão).
/// </summary>
public interface ICatalogStatus
{
    Task<bool> HasProductsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Produto criado, alterado, desativado, excluído ou com código de barras novo: o modelo de leitura do caixa (OFF-01) recarrega.</summary>
public sealed record ProductChanged(Guid ProductId) : IIntegrationEvent;

/// <summary>
/// Preço mudou numa tabela (CA 8): anterior (null = sem preço), novo e a versão da tabela depois da mudança. Sem dado pessoal.
/// </summary>
public sealed record PriceChanged(Guid PriceListId, Guid ProductId, decimal? PreviousPrice, decimal NewPrice, long Version, DateTimeOffset At) : IIntegrationEvent;

/// <summary>Tabela de preço para os seletores dos outros módulos (PDV-01: a tabela padrão da loja).</summary>
public sealed record PriceListOption(Guid Id, string Name, bool IsActive, bool IsDefault);

/// <summary>
/// Tabelas de preço para os outros módulos (PDV-01 CA 2): a loja guarda só o id. A padrão ("Balcão") nasce no primeiro uso, como na CAT-01.
/// </summary>
public interface IPriceLists
{
    /// <summary>As ativas, a padrão primeiro. Cria a "Balcão" se ainda não existe.</summary>
    Task<IReadOnlyList<PriceListOption>> ActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Por id, inclusive inativas.</summary>
    Task<IReadOnlyList<PriceListOption>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>A tabela padrão ("Balcão"), criada no primeiro uso.</summary>
    Task<PriceListOption> DefaultAsync(CancellationToken cancellationToken = default);
}

/// <summary>Código de barras do produto para o caixa (OFF-01): GTIN e a quantidade da embalagem (<c>numeric(18,4)</c>; 1 na unidade).</summary>
public sealed record PosFeedBarcode(string Gtin, decimal PackQuantity);

/// <summary>Preço do produto numa tabela (OFF-01): <c>numeric(18,2)</c> e a versão da tabela no momento da leitura.</summary>
public sealed record PosFeedPrice(Guid PriceListId, decimal Price, long PriceListVersion);

/// <summary>
/// Produto como desce para o caixa (OFF-01 CA 10): o que o aparelho precisa para vender e nada mais. <b>Sem custo e sem margem</b> (CA 12): o
/// contrato não os tem, então não há como descerem. <c>SellableAtPos</c> é falso para produto desativado, excluído, fora do canal do caixa ou pai
/// de grade (o aparelho recebe como removido). <c>Prices</c> traz só as tabelas pedidas que têm preço para o produto.
/// </summary>
public sealed record PosFeedProduct(
    Guid ProductId,
    Guid? ParentProductId,
    string Description,
    string UnitCode,
    Guid ItemId,
    bool IsWeighable,
    string? Plu,
    bool SellableAtPos,
    IReadOnlyList<PosFeedBarcode> Barcodes,
    IReadOnlyList<PosFeedPrice> Prices);

/// <summary>
/// Fonte do modelo de leitura do caixa (<c>pos.catalog_items</c>, OFF-01): o Catálogo é o dono; o <c>Pos</c> lê por aqui ao consumir
/// <see cref="ProductChanged"/> e <see cref="PriceChanged"/> e na carga inicial (backfill) de cada loja.
/// </summary>
public interface IPosCatalogFeed
{
    /// <summary>Os produtos pedidos, inclusive desativados e excluídos (com <c>SellableAtPos</c> falso); id inexistente fica fora.</summary>
    Task<IReadOnlyList<PosFeedProduct>> FindAsync(IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<Guid> priceListIds, CancellationToken cancellationToken = default);

    /// <summary>Ids dos produtos vendáveis no caixa, em ordem de id, depois de <paramref name="after"/> (página da carga inicial).</summary>
    Task<IReadOnlyList<Guid>> SellableIdsAsync(Guid? after, int take, CancellationToken cancellationToken = default);
}

/// <summary>Produto vendável e o item de estoque dele (PDV-05).</summary>
public sealed record ProductItemLink(Guid ProductId, Guid ItemId);
