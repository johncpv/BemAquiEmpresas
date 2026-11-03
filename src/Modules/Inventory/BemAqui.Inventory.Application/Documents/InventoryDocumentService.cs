using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application.Documents;

/// <summary>Arquivo PDF pronto para o download (nome com o número do documento).</summary>
public sealed record InventoryPdfFile(byte[] Content, string FileName);

/// <summary>
/// "Baixar PDF" do estoque (EXP-04 CA 10 a 13): folha de separação da requisição, ajuste, folha de contagem (cega, só em contagem) e
/// resultado do inventário (só fechado ou cancelado). Lê pelo mesmo detalhe da tela (404 do id inexistente) e registra cada download na
/// auditoria de exportação. Acima de <see cref="InventoryPdf.MaxLines"/> linhas, 422 antes de gerar.
/// </summary>
public sealed class InventoryDocumentService(
    IInventoryDb db, MaterialRequisitionService requisitions, StockAdjustmentService adjustments, InventoryCountService counts, IUserDirectory users, PdfFrameFactory frames,
    IPdfRenderer renderer, IAuditWriter audit)
{
    public const string CountSheetAudit = "InventoryCount.CountSheet";

    public async Task<Result<InventoryPdfFile>> RequisitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await requisitions.DetailAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var r = detail.Value;
        if (r.Lines.Count > InventoryPdf.MaxLines)
        {
            return TooMany("A folha de separação");
        }

        var (mark, notice) = InventoryPdf.RequisitionMark(r.Status, r.Status == MaterialRequisitionStatus.Reversed ? r.ReversalReason : r.RejectReason ?? r.DecisionComment);
        var model = new RequisitionPdfModel(
            r.Number, InventoryPdf.RequisitionKind(r.Kind), InventoryPdf.RequisitionStatus(r.Status), mark, notice, InventoryPdf.Day(r.RequestedAt), r.OrderNumber, r.StepName,
            r.ProjectNumber, r.RequestedByName, r.RequestedAt,
            r.Lines.Select(l => new RequisitionPdfLine(l.ItemCode, l.ItemName, l.Quantity, l.UnitCode, l.LocationCode, l.LotNumber, l.ReservationId is null ? "Extra" : "Reservado")).ToList());
        return await RenderAsync(nameof(MaterialRequisition), id, r.Number + ".pdf", f => new RequisitionPdfDocument(f, model), cancellationToken);
    }

    public async Task<Result<InventoryPdfFile>> AdjustmentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await adjustments.DetailAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var a = detail.Value;
        if (a.Lines.Count > InventoryPdf.MaxLines)
        {
            return TooMany("O ajuste");
        }

        var (mark, notice) = InventoryPdf.AdjustmentMark(a.Status, a.ReversedAt, a.ReversalReason, a.Status == StockAdjustmentStatus.Cancelled ? a.CancelReason : a.DecisionComment);
        var counted = a.Mode == StockAdjustmentMode.Counted;
        var lines = a.Lines.Select(l =>
        {
            var delta = l.AppliedDeltaQty ?? l.DeltaQty;
            var value = l.AppliedDeltaQty is { } applied && l.UnitCost is { } cost ? StockAdjustmentRules.Value(applied, cost) : l.EstimatedValue;
            return new StockAdjustmentPdfLine(l.ItemCode, l.ItemName, l.UnitCode, l.LotNumber, l.SystemQtyAtRequest, counted ? l.CountedQty ?? 0m : l.DeltaQty, delta, value);
        }).ToList();
        var reason = a.ReasonDetail is { Length: > 0 } detailText ? $"{a.ReasonLabel} — {detailText}" : a.ReasonLabel;
        var model = new StockAdjustmentPdfModel(
            a.Number, StockAdjustmentRules.ModeLabel(a.Mode), counted, InventoryPdf.AdjustmentStatus(a.Status, a.ReversedAt is not null), mark, notice, InventoryPdf.Day(a.RequestedAt),
            $"{a.LocationCode} — {a.LocationName}", reason, a.RequestedByName, a.RequestedAt, a.DecidedByName, a.DecidedAt, a.OriginNumber, lines, a.SurplusValue, a.ShortageValue,
            a.DifferenceValue);
        return await RenderAsync(nameof(StockAdjustment), id, a.Number + ".pdf", f => new StockAdjustmentPdfDocument(f, model), cancellationToken);
    }

    /// <summary>Folha de contagem (CA 12): só em contagem (409 nas demais), sem o saldo do sistema; recontagens pendentes primeiro.</summary>
    public async Task<Result<InventoryPdfFile>> CountSheetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await counts.DetailAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var count = detail.Value;
        if (count.Status != InventoryCountStatus.Counting)
        {
            return Error.Conflict("inventory_count_not_counting", InventoryPdf.SheetOnlyCounting);
        }

        // Só os campos da contagem cega: nunca o SystemQty da linha.
        var rows = await (from l in db.InventoryCountLines.AsNoTracking()
                          join i in db.Items.IgnoreQueryFilters() on l.ItemId equals i.Id
                          join u in db.Units.IgnoreQueryFilters() on i.UnitId equals u.Id
                          where l.CountId == id && !l.Superseded
                          select new
                          {
                              i.Code, i.Name, UnitCode = u.Code, LotNumber = db.Lots.Where(x => x.Id == l.LotId).Select(x => x.LotNumber).FirstOrDefault(),
                              Recount = l.RecountOfLineId != null && l.CountedQty == null,
                          }).ToListAsync(cancellationToken);
        if (rows.Count > InventoryPdf.MaxLines)
        {
            return TooMany("A folha de contagem");
        }

        var lines = InventoryPdf.SheetOrder(rows.Select(r => new InventoryCountSheetLine(r.Code, r.Name, r.UnitCode, r.LotNumber, r.Recount)));
        var model = new InventoryCountSheetModel(count.Number, $"{count.LocationCode} — {count.LocationName}", count.ScopeLabel, count.SnapshotAt, InventoryPdf.Day(count.SnapshotAt), lines);
        return await RenderAsync(CountSheetAudit, id, count.Number + "-folha-de-contagem.pdf", f => new InventoryCountSheetPdfDocument(f, model), cancellationToken);
    }

    /// <summary>Resultado do inventário (CA 13): em contagem, 409 "Feche o inventário para ver o resultado."; cancelado, com "Cancelado".</summary>
    public async Task<Result<InventoryPdfFile>> CountResultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await counts.DetailAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var count = detail.Value;
        if (count.Status == InventoryCountStatus.Counting)
        {
            return Error.Conflict("inventory_count_not_closed", InventoryPdf.CloseFirst);
        }

        var rows = await (from l in db.InventoryCountLines.AsNoTracking()
                          join i in db.Items.IgnoreQueryFilters() on l.ItemId equals i.Id
                          join u in db.Units.IgnoreQueryFilters() on i.UnitId equals u.Id
                          where l.CountId == id && !l.Superseded
                          orderby i.Code, l.Sequence
                          select new { Line = l, i.Code, i.Name, i.AverageCost, UnitCode = u.Code, LotNumber = db.Lots.Where(x => x.Id == l.LotId).Select(x => x.LotNumber).FirstOrDefault() })
            .ToListAsync(cancellationToken);
        if (rows.Count > InventoryPdf.MaxLines)
        {
            return TooMany("O resultado do inventário");
        }

        var names = new Dictionary<Guid, string?>();
        foreach (var user in rows.Where(r => r.Line.CountedBy is not null).Select(r => r.Line.CountedBy!.Value).Distinct())
        {
            names[user] = await users.DisplayNameAsync(user, cancellationToken);
        }

        var lines = rows.Select(r =>
        {
            var l = r.Line;
            var difference = l.Difference ?? 0m;
            var cost = l.UnitCost ?? r.AverageCost;
            return (Line: new InventoryCountResultLine(r.Code, r.Name, r.UnitCode, r.LotNumber, l.SystemQty, l.CountedQty, difference, StockAdjustmentRules.Value(difference, cost),
                l.CountedBy is { } by ? names.GetValueOrDefault(by) : null), Cost: cost);
        }).ToList();
        var (surplus, shortage) = StockAdjustmentRules.Totals(lines.Select(l => (l.Line.Difference, l.Cost)));
        var cancelled = count.Status == InventoryCountStatus.Cancelled;
        var model = new InventoryCountResultModel(
            count.Number, $"{count.LocationCode} — {count.LocationName}", count.ScopeLabel, count.SnapshotAt, InventoryPdf.Day(count.ClosedAt ?? count.CancelledAt ?? count.SnapshotAt),
            cancelled ? PdfMark.Cancelled : PdfMark.None, cancelled && count.CancelReason is { Length: > 0 } why ? "Motivo: " + why.Trim() : null, count.ClosedByName, count.ClosedAt,
            count.UncountedPolicy is { } policy ? InventoryCountRules.PolicyLabel(policy) : null, count.AdjustmentSituation, lines.Select(l => l.Line).ToList(), surplus, shortage,
            surplus - shortage);
        return await RenderAsync(nameof(InventoryCount), id, count.Number + ".pdf", f => new InventoryCountResultPdfDocument(f, model), cancellationToken);
    }

    private async Task<Result<InventoryPdfFile>> RenderAsync(string entity, Guid id, string fileName, Func<PdfFrame, PdfDocumentFrame> document, CancellationToken ct)
    {
        var frame = await frames.CreateAsync(ct);
        var bytes = await renderer.RenderAsync(document(frame), ct);
        await audit.RecordExportAsync(entity, id.ToString(), "pdf", 1, ct);
        return new InventoryPdfFile(bytes, fileName);
    }

    private static Error TooMany(string what)
    {
        var message = $"{what} aceita até {InventoryPdf.MaxLines} linhas no PDF. Exporte a lista em Excel ou divida o documento.";
        return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["lines"] = [message] });
    }
}
