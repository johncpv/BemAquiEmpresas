using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Contracts;

/// <summary>Registro gravado pela importação: id e <c>xmin</c> da linha de destino logo depois (idempotência da MIG-01a).</summary>
public sealed record InventoryImportWrite(Guid Id, long Xmin);

/// <summary>Item como a importação monta a partir do <c>llx_product</c> (MIG-01c CA 3 a 7); nunca traz custo, saldo nem lote.</summary>
public sealed record ItemImportData(
    ItemType Type,
    string Code,
    string Name,
    Guid UnitId,
    Guid? CategoryId,
    string? Ncm,
    decimal MinStock,
    bool TracksLot,
    bool TracksSerial,
    int? WarrantyMonths,
    bool IsActive,
    string ImportSource);

public sealed record SupplierCodeImportData(Guid ItemId, Guid PartyId, string SupplierCode, string? SupplierUnit, decimal ConversionFactor, string ImportSource);

public sealed record LocationImportData(string Code, string Name, bool IsActive, string ImportSource);

/// <summary>
/// Escrita da importação no catálogo de itens (ADR 0017): só o módulo Migrations usa. Passa pelas mesmas regras da tela
/// (CAD-02a e CAD-02b) e grava o <c>import_source</c>; o vínculo com cadastro existente não sobrescreve os dados.
/// </summary>
public interface IItemImport
{
    Task<Guid?> FindUnitAsync(string code, CancellationToken cancellationToken = default);

    Task<Result<InventoryImportWrite>> CreateUnitAsync(string code, string name, string importSource, CancellationToken cancellationToken = default);

    /// <summary>Liga a unidade do Dolibarr a uma do BemAqui; grava o <c>import_source</c> só se estiver vazio.</summary>
    Task<Result<InventoryImportWrite>> LinkUnitAsync(Guid unitId, string importSource, CancellationToken cancellationToken = default);

    Task<long?> UnitVersionAsync(Guid unitId, CancellationToken cancellationToken = default);

    Task<Result<InventoryImportWrite>> CreateCategoryAsync(string name, Guid? parentId, string importSource, CancellationToken cancellationToken = default);

    Task<Result<InventoryImportWrite>> UpdateCategoryAsync(Guid categoryId, string name, Guid? parentId, CancellationToken cancellationToken = default);

    Task<string?> CategoryNameAsync(Guid categoryId, CancellationToken cancellationToken = default);

    Task<long?> CategoryVersionAsync(Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>Garantia padrão do tipo (CAD-02a CA 4), para o produto sem <c>elev_garantia</c>.</summary>
    Task<int?> DefaultWarrantyAsync(ItemType type, CancellationToken cancellationToken = default);

    Task<Guid?> FindItemAsync(string code, CancellationToken cancellationToken = default);

    Task<Result<InventoryImportWrite>> CreateItemAsync(ItemImportData data, CancellationToken cancellationToken = default);

    Task<Result<InventoryImportWrite>> UpdateItemAsync(Guid itemId, ItemImportData data, CancellationToken cancellationToken = default);

    /// <summary>Liga a um item já cadastrado com o mesmo código; grava o <c>import_source</c> só se estiver vazio.</summary>
    Task<Result<InventoryImportWrite>> LinkItemAsync(Guid itemId, string importSource, CancellationToken cancellationToken = default);

    Task<ItemImportData?> CurrentItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    Task<long?> ItemVersionAsync(Guid itemId, CancellationToken cancellationToken = default);

    Task<Result<InventoryImportWrite>> CreateSupplierCodeAsync(SupplierCodeImportData data, CancellationToken cancellationToken = default);

    Task<Result<InventoryImportWrite>> UpdateSupplierCodeAsync(Guid codeId, SupplierCodeImportData data, CancellationToken cancellationToken = default);

    Task<SupplierCodeImportData?> CurrentSupplierCodeAsync(Guid codeId, CancellationToken cancellationToken = default);

    Task<long?> SupplierCodeVersionAsync(Guid codeId, CancellationToken cancellationToken = default);
}

/// <summary>Escrita da importação nos locais (MIG-01c CA 11 e 12): nunca troca o local padrão.</summary>
public interface ILocationImport
{
    Task<Guid?> FindAsync(string code, CancellationToken cancellationToken = default);

    Task<Guid?> DefaultAsync(CancellationToken cancellationToken = default);

    Task<Result<InventoryImportWrite>> CreateAsync(LocationImportData data, CancellationToken cancellationToken = default);

    Task<Result<InventoryImportWrite>> UpdateAsync(Guid locationId, LocationImportData data, CancellationToken cancellationToken = default);

    Task<Result<InventoryImportWrite>> LinkAsync(Guid locationId, string importSource, CancellationToken cancellationToken = default);

    Task<LocationImportData?> CurrentAsync(Guid locationId, CancellationToken cancellationToken = default);

    Task<long?> VersionAsync(Guid locationId, CancellationToken cancellationToken = default);
}
