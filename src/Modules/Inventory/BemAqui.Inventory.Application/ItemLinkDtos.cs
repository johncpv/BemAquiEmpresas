namespace BemAqui.Inventory.Application;

/// <summary>"1 {from} = {factor} {to}" (CA 1 e 2); uma das unidades é a de estoque do item.</summary>
public sealed record ConversionInput(Guid FromUnitId, Guid ToUnitId, decimal Factor);

public sealed record ConversionDetail(Guid Id, Guid ItemId, Guid FromUnitId, string FromUnitCode, Guid ToUnitId, string ToUnitCode, decimal Factor);

public sealed record SupplierCodeInput(Guid PartyId, string SupplierCode, string? SupplierUnit, decimal ConversionFactor);

/// <summary>Vínculo com o fornecedor (CA 8 a 12); <c>PartyDeleted</c> mostra o chip "Fornecedor excluído".</summary>
public sealed record SupplierCodeDetail(Guid Id, Guid ItemId, Guid PartyId, string PartyName, string? PartyDocument, bool PartyDeleted, string SupplierCode, string? SupplierUnit, decimal ConversionFactor);

/// <summary>Fornecedor ativo para o Combobox (CA 8), buscado por <c>IParties</c>.</summary>
public sealed record SupplierOption(Guid Id, string Name, string? Document);
