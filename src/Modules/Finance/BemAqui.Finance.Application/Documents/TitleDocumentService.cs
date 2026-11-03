using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Finance.Application.Exports;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application.Documents;

/// <summary>
/// "Baixar PDF" e "Imprimir" da ficha do título a receber e a pagar (EXP-05 CA 1, 2 e 7): gerada na hora pelo mesmo <c>GetAsync</c> da tela
/// (404 para id de outro título ou de outro tenant; o salário de funcionário só para quem aprova ou paga, como a tela), com as baixas, o
/// boleto e a ordem de pagamento. Cada download entra na auditoria de exportação.
/// </summary>
public sealed class TitleDocumentService(ReceivableService receivables, PayableService payables, IFinanceDb db, PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit)
{
    public const string ReceivableEntity = "Receivable";
    public const string PayableEntity = "Payable";

    public async Task<Result<(byte[] Content, string FileName)>> ReceivablePdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await receivables.GetAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var r = detail.Value;
        var settlements = await receivables.SettlementsAsync(id, cancellationToken);
        var rows = (settlements.IsSuccess ? settlements.Value : []).OrderBy(s => s.Date).ThenBy(s => s.CreatedAt)
            .Select(s => new TitlePdfSettlement(s.Date, s.Principal, s.Interest, s.Fine, s.Discount, s.Total, s.BankAccountName ?? (s.Kind == SettlementKind.DiscountWriteOff ? "Quitação com desconto" : null),
                s.ReversedAt is not null, s.ReversalReason is { Length: > 0 } why ? "Estorno: " + why : null))
            .ToList();
        TitlePdfSlip? slip = null;
        if (r.BillingSlipId is { } slipId)
        {
            var found = await db.BillingSlips.AsNoTracking().Where(s => s.Id == slipId).Select(s => new { s.Status, s.DigitableLine, s.DueDate, s.Amount }).FirstOrDefaultAsync(cancellationToken);
            slip = found is null ? null : new TitlePdfSlip(FinanceExports.SlipLabel(found.Status) ?? found.Status.ToString(), found.DigitableLine, found.DueDate, found.Amount);
        }

        var model = new ReceivablePdfModel(r, await CostCenterAsync(r.CostCenterId, cancellationToken), rows, slip);
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new ReceivablePdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(ReceivableEntity, id.ToString(), "pdf", rows.Count, cancellationToken);
        return (bytes, r.Number + ".pdf");
    }

    public async Task<Result<(byte[] Content, string FileName)>> PayablePdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await payables.GetAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var p = detail.Value;
        var paid = await db.PayableSettlements.AsNoTracking().Where(s => s.PayableId == id).OrderBy(s => s.Date).ThenBy(s => s.CreatedAt).ToListAsync(cancellationToken);
        var bankIds = paid.Select(s => s.BankAccountId).Distinct().ToList();
        var banks = await db.BankAccounts.IgnoreQueryFilters().AsNoTracking().Where(b => bankIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);
        var rows = paid.Select(s => new TitlePdfSettlement(s.Date, s.Principal, s.Interest, s.Fine, s.Discount, s.Total, banks.GetValueOrDefault(s.BankAccountId), s.ReversedAt is not null,
            s.ReversalReason is { Length: > 0 } why ? "Estorno: " + why : null)).ToList();
        var orders = (await db.PaymentOrderItems.AsNoTracking().Where(i => i.PayableId == id)
                .Join(db.PaymentOrders.AsNoTracking(), i => i.PaymentOrderId, o => o.Id, (i, o) => new { o.Number, o.Status, o.ExpectedDate, o.SubmittedAt })
                .OrderBy(o => o.SubmittedAt).ToListAsync(cancellationToken))
            .Select(o => new TitlePdfOrder(o.Number, PaymentOrderPdf.StatusLabel(o.Status), o.ExpectedDate)).ToList();
        var model = new PayablePdfModel(p, await CostCenterAsync(p.CostCenterId, cancellationToken), rows, orders);
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new PayablePdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(PayableEntity, id.ToString(), "pdf", rows.Count, cancellationToken);
        return (bytes, p.Number + ".pdf");
    }

    private async Task<string?> CostCenterAsync(Guid? id, CancellationToken ct) => id is { } costCenter
        ? await db.CostCenters.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == costCenter).Select(c => c.Code + " " + c.Name).FirstOrDefaultAsync(ct)
        : null;
}
