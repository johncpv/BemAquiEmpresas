using System.Text.Json.Serialization;

namespace BemAqui.Inventory.Contracts;

/// <summary>
/// Tipo do item (CAD-02a, ADR 0016), gravado como texto: Matéria-prima, Componente, Produto acabado, Material de consumo e
/// Serviço. Comanda o NCM obrigatório, o lote e a série sugeridos e a garantia padrão.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ItemType>))]
public enum ItemType
{
    RawMaterial,
    Component,
    Finished,
    Consumable,
    Service,

    /// <summary>
    /// Mercadoria para revenda (CAT-01, aditivo): comprada e vendida sem transformação. NCM não é obrigatório no cadastro (a loja que só
    /// emite cupom não fiscal vende sem ele). Na tela de itens, só aparece para os perfis de comércio.
    /// </summary>
    Merchandise,
}

/// <summary>Resumo do item para os outros módulos (CA 21).</summary>
public sealed record ItemSummary(
    Guid Id,
    string Code,
    string Name,
    ItemType Type,
    Guid UnitId,
    string UnitCode,
    string? Ncm,
    bool TracksLot,
    bool TracksSerial,
    int? WarrantyMonths,
    bool IsActive,
    bool IsDeleted,
    string? Cest = null,
    int Origin = 0);

/// <summary>
/// Itens para Commercial, Engineering, Purchasing, Production e Fiscal (CA 21): busca só com ativos; resolução por id inclusive
/// de inativos e excluídos (documento antigo continua mostrando o item).
/// </summary>
public interface IItems
{
    Task<IReadOnlyList<ItemSummary>> SearchAsync(ItemType? type, string? term, int limit = 20, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ItemSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Itens não excluídos pelo código exato (CAT-02: o código interno é a chave da importação de produtos), ativos e inativos; código que não
    /// existe fica de fora.
    /// </summary>
    Task<IReadOnlyList<ItemSummary>> FindByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Itens ativos de estoque (fora Serviço) com estoque mínimo &gt; 0, na unidade de estoque (CAD-02a): o job do mínimo da CMP-01 (CA 9).
    /// </summary>
    Task<IReadOnlyList<ItemMinStock>> WithMinStockAsync(CancellationToken cancellationToken = default);
}

/// <summary>Estoque mínimo de um item, na unidade de estoque (CMP-01 CA 9).</summary>
public sealed record ItemMinStock(Guid ItemId, decimal MinStock);

/// <summary>
/// Uso de um item (CA 14, 16 e 17): o EST-01 informa movimento e saldo; BOM (ENG-02), códigos do fornecedor (CAD-02b) e
/// documentos informam o uso. Sem provedores, a resposta é "não".
/// </summary>
public interface IItemUsageProvider
{
    Task<bool> HasMovementAsync(Guid itemId, CancellationToken cancellationToken = default);

    Task<bool> HasBalanceAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Descrição curta do uso ("estruturas de produto") ou null se não usa.</summary>
    Task<string?> UsageAsync(Guid itemId, CancellationToken cancellationToken = default);
}
