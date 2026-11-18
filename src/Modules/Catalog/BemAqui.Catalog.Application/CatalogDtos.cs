using System.Text.Json.Serialization;
using BemAqui.Catalog.Domain;

namespace BemAqui.Catalog.Application;

/// <summary>Categoria (CA 14). A tela monta a árvore pelo <c>ParentId</c>.</summary>
public sealed record ProductCategoryInput(string Name, Guid? ParentId, int SortOrder, decimal? DefaultMarginPercent, decimal? DefaultMarkupPercent);

public sealed record ProductCategoryDetail(
    Guid Id, string Name, Guid? ParentId, int SortOrder, decimal? DefaultMarginPercent, decimal? DefaultMarkupPercent, bool IsActive, bool IsDefault, int ProductCount);

/// <summary>Item novo do tipo Mercadoria, criado no mesmo formulário do produto (CA 3): nome, unidade e NCM digitados uma vez só.</summary>
public sealed record NewItemInput(string? Code, Guid UnitId, string? Ncm);

/// <summary>
/// Criação do produto (CA 3): um item existente (<c>ItemId</c>) <b>ou</b> um item novo (<c>NewItem</c>), nunca os dois. O nome do item novo é o
/// nome de venda. <c>Price</c> só com <c>catalog.edit_price</c> (CA 9); <c>Barcodes</c> já com o produto.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductCreateInput(
    string SaleName,
    Guid? CategoryId,
    Guid? ItemId,
    NewItemInput? NewItem,
    string? Plu,
    bool IsWeighable,
    bool SellablePos,
    bool SellableOnline,
    decimal? Price,
    IReadOnlyList<BarcodeInput>? Barcodes);

/// <summary>Edição do produto (CA 9 e 16). <c>Price</c> preenchido sem <c>catalog.edit_price</c> = 403 e nada muda.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductUpdateInput(string SaleName, Guid? CategoryId, string? Plu, bool IsWeighable, bool SellablePos, bool SellableOnline, decimal? Price, uint Version);

public sealed record BarcodeInput(string Gtin, decimal? PackQuantity);

public sealed record BarcodeView(Guid Id, string Gtin, decimal PackQuantity, DateTimeOffset CreatedAt);

/// <summary>Preço novo (CA 8). No pai de grade, <c>ApplyToVariants</c> grava o mesmo preço em todas as variações (CA 16).</summary>
public sealed record PriceInput(decimal Price, Guid? PriceListId, bool ApplyToVariants);

/// <summary>Atributo da grade (CA 15): nome ("Cor") e valores ("Azul", "Preto").</summary>
public sealed record VariantAttributeInput(string Name, IReadOnlyList<string> Values);

/// <summary>Gerar a grade (CA 15): as combinações que ainda não existem viram variações; <c>Price</c> opcional vale para todas (com <c>edit_price</c>).</summary>
public sealed record VariantsInput(IReadOnlyList<VariantAttributeInput> Attributes, decimal? Price);

/// <summary>Custo médio (por <c>IItemCosts</c>, <c>numeric(18,6)</c>), margem e markup sobre o preço vigente (CA 11). Só com <c>catalog.view_cost</c>.</summary>
public sealed record ProductCostView(decimal AverageCost, decimal? MarginPercent, decimal? MarkupPercent, decimal? SuggestedPrice);

/// <summary>
/// Linha da lista (CA 15: o pai mostra as variações agrupadas, em <c>VariantCount</c>). <c>Cost</c> fica <b>ausente</b> da resposta sem
/// <c>catalog.view_cost</c> (CA 10).
/// </summary>
public sealed record ProductListItem(
    Guid Id,
    string SaleName,
    Guid CategoryId,
    string? CategoryName,
    Guid ItemId,
    string ItemCode,
    string UnitCode,
    string? Plu,
    string? FirstGtin,
    int BarcodeCount,
    decimal? Price,
    Guid? VariantOfProductId,
    int VariantCount,
    bool IsActive,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProductCostView? Cost);

/// <summary>Variação na ficha do pai (CA 15).</summary>
public sealed record VariantView(
    Guid Id,
    string SaleName,
    IReadOnlyDictionary<string, string> Attributes,
    Guid ItemId,
    string ItemCode,
    string? FirstGtin,
    decimal? Price,
    bool IsActive,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProductCostView? Cost);

/// <summary>Ficha do produto (padrão da ADR 0044): Dados, Variações, Códigos de barras, Preço e o painel de custo e margem (só com <c>view_cost</c>).</summary>
public sealed record ProductDetail(
    Guid Id,
    string SaleName,
    Guid CategoryId,
    string? CategoryName,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid UnitId,
    string UnitCode,
    string? Ncm,
    string? Plu,
    bool IsWeighable,
    bool SellablePos,
    bool SellableOnline,
    Guid? VariantOfProductId,
    string? ParentName,
    IReadOnlyDictionary<string, string>? VariantAttributes,
    bool HasVariants,
    bool IsActive,
    decimal? Price,
    Guid PriceListId,
    string PriceListName,
    long PriceListVersion,
    IReadOnlyList<BarcodeView> Barcodes,
    IReadOnlyList<VariantView> Variants,
    DateTimeOffset CreatedAt,
    uint Version,
    Guid? PhotoId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProductCostView? Cost);

public sealed record ProductListQuery(string? Q, Guid? CategoryId, bool IncludeInactive, bool WithoutPrice);

/// <summary>Item do Estoque que ainda não tem produto, para o seletor do formulário (CA 3).</summary>
public sealed record ItemChoice(Guid Id, string Code, string Name, string UnitCode, string? Ncm, string Type);

public sealed record UnitChoiceView(Guid Id, string Code, string Name);

/// <summary>Tabela de preço (CA 8): nesta story, só a "Balcão".</summary>
public sealed record PriceListView(Guid Id, string Name, PriceChannel Channel, Guid? StoreId, bool IsDefault, long Version, int ProductCount);

/// <summary>Uma mudança de preço (CA 8): anterior, novo, versão da tabela, quem e quando.</summary>
public sealed record PriceChangeView(Guid Id, string PriceListName, decimal? PreviousPrice, decimal NewPrice, long ListVersion, DateTimeOffset ChangedAt, string? ChangedByName);

/// <summary>Resultado da consulta rápida na tela (CA 12).</summary>
public sealed record LookupView(Guid ProductId, string Description, string? Gtin, decimal PackQuantity, string Match, decimal? Price, string UnitCode);
