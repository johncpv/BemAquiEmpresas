using BemAqui.BuildingBlocks.Export;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;

namespace BemAqui.Inventory.Application.Documents;

/// <summary>Linha de Saldos para exportar: a linha da tela ou a de total do valor (só o valor preenchido).</summary>
public sealed record BalanceExportRow(
    string? Code, string Item, string? Unit, string? Location, decimal? OnHand, decimal? Reserved, decimal? Available, decimal? Min, decimal? AverageCost, decimal Value, string? Situation,
    bool IsTotal = false);

/// <summary>Ajuste na exportação: a linha da lista e quem decidiu e quando (a lista não traz).</summary>
public sealed record AdjustmentExportRow(StockAdjustmentSummary Row, string? DecidedBy, DateTimeOffset? DecidedAt);

/// <summary>
/// Colunas das listas do estoque em Excel e PDF (EXP-04 CA 15, ADR 0045 §3), com os rótulos da tela. Sem coluna sensível: custo e valor saem
/// como a tela mostra (não há ação de custo no Estoque).
/// </summary>
public static class InventoryExports
{
    public static readonly ExportSpec<BalanceExportRow> Balances = new ExportSpec<BalanceExportRow>("StockBalance", "saldos-de-estoque", "Saldos de estoque")
        .Text("Código", r => r.Code)
        .Text("Item", r => r.Item)
        .Text("Unidade", r => r.Unit)
        .Text("Local", r => r.Location)
        .Quantity("Físico", r => r.OnHand)
        .Quantity("Reservado", r => r.Reserved)
        .Quantity("Disponível", r => r.Available)
        .Quantity("Mínimo", r => r.Min).NotInPdf()
        .Money("Custo médio", r => r.AverageCost)
        .Money("Valor", r => r.Value)
        .Text("Situação", r => r.Situation)
        .Style(r => r.IsTotal ? new ExportRowStyle(Bold: true) : null);

    public static readonly ExportSpec<MovementRow> Movements = new ExportSpec<MovementRow>("StockMovement", "movimentos-de-estoque", "Movimentos de estoque")
        .DateTime("Data", r => r.At)
        .Text("Número", r => r.Number)
        .Text("Tipo", MovementType)
        .Text("Origem", r => r.SourceNumber ?? (r.Origin == "Manual" ? "Lançamento manual" : r.SourceType))
        .Text("Item", r => $"{r.ItemCode} {r.ItemName}")
        .Text("Local", r => r.LocationCode)
        .Text("Lote/série", r => r.LotNumber)
        .Quantity("Quantidade", r => r.Quantity)
        .Money("Custo unitário", r => r.UnitCost)
        .Money("Valor", r => r.TotalCost)
        .Text("Lançado por", r => r.UserName ?? "Sistema");

    public static readonly ExportSpec<AdjustmentExportRow> Adjustments = new ExportSpec<AdjustmentExportRow>("StockAdjustment", "ajustes-de-estoque", "Ajustes de estoque")
        .Text("Número", r => r.Row.Number)
        .Text("Modo", r => StockAdjustmentRules.ModeLabel(r.Row.Mode))
        .Text("Local", r => r.Row.LocationName)
        .Quantity("Itens", r => r.Row.ItemCount)
        .Money("Sobras", r => r.Row.SurplusValue)
        .Money("Faltas", r => r.Row.ShortageValue)
        .Money("Valor líquido", r => r.Row.DifferenceValue)
        .Text("Lançado por", r => r.Row.RequestedByName)
        .Text("Situação", r => InventoryPdf.AdjustmentStatus(r.Row.Status, r.Row.Reversed))
        .Text("Decidido por", r => r.DecidedBy)
        .DateTime("Decidido em", r => r.DecidedAt);

    public static readonly ExportSpec<RequisitionSummary> Requisitions = new ExportSpec<RequisitionSummary>("MaterialRequisition", "requisicoes", "Requisições de material")
        .Text("Número", r => r.Number)
        .Text("Tipo", r => InventoryPdf.RequisitionKind(r.Kind))
        .Text("OS", r => r.OrderNumber)
        .Text("Etapa", r => r.StepName)
        .Text("Projeto", r => r.ProjectNumber)
        .Quantity("Itens", r => r.LineCount)
        .Text("Pedida por", r => r.RequestedByName)
        .DateTime("Pedida em", r => r.RequestedAt)
        .Text("Situação", r => InventoryPdf.RequisitionStatus(r.Status));

    public static readonly ExportSpec<InventoryCountSummary> Counts = new ExportSpec<InventoryCountSummary>("InventoryCount", "inventarios", "Inventários")
        .Text("Número", r => r.Number)
        .Text("Local", r => r.LocationName)
        .DateTime("Foto do saldo", r => r.SnapshotAt)
        .Quantity("Itens contados", r => r.CountedCount)
        .Quantity("Itens no escopo", r => r.LineCount)
        .Text("Ajuste", r => r.AdjustmentNumber)
        .Text("Situação", r => InventoryCountRules.StatusLabel(r.Status));

    public static string SituationLabel(StockSituation situation) => situation switch
    {
        StockSituation.Low => "Abaixo do mínimo",
        StockSituation.Out => "Sem saldo",
        _ => "Normal",
    };

    public static string MovementTypeLabel(StockMovementType type) => type switch
    {
        StockMovementType.Receipt => "Entrada",
        StockMovementType.Issue => "Saída",
        StockMovementType.Transfer => "Transferência",
        StockMovementType.Adjustment => "Ajuste",
        StockMovementType.Return => "Devolução",
        StockMovementType.Production => "Produção",
        StockMovementType.Shipment => "Expedição",
        _ => "Saldo inicial",
    };

    public static string ItemTypeLabel(ItemType type) => type switch
    {
        ItemType.RawMaterial => "Matéria-prima",
        ItemType.Component => "Componente",
        ItemType.Finished => "Produto acabado",
        ItemType.Consumable => "Consumível",
        _ => "Serviço",
    };

    /// <summary>Saldos com a linha de total do valor no fim (CA 15), igual à soma das linhas.</summary>
    public static IReadOnlyList<BalanceExportRow> BalanceRows(IReadOnlyList<BalanceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var list = rows.Select(b => new BalanceExportRow(
            b.ItemCode, b.ItemName, b.UnitCode, b.LocationCode, b.OnHand, b.Reserved, b.Available, b.MinStock, b.AverageCost, Value(b.OnHand, b.AverageCost), SituationLabel(b.Situation))).ToList();
        list.Add(new BalanceExportRow(null, "Total", null, null, null, null, null, null, null, list.Sum(r => r.Value), null, true));
        return list;
    }

    /// <summary>Valor da posição: físico × custo médio, em 2 casas.</summary>
    public static decimal Value(decimal onHand, decimal averageCost) => Math.Round(onHand * averageCost, 2, MidpointRounding.AwayFromZero);

    private static string MovementType(MovementRow r) =>
        MovementTypeLabel(r.Type) + (r.ReversesMovementId is not null ? " (estorno)" : r.ReversedByMovementId is not null ? " (estornado)" : string.Empty);
}
