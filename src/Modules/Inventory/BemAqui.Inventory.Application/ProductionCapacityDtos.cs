namespace BemAqui.Inventory.Application;

/// <summary>
/// BOM de referência da capacidade de produzir (EST-06 CA 1): a liberada vigente de uma OS, com o produto. <c>Suggested</c> é a última
/// liberada do produto (a tag "Sugerido").
/// </summary>
public sealed record CapacityReference(
    Guid BomId, Guid ProductionOrderId, string OrderNumber, int Revision, DateTimeOffset ReleasedAt, Guid? ProductId, string? ProductCode, string? ProductName,
    int LineCount, bool Suggested);

/// <summary>
/// Linha da capacidade (CA 1, 2, 3 e 5), na unidade de estoque: necessidade por unidade do produto (com perda), físico, reservado e
/// disponível (todos os locais, da posição), unidades possíveis pelo item, necessidade e falta para a quantidade desejada, saldo a
/// receber dos pedidos em aberto (só com <c>purchasing.view</c>) e o valor estimado da falta (só com <c>projects.view_cost</c>).
/// Item inativo, excluído ou sem conversão: <c>Warning</c> e "não calculado" (os campos do cálculo nulos).
/// </summary>
public sealed record CapacityLine(
    Guid BomLineId, Guid ItemId, string ItemCode, string ItemName, Guid? UnitId, string UnitCode, decimal? PerUnit, decimal OnHand, decimal Reserved, decimal Available,
    decimal? Possible, decimal? Required, decimal? Shortage, decimal? OpenQuantity, decimal? EstimatedShortageCost, bool Limiting, string? Warning);

/// <summary>
/// Capacidade de produzir (EST-06): quantas unidades o disponível de hoje permite (<c>Possible</c>, o menor dos itens; null sem item
/// calculado), o item que limita, as faltas para a quantidade desejada e a linha informativa "Com os pedidos em aberto"
/// (<c>PossibleWithOpenOrders</c>, só com <c>OpenOrdersShown</c>; não muda o total nem as faltas). Nada é gravado.
/// </summary>
public sealed record ProductionCapacityView(
    CapacityReference Reference, decimal Quantity, decimal? Possible, Guid? LimitingItemId, string? LimitingItemName, int ShortageCount, int NotCalculatedCount,
    bool OpenOrdersShown, decimal? PossibleWithOpenOrders, bool CostShown, decimal? EstimatedShortageCost, IReadOnlyList<CapacityLine> Lines);
