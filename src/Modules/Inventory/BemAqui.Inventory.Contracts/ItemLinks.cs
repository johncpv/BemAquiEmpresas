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
