using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Catalog.Domain;

/// <summary>
/// Categoria de produto (<c>catalog_products.categories</c>, CAT-01 CA 14): nome, pai opcional, ordem e, opcionalmente, margem ou markup padrão
/// (um dos dois). Todo produto tem uma; a "Geral" (<c>seed_key = geral</c>) é a padrão. Com produto, não é excluída, só desativada.
/// </summary>
public sealed class ProductCategory : Entity, IAuditable, ISoftDeletable
{
    public const string GeneralSeedKey = "geral";

    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Margem sobre o preço, em % (<c>numeric(9,4)</c>); exclusiva com o markup.</summary>
    public decimal? DefaultMarginPercent { get; set; }

    /// <summary>Markup sobre o custo, em % (<c>numeric(9,4)</c>); exclusivo com a margem.</summary>
    public decimal? DefaultMarkupPercent { get; set; }

    public bool IsActive { get; set; } = true;

    public string? SeedKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
        IsActive = false;
    }
}

/// <summary>
/// Produto vendável (<c>catalog_products.products</c>, CAT-01): um por item do Estoque (o Estoque segue dono de unidade, NCM, custo e saldo; o
/// Catálogo é dono do nome de venda, da foto, dos códigos de barras e do preço). <b>Grade:</b> o pai agrupa; cada variação é um produto com
/// item, código de barras e preço próprios, <see cref="VariantOfProductId"/> apontando para o pai e os <see cref="VariantAttributes"/>
/// (ex.: Cor = Azul, Tamanho = M). Nome, categoria e foto da variação vêm do pai (CA 16). Pai com variação não é vendido: vende-se a variação.
/// </summary>
[DocumentName("Produto")]
public sealed class Product : Entity, IAuditable, ISoftDeletable, IConcurrencyVersioned
{
    private string _saleName = string.Empty;
    private string? _plu;

    public Guid ItemId { get; set; }

    public Guid CategoryId { get; set; }

    public string SaleName
    {
        get => _saleName;
        set { _saleName = value; SearchText = BuildSearch(); }
    }

    /// <summary>Nome de venda e PLU em minúsculas e sem acento, para a busca por trecho com pg_trgm (CA 12 e 13).</summary>
    public string SearchText { get; private set; } = string.Empty;

    public bool SellablePos { get; set; } = true;

    public bool SellableOnline { get; set; }

    /// <summary>Vendido por peso na balança (usa o PLU).</summary>
    public bool IsWeighable { get; set; }

    /// <summary>Código curto da balança (1 a 6 dígitos), único entre os não excluídos.</summary>
    public string? Plu
    {
        get => _plu;
        set { _plu = value; SearchText = BuildSearch(); }
    }

    public Guid? VariantOfProductId { get; set; }

    /// <summary>Atributos da variação (jsonb), na ordem da grade: nome → valor.</summary>
    public Dictionary<string, string>? VariantAttributes { get; set; }

    /// <summary>Pai de grade (tem ao menos uma variação não excluída): não é vendido nem achado pela consulta rápida.</summary>
    public bool HasVariants { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public uint Version { get; set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
        IsActive = false;
    }

    private string BuildSearch() => BemAqui.SharedKernel.SearchText.Normalize(_saleName + (_plu is null ? string.Empty : " " + _plu));
}

/// <summary>
/// Código de barras do produto (<c>catalog_products.product_barcodes</c>, CA 5 a 7): vários por produto, cada um com a quantidade da embalagem
/// (<c>numeric(18,4)</c>; 1 na unidade, 12 na caixa com 12). GTIN único no tenant, com o dígito verificador conferido.
/// </summary>
public sealed class ProductBarcode : Entity, IAuditable
{
    public Guid ProductId { get; init; }

    public string Gtin { get; init; } = string.Empty;

    public decimal PackQuantity { get; init; } = 1m;

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Canal da tabela de preço.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PriceChannel>))]
public enum PriceChannel
{
    Pos,
    Online,
    Delivery,
}

/// <summary>
/// Tabela de preço (<c>catalog_products.price_lists</c>, CA 8). Nesta story existe só a padrão "Balcão" (<c>seed_key = balcao</c>), de todas as
/// lojas e do canal Pos; tabelas por loja e canal chegam na CAT-03. <see cref="Version"/> sobe a cada mudança de preço (o aparelho informa
/// a versão em que vendeu); a gravação trava a linha da tabela (<c>FOR UPDATE</c>), então duas mudanças simultâneas não perdem versão.
/// </summary>
public sealed class PriceList : Entity, IAuditable
{
    public const string DefaultSeedKey = "balcao";

    public string Name { get; set; } = string.Empty;

    /// <summary>Loja da tabela; null = todas.</summary>
    public Guid? StoreId { get; set; }

    public PriceChannel Channel { get; set; } = PriceChannel.Pos;

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public long Version { get; private set; }

    public bool IsDefault { get; init; }

    public bool IsActive { get; set; } = true;

    public string? SeedKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public long Bump() => ++Version;
}

/// <summary>Preço de um produto numa tabela (<c>catalog_products.price_list_items</c>, CA 8): <c>numeric(18,2)</c>; a faixa de quantidade é da Fase 12.</summary>
public sealed class PriceListItem : Entity, IAuditable
{
    public Guid PriceListId { get; init; }

    public Guid ProductId { get; init; }

    public decimal Price { get; set; }

    /// <summary>A partir de quantas unidades vale (atacado, Fase 12); 1 nesta story.</summary>
    public decimal MinQuantity { get; init; } = 1m;

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// Histórico do preço (<c>catalog_products.price_changes</c>, CA 8): preço anterior (null = sem preço), novo, a versão da tabela depois da
/// mudança, quem e quando. Só inclusão; nunca alterado.
/// </summary>
public sealed class PriceChange : Entity
{
    public Guid PriceListId { get; init; }

    public Guid ProductId { get; init; }

    public decimal? PreviousPrice { get; init; }

    public decimal NewPrice { get; init; }

    public long ListVersion { get; init; }

    public DateTimeOffset ChangedAt { get; init; }

    public Guid? ChangedBy { get; init; }
}
