using BemAqui.Production.Contracts;

namespace BemAqui.Production.Application;

// PRD-04: DTOs de custo de mão de obra, separados dos DTOs sem custo (aba Apontamentos e /fab): só com projects.view_cost (CA 10,
// 14 e 15). Só o custo-hora do cargo (snapshot) e o valor; nenhum salário nem dado do funcionário além do id do apontamento.

/// <summary>Custo de um apontamento: snapshot do custo-hora do cargo, valor, versão e as marcas (CA 5, 13, 14).</summary>
public sealed record LaborEntryCostView(Guid EntryId, decimal? HourlyCostSnapshot, decimal Cost, bool CostMissing, bool CostBackfilled, int CostVersion, bool Pending);

/// <summary>Horas e custo de mão de obra de uma etapa (CA 14).</summary>
public sealed record StepLaborCostView(Guid StepId, string StepCode, string StepName, int Minutes, decimal Hours, decimal Cost, int MissingCount, bool IncludesBackfill);

/// <summary>Cargo com apontamentos "Sem custo-hora" (CA 5 e 6): nome, quantos e se o cargo já tem custo-hora (dá para recalcular).</summary>
public sealed record MissingCostPosition(Guid? PositionId, string PositionName, int Count, bool HasHourlyCost);

/// <summary>
/// Aba Custos da OS e coluna Custo da aba Apontamentos (CA 14), só com <c>projects.view_cost</c>. <c>ShowsEntryCost</c> segue o
/// parâmetro "Exibir custo-hora em cada apontamento" (padrão desligado): desligado, <c>Entries</c> vem vazio e só há o total por etapa e
/// por OS (o custo-hora e o custo de cada apontamento nem saem do servidor).
/// </summary>
public sealed record OrderLaborCostView(
    Guid OrderId, string OrderNumber, bool ShowsEntryCost, IReadOnlyList<LaborEntryCostView> Entries, IReadOnlyList<StepLaborCostView> Steps, decimal TotalHours, decimal LaborCost, int MissingCount,
    bool IncludesBackfill, IReadOnlyList<MissingCostPosition> MissingPositions, FinishedGoodsCostView? FinishedGoods = null);

/// <summary>
/// Produto acabado da OS concluída na aba Custos (PRD-06a2 CA 11 e 15), só com <c>projects.view_cost</c>: o movimento de entrada, a
/// quantidade, o custo unitário (custo apurado ÷ quantidade, 6 casas) e o total do movimento; <c>Reversed</c> quando a conclusão foi estornada.
/// </summary>
public sealed record FinishedGoodsCostView(Guid MovementId, string MovementNumber, decimal Quantity, decimal UnitCost, decimal TotalCost, DateTimeOffset PostedAt, bool Reversed);

/// <summary>Uma versão do custo do apontamento (CA 17): snapshot, horas, valor, versão, origem e quem fez.</summary>
public sealed record LaborCostHistoryView(
    int CostVersion, LaborCostOrigin Origin, int Minutes, decimal Hours, decimal? HourlyCostSnapshot, decimal PreviousAmount, decimal Amount, bool CostMissing, string? RecordedByName,
    DateTimeOffset RecordedAt);

/// <summary>
/// Impacto do "Recalcular apontamentos sem custo" (CA 6): quantos estão sem custo, quantos já podem ser recalculados (cargo com
/// custo-hora) e, só com <c>projects.view_cost</c>, o total novo ("R$ 0,00 → R$ {total}").
/// </summary>
public sealed record LaborCostMissingView(int Count, int RecalculableCount, decimal? NewTotal, IReadOnlyList<MissingCostPosition> Positions);

/// <summary>Filtro do recálculo: por cargo (tela do cargo), por OS (tela da OS) ou todos.</summary>
public sealed record LaborRecalculationInput(Guid? PositionId, Guid? OrderId);

/// <summary>Resultado do recálculo: quantos recalculados e, com <c>projects.view_cost</c>, o total.</summary>
public sealed record LaborRecalculationResult(int Recalculated, decimal? Total);