using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Contracts;

/// <summary>
/// Conversão de quantidade por item (CAD-02b CA 3 a 6, R-EST-3), usada por movimentos, requisições e recebimentos: passa pela
/// unidade de estoque e arredonda uma vez, no fim, para 4 casas (AwayFromZero). Sem conversão, erro <c>unit_conversion_missing</c>;
/// nunca converte com fator 1.
/// </summary>
public interface IUnitConversion
{
    Task<Result<decimal>> ConvertAsync(Guid itemId, decimal quantity, Guid fromUnitId, Guid toUnitId, CancellationToken cancellationToken = default);
}

/// <summary>Item que um fornecedor chama pelo código dele (CA 11): unidade de estoque e fator da unidade do fornecedor.</summary>
public sealed record SupplierCodeMatch(Guid ItemId, string ItemCode, string ItemName, Guid UnitId, string? SupplierUnit, decimal ConversionFactor);

/// <summary>De-para do fornecedor para a nota de entrada e o recebimento (FIS-04, CMP-04). Fornecedor excluído não casa (CA 12).</summary>
public interface IItemSupplierCodes
{
    Task<SupplierCodeMatch?> FindAsync(Guid partyId, string supplierCode, CancellationToken cancellationToken = default);
}

/// <summary>Unidade em que um item pode ser informado: a de estoque ou outra com conversão cadastrada (ENG-02 CA 2).</summary>
public sealed record ItemUnitOption(Guid UnitId, string Code, bool IsStockUnit);

/// <summary>
/// Unidades usáveis por item para os editores de outros módulos (BOM da ENG-02): a de estoque primeiro e depois as convertidas. A conta
/// continua no <see cref="IUnitConversion"/>; aqui é só a lista e o código das unidades.
/// </summary>
public interface IItemUnits
{
    /// <summary>Por item (inclusive inativos e excluídos); item que não existe fica fora do dicionário.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ItemUnitOption>>> ForItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);

    /// <summary>Código de cada unidade (inclusive inativas); unidade que não existe fica fora.</summary>
    Task<IReadOnlyDictionary<Guid, string>> CodesAsync(IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken = default);
}
