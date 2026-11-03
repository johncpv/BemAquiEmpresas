using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// "Baixar PDF" da solicitação de compra (EXP-05 CA 6): pelo mesmo <see cref="PurchaseRequestService.DetailAsync"/> da tela (404 fora do
/// tenant), com as cotações e os pedidos que atendem as linhas, o limite de linhas e a auditoria do download.
/// </summary>
public sealed class PurchaseRequestDocumentService(IPurchasingDb db, PurchaseRequestService requests, PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit, TimeProvider clock)
{
    public const string AuditEntity = "PurchaseRequest";

    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await requests.DetailAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var request = detail.Value;
        if (request.Lines.Count > PurchaseRequestPdf.MaxLines)
        {
            var message = $"A solicitação aceita até {PurchaseRequestPdf.MaxLines} linhas no PDF.";
            return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["lines"] = [message] });
        }

        var model = PurchaseRequestPdf.Build(request, await LinksAsync(request.Lines.Select(l => l.Id).ToList(), cancellationToken), BusinessTime.Today(clock));
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new PurchaseRequestPdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(AuditEntity, id.ToString(), "pdf", model.Lines.Count, cancellationToken);
        return (bytes, request.Number + ".pdf");
    }

    /// <summary>Cotações (pela ligação linha da cotação × linha da SC) e pedidos (pelas linhas de SC de cada linha do pedido) das linhas.</summary>
    private async Task<IReadOnlyList<PurchaseRequestPdfLink>> LinksAsync(IReadOnlyList<Guid> lineIds, CancellationToken ct)
    {
        if (lineIds.Count == 0)
        {
            return [];
        }

        var rfqIds = await db.RfqLineRequests.AsNoTracking().Where(r => lineIds.Contains(r.RequestLineId)).Select(r => r.RfqId).Distinct().ToListAsync(ct);
        var rfqs = await db.Rfqs.AsNoTracking().Where(r => rfqIds.Contains(r.Id)).OrderBy(r => r.Number).Select(r => new { r.Number, r.Status }).ToListAsync(ct);
        var orderIds = await db.PurchaseOrderLines.AsNoTracking().Where(l => l.RequestLineIds.Any(x => lineIds.Contains(x))).Select(l => l.OrderId).Distinct().ToListAsync(ct);
        var orders = await db.PurchaseOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).OrderBy(o => o.Number).Select(o => new { o.Number, o.Status }).ToListAsync(ct);
        return rfqs.Select(r => new PurchaseRequestPdfLink("Cotação", r.Number, PurchaseRequestPdf.RfqStatusLabel(r.Status)))
            .Concat(orders.Select(o => new PurchaseRequestPdfLink("Pedido", o.Number, PurchaseRequestPdf.OrderStatusLabel(o.Status)))).ToList();
    }
}
