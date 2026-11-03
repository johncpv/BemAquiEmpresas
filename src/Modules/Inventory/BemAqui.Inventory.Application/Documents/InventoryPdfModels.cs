using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Inventory.Domain;

namespace BemAqui.Inventory.Application.Documents;

/// <summary>
/// Folha de separação da requisição (EXP-04 CA 10): o que o almoxarife separa. <b>Não existe</b> campo de custo nem de valor aqui, para
/// ninguém (CA 3, conferido por teste no record).
/// </summary>
public sealed record RequisitionPdfModel(
    string Number, string KindLabel, string StatusLabel, PdfMark Mark, string? Notice, DateOnly Date, string? OrderNumber, string? StepName, string? ProjectNumber,
    string? RequestedByName, DateTimeOffset RequestedAt, IReadOnlyList<RequisitionPdfLine> Lines);

/// <param name="Reserved">"Reservado" (da reserva da OS) ou "Extra" (material além da BOM).</param>
public sealed record RequisitionPdfLine(string ItemCode, string ItemName, decimal Quantity, string Unit, string Location, string? Lot, string Reserved);

/// <summary>Ajuste de estoque (EXP-04 CA 11): como a tela, com o valor pelo custo médio e os totais de sobras, faltas e líquido.</summary>
public sealed record StockAdjustmentPdfModel(
    string Number, string ModeLabel, bool Counted, string StatusLabel, PdfMark Mark, string? Notice, DateOnly Date, string Location, string Reason, string? RequestedByName,
    DateTimeOffset RequestedAt, string? DecidedByName, DateTimeOffset? DecidedAt, string? OriginNumber, IReadOnlyList<StockAdjustmentPdfLine> Lines, decimal Surplus, decimal Shortage,
    decimal Net);

/// <param name="Entered">A quantidade contada (modo Quantidade contada) ou a diferença com sinal (modo Diferença).</param>
public sealed record StockAdjustmentPdfLine(string ItemCode, string ItemName, string Unit, string? Lot, decimal SystemAtRequest, decimal Entered, decimal Delta, decimal Value);

/// <summary>
/// Folha de contagem (EXP-04 CA 12): contagem <b>cega</b>. O record não tem saldo do sistema, diferença nem custo (teste no record): o
/// PDF não tem de onde tirar o que a tela de contagem esconde.
/// </summary>
public sealed record InventoryCountSheetModel(string Number, string Location, string Scope, DateTimeOffset SnapshotAt, DateOnly Date, IReadOnlyList<InventoryCountSheetLine> Lines);

public sealed record InventoryCountSheetLine(string ItemCode, string ItemName, string Unit, string? Lot, bool Recount);

/// <summary>Resultado do inventário fechado (EXP-04 CA 13): a foto, o contado, a diferença, o valor e quem contou, com os totais e o ajuste.</summary>
public sealed record InventoryCountResultModel(
    string Number, string Location, string Scope, DateTimeOffset SnapshotAt, DateOnly Date, PdfMark Mark, string? Notice, string? ClosedByName, DateTimeOffset? ClosedAt,
    string? Policy, string? Adjustment, IReadOnlyList<InventoryCountResultLine> Lines, decimal Surplus, decimal Shortage, decimal Net);

public sealed record InventoryCountResultLine(
    string ItemCode, string ItemName, string Unit, string? Lot, decimal SystemQty, decimal? CountedQty, decimal Difference, decimal Value, string? CountedBy);

/// <summary>Regras puras dos PDFs do estoque (EXP-04): marcas por situação, rótulos, ordem da folha e o limite de linhas.</summary>
public static class InventoryPdf
{
    public const int MaxLines = 300;
    public const string AwaitingPick = "Aguardando aprovação: não separe.";
    public const string AwaitingApproval = "Aguardando aprovação";
    public const string CloseFirst = "Feche o inventário para ver o resultado.";
    public const string SheetOnlyCounting = "A folha de contagem só sai com o inventário em contagem.";
    public const string RecountLabel = "Recontagem";

    public static DateOnly Day(DateTimeOffset utc) => DateOnly.FromDateTime(BusinessTime.ToBusiness(utc).DateTime);

    public static string RequisitionStatus(MaterialRequisitionStatus status) => status switch
    {
        MaterialRequisitionStatus.Requested => "Pedido do operador",
        MaterialRequisitionStatus.PendingApproval => "Aguardando aprovação",
        MaterialRequisitionStatus.Completed => "Baixada",
        MaterialRequisitionStatus.Rejected => "Recusada",
        _ => "Estornada",
    };

    public static string RequisitionKind(MaterialRequisitionKind kind) => kind switch
    {
        MaterialRequisitionKind.Issue => "Requisição",
        MaterialRequisitionKind.Return => "Devolução",
        _ => "Perda",
    };

    public static string AdjustmentStatus(StockAdjustmentStatus status, bool reversed) => reversed ? "Estornado" : status switch
    {
        StockAdjustmentStatus.Pending => "Aguardando aprovação",
        StockAdjustmentStatus.Approved => "Aprovado",
        StockAdjustmentStatus.Rejected => "Reprovado",
        _ => "Cancelado",
    };

    /// <summary>CA 10: aguardando aprovação, só a faixa; recusada ou estornada, "Cancelado" com o motivo; pedida e baixada, nada.</summary>
    public static (PdfMark Mark, string? Notice) RequisitionMark(MaterialRequisitionStatus status, string? reason) => status switch
    {
        MaterialRequisitionStatus.PendingApproval => (PdfMark.None, AwaitingPick),
        MaterialRequisitionStatus.Rejected or MaterialRequisitionStatus.Reversed => (PdfMark.Cancelled, Reason(reason)),
        _ => (PdfMark.None, null),
    };

    /// <summary>CA 11: pendente, "Prévia" e "Aguardando aprovação"; estornado, a faixa com a data e o motivo; reprovado e cancelado, "Cancelado".</summary>
    public static (PdfMark Mark, string? Notice) AdjustmentMark(StockAdjustmentStatus status, DateTimeOffset? reversedAt, string? reversalReason, string? reason) => status switch
    {
        StockAdjustmentStatus.Pending => (PdfMark.Preview, AwaitingApproval),
        StockAdjustmentStatus.Approved when reversedAt is { } at => (PdfMark.None, $"Estornado em {PdfTheme.Date(Day(at))}: {reversalReason?.Trim()}".TrimEnd(' ', ':')),
        StockAdjustmentStatus.Rejected or StockAdjustmentStatus.Cancelled => (PdfMark.Cancelled, Reason(reason)),
        _ => (PdfMark.None, null),
    };

    /// <summary>CA 12: as recontagens pendentes primeiro, depois pelo código do item e pelo lote.</summary>
    public static IReadOnlyList<InventoryCountSheetLine> SheetOrder(IEnumerable<InventoryCountSheetLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.OrderByDescending(l => l.Recount).ThenBy(l => l.ItemCode, StringComparer.Ordinal).ThenBy(l => l.Lot ?? string.Empty, StringComparer.Ordinal).ToList();
    }

    private static string? Reason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : "Motivo: " + reason.Trim();
}
